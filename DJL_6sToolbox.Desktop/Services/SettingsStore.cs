using System.IO;
using System.Text.Json;
using DJL_6sToolbox.Desktop.Models;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>线程安全的 JSON 设置读写。</summary>
public sealed class SettingsStore
{
    private readonly object _lock = new();
    private readonly string _file = AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_file))
                {
                    var json = File.ReadAllText(_file);
                    return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
            }
            catch
            {
                // 配置损坏时回退默认值，不让程序启动失败。
            }

            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_lock)
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(_file, JsonSerializer.Serialize(settings, JsonOptions));
        }
    }
}
