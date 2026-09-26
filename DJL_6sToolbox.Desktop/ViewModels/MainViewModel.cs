using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using System.Windows.Threading;
using DJL_6sToolbox.Desktop.Models;
using DJL_6sToolbox.Desktop.Services;
using DJL_6sToolbox.Desktop.Views;

namespace DJL_6sToolbox.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const long MaxAppLogFileBytes = 20 * 1024 * 1024;

    private readonly SettingsStore _settingsStore = new();
    private readonly LikedAidStore _likedAidStore = new();
    private readonly UpdateService _updateService = new();
    private readonly BilibiliClient _client = new();
    private readonly LikeService _likeService;

    private AppSettings _settings;
    private NavItem? _selectedNav;
    private bool _isRunning;
    private string _statusText = "就绪";
    private string _loginStatus = "未登录";
    private string _loginDetail = "请先配置 Cookie";
    private string _updateText = string.Empty;
    private int _totalLiked;
    private string _lastRunText = "尚未运行";
    private string _currentPhoneName = "未设置";
    private string _searchKeyword = string.Empty;
    private int _searchCount = 20;
    private string _searchAction = "点赞";
    private string _commentText = string.Empty;
    private int _reportReason = 1;
    private string _reportDetail = string.Empty;
    private bool _isSearchRunning;
    private PhoneProfile? _selectedPhoneProfile;
    private AdbDeviceProfile? _selectedAdbDevice;
    private string _adbStatusText = "未启用";
    private int _adbStatusRefreshing;
    private readonly DispatcherTimer _adbStatusTimer;
    private System.Windows.Media.ImageSource? _captchaImageSource;
    private string _captchaProgressText = "等待验证码...";

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        if (_settings.PhoneProfiles is { Count: > 0 } phoneProfiles)
        {
            foreach (var profile in phoneProfiles)
            {
                PhoneProfiles.Add(profile);
            }
        }

        if (PhoneProfiles.Count == 0)
        {
            PhoneProfiles.Add(new PhoneProfile
            {
                Name = "默认手机环境",
                AccessKey = _settings.AccessKey,
                DeviceId = _settings.DeviceId,
                BuvidFp = _settings.BuvidFp,
                Buvid3 = _settings.Buvid3,
                Buvid = _settings.Buvid,
                Buvid4 = _settings.Buvid4,
                Build = _settings.Build,
                Channel = _settings.Channel,
                DeviceBrand = _settings.DeviceBrand,
                DeviceModel = _settings.DeviceModel,
                OsVersion = _settings.OsVersion,
                BiliTicket = _settings.BiliTicket,
                BiliTicketV2 = _settings.BiliTicketV2
            });
        }

        _selectedPhoneProfile = PhoneProfiles.FirstOrDefault(p => p.Name == _settings.LastPhoneProfileName) ?? PhoneProfiles.FirstOrDefault();
        if (_settings.AdbDevices is { Count: > 0 } adbDevices)
        {
            foreach (var device in adbDevices)
            {
                AdbDevices.Add(device);
            }
        }
        else if (!string.IsNullOrWhiteSpace(_settings.AdbSerial))
        {
            AdbDevices.Add(new AdbDeviceProfile
            {
                Name = "ADB 安卓设备",
                Serial = _settings.AdbSerial,
                Enabled = true
            });
        }

        _selectedAdbDevice = AdbDevices.FirstOrDefault(d => d.Name == _settings.LastAdbDeviceName) ?? AdbDevices.FirstOrDefault();
        _client.SetCookie(_settings.Cookie);
        _client.SetMobilePortraitMode(_settings.UseMobilePortraitLike);
        _client.SetAppSignedMode(_settings.UseAppSignedLike);
        _client.SetAccessKey(_settings.AccessKey);
        _client.SetDeviceTrust(_settings.DeviceId, _settings.BuvidFp, _settings.Buvid3, _settings.Buvid, _settings.Buvid4, _settings.Build, _settings.Channel, _settings.DeviceBrand, _settings.DeviceModel, _settings.OsVersion, _settings.BiliTicket, _settings.BiliTicketV2);
        if (SelectedPhoneProfile is { } savedProfile)
        {
            _client.SetPhoneProfile(savedProfile);
        }
        _client.SetAdbBridge(_settings.EnableAdbBridge, GetActiveAdbSerial());
        _client.ApplyAdbProxy(GetActiveAdbDevice()?.Proxy);
        _likeService = new LikeService(_client, _settings, _likedAidStore);
        _likeService.LogEntryAdded += OnLogEntryAdded;
        _likeService.PhoneProfileSwitched += OnPhoneProfileSwitched;
        _likeService.LikeMethodChanged += OnLikeMethodChanged;
        _likeService.AdbDeviceSwitched += OnAdbDeviceSwitched;

        _adbStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _adbStatusTimer.Tick += async (_, _) => await RefreshAdbStatusAsync();
        _adbStatusTimer.Start();
        _ = RefreshAdbStatusAsync();

        NavItems =
        [
            new NavItem("总览", "◈"),
            new NavItem("自动点赞", "♡"),
            new NavItem("原版功能", "⚙"),
            new NavItem("Cookie", "🔑"),
            new NavItem("关于更新", "⟳")
        ];

        _selectedNav = NavItems[0];
        _totalLiked = _likedAidStore.Count;

        StartStopCommand = new RelayCommand(_ => ToggleRunning());
        RunOnceCommand = new AsyncRelayCommand(_ => RunOnceAsync());
        SaveCookieCommand = new AsyncRelayCommand(_ => SaveCookieAsync());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        CheckUpdateCommand = new AsyncRelayCommand(_ => CheckUpdateAsync(showNoUpdate: true));
        OpenGitHubCommand = new RelayCommand(_ => OpenGitHub());
        RunSearchTaskCommand = new AsyncRelayCommand(_ => RunSearchTaskAsync());
        AutoFetchCookieCommand = new AsyncRelayCommand(_ => AutoFetchCookieAsync());
        AddPhoneProfileCommand = new RelayCommand(_ => AddPhoneProfile());
        RemovePhoneProfileCommand = new RelayCommand(_ => RemovePhoneProfile(), _ => SelectedPhoneProfile != null);
        ApplyPhoneProfileCommand = new RelayCommand(_ => ApplySelectedPhoneProfile(), _ => SelectedPhoneProfile != null);
        AddAdbDeviceCommand = new RelayCommand(_ => AddAdbDevice());
        RemoveAdbDeviceCommand = new RelayCommand(_ => RemoveAdbDevice(), _ => SelectedAdbDevice != null);
        SetLikeMethodWebCommand = new RelayCommand(_ => SetLikeMethodWeb());
        SetLikeMethodMobileCommand = new RelayCommand(_ => SetLikeMethodMobile());
        SetLikeMethodAppCommand = new RelayCommand(_ => SetLikeMethodApp());
        ApplyAdbDeviceCommand = new RelayCommand(_ => ApplySelectedAdbDevice(), _ => SelectedAdbDevice is { Serial.Length: > 0 });
    }

    public IReadOnlyList<string> SearchActions { get; } = ["点赞", "评论", "举报"];

    public IReadOnlyList<KeyValuePair<int, string>> ReportReasons { get; } =
    [
        new(1, "违法违禁"),
        new(2, "色情低俗"),
        new(3, "赌博诈骗"),
        new(4, "人身攻击"),
        new(5, "侵犯隐私"),
        new(6, "垃圾广告"),
        new(7, "引战"),
        new(8, "剧透"),
        new(9, "政治敏感"),
        new(10, "其他")
    ];

    public ObservableCollection<NavItem> NavItems { get; }

    public ObservableCollection<LikeLogEntry> Logs { get; } = [];
    public ObservableCollection<LikeLogEntry> CaptchaLogs { get; } = [];
    public ObservableCollection<PhoneProfile> PhoneProfiles { get; } = [];
    public ObservableCollection<AdbDeviceProfile> AdbDevices { get; } = [];

    public AdbDeviceProfile? SelectedAdbDevice
    {
        get => _selectedAdbDevice;
        set
        {
            if (SetProperty(ref _selectedAdbDevice, value))
            {
                OnPropertyChanged(nameof(CurrentAdbDisplay));
                _ = RefreshAdbStatusAsync();
            }
        }
    }

    public string CurrentAdbDisplay => SelectedAdbDevice is { } adbDevice ? $"当前 ADB 设备：{adbDevice.Name}" : "当前 ADB 设备：未选择";

    public string AdbStatusText
    {
        get => _adbStatusText;
        private set => SetProperty(ref _adbStatusText, value);
    }

    public System.Windows.Media.ImageSource? CaptchaImageSource
    {
        get => _captchaImageSource;
        private set => SetProperty(ref _captchaImageSource, value);
    }

    public string CaptchaProgressText
    {
        get => _captchaProgressText;
        private set => SetProperty(ref _captchaProgressText, value);
    }

    public PhoneProfile? SelectedPhoneProfile
    {
        get => _selectedPhoneProfile;
        set => SetProperty(ref _selectedPhoneProfile, value);
    }

    public AppSettings Settings => _settings;

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set => SetProperty(ref _selectedNav, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StartStopText));
                OnPropertyChanged(nameof(CanRunOnce));
                OnPropertyChanged(nameof(TopRightStatusText));
            }
        }
    }

    public string StartStopText => IsRunning ? "停止自动点赞" : "启动自动点赞";
    public string TopRightStatusText => IsRunning ? "运行中" : LastRunText;

    public string TrayStatusText
    {
        get
        {
            if (IsRunning)
            {
                return "运行中";
            }

            if (_likeService.IsCoolingDown)
            {
                return "冷却中";
            }

            return "已停止";
        }
    }

    public string CurrentPhoneName
    {
        get => _currentPhoneName;
        private set
        {
            if (SetProperty(ref _currentPhoneName, value))
            {
                OnPropertyChanged(nameof(CurrentPhoneDisplay));
            }
        }
    }

    public string CurrentPhoneDisplay => $"当前设备：{CurrentPhoneName}";
    public string CurrentLikeMethodText
    {
        get
        {
            if (_settings.UseAppSignedLike)
            {
                return "APP 签名";
            }

            return _settings.UseMobilePortraitLike ? "移动端竖屏" : "纯网页";
        }
    }
    public bool IsWebMethod => !_settings.UseMobilePortraitLike && !_settings.UseAppSignedLike;
    public bool IsMobileMethod => _settings.UseMobilePortraitLike && !_settings.UseAppSignedLike;
    public bool IsAppMethod => _settings.UseAppSignedLike;



    public bool CanRunOnce => !IsRunning;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LoginStatus
    {
        get => _loginStatus;
        private set => SetProperty(ref _loginStatus, value);
    }

    public string LoginDetail
    {
        get => _loginDetail;
        private set => SetProperty(ref _loginDetail, value);
    }

    public string UpdateText
    {
        get => _updateText;
        private set => SetProperty(ref _updateText, value);
    }

    public int TotalLiked
    {
        get => _totalLiked;
        private set => SetProperty(ref _totalLiked, value);
    }

    public string LastRunText
    {
        get => _lastRunText;
        private set
        {
            if (SetProperty(ref _lastRunText, value))
            {
                OnPropertyChanged(nameof(TopRightStatusText));
            }
        }
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set => SetProperty(ref _searchKeyword, value);
    }

    public int SearchCount
    {
        get => _searchCount;
        set => SetProperty(ref _searchCount, value);
    }

    public string SearchAction
    {
        get => _searchAction;
        set => SetProperty(ref _searchAction, value);
    }

    public string CommentText
    {
        get => _commentText;
        set => SetProperty(ref _commentText, value);
    }

    public int ReportReason
    {
        get => _reportReason;
        set => SetProperty(ref _reportReason, value);
    }

    public string ReportDetail
    {
        get => _reportDetail;
        set => SetProperty(ref _reportDetail, value);
    }

    public bool IsSearchRunning
    {
        get => _isSearchRunning;
        private set
        {
            if (SetProperty(ref _isSearchRunning, value))
            {
                OnPropertyChanged(nameof(CanRunSearch));
            }
        }
    }

    public bool CanRunSearch => !IsSearchRunning;

    public RelayCommand StartStopCommand { get; }

    public AsyncRelayCommand RunOnceCommand { get; }

    public AsyncRelayCommand SaveCookieCommand { get; }

    public RelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand CheckUpdateCommand { get; }

    public RelayCommand OpenGitHubCommand { get; }

    public AsyncRelayCommand RunSearchTaskCommand { get; }

    public AsyncRelayCommand AutoFetchCookieCommand { get; }
    public RelayCommand AddPhoneProfileCommand { get; }

    public RelayCommand RemovePhoneProfileCommand { get; }
    public RelayCommand ApplyPhoneProfileCommand { get; }
    public RelayCommand AddAdbDeviceCommand { get; }

    public RelayCommand RemoveAdbDeviceCommand { get; }
    public RelayCommand SetLikeMethodWebCommand { get; }
    public RelayCommand SetLikeMethodMobileCommand { get; }
    public RelayCommand SetLikeMethodAppCommand { get; }
    public RelayCommand ApplyAdbDeviceCommand { get; }

    public void StartAutoLike()
    {
        if (IsRunning)
        {
            return;
        }

        if (!_client.HasCookie)
        {
            ModernDialog.Show("请先在 Cookie 页粘贴有效的 B 站 Cookie。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartLikeService();
    }

    public void StopAutoLike()
    {
        if (IsRunning)
        {
            ToggleRunning();
        }
    }

    public void Initialize()
    {
        if (_settings.AutoLikeEnabled)
        {
            StartLikeService();
        }

        if (_settings.AutoCheckUpdate)
        {
            _ = CheckUpdateAsync(showNoUpdate: false);
        }

        _ = CheckLoginAsync();
    }

    public void Shutdown()
    {
        _adbStatusTimer.Stop();
        _likeService.Stop();
        _settings.PhoneProfiles = PhoneProfiles.ToList();
        _settings.AdbDevices = AdbDevices.ToList();
        _settings.LastPhoneProfileName = SelectedPhoneProfile?.Name ?? string.Empty;
        _settings.LastAdbDeviceName = SelectedAdbDevice?.Name ?? string.Empty;
        _settingsStore.Save(_settings);
        StartupService.SetEnabled(_settings.AutoStartOnBoot);
        if (!_settings.EnablePhoneRotation && SelectedPhoneProfile is { } selectedProfile)
        {
            _client.SetPhoneProfile(selectedProfile);
        }
        PowerHelper.SetKeepAwake(false);
        SetAllAdbDevicesKeepAwake(false);
        _client.Dispose();
    }

    private void SetAllAdbDevicesKeepAwake(bool enabled)
    {
        var serials = AdbDevices.Select(d => d.Serial)
            .Concat(PhoneProfiles.Select(p => p.AdbSerial))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var serial in serials)
        {
            _client.SetAdbDeviceKeepAwake(serial, enabled);
        }
    }

    private void ToggleRunning()
    {
        if (IsRunning)
        {
            _likeService.Stop();
            IsRunning = false;
            _settings.AutoLikeEnabled = false;
            PowerHelper.SetKeepAwake(false);
            SetAllAdbDevicesKeepAwake(false);
            SaveSettings();
            StatusText = "已停止";
            return;
        }

        if (!_client.HasCookie)
        {
            ModernDialog.Show("请先在 Cookie 页粘贴有效的 B 站 Cookie。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartLikeService();
    }

    private void StartLikeService()
    {
        _settings.AutoLikeEnabled = true;
        _likeService.Start();
        PowerHelper.SetKeepAwake(_settings.PreventSleepWhileRunning);
        SetAllAdbDevicesKeepAwake(_settings.PreventSleepWhileRunning);
        IsRunning = true;
        StatusText = "自动点赞运行中";
        SaveSettings();
    }

    private async Task RunOnceAsync()
    {
        if (!_client.HasCookie)
        {
            ModernDialog.Show("请先配置 Cookie。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText = "正在扫描全站最新视频...";
        await _likeService.RunOnceAsync();
        StatusText = "本轮扫描完成";
        LastRunText = $"上次运行：{DateTime.Now:HH:mm:ss}";
        TotalLiked = _likedAidStore.Count;
    }

    private async Task RunSearchTaskAsync()
    {
        if (!_client.HasCookie)
        {
            ModernDialog.Show("请先配置 Cookie。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            ModernDialog.Show("请输入搜索关键词。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SearchAction == "评论" && string.IsNullOrWhiteSpace(CommentText))
        {
            ModernDialog.Show("请填写评论内容（每行一条，随机选用）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsSearchRunning = true;
        StatusText = "正在执行原版搜索任务...";
        var random = new Random();
        var processed = 0;

        try
        {
            var videos = new List<VideoInfo>();
            var page = 1;
            while (videos.Count < Math.Max(1, SearchCount) && page <= 10)
            {
                var batch = await _client.SearchVideosAsync(SearchKeyword, page, 20);
                if (batch.Count == 0)
                {
                    break;
                }

                videos.AddRange(batch);
                page++;
                await Task.Delay(500 + random.Next(600));
            }

            videos = videos.Take(Math.Max(1, SearchCount)).ToList();
            AddLogEntry(new LikeLogEntry(DateTime.Now, LogLevel.Info, $"搜索到 {videos.Count} 个视频，开始执行：{SearchAction}"));

            foreach (var video in videos)
            {
                if (SearchAction == "点赞")
                {
                    var like = await _client.LikeVideoAsync(video.Aid);
                    AddLogEntry(new LikeLogEntry(DateTime.Now,
                        like.Success ? LogLevel.Success : LogLevel.Warning,
                        like.Success ? $"已点赞：{video.Title}" : $"点赞失败：{video.Title} [{like.Code}] {like.Message}"));
                    if (like.IsRiskControl)
                    {
                        AddLogEntry(new LikeLogEntry(DateTime.Now, LogLevel.Error, "检测到风控，已停止本次搜索任务。"));
                        break;
                    }
                }
                else if (SearchAction == "评论")
                {
                    var lines = CommentText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var message = lines[random.Next(lines.Length)];
                    var comment = await _client.CommentVideoAsync(video.Aid, message);
                    AddLogEntry(new LikeLogEntry(DateTime.Now,
                        comment.Success ? LogLevel.Success : LogLevel.Warning,
                        comment.Success ? $"已评论：{video.Title}" : $"评论失败：{video.Title} [{comment.Code}] {comment.Message}"));
                    if (comment.IsRiskControl)
                    {
                        AddLogEntry(new LikeLogEntry(DateTime.Now, LogLevel.Error, "检测到风控，已停止本次搜索任务。"));
                        break;
                    }
                }
                else if (SearchAction == "举报")
                {
                    var report = await _client.ReportVideoAsync(video.Aid, ReportReason, ReportDetail);
                    AddLogEntry(new LikeLogEntry(DateTime.Now,
                        report.Success ? LogLevel.Success : LogLevel.Warning,
                        report.Success ? $"已举报：{video.Title}" : $"举报失败：{video.Title} [{report.Code}] {report.Message}"));
                    if (report.IsRiskControl)
                    {
                        AddLogEntry(new LikeLogEntry(DateTime.Now, LogLevel.Error, "检测到风控，已停止本次搜索任务。"));
                        break;
                    }
                }

                processed++;
                await Task.Delay(TimeSpan.FromSeconds(2 + random.NextDouble() * 3));
            }

            StatusText = $"原版搜索任务完成，共处理 {processed} 个视频";
        }
        catch (Exception ex)
        {
            AddLogEntry(new LikeLogEntry(DateTime.Now, LogLevel.Error, $"搜索任务异常：{ex.Message}"));
            StatusText = "搜索任务异常";
        }
        finally
        {
            IsSearchRunning = false;
        }
    }

    private async Task AutoFetchCookieAsync()
    {
        StatusText = "正在从浏览器自动获取 B 站 Cookie...";
        var service = new BrowserCookieService();
        var cookie = await service.TryGetBilibiliCookieAsync();

        if (cookie is null || string.IsNullOrWhiteSpace(cookie.Sessdata))
        {
            StatusText = "自动获取失败";
            var choose = ModernDialog.Show(
                "未能从浏览器 Cookie 数据库中读取 B 站 Cookie。\n\n是否打开内置登录窗口，登录后自动获取 Cookie？",
                "自动获取 Cookie", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choose != MessageBoxResult.Yes)
            {
                return;
            }

            var loginWindow = new CookieLoginWindow
            {
                Owner = Application.Current?.MainWindow
            };

            if (loginWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(loginWindow.CookieString))
            {
                return;
            }

            await ApplyCookieStringAsync(loginWindow.CookieString, "内置登录窗口");
            return;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(cookie.Sessdata)) parts.Add($"SESSDATA={cookie.Sessdata}");
        if (!string.IsNullOrWhiteSpace(cookie.BiliJct)) parts.Add($"bili_jct={cookie.BiliJct}");
        if (!string.IsNullOrWhiteSpace(cookie.DedeUserId)) parts.Add($"DedeUserID={cookie.DedeUserId}");
        if (!string.IsNullOrWhiteSpace(cookie.DedeUserIdCkMd5)) parts.Add($"DedeUserID__ckMd5={cookie.DedeUserIdCkMd5}");
        if (!string.IsNullOrWhiteSpace(cookie.Sid)) parts.Add($"sid={cookie.Sid}");

        await ApplyCookieStringAsync(string.Join("; ", parts), cookie.BrowserName ?? "浏览器");
    }

    private async Task ApplyCookieStringAsync(string cookieString, string source)
    {
        _settings.Cookie = cookieString;
        OnPropertyChanged(nameof(Settings));
        _client.SetCookie(_settings.Cookie);
        _client.SetMobilePortraitMode(_settings.UseMobilePortraitLike);
        _client.SetAppSignedMode(_settings.UseAppSignedLike);
        ApplyClientPhoneProfileOrLegacy();
        _client.SetAdbBridge(_settings.EnableAdbBridge, GetActiveAdbSerial());
        _client.ApplyAdbProxy(GetActiveAdbDevice()?.Proxy);
        _settings.PhoneProfiles = PhoneProfiles.ToList();
        _settings.AdbDevices = AdbDevices.ToList();
        _settings.LastPhoneProfileName = SelectedPhoneProfile?.Name ?? string.Empty;
        _settings.LastAdbDeviceName = SelectedAdbDevice?.Name ?? string.Empty;
        _settingsStore.Save(_settings);
        StartupService.SetEnabled(_settings.AutoStartOnBoot);
        StatusText = $"已从 {source} 获取 Cookie，正在验证...";

        var login = await _client.CheckLoginAsync();
        if (login is { IsLogin: true })
        {
            LoginStatus = $"已登录：{login.UserName}";
            LoginDetail = $"UID {login.UserId} · Lv.{login.Level}";
            StatusText = "Cookie 获取并验证成功";
            ModernDialog.Show($"已从 {source} 获取 Cookie，验证成功：{login.UserName}",
                "获取成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            LoginStatus = "未登录";
            LoginDetail = "Cookie 无效或已过期";
            StatusText = "Cookie 已获取，但验证失败";
            ModernDialog.Show($"已获取 Cookie，但验证失败。\n\n{login?.Message ?? "未知错误"}",
                "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SaveCookieAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.Cookie))
        {
            ModernDialog.Show("Cookie 不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _client.SetCookie(_settings.Cookie);
        _client.SetMobilePortraitMode(_settings.UseMobilePortraitLike);
        _client.SetAppSignedMode(_settings.UseAppSignedLike);
        ApplyClientPhoneProfileOrLegacy();
        _client.SetAdbBridge(_settings.EnableAdbBridge, GetActiveAdbSerial());
        _client.ApplyAdbProxy(GetActiveAdbDevice()?.Proxy);
        _settings.PhoneProfiles = PhoneProfiles.ToList();
        _settings.AdbDevices = AdbDevices.ToList();
        _settings.LastPhoneProfileName = SelectedPhoneProfile?.Name ?? string.Empty;
        _settings.LastAdbDeviceName = SelectedAdbDevice?.Name ?? string.Empty;
        _settingsStore.Save(_settings);
        StartupService.SetEnabled(_settings.AutoStartOnBoot);
        StatusText = "正在验证 Cookie...";
        var login = await _client.CheckLoginAsync();
        if (login is { IsLogin: true })
        {
            LoginStatus = $"已登录：{login.UserName}";
            LoginDetail = $"UID {login.UserId} · Lv.{login.Level}";
            StatusText = "Cookie 验证成功";
            ModernDialog.Show($"Cookie 验证成功，当前账号：{login.UserName}", "验证成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            LoginStatus = "未登录";
            LoginDetail = "Cookie 无效或已过期";
            StatusText = "Cookie 验证失败";
            ModernDialog.Show($"Cookie 验证失败。\n\n{login?.Message ?? "未知错误"}",
                "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CheckLoginAsync()
    {
        LoginStatus = "正在验证...";
        var login = await _client.CheckLoginAsync();
        if (login is { IsLogin: true })
        {
            LoginStatus = $"已登录：{login.UserName}";
            LoginDetail = $"UID {login.UserId} · Lv.{login.Level}";
        }
        else
        {
            LoginStatus = "未登录";
            LoginDetail = login?.Message ?? "Cookie 无效或已过期";
        }
    }

    private void AddPhoneProfile()
    {
        var profile = new PhoneProfile { Name = $"手机环境 {PhoneProfiles.Count + 1}" };
        PhoneProfiles.Add(profile);
        SelectedPhoneProfile = profile;
        SaveSettings();
    }

    private void RemovePhoneProfile()
    {
        if (SelectedPhoneProfile is null)
        {
            return;
        }

        PhoneProfiles.Remove(SelectedPhoneProfile);
        SelectedPhoneProfile = PhoneProfiles.FirstOrDefault();
        SaveSettings();
    }

    private void ApplySelectedPhoneProfile()
    {
        if (SelectedPhoneProfile is { } profile)
        {
            _likeService.ApplyProfile(profile);
            SaveSettings();
        }
    }

    private AdbDeviceProfile? GetActiveAdbDevice()
    {
        if (_settings.AdbFollowPhoneSwitch && SelectedPhoneProfile is { } phone)
        {
            if (string.IsNullOrWhiteSpace(phone.AdbSerial))
            {
                return null;
            }

            return AdbDevices.FirstOrDefault(d => string.Equals(d.Serial, phone.AdbSerial, StringComparison.OrdinalIgnoreCase));
        }

        return SelectedAdbDevice ?? AdbDevices.FirstOrDefault();
    }

    private string? GetActiveAdbSerial()
    {
        if (_settings.AdbFollowPhoneSwitch && SelectedPhoneProfile is { } phone)
        {
            return string.IsNullOrWhiteSpace(phone.AdbSerial) ? null : phone.AdbSerial.Trim();
        }

        return SelectedAdbDevice?.Serial ?? AdbDevices.FirstOrDefault()?.Serial;
    }

    public async Task RefreshAdbStatusAsync()
    {
        if (Interlocked.CompareExchange(ref _adbStatusRefreshing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!_settings.EnableAdbBridge)
            {
                AdbStatusText = "ADB 状态：未启用";
                return;
            }

            var serial = GetActiveAdbSerial();
            if (string.IsNullOrWhiteSpace(serial))
            {
                AdbStatusText = "ADB 状态：未绑定设备";
                return;
            }

            AdbStatusText = "ADB 状态：检测中...";
            var online = await _client.IsAdbDeviceOnlineAsync(serial).ConfigureAwait(true);
            AdbStatusText = online ? "ADB 状态：在线" : "ADB 状态：离线";
        }
        catch
        {
            AdbStatusText = "ADB 状态：检测失败";
        }
        finally
        {
            Interlocked.Exchange(ref _adbStatusRefreshing, 0);
        }
    }

    public void SetCaptchaImage(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            CaptchaImageSource = null;
            return;
        }

        using var stream = new MemoryStream(imageBytes);
        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        CaptchaImageSource = bitmap;
    }

    public void SetCaptchaProgress(string text)
    {
        CaptchaProgressText = text;
    }

    private void ApplyClientPhoneProfileOrLegacy()
    {
        if (SelectedPhoneProfile is { } profile)
        {
            _client.SetPhoneProfile(profile);
        }
        else
        {
            _client.SetAccessKey(_settings.AccessKey);
            _client.SetDeviceTrust(_settings.DeviceId, _settings.BuvidFp, _settings.Buvid3, _settings.Buvid, _settings.Buvid4, _settings.Build, _settings.Channel, _settings.DeviceBrand, _settings.DeviceModel, _settings.OsVersion, _settings.BiliTicket, _settings.BiliTicketV2);
        }
    }

    private void ApplySelectedAdbDevice()
    {
        var device = SelectedAdbDevice;
        if (device is null || string.IsNullOrWhiteSpace(device.Serial))
        {
            return;
        }

        _settings.EnableAdbBridge = true;
        _settings.AdbFollowPhoneSwitch = false;
        _client.SetAdbBridge(true, device.Serial);
        _client.ApplyAdbProxy(device.Proxy);
        SaveSettings();
        OnPropertyChanged(nameof(Settings));
        StatusText = $"ADB 接口已切换到：{device.Name}（已关闭跟随切换）";
        _ = RefreshAdbStatusAsync();
    }

    private void AddAdbDevice()
    {
        var device = new AdbDeviceProfile { Name = $"ADB 安卓设备 {AdbDevices.Count + 1}" };
        AdbDevices.Add(device);
        SelectedAdbDevice = device;
        SaveSettings();
    }

    private void RemoveAdbDevice()
    {
        if (SelectedAdbDevice is null)
        {
            return;
        }

        AdbDevices.Remove(SelectedAdbDevice);
        SelectedAdbDevice = AdbDevices.FirstOrDefault();
        SaveSettings();
    }

    private void SetLikeMethodWeb()
    {
        _settings.UseMobilePortraitLike = false;
        _settings.UseAppSignedLike = false;
        ApplyLikeMethodChange();
    }

    private void SetLikeMethodMobile()
    {
        _settings.UseMobilePortraitLike = true;
        _settings.UseAppSignedLike = false;
        ApplyLikeMethodChange();
    }

    private void SetLikeMethodApp()
    {
        _settings.UseMobilePortraitLike = true;
        _settings.UseAppSignedLike = true;
        ApplyLikeMethodChange();
    }

    private void ApplyLikeMethodChange()
    {
        _client.SetMobilePortraitMode(_settings.UseMobilePortraitLike);
        _client.SetAppSignedMode(_settings.UseAppSignedLike);
        SaveSettings();
        OnPropertyChanged(nameof(CurrentLikeMethodText));
        OnPropertyChanged(nameof(IsWebMethod));
        OnPropertyChanged(nameof(IsMobileMethod));
        OnPropertyChanged(nameof(IsAppMethod));
        OnPropertyChanged(nameof(Settings));
    }

    private void SaveSettings()
    {
        _settings.PhoneProfiles = PhoneProfiles.ToList();
        _settings.AdbDevices = AdbDevices.ToList();
        if (_settings.AdbFollowPhoneSwitch)
        {
            SelectedAdbDevice = GetActiveAdbDevice();
        }

        _settings.LastPhoneProfileName = SelectedPhoneProfile?.Name ?? string.Empty;
        _settings.LastAdbDeviceName = SelectedAdbDevice?.Name ?? string.Empty;
        _settingsStore.Save(_settings);
        StartupService.SetEnabled(_settings.AutoStartOnBoot);
        _client.SetMobilePortraitMode(_settings.UseMobilePortraitLike);
        _client.SetAppSignedMode(_settings.UseAppSignedLike);
        ApplyClientPhoneProfileOrLegacy();
        _client.SetAdbBridge(_settings.EnableAdbBridge, GetActiveAdbSerial());
        _client.ApplyAdbProxy(GetActiveAdbDevice()?.Proxy);
        _ = RefreshAdbStatusAsync();
        StatusText = "设置已保存";
    }

    private async Task CheckUpdateAsync(bool showNoUpdate)
    {
        UpdateText = "正在检查更新...";
        var update = await _updateService.CheckAsync();
        if (update is null)
        {
            UpdateText = "当前已是最新版本";
            if (showNoUpdate)
            {
                ModernDialog.Show("当前已是最新版本。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        UpdateText = $"发现新版本：{update.LatestVersion}";
        var message = update.IsSourceUpdateOnly
            ? $"源码有可用更新（{update.LatestVersion}）。\n\n是否打开 GitHub 查看？"
            : $"发现新版本 {update.LatestVersion}，是否下载并更新？\n\n{update.ReleaseNotes}";

        var result = ModernDialog.Show(message, "发现更新",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            if (update.IsSourceUpdateOnly)
            {
                OpenGitHub();
                return;
            }

            var progress = new Progress<string>(p => UpdateText = p);
            var ok = await _updateService.InstallAsync(update, progress);
            if (!ok && update.DownloadUrl is not null)
            {
                OpenUrl(update.DownloadUrl);
            }
        }
    }

    private void OpenGitHub()
    {
        OpenUrl("https://github.com/greedisgoood/bilibili-tool");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // 忽略打开失败。
        }
    }

    private void OnAdbDeviceSwitched(object? sender, string name)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyAdbSwitchFeedback(name);
            return;
        }

        dispatcher.BeginInvoke(() => ApplyAdbSwitchFeedback(name));
    }

    private void ApplyAdbSwitchFeedback(string name)
    {
        if (name == "未绑定 ADB")
        {
            SelectedAdbDevice = null;
            OnPropertyChanged(nameof(CurrentAdbDisplay));
            _ = RefreshAdbStatusAsync();
            return;
        }

        var device = AdbDevices.FirstOrDefault(d => d.Name == name) ?? AdbDevices.FirstOrDefault(d => d.Serial == name);
        if (device != null)
        {
            SelectedAdbDevice = device;
        }
        else
        {
            OnPropertyChanged(nameof(CurrentAdbDisplay));
        }

        _ = RefreshAdbStatusAsync();
    }

    private void OnLikeMethodChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshLikeMethodUi();
            return;
        }

        dispatcher.BeginInvoke(RefreshLikeMethodUi);
    }

    private void RefreshLikeMethodUi()
    {
        OnPropertyChanged(nameof(CurrentLikeMethodText));
        OnPropertyChanged(nameof(IsWebMethod));
        OnPropertyChanged(nameof(IsMobileMethod));
        OnPropertyChanged(nameof(IsAppMethod));
        OnPropertyChanged(nameof(Settings));
    }

    private void OnPhoneProfileSwitched(object? sender, string name)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyPhoneSwitchFeedback(name);
            return;
        }

        dispatcher.BeginInvoke(() => ApplyPhoneSwitchFeedback(name));
    }

    private void ApplyPhoneSwitchFeedback(string name)
    {
        CurrentPhoneName = name;
        var profile = PhoneProfiles.FirstOrDefault(p => p.Name == name);
        if (profile != null)
        {
            SelectedPhoneProfile = profile;
        }

        _ = RefreshAdbStatusAsync();
    }

    private void OnLogEntryAdded(object? sender, LikeLogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AddLogEntry(entry);
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, () => AddLogEntry(entry));
    }

    private static readonly object LogFileLock = new();

    private static void AppendLogWithLimit(string file, string text)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Exists && info.Length > MaxAppLogFileBytes)
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

    private void AddLogEntry(LikeLogEntry entry)
    {
        Logs.Insert(0, entry);
        TrimLogs();

        try
        {
            AppPaths.EnsureCreated();
            var logFile = Path.Combine(AppPaths.LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
            lock (LogFileLock)
            {
                AppendLogWithLimit(logFile, $"[{entry.Time:HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 写日志失败不影响主流程。
        }

        if (entry.Level == LogLevel.Success)
        {
            TotalLiked = _likedAidStore.Count;
        }

        if (entry.Message.Contains("本轮完成", StringComparison.OrdinalIgnoreCase))
        {
            LastRunText = $"上次运行：{DateTime.Now:HH:mm:ss}";
        }
    }

    public void AddCaptchaLog(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LikeLogEntry(DateTime.Now, level, message);
        CaptchaLogs.Insert(0, entry);
        const int maxCaptchaLogs = 300;
        while (CaptchaLogs.Count > maxCaptchaLogs)
        {
            CaptchaLogs.RemoveAt(CaptchaLogs.Count - 1);
        }

        try
        {
            AppPaths.EnsureCreated();
            var logFile = Path.Combine(AppPaths.LogDirectory, $"captcha-{DateTime.Now:yyyyMMdd}.log");
            lock (LogFileLock)
            {
                AppendLogWithLimit(logFile, $"[{entry.Time:HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 写日志失败不影响主流程。
        }
    }

    private void TrimLogs()
    {
        const int maxLogs = 500;
        while (Logs.Count > maxLogs)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    public sealed record NavItem(string Title, string Icon);
}
