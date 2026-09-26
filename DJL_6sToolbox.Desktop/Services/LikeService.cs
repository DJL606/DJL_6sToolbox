using DJL_6sToolbox.Desktop.Models;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>
/// 自动点赞调度器：扫描全站最新视频，按 UP 主粉丝数/等级筛选后点赞。
/// 所有网络操作均为异步，不阻塞 UI 线程。
/// </summary>
public sealed class LikeService
{
    private readonly BilibiliClient _client;
    private readonly AppSettings _settings;
    private readonly LikedAidStore _likedAidStore;
    private readonly Random _random = new();
    private CancellationTokenSource? _cts;
    private DateTime _cooldownUntil;
    private DateTime _lastProfileSwitch = DateTime.Now;
    private int _currentProfileIndex = -1;
    private int _currentProfileRunLiked;
    private int _consecutiveFailedVideos;

    public event EventHandler<LikeLogEntry>? LogEntryAdded;

    public event EventHandler<string>? PhoneProfileSwitched;
    public event EventHandler? LikeMethodChanged;
    public event EventHandler<string>? AdbDeviceSwitched;

    public string CurrentPhoneName { get; private set; } = "未设置";

    public bool IsRunning { get; private set; }

    public bool IsCoolingDown => _cooldownUntil > DateTime.Now;

    public DateTime CooldownUntil => _cooldownUntil;

    public DateTime? LastRunAt { get; private set; }

    public int LikedThisRun { get; private set; }

    public int TotalLikedCount => _likedAidStore.Count;

    public LikeService(BilibiliClient client, AppSettings settings, LikedAidStore likedAidStore)
    {
        _client = client;
        _settings = settings;
        _likedAidStore = likedAidStore;
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _cts?.Cancel();
        _cts = null;
        IsRunning = false;
        AddLog(LogLevel.Info, "自动点赞已停止。");
    }

