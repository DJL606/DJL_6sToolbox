using System.IO;
namespace DJL_6sToolbox.Desktop.Services;

/// <summary>集中管理应用数据目录，避免路径散落各处。</summary>
public static class AppPaths
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DJL_6sToolbox");

    public static string DataDirectory => Root;

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LikedAidsFile => Path.Combine(Root, "liked_aids.json");

    public static string LogDirectory => Path.Combine(Root, "logs");

    public static string UpdateDirectory => Path.Combine(Root, "updates");

    public static string TessDataDirectory => Path.Combine(Root, "tessdata");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(UpdateDirectory);
        Directory.CreateDirectory(TessDataDirectory);
    }
}
