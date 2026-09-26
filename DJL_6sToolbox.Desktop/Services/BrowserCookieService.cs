using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DJL_6sToolbox.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>
/// 从本机 Chromium 系浏览器（Chrome / Edge / Brave）自动提取 B 站 Cookie。
/// 读取流程与浏览器关闭后复制 SQLite 文件到临时目录再解密，避免占用锁定。
/// </summary>
public sealed class BrowserCookieService
{
    private const int CryptProtectUiForbidden = 0x1;

    public Task<BrowserCookieInfo?> TryGetBilibiliCookieAsync(CancellationToken ct = default)
        => Task.Run(() => TryGetBilibiliCookie(), ct);

    private static BrowserCookieInfo? TryGetBilibiliCookie()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var browsers = new (string Name, string UserData)[]
        {
            ("Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"))
        };

        foreach (var browser in browsers)
        {
            if (!Directory.Exists(browser.UserData))
            {
                continue;
            }

            var result = TryGetFromBrowser(browser.Name, browser.UserData);
            if (result is not null && !string.IsNullOrWhiteSpace(result.Sessdata))
            {
                return result;
            }
        }

        return null;
    }

    private static BrowserCookieInfo? TryGetFromBrowser(string browserName, string userDataDir)
    {
        try
        {
            var localStatePath = Path.Combine(userDataDir, "Local State");
            if (!File.Exists(localStatePath))
            {
                return null;
            }

            byte[]? key = null;
            using (var json = JsonDocument.Parse(File.ReadAllText(localStatePath)))
            {
                var root = json.RootElement;
                if (root.TryGetProperty("os_crypt", out var osCrypt))
                {
                    key = DecryptDpapiKey(osCrypt.TryGetProperty("encrypted_key", out var ek) ? ek.GetString() : null);
                    key ??= DecryptDpapiKey(osCrypt.TryGetProperty("app_bound_encrypted_key", out var ak) ? ak.GetString() : null);
                }
            }

            if (key is null)
            {
                return null;
            }

            for (var i = 0; i <= 8; i++)
            {
                var profile = i == 0 ? "Default" : $"Profile {i}";
                var cookiePath = Path.Combine(userDataDir, profile, "Network", "Cookies");
                if (!File.Exists(cookiePath))
                {
                    continue;
                }

                var info = TryReadCookies(browserName, cookiePath, key);
                if (info is not null)
                {
                    return info;
                }
            }
        }
        catch
        {
            // 单浏览器失败继续尝试下一个。
        }

        return null;
    }

    private static BrowserCookieInfo? TryReadCookies(string browserName, string cookiePath, byte[] key)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"DJL_6sToolbox_cookies_{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(cookiePath, tempFile, true);

            using var connection = new SqliteConnection($"Data Source={tempFile};Mode=ReadOnly");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT name, encrypted_value
                FROM cookies
                WHERE host_key LIKE '%bilibili.com%'
                  AND name IN ('SESSDATA','bili_jct','DedeUserID','DedeUserID__ckMd5','sid')
                """;

            string? sessdata = null, biliJct = null, dedeUserId = null, ckMd5 = null, sid = null;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var encrypted = reader.IsDBNull(1) ? Array.Empty<byte>() : (byte[])reader[1];
                var value = DecryptCookieValue(encrypted, key);

                switch (name)
                {
                    case "SESSDATA": sessdata = value; break;
                    case "bili_jct": biliJct = value; break;
                    case "DedeUserID": dedeUserId = value; break;
                    case "DedeUserID__ckMd5": ckMd5 = value; break;
                    case "sid": sid = value; break;
                }
            }

            if (string.IsNullOrWhiteSpace(sessdata))
            {
                return null;
            }

            return new BrowserCookieInfo(browserName, sessdata, biliJct, dedeUserId, ckMd5, sid);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static byte[]? DecryptDpapiKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var prefix = base64.StartsWith("DPAPI", StringComparison.Ordinal) ? "DPAPI" : "APPB";
            var data = Convert.FromBase64String(base64[prefix.Length..]);
            return DpapiUnprotect(data);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? DpapiUnprotect(byte[] data)
    {
        var input = new DataBlob
        {
            cbData = data.Length,
            pbData = Marshal.AllocHGlobal(data.Length)
        };

        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            var output = new DataBlob();
            var entropy = new DataBlob();
            var prompt = new CryptProtectPromptStruct { cbSize = 0, dwPromptFlags = 0, hwndApp = IntPtr.Zero, szPrompt = null };

            if (!CryptUnprotectData(ref input, null, ref entropy, IntPtr.Zero, ref prompt, CryptProtectUiForbidden, ref output))
            {
                return null;
            }

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            if (output.pbData != IntPtr.Zero)
            {
                _ = LocalFree(output.pbData);
            }

            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input.pbData);
            }
        }
    }

    private static string? DecryptCookieValue(byte[] encrypted, byte[] key)
    {
        if (encrypted.Length == 0)
        {
            return string.Empty;
        }

        var prefix = Encoding.ASCII.GetString(encrypted, 0, Math.Min(3, encrypted.Length));
        if (prefix is "v10" or "v11")
        {
            var nonce = encrypted.AsSpan(3, 12).ToArray();
            var tag = encrypted.AsSpan(encrypted.Length - 16, 16).ToArray();
            var ciphertext = encrypted.AsSpan(15, encrypted.Length - 31).ToArray();

            try
            {
                var plaintext = new byte[ciphertext.Length];
                using var aes = new System.Security.Cryptography.AesGcm(key, 16);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch
            {
                return null;
            }
        }

        return Encoding.UTF8.GetString(encrypted);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件残留不影响。
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct CryptProtectPromptStruct
    {
        public int cbSize;
        public int dwPromptFlags;
        public IntPtr hwndApp;
        public string? szPrompt;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("crypt32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        ref CryptProtectPromptStruct pPromptStruct,
        int dwFlags,
        ref DataBlob pDataOut);
}