    /// <summary>立即执行一轮扫描，适合“立即运行”按钮。</summary>
    public async Task RunOnceAsync(CancellationToken externalToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        await RunCoreAsync(linked.Token).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        AddLog(LogLevel.Info, "自动点赞已启动。");
        try
        {
            while (!token.IsCancellationRequested)
            {
                await RunCoreAsync(token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    break;
                }

                var delay = TimeSpan.FromSeconds(Math.Max(1, _settings.ScanIntervalSeconds));
                if (_cooldownUntil > DateTime.Now)
                {
                    var remaining = _cooldownUntil - DateTime.Now;
                    if (remaining > delay)
                    {
                        delay = remaining;
                        AddLog(LogLevel.Warning, $"风控冷却中，将在 {Math.Ceiling(remaining.TotalSeconds)} 秒后重试。");
                    }
                }

                AddLog(LogLevel.Info, $"将在 {delay.TotalSeconds:0.#} 秒后扫描下一轮。");
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"自动点赞异常：{ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunCoreAsync(CancellationToken token)
    {
        if (!_client.HasCookie)
        {
            AddLog(LogLevel.Warning, "尚未配置 Cookie，无法执行点赞。请先在“Cookie”页配置。");
            return;
        }

        EnsureProfileRotation();
        _currentProfileRunLiked = 0;
        _consecutiveFailedVideos = 0;

        LastRunAt = DateTime.Now;
        LikedThisRun = 0;
        AddLog(LogLevel.Info, $"开始扫描全站最新视频（{_settings.MaxPages} 页）。");

        var seen = new HashSet<long>();
        var candidates = new List<VideoInfo>();

        for (var page = 1; page <= Math.Max(1, _settings.MaxPages); page++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var videos = await _client.GetNewVideosAsync(page, 20, token).ConfigureAwait(false);
                if (videos.Count == 0)
                {
                    break;
                }

                foreach (var video in videos)
                {
                    if (seen.Add(video.Aid))
                    {
                        candidates.Add(video);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"获取第 {page} 页最新视频失败：{ex.Message}");
                break;
            }

            await Task.Delay(300 + _random.Next(500), token).ConfigureAwait(false);
        }

        AddLog(LogLevel.Info, $"共获取 {candidates.Count} 个候选视频，开始筛选。");

        foreach (var video in candidates)
        {
            token.ThrowIfCancellationRequested();

            if (_likedAidStore.Contains(video.Aid))
            {
                continue;
            }

            if (!IsTitleAllowed(video.Title))
            {
                AddLog(LogLevel.Info, $"标题不满足筛选条件，已跳过：{video.Title}");
                continue;
            }

            if (!await IsAuthorAllowedAsync(video, token).ConfigureAwait(false))
            {
                continue;
            }

            if (_settings.EnableAdbBridge)
            {
                await _client.GetVideoDetailAsync(video.Aid, token).ConfigureAwait(false);
            }

            var environmentRetries = 0;
            var isPureWeb = !_settings.UseMobilePortraitLike && !_settings.UseAppSignedLike;
            var maxEnvironmentRetries = !isPureWeb &&
                                        _settings.EnablePhoneRotation &&
                                        _settings.PhoneProfiles is { Count: > 1 } profiles
                ? profiles.Count - 1
                : 0;
            var result = await _client.LikeVideoAsync(video.Aid, token).ConfigureAwait(false);
            while (!result.Success && environmentRetries < maxEnvironmentRetries)
            {
                AddLog(LogLevel.Warning, $"当前模拟环境失败，切换下一套模拟环境重试：{video.Title}（{video.Bvid}）[code={result.Code}] {result.Message}");
                SwitchToNextProfile();
                environmentRetries++;
                await Task.Delay(GetLikeDelay(), token).ConfigureAwait(false);
                result = await _client.LikeVideoAsync(video.Aid, token).ConfigureAwait(false);
            }

            if (_settings.EnableLikeModeSwitch && !result.Success && !_settings.UseMobilePortraitLike)
            {
                AddLog(LogLevel.Warning, $"点赞失败，切换移动端竖屏模式重试：{video.Title}（{video.Bvid}）[code={result.Code}] {result.Message}");
                _settings.UseMobilePortraitLike = true;
                _client.SetMobilePortraitMode(true);
                LikeMethodChanged?.Invoke(this, EventArgs.Empty);
                await Task.Delay(GetLikeDelay(), token).ConfigureAwait(false);
                result = await _client.LikeVideoAsync(video.Aid, token).ConfigureAwait(false);
            }

            if (_settings.EnableLikeModeSwitch && !result.Success && !_settings.UseAppSignedLike && _client.HasAccessKey)
            {
                AddLog(LogLevel.Warning, $"移动端模式仍失败，切换 APP 签名请求重试：{video.Title}（{video.Bvid}）[code={result.Code}] {result.Message}");
                _settings.UseAppSignedLike = true;
                _client.SetAppSignedMode(true);
                LikeMethodChanged?.Invoke(this, EventArgs.Empty);
                await Task.Delay(GetLikeDelay(), token).ConfigureAwait(false);
                result = await _client.LikeVideoAsync(video.Aid, token).ConfigureAwait(false);
            }

            if (_settings.EnableLikeModeSwitch &&
                !result.Success &&
                (_settings.UseMobilePortraitLike || _settings.UseAppSignedLike))
            {
                AddLog(LogLevel.Info, "当前视频三种模式均已尝试，下个视频切回纯网页模式重新开始。");
                _settings.UseMobilePortraitLike = false;
                _settings.UseAppSignedLike = false;
                _client.SetMobilePortraitMode(false);
                _client.SetAppSignedMode(false);
                LikeMethodChanged?.Invoke(this, EventArgs.Empty);
            }

            if (result.Success)
            {
                _consecutiveFailedVideos = 0;
                _likedAidStore.Add(video.Aid);
                LikedThisRun++;
                _currentProfileRunLiked++;
                AddLog(LogLevel.Success, $"已点赞：{video.Title}（UP：{video.AuthorName}）");
            }
            else
            {
                _consecutiveFailedVideos++;
                AddLog(LogLevel.Warning, $"该视频连续失败 3 次，切换到下一个视频：{video.Title}（{video.Bvid}）[code={result.Code}] {result.Message}");

                if (_consecutiveFailedVideos >= 2)
                {
                    AddLog(LogLevel.Error, $"连续两个视频都失败 3 次，判定为风控/频率限制，本轮停止。请等待 {_settings.RiskControlCooldownMinutes} 分钟后再试。");
                    _cooldownUntil = DateTime.Now.AddMinutes(Math.Max(1, _settings.RiskControlCooldownMinutes));
                    break;
                }

                continue;
            }

            if (_settings.MaxLikesPerRun > 0 && LikedThisRun >= _settings.MaxLikesPerRun)
            {
                AddLog(LogLevel.Info, "已达到本轮点赞上限。");
                break;
            }

            if (_settings.EnablePhoneRotation &&
                _settings.PhoneProfiles is { Count: > 1 } &&
                GetMaxLikesPerRun() > 0 &&
                _currentProfileRunLiked >= GetMaxLikesPerRun())
            {
                AddLog(LogLevel.Info, $"手机环境 {CurrentPhoneName} 已达本轮上限，自动切换到下一部。");
                SwitchToNextProfile();
            }

            // 模拟真实操作间隔，降低风控概率。
            await Task.Delay(GetLikeDelay(), token).ConfigureAwait(false);
        }

        AddLog(LogLevel.Info, $"本轮完成，共点赞 {LikedThisRun} 个视频。");
    }

    private void OnPhoneProfileSwitched(string name)
    {
        CurrentPhoneName = name;
        PhoneProfileSwitched?.Invoke(this, name);
    }

    private void SwitchToNextProfile()
    {
        var profiles = _settings.PhoneProfiles;
        if (profiles is null || profiles.Count <= 1)
        {
            return;
        }

        _currentProfileIndex = (_currentProfileIndex + 1) % profiles.Count;
        _lastProfileSwitch = DateTime.Now;
        _currentProfileRunLiked = 0;
        _client.SetPhoneProfile(profiles[_currentProfileIndex]);
        AddLog(LogLevel.Info, $"已自动切换到下一部手机环境：{profiles[_currentProfileIndex].Name}");
        ApplyProfileAdb(profiles[_currentProfileIndex]);
        OnPhoneProfileSwitched(profiles[_currentProfileIndex].Name);
    }


    private void ApplyProfileAdb(PhoneProfile profile)
    {
        if (!_settings.EnableAdbBridge || !_settings.AdbFollowPhoneSwitch)
        {
            return;
        }

        var serial = profile.AdbSerial;
        if (string.IsNullOrWhiteSpace(serial))
        {
            _client.SetAdbBridge(true, null);
            _client.ApplyAdbProxy(null);
            AddLog(LogLevel.Info, "当前手机环境未绑定 ADB，已切换为本机请求。");
            AdbDeviceSwitched?.Invoke(this, "未绑定 ADB");
            return;
        }

        _client.SetAdbBridge(true, serial);
        var adb = _settings.AdbDevices?.FirstOrDefault(d => d.Serial == serial);
        _client.ApplyAdbProxy(adb?.Proxy);
        AddLog(LogLevel.Info, $"已跟随系统切换 ADB 设备：{adb?.Name ?? serial}");
        AdbDeviceSwitched?.Invoke(this, adb?.Name ?? serial);
    }


    public void ApplyProfile(PhoneProfile profile)
    {
        var index = _settings.PhoneProfiles?.IndexOf(profile) ?? -1;
        _currentProfileIndex = index;
        _lastProfileSwitch = DateTime.Now;
        _currentProfileRunLiked = 0;
        _client.SetPhoneProfile(profile);
        AddLog(LogLevel.Info, $"已手动切换到手机环境：{profile.Name}");
        ApplyProfileAdb(profile);
        OnPhoneProfileSwitched(profile.Name);
    }

    private void EnsureProfileRotation()
    {
        var profiles = _settings.PhoneProfiles;
        if (profiles is null || profiles.Count == 0)
        {
            return;
        }

        if (_currentProfileIndex < 0)
        {
            _currentProfileIndex = 0;
            _lastProfileSwitch = DateTime.Now;
            _currentProfileRunLiked = 0;
            _client.SetPhoneProfile(profiles[0]);
            AddLog(LogLevel.Info, $"已切换到手机环境：{profiles[0].Name}");
            ApplyProfileAdb(profiles[0]);
            OnPhoneProfileSwitched(profiles[0].Name);
            return;
        }

        if (!_settings.EnablePhoneRotation)
        {
            return;
        }

        var minutes = Math.Max(1, _settings.PhoneRotationMinutes);
        if ((DateTime.Now - _lastProfileSwitch).TotalMinutes < minutes)
        {
            return;
        }

        _currentProfileIndex = (_currentProfileIndex + 1) % profiles.Count;
        _lastProfileSwitch = DateTime.Now;
        _currentProfileRunLiked = 0;
        _client.SetPhoneProfile(profiles[_currentProfileIndex]);
        AddLog(LogLevel.Info, $"已轮换到手机环境：{profiles[_currentProfileIndex].Name}");
        ApplyProfileAdb(profiles[_currentProfileIndex]);
        OnPhoneProfileSwitched(profiles[_currentProfileIndex].Name);
    }

    private int GetMaxLikesPerRun()
    {
        if (_settings.PhoneProfiles is { Count: > 0 } profiles && _currentProfileIndex >= 0 && _currentProfileIndex < profiles.Count)
        {
            var profileMax = profiles[_currentProfileIndex].MaxLikesPerRun;
            if (profileMax > 0)
            {
                return profileMax;
            }
        }

        return _settings.MaxLikesPerRun;
    }

    private TimeSpan GetLikeDelay()
    {
        var min = Math.Max(0.5, _settings.MinLikeDelaySeconds);
        var max = Math.Max(min, _settings.MaxLikeDelaySeconds);
        return TimeSpan.FromSeconds(min + _random.NextDouble() * (max - min));
    }

    private bool IsTitleAllowed(string title)
    {
        var include = _settings.TitleKeywords;
        var exclude = _settings.ExcludeKeywords;

        if (!string.IsNullOrWhiteSpace(include))
        {
            var includes = include.Split(new[] { ' ', ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (includes.Length > 0 && !includes.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(exclude))
        {
            var excludes = exclude.Split(new[] { ' ', ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (excludes.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> IsAuthorAllowedAsync(VideoInfo video, CancellationToken token)
    {
        // 没有粉丝/等级限制时无需请求用户卡片，性能更高。
        if (_settings.MinFans <= 0 && _settings.MaxFans <= 0 && _settings.MinLevel <= 0 && _settings.MaxLevel <= 0)
        {
            return true;
        }

        var author = await _client.GetAuthorAsync(video.AuthorMid, token).ConfigureAwait(false);
        if (author is null)
        {
            // 偶发风控/网络抖动，短暂等待后重试一次，尽量不误跳过。
            await Task.Delay(800 + _random.Next(700), token).ConfigureAwait(false);
            author = await _client.GetAuthorAsync(video.AuthorMid, token).ConfigureAwait(false);
        }

        if (author is null)
        {
            AddLog(LogLevel.Warning, $"无法获取 UP 主信息，已跳过：{video.Title}（UP：{video.AuthorName}，mid={video.AuthorMid}）");
            return false;
        }

        var authorName = string.IsNullOrWhiteSpace(author.Name) ? video.AuthorName : author.Name;
        if (_settings.MinFans > 0 && author.Fans < _settings.MinFans)
        {
            AddLog(LogLevel.Info, $"UP 主 {authorName} 粉丝数 {author.Fans} < {_settings.MinFans}，已跳过：{video.Title}");
            return false;
        }

        if (_settings.MaxFans > 0 && author.Fans > _settings.MaxFans)
        {
            AddLog(LogLevel.Info, $"UP 主 {authorName} 粉丝数 {author.Fans} > {_settings.MaxFans}，已跳过：{video.Title}");
            return false;
        }

        if (author.Level <= 0 && (_settings.MinLevel > 0 || _settings.MaxLevel > 0))
        {
            AddLog(LogLevel.Info, $"UP 主 {authorName} 等级未知，跳过等级筛选，按粉丝数继续：{video.Title}");
        }
        else if (_settings.MinLevel > 0 && author.Level < _settings.MinLevel)
        {
            AddLog(LogLevel.Info, $"UP 主 {authorName} 等级 {author.Level} < {_settings.MinLevel}，已跳过：{video.Title}");
            return false;
        }
        else if (_settings.MaxLevel > 0 && author.Level > _settings.MaxLevel)
        {
            AddLog(LogLevel.Info, $"UP 主 {authorName} 等级 {author.Level} > {_settings.MaxLevel}，已跳过：{video.Title}");
            return false;
        }

        if (author.Level > 0)
        {
            AddLog(LogLevel.Info, $"UP 主 {authorName} 等级 {author.Level}，通过筛选：{video.Title}");
        }

        return true;
    }

    private void AddLog(LogLevel level, string message)
    {
        LogEntryAdded?.Invoke(this, new LikeLogEntry(DateTime.Now, level, message));
    }
}
