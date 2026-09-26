namespace DJL_6sToolbox.Desktop.Models;

/// <summary>一套完整的手机端模拟环境参数。</summary>
public sealed class PhoneProfile
{
    public string Name { get; set; } = "新手机环境";
    public int MaxLikesPerRun { get; set; } = 50;
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
    public string AdbSerial { get; set; } = string.Empty;
}
