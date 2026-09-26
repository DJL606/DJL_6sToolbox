using Microsoft.Win32;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>管理系统开机自启动（当前用户，静默托盘模式）。</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DJL_6sToolbox";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value &&
               value.Contains("DJL_6sToolbox", StringComparison.OrdinalIgnoreCase);
    }
}
