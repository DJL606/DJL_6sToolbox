namespace DJL_6sToolbox.Desktop.Models;

/// <summary>应用程序设置，使用 JSON 持久化到 %AppData%\DJL_6sToolbox\settings.json。</summary>
public sealed class AppSettings
{
    public bool AutoLikeEnabled { get; set; }
    public int IntervalMinutes { get; set; } = 10;
    public double ScanIntervalSeconds { get; set; } = 60.0;
    public int MaxPages { get; set; } = 3;
    public int MaxLikesPerRun { get; set; } = 50;
    public double MinLikeDelaySeconds { get; set; } = 2.0;
    public double MaxLikeDelaySeconds { get; set; } = 5.0;
    public int RiskControlCooldownMinutes { get; set; } = 30;
    public bool UseMobilePortraitLike { get; set; }
    public bool UseAppSignedLike { get; set; }
    public bool EnableLikeModeSwitch { get; set; } = true;
    public bool CaptchaEnabled { get; set; }
    public string AccessKey { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string BuvidFp { get; set; } = string.Empty;
    public string Buvid3 { get; set; } = string.Empty;
    public string Buvid { get; set; } = string.Empty;
    public string Buvid4 { get; set; } = string.Empty;
    public string Build { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string DeviceBrand { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string BiliTicket { get; set; } = string.Empty;
    public string BiliTicketV2 { get; set; } = string.Empty;
    public List<PhoneProfile> PhoneProfiles { get; set; } = new();
    public bool EnablePhoneRotation { get; set; }
    public int PhoneRotationMinutes { get; set; } = 30;
    public long MinFans { get; set; }
    public long MaxFans { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public string TitleKeywords { get; set; } = string.Empty;
    public string ExcludeKeywords { get; set; } = string.Empty;
    public string Cookie { get; set; } = string.Empty;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoCheckUpdate { get; set; } = true;
    public bool AutoStartOnBoot { get; set; }
    public bool PreventSleepWhileRunning { get; set; } = true;
    public string LastPhoneProfileName { get; set; } = string.Empty;
    public string LastAdbDeviceName { get; set; } = string.Empty;
    public bool EnableAdbBridge { get; set; }
    public bool AdbFollowPhoneSwitch { get; set; } = true;
    public string AdbSerial { get; set; } = string.Empty;
    public List<AdbDeviceProfile> AdbDevices { get; set; } = new();
}
