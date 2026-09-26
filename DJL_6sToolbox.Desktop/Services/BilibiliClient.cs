using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using DJL_6sToolbox.Desktop.Models;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>
/// 封装 B 站 Web API。所有请求都走同一个 HttpClient，
/// 复用连接并统一设置 UA/Referer，降低资源开销。
/// </summary>
public sealed class BilibiliClient : IDisposable
{
    private const string BaseUrl = "https://api.bilibili.com";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
    private const string MobileUserAgent =
        "Mozilla/5.0 (Linux; Android 13; 2308CPXD0C Build/TKQ1.220829.002; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/120.0.0.0 Mobile Safari/537.36";
    private const string MobileReferer = "https://m.bilibili.com/";
    private const string AppKey = "1d8b6e7d45233436";
    private const string AppSecret = "560c52ccd288fed045859ed18bffd973";

    private readonly HttpClient _http;
    private readonly HttpClient _publicHttp;
    private readonly HttpClient _mobileHttp;
    private readonly AdbRequestService _adb = new();
    private readonly Dictionary<long, AuthorInfo> _authorCache = new();
    private readonly SemaphoreSlim _buvidLock = new(1, 1);
    private readonly SemaphoreSlim _cardRate = new(1, 1);
    private readonly SemaphoreSlim _wbiKeysLock = new(1, 1);
    private readonly Random _random = new();
    private bool _useMobilePortrait;
    private bool _useAppSigned;
    private bool _enableAdbBridge;
    private string? _adbSerial;
    private string? _accessKey;
    private string? _deviceId;
    private string? _buvidFp;
    private string? _buvid3;
    private string? _buvid;
    private string? _buvid4;
    private string? _build;
    private string? _channel;
    private string? _deviceBrand;
    private string? _deviceModel;
    private string? _osVersion;
    private string? _biliTicket;
    private string? _biliTicketV2;
    private string? _cookie;
    private string? _csrf;
    private bool _buvidReady;
    private string? _wbiImgKey;
    private string? _wbiSubKey;

    public BilibiliClient()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");

        // 部分公开接口（如用户卡片）在携带登录 Cookie 时会返回 -352 风控，
        // 使用独立的无 Cookie 客户端请求这些接口更稳定。
        _publicHttp = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        _publicHttp.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _publicHttp.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        _publicHttp.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");

