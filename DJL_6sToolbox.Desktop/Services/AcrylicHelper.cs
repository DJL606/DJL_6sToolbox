using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>
/// 为窗口启用 Windows 10/11 原生亚克力/毛玻璃效果。
/// 使用 SetWindowCompositionAttribute，不依赖第三方库。
/// </summary>
public static class AcrylicHelper
{
    private enum AccentState
    {
        Disabled = 0,
        EnableGradient = 1,
        EnableTransparentGradient = 2,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
        EnableHostBackdrop = 5
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint dwFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fEnable;
        public IntPtr hRgnBlur;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fTransitionOnMaximized;
    }

    /// <summary>
    /// 在窗口句柄可用后调用。
    /// </summary>
    /// <param name="window">目标 WPF 窗口。</param>
    /// <param name="tintColor">ARGB 着色，例如 0x800F1115。</param>
    public static void EnableAcrylic(Window window, uint tintColor = 0x800F1115)
    {
        if (window is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) => Apply(new WindowInteropHelper(window).Handle, tintColor);
            return;
        }

        Apply(handle, tintColor);
    }

    private static void Apply(IntPtr hwnd, uint tintColor)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var accent = new AccentPolicy
        {
            AccentState = AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = tintColor,
            AnimationId = 0
        };

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
            SizeOfData = Marshal.SizeOf<AccentPolicy>()
        };

        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            var ok = SetWindowCompositionAttribute(hwnd, ref data);
            if (ok == 0)
            {
                var blur = new DwmBlurBehind
                {
                    dwFlags = 0x1,
                    fEnable = true,
                    hRgnBlur = IntPtr.Zero,
                    fTransitionOnMaximized = true
                };
                _ = DwmEnableBlurBehindWindow(hwnd, ref blur);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
    }
}
