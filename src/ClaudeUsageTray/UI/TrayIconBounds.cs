using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeUsageTray.UI;

/// <summary>Screen rectangle of a NotifyIcon, which WinForms does not expose.</summary>
internal static class TrayIconBounds
{
    // Shell_NotifyIconGetRect needs the icon's owner window and id, both private in NotifyIcon.
    private static readonly FieldInfo? WindowField =
        typeof(NotifyIcon).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? IdField =
        typeof(NotifyIcon).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <param name="fallbackCenter">Used if the shell call is unavailable: a small square around this point.</param>
    public static Rectangle Get(NotifyIcon notifyIcon, Point fallbackCenter)
    {
        if (WindowField?.GetValue(notifyIcon) is NativeWindow window && IdField?.GetValue(notifyIcon) is uint id)
        {
            var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
                hWnd = window.Handle,
                uID = id,
            };
            if (NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var r) == 0)
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, NativeMethods.GetDpiForSystem()) * 2;
        return new Rectangle(fallbackCenter.X - size / 2, fallbackCenter.Y - size / 2, size, size);
    }
}
