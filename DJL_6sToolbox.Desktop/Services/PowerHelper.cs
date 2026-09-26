using System.Runtime.InteropServices;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>防止系统在自动点赞运行时进入睡眠。</summary>
public static class PowerHelper
{
    [Flags]
    private enum ExecutionState : uint
    {
        ES_CONTINUOUS = 0x80000000,
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private static bool _isKeepingAwake;

    public static void SetKeepAwake(bool enabled)
    {
        if (enabled)
        {
            SetThreadExecutionState(ExecutionState.ES_CONTINUOUS | ExecutionState.ES_SYSTEM_REQUIRED);
            _isKeepingAwake = true;
        }
        else if (_isKeepingAwake)
        {
            SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
            _isKeepingAwake = false;
        }
    }
}
