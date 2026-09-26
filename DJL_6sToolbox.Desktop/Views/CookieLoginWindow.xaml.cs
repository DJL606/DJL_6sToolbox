using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Input;

namespace DJL_6sToolbox.Desktop.Views;

public partial class CookieLoginWindow : Window
{
    private static readonly HashSet<string> NeededCookies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5", "sid"
        };

    public CookieLoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
                WebView.CoreWebView2.CookieManager.DeleteAllCookies();
                WebView.Source = new Uri("https://passport.bilibili.com/login");
            }
            catch (Exception ex)
            {
                ModernDialog.Show($"无法启动内置浏览器：{ex.Message}\n\n请确认系统已安装 WebView2 Runtime（Edge 通常自带）。",
                    "DJL_6's 工具箱", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }

    public string? CookieString { get; private set; }

    private async void FetchCookie_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (WebView.CoreWebView2 is null)
            {
                await WebView.EnsureCoreWebView2Async();
            }

            var core = WebView.CoreWebView2;
            if (core is null)
            {
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var uri in new[] { "https://www.bilibili.com", "https://bilibili.com", "https://passport.bilibili.com" })
            {
                var cookies = await core.CookieManager.GetCookiesAsync(uri);
                foreach (var cookie in cookies)
                {
                    if (NeededCookies.Contains(cookie.Name) && !values.ContainsKey(cookie.Name))
                    {
                        values[cookie.Name] = cookie.Value;
                    }
                }
            }

            if (!values.TryGetValue("SESSDATA", out var sessdata) || string.IsNullOrWhiteSpace(sessdata))
            {
                ModernDialog.Show("没有读取到 SESSDATA。\n\n请确认已在窗口内完成 B 站登录，再点击“获取 Cookie”。",
                    "DJL_6's 工具箱", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var parts = new List<string>();
            if (values.TryGetValue("SESSDATA", out var s)) parts.Add($"SESSDATA={s}");
            if (values.TryGetValue("bili_jct", out var j)) parts.Add($"bili_jct={j}");
            if (values.TryGetValue("DedeUserID", out var u)) parts.Add($"DedeUserID={u}");
            if (values.TryGetValue("DedeUserID__ckMd5", out var m)) parts.Add($"DedeUserID__ckMd5={m}");
            if (values.TryGetValue("sid", out var sid)) parts.Add($"sid={sid}");

            CookieString = string.Join("; ", parts);
            ModernDialog.Show("已成功获取 B 站 Cookie，窗口即将关闭。", "获取成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ModernDialog.Show($"读取 Cookie 失败：{ex.Message}", "DJL_6's 工具箱",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
