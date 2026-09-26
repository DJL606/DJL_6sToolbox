using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text.Json;
using DJL_6sToolbox.Desktop.Models;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>
/// 检查并安装更新。优先读取 GitHub Releases；若项目尚无 Release，
/// 则退化为检查源码提交（仅当应用以 Git 源码目录运行时提示拉取更新）。
/// </summary>
public sealed class UpdateService
{
    private const string RepoOwner = "greedisgoood";
    private const string RepoName = "bilibili-tool";
    private const string ApiBase = "https://api.github.com/repos";
    private const string DefaultBranch = "main";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DJL_6sToolbox-Desktop");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>检查是否有可用更新。无更新返回 null。</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await TryGetLatestReleaseAsync(ct).ConfigureAwait(false);
            if (release is not null)
            {
                var latestTag = release.TagName?.TrimStart('v', 'V') ?? string.Empty;
                if (!string.IsNullOrEmpty(latestTag) &&
                    !string.Equals(latestTag, CurrentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return new UpdateInfo(
                        CurrentVersion,
                        latestTag,
                        release.AssetUrl,
                        release.Body,
                        null,
                        false);
                }

                return null;
            }

            return await TryGetSourceUpdateAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查更新失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>下载并安装 Release 更新。成功后当前进程会被替换。</summary>
    public async Task<bool> InstallAsync(
        UpdateInfo update,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            return false;
        }

        AppPaths.EnsureCreated();
        var updateDir = AppPaths.UpdateDirectory;
        Directory.CreateDirectory(updateDir);

        var fileName = Path.GetFileName(new Uri(update.DownloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "bilitool-update.zip";
        }

        var downloadPath = Path.Combine(updateDir, fileName);
        progress?.Report($"正在下载 {fileName} ...");

        using (var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(downloadPath);
            await source.CopyToAsync(target, ct).ConfigureAwait(false);
        }

        progress?.Report("下载完成，正在准备更新...");

        var stagingDir = Path.Combine(updateDir, "stage");
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
        }

        string? stagedExe;

        if (string.Equals(Path.GetExtension(downloadPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(downloadPath, stagingDir);
            stagedExe = Directory.EnumerateFiles(stagingDir, "*.exe", SearchOption.AllDirectories)
                .OrderByDescending(x => new FileInfo(x).Length)
                .FirstOrDefault();
        }
        else
        {
            stagedExe = downloadPath;
        }

        if (stagedExe is null)
        {
            progress?.Report("未在更新包中找到可执行文件。");
            return false;
        }

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            progress?.Report("无法定位当前程序路径。");
            return false;
        }

        progress?.Report("正在启动更新程序...");
        LaunchUpdater(currentExe, stagedExe);
        return true;
    }

    private static void LaunchUpdater(string currentExe, string stagedExe)
    {
        var currentDir = Path.GetDirectoryName(currentExe)!;
        var script = Path.Combine(Path.GetTempPath(), $"bilitool_update_{Guid.NewGuid():N}.cmd");

        var lines = new[]
        {
            "@echo off",
            "chcp 65001 >nul",
            "timeout /t 2 /nobreak >nul",
            $"taskkill /pid {Environment.ProcessId} /f >nul 2>&1",
            $"copy /y \"{stagedExe}\" \"{currentExe}\" >nul",
            $"start \"\" \"{currentExe}\"",
            $"del /q \"{script}\"",
        };

        File.WriteAllLines(script, lines, System.Text.Encoding.UTF8);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = currentDir
        };

        Process.Start(psi);
    }

    private async Task<ReleaseInfo?> TryGetLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{ApiBase}/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;

            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("browser_download_url", out var u))
                    {
                        assetUrl = u.GetString();
                        break;
                    }
                }
            }

            return new ReleaseInfo(
                root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : string.Empty,
                root.TryGetProperty("body", out var body) ? body.GetString() : string.Empty,
                assetUrl);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<UpdateInfo?> TryGetSourceUpdateAsync(CancellationToken ct)
    {
        var gitDir = FindGitDirectory(AppContext.BaseDirectory);
        if (gitDir is null)
        {
            return null;
        }

        var remoteSha = await GetRemoteHeadShaAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(remoteSha))
        {
            return null;
        }

        var localSha = await GetLocalHeadShaAsync(gitDir).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(localSha) &&
            string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UpdateInfo(
            CurrentVersion,
            "main",
            $"https://github.com/{RepoOwner}/{RepoName}",
            "源码有可用更新。",
            remoteSha,
            true);
    }

    
    private static string? FindGitDirectory(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static async Task<string?> GetLocalHeadShaAsync(string gitDir)
    {
        try
        {
            var headFile = Path.Combine(gitDir, ".git", "HEAD");
            if (!File.Exists(headFile))
            {
                return null;
            }

            var head = (await File.ReadAllTextAsync(headFile).ConfigureAwait(false)).Trim();
            if (head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            {
                var refPath = head[4..].Trim();
                var refFile = Path.Combine(gitDir, ".git", refPath.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(refFile) ? (await File.ReadAllTextAsync(refFile).ConfigureAwait(false)).Trim() : null;
            }

            return head;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> GetRemoteHeadShaAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{ApiBase}/{RepoOwner}/{RepoName}/commits/{DefaultBranch}";
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ReleaseInfo(string? TagName, string? Body, string? AssetUrl);
}
