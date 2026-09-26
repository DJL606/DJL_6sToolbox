using System.Diagnostics;
using System.IO;
using System.Text;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>
/// 通过 ADB 让安卓虚拟机/设备上的 curl 发起请求，
/// 使请求的 IP、TLS、UA 都来自安卓环境。
/// </summary>
public sealed class AdbRequestService
{
    private const long MaxLogFileBytes = 20 * 1024 * 1024;

    private readonly object _availabilityLock = new();
    private string? _availabilitySerial;
    private bool _availabilityResult;
    private DateTime _availabilityCheckedAt;

    public async Task<string?> ExecuteAsync(
        string serial,
        string method,
        string url,
        IReadOnlyDictionary<string, string>? form = null,
        string? cookie = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (!await IsDeviceAvailableAsync(serial, ct).ConfigureAwait(false))
        {
            TryLogResponse(serial, method, url, "adb: device not available", isJson: false);
            return null;
        }

        var command = BuildCommand(method, url, form, cookie);
        var psi = new ProcessStartInfo
        {
            FileName = "adb",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(serial);
        psi.ArgumentList.Add("shell");
        psi.ArgumentList.Add(command);

        TryLog(serial, method, url, command);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            var result = string.IsNullOrWhiteSpace(output) ? error : output;
            if (!LooksLikeJson(result))
            {
                TryLogResponse(serial, method, url, result, isJson: false);
                return null;
            }

            TryLogResponse(serial, method, url, result);
            return result;
        }
        catch (Exception ex)
        {
            TryLogResponse(serial, method, url, ex.Message, isJson: false);
            return null;
        }
    }

    private static bool LooksLikeJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().TrimStart('\uFEFF');
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    public async Task<bool> IsDeviceAvailableAsync(string serial, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        lock (_availabilityLock)
        {
            if (string.Equals(_availabilitySerial, serial, StringComparison.OrdinalIgnoreCase) &&
                now - _availabilityCheckedAt < TimeSpan.FromSeconds(5))
            {
                return _availabilityResult;
            }
        }

        var available = false;
        try
        {
            var text = await RunAdbCommandAsync(new[] { "devices" }, ct).ConfigureAwait(false);
            available = IsListedDevice(text ?? string.Empty, serial);
            if (!available && IsTcpAddress(serial))
            {
                await RunAdbCommandAsync(new[] { "connect", serial }, ct).ConfigureAwait(false);
                text = await RunAdbCommandAsync(new[] { "devices" }, ct).ConfigureAwait(false);
                available = IsListedDevice(text ?? string.Empty, serial);
            }
        }
        catch
        {
            available = false;
        }

        lock (_availabilityLock)
        {
            _availabilitySerial = serial;
            _availabilityResult = available;
            _availabilityCheckedAt = now;
        }

        return available;
    }

    private async Task<string?> RunAdbCommandAsync(IEnumerable<string> args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            var exitTask = process.WaitForExitAsync(ct);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3), ct);
            var completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
            if (completed == exitTask)
            {
                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(output) ? error : output;
            }

            try
            {
                process.Kill();
            }
            catch
            {
                // 忽略结束失败。
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTcpAddress(string serial)
        => serial.Contains(':') && !serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase);

    private static bool IsListedDevice(string text, string serial)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                string.Equals(parts[0], serial, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendWithLimit(string file, string text)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Exists && info.Length > MaxLogFileBytes)
            {
                var old = file + ".old";
                if (File.Exists(old))
                {
                    File.Delete(old);
                }

                File.Move(file, old);
            }

            File.AppendAllText(file, text);
        }
        catch
        {
            // 日志失败不影响主流程。
        }
    }

    private static void TryLogResponse(string serial, string method, string url, string? response, bool isJson = true)
    {
        try
        {
            AppPaths.EnsureCreated();
            var logFile = Path.Combine(AppPaths.LogDirectory, "adb-bridge.log");
            var preview = string.IsNullOrWhiteSpace(response) ? "<empty>" : response[..Math.Min(response.Length, 300)];
            var marker = isJson ? "RESP" : "RESP_NON_JSON";
            AppendWithLimit(logFile, $"{marker} {method} {serial} {url}{Environment.NewLine}{preview}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响请求。
        }
    }


    private static void TryLog(string serial, string method, string url, string command)
    {
        try
        {
            AppPaths.EnsureCreated();
            var logFile = Path.Combine(AppPaths.LogDirectory, "adb-bridge.log");
            AppendWithLimit(logFile, $"[{DateTime.Now:HH:mm:ss}] {method} {serial} {url}{Environment.NewLine}{command}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响请求。
        }
    }


    private static string BuildCommand(string method, string url, IReadOnlyDictionary<string, string>? form, string? cookie = null)
    {
        var sb = new StringBuilder("curl -s --max-time 20");
        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" -X POST");
        }

        sb.Append(" -A 'Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36'");
        sb.Append(" -e 'https://m.bilibili.com/'");
        sb.Append(" -H 'Origin: https://m.bilibili.com'");
        sb.Append(" -H 'X-Requested-With: XMLHttpRequest'");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            sb.Append(" -H 'Cookie: ").Append(cookie).Append('\'');
        }

        if (form is { Count: > 0 })
        {
            var data = string.Join("&", form.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
            sb.Append(" -d '").Append(data).Append('\'');
        }

        sb.Append(' ').Append('\'').Append(url).Append('\'');
        return sb.ToString();
    }

    public void ApplyProxy(string serial, string? proxy)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(serial);
            psi.ArgumentList.Add("shell");
            psi.ArgumentList.Add(string.IsNullOrWhiteSpace(proxy)
                ? "settings delete global http_proxy"
                : $"settings put global http_proxy {proxy.Trim()}");
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // 代理设置失败不影响主流程。
        }
    }


    public void SetDeviceKeepAwake(string serial, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        try
        {
            var command = enabled
                ? "svc power stayon false && settings put global stay_on_while_plugged_in 0"
                : "svc power stayon false && settings put global stay_on_while_plugged_in 0 && settings put system screen_off_timeout 60000";
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(serial);
            psi.ArgumentList.Add("shell");
            psi.ArgumentList.Add(command);
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // 设置失败不影响主流程。
        }
    }

}
