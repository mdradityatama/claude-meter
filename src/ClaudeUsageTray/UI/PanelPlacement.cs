namespace ClaudeUsageTray.UI;

internal static class PanelPlacement
{
    /// <summary>
    /// Centers the panel on the tray icon, on the work-area side of the taskbar (above a bottom
    /// taskbar, below a top one), and keeps it fully inside the work area.
    /// </summary>
    public static Point Compute(Rectangle icon, Size panel, Rectangle workArea, int gap)
    {
        var x = icon.Left + icon.Width / 2 - panel.Width / 2;
        x = Math.Clamp(x, workArea.Left + gap, workArea.Right - panel.Width - gap);

        var iconBelowCenter = icon.Top + icon.Height / 2 > workArea.Top + workArea.Height / 2;
        var y = iconBelowCenter
            ? Math.Min(icon.Top, workArea.Bottom) - gap - panel.Height
            : Math.Max(icon.Bottom, workArea.Top) + gap;
        y = Math.Clamp(y, workArea.Top + gap, workArea.Bottom - panel.Height - gap);

        return new Point(x, y);
    }
}