        _mobileHttp = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        _mobileHttp.DefaultRequestHeaders.UserAgent.ParseAdd(MobileUserAgent);
        _mobileHttp.DefaultRequestHeaders.Referrer = new Uri(MobileReferer);
        _mobileHttp.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        _mobileHttp.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    }

    public bool HasCookie => !string.IsNullOrWhiteSpace(_cookie);

    public bool HasAccessKey => !string.IsNullOrWhiteSpace(_accessKey);

    public void SetCookie(string cookieString)
    {
        _cookie = NormalizeCookie(cookieString);
        _csrf = ExtractCookie(_cookie, "bili_jct");
        ApplyCookie(_http);
        ApplyCookie(_mobileHttp);
        if (_useMobilePortrait)
        {
            EnsureMobileFingerprintCookies();
        }

        _authorCache.Clear();
    }

    public void SetMobilePortraitMode(bool enabled)
    {
        _useMobilePortrait = enabled;
        if (enabled)
        {
            EnsureMobileFingerprintCookies();
        }
    }

    public void SetAppSignedMode(bool enabled) => _useAppSigned = enabled;
    public void SetAdbBridge(bool enabled, string? serial)
    {
        _enableAdbBridge = enabled;
        _adbSerial = string.IsNullOrWhiteSpace(serial) ? null : serial.Trim();
    }
    public void ApplyAdbProxy(string? proxy)
    {
        if (_enableAdbBridge && !string.IsNullOrWhiteSpace(_adbSerial))
        {
            _adb.ApplyProxy(_adbSerial, proxy);
        }
    }
    public void SetAdbDeviceKeepAwake(bool enabled)
    {
        if (_enableAdbBridge && !string.IsNullOrWhiteSpace(_adbSerial))
        {
            _adb.SetDeviceKeepAwake(_adbSerial, enabled);
        }
    }

    public void SetAdbDeviceKeepAwake(string serial, bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(serial))
        {
            _adb.SetDeviceKeepAwake(serial, enabled);
        }
    }

    public Task<bool> IsAdbDeviceOnlineAsync(string serial, CancellationToken ct = default)
        => _adb.IsDeviceAvailableAsync(serial, ct);

    public void SetAccessKey(string? accessKey) => _accessKey = string.IsNullOrWhiteSpace(accessKey) ? null : accessKey.Trim();

    public void SetDeviceTrust(
        string? deviceId,
        string? buvidFp,
        string? buvid3,
        string? buvid,
        string? buvid4,
        string? build,
        string? channel,
        string? deviceBrand,
        string? deviceModel,
        string? osVersion,
        string? biliTicket,
        string? biliTicketV2)
    {
        _deviceId = NullIfEmpty(deviceId);
        _buvidFp = NullIfEmpty(buvidFp);
        _buvid3 = NullIfEmpty(buvid3);
        _buvid = NullIfEmpty(buvid);
        _buvid4 = NullIfEmpty(buvid4);
        _build = NullIfEmpty(build);
        _channel = NullIfEmpty(channel);
        _deviceBrand = NullIfEmpty(deviceBrand);
        _deviceModel = NullIfEmpty(deviceModel);
        _osVersion = NullIfEmpty(osVersion);
        _biliTicket = NullIfEmpty(biliTicket);
        _biliTicketV2 = NullIfEmpty(biliTicketV2);
        if (_useMobilePortrait)
        {
            EnsureMobileFingerprintCookies();
        }
    }

    public void SetPhoneProfile(PhoneProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        SetAccessKey(profile.AccessKey);
        SetDeviceTrust(
            profile.DeviceId,
            profile.BuvidFp,
            profile.Buvid3,
            profile.Buvid,
            profile.Buvid4,
            profile.Build,
            profile.Channel,
            profile.DeviceBrand,
            profile.DeviceModel,
            profile.OsVersion,
            profile.BiliTicket,
            profile.BiliTicketV2);
    }


    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ApplyCookie(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        if (!string.IsNullOrWhiteSpace(_cookie))
        {
            client.DefaultRequestHeaders.Add("Cookie", _cookie);
        }
    }

    private void EnsureMobileFingerprintCookies()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var lsid = RandomHex(32) + "_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        _cookie = UpsertCookie(_cookie, "b_nut", nowMs);
        _cookie = UpsertCookie(_cookie, "b_lsid", lsid);
        _cookie = UpsertCookie(_cookie, "buvid3", _buvid3 ?? string.Empty);
        _cookie = UpsertCookie(_cookie, "buvid", _buvid ?? string.Empty);
        _cookie = UpsertCookie(_cookie, "buvid4", _buvid4 ?? string.Empty);
        _cookie = UpsertCookie(_cookie, "buvid_fp", _buvidFp ?? string.Empty);
        _cookie = UpsertCookie(_cookie, "device_id", _deviceId ?? string.Empty);
        _cookie = UpsertCookie(_cookie, "bili_ticket", _biliTicket ?? string.Empty);
        _cookie = UpsertCookie(_cookie, "bili_ticket_v2", _biliTicketV2 ?? string.Empty);
        ApplyCookie(_http);
        ApplyCookie(_mobileHttp);
    }

    private static string UpsertCookie(string? cookie, string key, string value)
    {
        var prefix = key + "=";
        var parts = (cookie ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Contains('=', StringComparison.Ordinal) &&
                        !x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add($"{key}={value}");
        return string.Join("; ", parts);
    }

    private static string RandomHex(int length)
    {
        const string chars = "0123456789abcdef";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(chars[Random.Shared.Next(chars.Length)]);
        }

        return sb.ToString();
    }

    private static string NormalizeCookie(string? cookieString)
    {
        if (string.IsNullOrWhiteSpace(cookieString))
        {
            return string.Empty;
        }

        var cleaned = cookieString
            .Replace((char)13, ';')
            .Replace((char)10, ';')
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Contains('=', StringComparison.Ordinal))
            .Select(x => x.Trim());
        return string.Join("; ", cleaned);
    }

    public async Task<LoginInfo?> CheckLoginAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"{BaseUrl}/x/web-interface/nav", ct).ConfigureAwait(false);
        if (!TryGetRoot(doc, out var root))
        {
            return null;
        }

        var code = root.TryGetProperty("code", out var codeProp) ? ToInt32(codeProp) : -1;
        var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? string.Empty : string.Empty;

        if (code != 0)
        {
            return new LoginInfo(false, 0, string.Empty, 0, 0, $"接口返回 {code}：{message}");
        }

        var data = root.GetProperty("data");
        var isLogin = ToBool(data.GetProperty("isLogin"));
        if (!isLogin)
        {
            return new LoginInfo(false, 0, string.Empty, 0, 0, "账号未登录，Cookie 无效或已过期");
        }

        var levelInfo = data.TryGetProperty("level_info", out var li) ? li : default;
        return new LoginInfo(
            true,
            data.TryGetProperty("mid", out var mid) ? ToInt64(mid) : 0,
            data.TryGetProperty("uname", out var uname) ? uname.GetString() ?? string.Empty : string.Empty,
            levelInfo.TryGetProperty("current_level", out var lv) ? ToInt32(lv) : 0,
            data.TryGetProperty("money", out var money) ? ToInt64(money) : 0,
            "OK");
    }

    /// <summary>获取全站最新投稿（rid=0 表示全部分区）。</summary>
    public async Task<IReadOnlyList<VideoInfo>> GetNewVideosAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/x/web-interface/newlist?rid=0&pn={page}&ps={pageSize}";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        var result = new List<VideoInfo>();

        if (!TryGetRoot(doc, out var root) || ToInt32(root.GetProperty("code")) != 0)
        {
            return result;
        }

        if (!root.GetProperty("data").TryGetProperty("archives", out var archives))
        {
            return result;
        }

        foreach (var item in archives.EnumerateArray())
        {
            var owner = item.TryGetProperty("owner", out var o) ? o : default;
            result.Add(new VideoInfo(
                ToInt64(item.GetProperty("aid")),
                item.TryGetProperty("bvid", out var bvid) ? bvid.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                owner.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                owner.TryGetProperty("mid", out var mid) ? ToInt64(mid) : 0,
                item.TryGetProperty("pubdate", out var pub) ? ToInt64(pub) : 0,
                item.TryGetProperty("pic", out var pic) ? pic.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("tname", out var tname) ? tname.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("duration", out var dur) ? ToInt64(dur) : 0));
        }

        return result;
    }

    /// <summary>按关键词搜索视频（使用综合搜索接口）。</summary>
    public async Task<IReadOnlyList<VideoInfo>> SearchVideosAsync(
        string keyword,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/x/web-interface/search/all/v2?keyword={Uri.EscapeDataString(keyword)}&page={page}&page_size={pageSize}";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        var result = new List<VideoInfo>();

        if (!TryGetRoot(doc, out var root) || ToInt32(root.GetProperty("code")) != 0)
        {
            return result;
        }

        if (!root.GetProperty("data").TryGetProperty("result", out var items))
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("result_type", out var type) ||
                !string.Equals(type.GetString(), "video", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var data in dataArr.EnumerateArray())
            {
                if (data.TryGetProperty("type", out var dataType) &&
                    string.Equals(dataType.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new VideoInfo(
                        data.TryGetProperty("aid", out var aid) ? ToInt64(aid) : 0,
                        data.TryGetProperty("bvid", out var bvid) ? bvid.GetString() ?? string.Empty : string.Empty,
                        data.TryGetProperty("title", out var title) ? CleanHtml(title.GetString() ?? string.Empty) : string.Empty,
                        data.TryGetProperty("author", out var author) ? author.GetString() ?? string.Empty : string.Empty,
                        data.TryGetProperty("mid", out var mid) ? ToInt64(mid) : 0,
                        data.TryGetProperty("pubdate", out var pub) ? ToInt64(pub) : 0,
                        data.TryGetProperty("pic", out var pic) ? pic.GetString() ?? string.Empty : string.Empty,
                        data.TryGetProperty("typename", out var tname) ? tname.GetString() ?? string.Empty : string.Empty,
                        ParseDurationSeconds(data.TryGetProperty("duration", out var dur) ? dur.GetString() : null)));
                }
            }
        }

        return result;
    }

    /// <summary>获取 UP 主卡片信息（含粉丝数、等级），带进程内缓存。</summary>
    public async Task<AuthorInfo?> GetAuthorAsync(long mid, CancellationToken ct = default)
    {
        if (mid <= 0)
        {
            return null;
        }

        if (_authorCache.TryGetValue(mid, out var cached))
        {
            return cached;
        }

        await _cardRate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_authorCache.TryGetValue(mid, out var secondCache))
            {
                return secondCache;
            }

            long fans = 0;
            int level = 0;
            string name = string.Empty;

            try
            {
                var statUrl = $"{BaseUrl}/x/relation/stat?vmid={mid}";
                using var statDoc = await GetPublicJsonAsync(statUrl, ct).ConfigureAwait(false);
                if (TryGetRoot(statDoc, out var statRoot) && ToInt32(statRoot.GetProperty("code")) == 0)
                {
                    fans = ToInt64(statRoot.GetProperty("data").GetProperty("follower"));
                }
            }
            catch
            {
                // 粉丝接口失败时继续尝试等级接口。
            }

            try
            {
                var appInfo = await GetAuthorInfoAppAsync(mid, ct).ConfigureAwait(false);
                if (appInfo is { } appAuthorInfo)
                {
                    level = appAuthorInfo.Level;
                    name = appAuthorInfo.Name;

                    // 等级仍为 0/未知时，自动切换 WBI 接口再试一次
                    if (level <= 0)
                    {
                        var wbiInfo = await GetAuthorInfoWbiAsync(mid, ct).ConfigureAwait(false);
                        if (wbiInfo is { Level: > 0 } wbiAuthorInfo)
                        {
                            level = wbiAuthorInfo.Level;
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                name = wbiAuthorInfo.Name;
                            }
                        }
                    }
                }
                else
                {
                    // App 接口没拿到，直接切换 WBI 接口
                    var wbiInfo = await GetAuthorInfoWbiAsync(mid, ct).ConfigureAwait(false);
                    if (wbiInfo is { } wbiAuthorInfo)
                    {
                        level = wbiAuthorInfo.Level;
                        name = wbiAuthorInfo.Name;
                    }
                }
            }
            catch
            {
                // 等级接口失败时，粉丝信息仍可用于筛选。
            }

            if (fans == 0 && level == 0 && string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var info = new AuthorInfo(mid, name, level, fans);
            if (_authorCache.Count >= 5000)
            {
                _authorCache.Clear();
            }

            _authorCache[mid] = info;
            return info;
        }
        finally
        {
            _cardRate.Release();
        }
    }

    private async Task<(int Level, string Name)?> GetAuthorInfoAppAsync(long mid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_accessKey))
        {
            return null;
        }

        EnsureMobileFingerprintCookies();

        var parameters = new Dictionary<string, string>
        {
            ["access_key"] = _accessKey,
            ["appkey"] = AppKey,
            ["mobi_app"] = "android",
            ["platform"] = "android",
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["vmid"] = mid.ToString()
        };
        AddAppTrustParameters(parameters);

        parameters["sign"] = SignParameters(parameters, AppSecret);

        var query = string.Join("&", parameters
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var url = $"https://app.bilibili.com/x/v2/space?{query}";

        string? adbJson = null;
        if (_enableAdbBridge && !string.IsNullOrWhiteSpace(_adbSerial))
        {
            adbJson = await _adb.ExecuteAsync(_adbSerial, "GET", url, null, _cookie, ct).ConfigureAwait(false);
        }

        string json;
        if (adbJson is null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://m.bilibili.com/");
            request.Headers.Add("Origin", "https://m.bilibili.com");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");

            using var response = await _mobileHttp.SendAsync(request, ct).ConfigureAwait(false);
            json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        else
        {
            json = adbJson;
        }

        using var doc = JsonDocument.Parse(json);
        if (!TryGetRoot(doc, out var root) || ToInt32(root.GetProperty("code")) != 0)
        {
            return null;
        }

        var data = root.GetProperty("data");
        var level = 0;
        if (data.TryGetProperty("level_info", out var levelInfo))
        {
            level = ToInt32(levelInfo.TryGetProperty("current_level", out var lv) ? lv : default);
        }

        var name = data.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        return (level, name);
    }


    private async Task<(int Level, string Name)?> GetAuthorInfoWbiAsync(long mid, CancellationToken ct)
    {
        try
        {
            await EnsureWbiKeysAsync(ct).ConfigureAwait(false);
            var accUrl = BuildWbiUrl($"{BaseUrl}/x/space/wbi/acc/info", ("mid", mid.ToString()));
            using var accDoc = await GetJsonAsync(accUrl, ct).ConfigureAwait(false);
            if (!TryGetRoot(accDoc, out var accRoot) || ToInt32(accRoot.GetProperty("code")) != 0)
            {
                return null;
            }

            var data = accRoot.GetProperty("data");
            var level = ToInt32(data.TryGetProperty("level", out var lv) ? lv : default);
            var name = data.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            return (level, name);
        }
        catch
        {
            return null;
        }
    }


    public async Task GetVideoDetailAsync(long aid, CancellationToken ct = default)
    {
        try
        {
            await GetJsonAsync($"{BaseUrl}/x/web-interface/view?aid={aid}", ct).ConfigureAwait(false);
        }
        catch
        {
            // 预热失败不影响点赞。
        }
    }

    public async Task<LikeResult> LikeVideoAsync(long aid, CancellationToken ct = default)
    {
        await EnsureBuvidAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(_csrf))
        {
            return new LikeResult(false, -101, "缺少 CSRF Token", false);
        }

        if (_useAppSigned)
        {
            return await LikeVideoAppSignedAsync(aid, ct).ConfigureAwait(false);
        }

        if (_useMobilePortrait)
        {
            EnsureMobileFingerprintCookies();
        }

        var fields = new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["like"] = "1",
            ["csrf"] = _csrf
        };
        if (_useMobilePortrait)
        {
            fields["platform"] = "android";
            fields["mobi_app"] = "android";
        }

        using var content = new FormUrlEncodedContent(fields);

        var client = _useMobilePortrait ? _mobileHttp : _http;
        var referer = _useMobilePortrait ? MobileReferer : "https://www.bilibili.com/";
        var origin = _useMobilePortrait ? "https://m.bilibili.com" : "https://www.bilibili.com";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/x/web-interface/archive/like")
        {
            Content = content
        };
        request.Headers.Referrer = new Uri(referer);
        request.Headers.Add("Origin", origin);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetRoot(doc, out var root))
            {
                return new LikeResult(false, -1, "响应格式错误", false);
            }

            var code = root.TryGetProperty("code", out var codeProp) ? ToInt32(codeProp) : -1;
            var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? string.Empty : string.Empty;
            var success = code == 0 || code == 65006;
            return new LikeResult(success, code, message, IsRiskControlCode(code, message));
        }
        catch (JsonException)
        {
            return new LikeResult(false, -1, "响应解析失败", false);
        }
    }

    private async Task<LikeResult> LikeVideoAppSignedAsync(long aid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_accessKey))
        {
            return new LikeResult(false, -401, "APP 签名请求需要 access_key，请在设置中填写", true);
        }

        EnsureMobileFingerprintCookies();

        var parameters = new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["like"] = "1",
            ["access_key"] = _accessKey,
            ["platform"] = "android",
            ["mobi_app"] = "android",
            ["appkey"] = AppKey,
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        };
        AddAppTrustParameters(parameters);

        parameters["sign"] = SignParameters(parameters, AppSecret);

        var url = $"{BaseUrl}/x/web-interface/archive/like";
        if (_enableAdbBridge && !string.IsNullOrWhiteSpace(_adbSerial))
        {
            var adbJson = await _adb.ExecuteAsync(_adbSerial, "POST", url, parameters, _cookie, ct).ConfigureAwait(false);
            if (adbJson is not null)
            {
                return ParseLikeJson(adbJson);
            }
        }

        using var content = new FormUrlEncodedContent(parameters);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.Referrer = new Uri("https://m.bilibili.com/");
        request.Headers.Add("Origin", "https://m.bilibili.com");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        using var response = await _mobileHttp.SendAsync(request, ct).ConfigureAwait(false);
        return await ParseResult(response, ct);
    }

    private void AddAppTrustParameters(Dictionary<string, string> parameters)
    {
        parameters["build"] = _build ?? "7040400";
        parameters["channel"] = _channel ?? "master";
        parameters["device"] = "android";
        parameters["buvid"] = _buvid ?? _buvid3 ?? string.Empty;
        parameters["brand"] = _deviceBrand ?? string.Empty;
        parameters["model"] = _deviceModel ?? string.Empty;
        parameters["osver"] = _osVersion ?? string.Empty;
        parameters["statistics"] = "{\"app\":1,\"version\":\"" + (_build ?? "7040400") + "\",\"device\":\"android\",\"platform\":1}";
        if (!string.IsNullOrWhiteSpace(_biliTicket))
        {
            parameters["bili_ticket"] = _biliTicket!;
        }

        if (!string.IsNullOrWhiteSpace(_biliTicketV2))
        {
            parameters["bili_ticket_v2"] = _biliTicketV2!;
        }
    }

    private static string SignParameters(IEnumerable<KeyValuePair<string, string>> parameters, string secret)
    {
        var query = string.Join("&", parameters
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        return Md5(query + secret);
    }

    public async Task<LikeResult> CommentVideoAsync(long aid, string message, CancellationToken ct = default)
    {
        await EnsureBuvidAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(_csrf))
        {
            return new LikeResult(false, -101, "缺少 CSRF Token", false);
        }

        var fields = new Dictionary<string, string>
        {
            ["oid"] = aid.ToString(),
            ["type"] = "1",
            ["message"] = message,
            ["plat"] = "1",
            ["csrf"] = _csrf
        };

        if (_enableAdbBridge && !string.IsNullOrWhiteSpace(_adbSerial))
        {
            var adbJson = await _adb.ExecuteAsync(_adbSerial, "POST", $"{BaseUrl}/x/v2/reply/add", fields, _cookie, ct).ConfigureAwait(false);
            if (adbJson is not null)
            {
                return ParseLikeJson(adbJson);
            }
        }

        using var content = new FormUrlEncodedContent(fields);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/x/v2/reply/add")
        {
            Content = content
        };
        request.Headers.Referrer = new Uri("https://www.bilibili.com/");
        request.Headers.Add("Origin", "https://www.bilibili.com");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return await ParseResult(response, ct);
    }

    public async Task<LikeResult> ReportVideoAsync(long aid, int reasonType, string detail, CancellationToken ct = default)
    {
        await EnsureBuvidAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(_csrf))
        {
            return new LikeResult(false, -101, "缺少 CSRF Token", false);
        }

        var fields = new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["reason"] = reasonType.ToString(),
            ["detail"] = detail,
            ["csrf"] = _csrf
        };

        if (_enableAdbBridge && !string.IsNullOrWhiteSpace(_adbSerial))
        {
            var adbJson = await _adb.ExecuteAsync(_adbSerial, "POST", $"{BaseUrl}/x/web-interface/archive/report", fields, _cookie, ct).ConfigureAwait(false);
            if (adbJson is not null)
            {
                return ParseLikeJson(adbJson);
            }
        }

        using var content = new FormUrlEncodedContent(fields);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/x/web-interface/archive/report")
        {
            Content = content
        };
        request.Headers.Referrer = new Uri("https://www.bilibili.com/");
        request.Headers.Add("Origin", "https://www.bilibili.com");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return await ParseResult(response, ct);
    }

    private static LikeResult ParseLikeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LikeResult(false, -1, "响应为空", false);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetRoot(doc, out var root))
            {
                return new LikeResult(false, -1, "响应格式错误", false);
            }

            var code = root.TryGetProperty("code", out var codeProp) ? ToInt32(codeProp) : -1;
            var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? string.Empty : string.Empty;
            var success = code == 0 || code == 65006;
            return new LikeResult(success, code, message, IsRiskControlCode(code, message));
        }
        catch (JsonException)
        {
            return new LikeResult(false, -1, "响应解析失败", false);
        }
    }

    private static async Task<LikeResult> ParseResult(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetRoot(doc, out var root))
            {
                return new LikeResult(false, -1, "响应格式错误", false);
            }

            var code = root.TryGetProperty("code", out var codeProp) ? ToInt32(codeProp) : -1;
            var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? string.Empty : string.Empty;
            return new LikeResult(code == 0 || code == 65006, code, message, IsRiskControlCode(code, message));
        }
        catch (JsonException)
        {
            return new LikeResult(false, -1, "响应解析失败", false);
        }
    }

    private static long ParseDurationSeconds(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return 0;
        }

        var parts = duration.Split(':');
        long seconds = 0;
        foreach (var part in parts)
        {
            if (long.TryParse(part, out var value))
            {
                seconds = seconds * 60 + value;
            }
        }

        return seconds;
    }

    private static string CleanHtml(string text)
        => System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty));

    public void Dispose()
    {
        _http.Dispose();
        _publicHttp.Dispose();
        _mobileHttp.Dispose();
    }

    private async Task EnsureBuvidAsync(CancellationToken ct)
    {
        if (_buvidReady)
        {
            return;
        }

        await _buvidLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_buvidReady)
            {
                return;
            }

            using var response = await _http.GetAsync($"{BaseUrl}/x/frontend/finger/spi", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _buvidReady = true;
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            if (ToInt32(root.GetProperty("code")) != 0)
            {
                _buvidReady = true;
                return;
            }

            var data = root.GetProperty("data");
            var b3 = data.TryGetProperty("b_3", out var b3Prop) ? b3Prop.GetString() : null;
            var b4 = data.TryGetProperty("b_4", out var b4Prop) ? b4Prop.GetString() : null;

            if (!string.IsNullOrWhiteSpace(b3) || !string.IsNullOrWhiteSpace(b4))
            {
                var extra = new List<string>();
                if (!string.IsNullOrWhiteSpace(b3))
                {
                    extra.Add($"buvid3={b3}");
                }

                if (!string.IsNullOrWhiteSpace(b4))
                {
                    extra.Add($"buvid4={b4}");
                }

                var combined = string.Join("; ", new[] { _cookie, string.Join("; ", extra) }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                _cookie = combined;
                ApplyCookie(_http);
                ApplyCookie(_mobileHttp);
            }

            _buvidReady = true;
        }
        catch
        {
            // buvid 获取失败不阻断主流程，且只尝试一次。
            _buvidReady = true;
        }
        finally
        {
            _buvidLock.Release();
        }
    }

    private async Task EnsureWbiKeysAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_wbiImgKey) && !string.IsNullOrWhiteSpace(_wbiSubKey))
        {
            return;
        }

        await _wbiKeysLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_wbiImgKey) && !string.IsNullOrWhiteSpace(_wbiSubKey))
            {
                return;
            }

            using var doc = await GetJsonAsync($"{BaseUrl}/x/web-interface/nav", ct).ConfigureAwait(false);
            if (!TryGetRoot(doc, out var root) || ToInt32(root.GetProperty("code")) != 0)
            {
                return;
            }

            var wbi = root.GetProperty("data").GetProperty("wbi_img");
            _wbiImgKey = ExtractKey(wbi.GetProperty("img_url").GetString());
            _wbiSubKey = ExtractKey(wbi.GetProperty("sub_url").GetString());
        }
        finally
        {
            _wbiKeysLock.Release();
        }
    }

    private string BuildWbiUrl(string baseUrl, params (string Key, string Value)[] parameters)
    {
        var dict = new Dictionary<string, string>();
        foreach (var p in parameters)
        {
            dict[p.Key] = p.Value;
        }

        dict["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var query = string.Join("&", dict.OrderBy(x => x.Key)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var mixinKey = GetMixinKey(_wbiImgKey ?? string.Empty, _wbiSubKey ?? string.Empty);
        var wrid = Md5(query + mixinKey);
        return $"{baseUrl}?{query}&w_rid={wrid}";
    }

    private static string ExtractKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var fileName = url.Substring(url.LastIndexOf('/') + 1);
        var dot = fileName.IndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private static string GetMixinKey(string imgKey, string subKey)
    {
        const string table = "46,47,18,2,53,8,23,32,15,50,10,31,58,3,45,35,27,43,5,49,33,9,42,19,29,28,14,39,12,38,41,13,37,48,7,16,24,55,40,61,26,17,0,1,60,51,30,4,22,25,54,21,56,59,6,63,57,62,11,36,20,34,44,52";
        var indices = table.Split(',').Select(int.Parse).ToArray();
        var raw = imgKey + subKey;
        var chars = indices.Select(i => i < raw.Length ? raw[i] : ' ').ToArray();
        return new string(chars, 0, 32);
    }

    private static string Md5(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<JsonDocument> GetPublicJsonAsync(string url, CancellationToken ct)
    {
        var json = await GetStringAsync(url, ct, usePublic: true).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        await EnsureBuvidAsync(ct).ConfigureAwait(false);
        var json = await GetStringAsync(url, ct, usePublic: false).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct, bool usePublic)
    {
        if (_enableAdbBridge && !string.IsNullOrWhiteSpace(_adbSerial))
        {
            var adbJson = await _adb.ExecuteAsync(_adbSerial, "GET", url, null, _cookie, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(adbJson))
            {
                return adbJson;
            }
        }

        var client = usePublic ? _publicHttp : _http;
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static bool TryGetRoot(JsonDocument doc, out JsonElement root)
    {
        root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Object;
    }

    private static int ToInt32(JsonElement element, int fallback = 0)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var text))
        {
            return text;
        }

        return fallback;
    }

    private static long ToInt64(JsonElement element, long fallback = 0)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var text))
        {
            return text;
        }

        return fallback;
    }

    private static bool ToBool(JsonElement element, bool fallback = false)
    {
        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var text))
        {
            return text;
        }

        return fallback;
    }

    private static bool IsRiskControlCode(int code, string message)
    {
        if (code is -401 or -352 or -403 or -412 or -509 or -799 or 1011040)
        {
            return true;
        }

        return message.Contains("风控", StringComparison.OrdinalIgnoreCase)
               || message.Contains("频繁", StringComparison.OrdinalIgnoreCase)
               || message.Contains("操作太快", StringComparison.OrdinalIgnoreCase)
               || message.Contains("验证", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractCookie(string? cookie, string key)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        foreach (var part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            if (string.Equals(part[..idx].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return part[(idx + 1)..].Trim();
            }
        }

        return null;
    }
}
