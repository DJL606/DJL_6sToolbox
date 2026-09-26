namespace DJL_6sToolbox.Desktop.Models;

/// <summary>一台可通过 ADB 代发请求的安卓设备/虚拟机。</summary>
public sealed class AdbDeviceProfile
{
    public string Name { get; set; } = "ADB 安卓设备";
    public string Serial { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Proxy { get; set; } = string.Empty;
}
