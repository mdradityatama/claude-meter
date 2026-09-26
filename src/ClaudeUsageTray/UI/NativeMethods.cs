using System.Runtime.InteropServices;

namespace ClaudeUsageTray.UI;

internal static class NativeMethods
{
    public const int SM_CXSMICON = 49;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();
}
