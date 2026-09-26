using ClaudeUsageTray.Core;

namespace ClaudeUsageTray.UI;

/// <summary>Level colors shared by the tray icon and the panel's progress bars.</summary>
internal static class Palette
{
    public static (Color Background, Color Foreground) ForLevel(UsageLevel level) => level switch
    {
        UsageLevel.Green => (Color.FromArgb(0x2E, 0x7D, 0x32), Color.White),
        UsageLevel.Yellow => (Color.FromArgb(0xF9, 0xA8, 0x25), Color.Black),
        UsageLevel.Red => (Color.FromArgb(0xC6, 0x28, 0x28), Color.White),
        UsageLevel.Error => (Color.FromArgb(0x42, 0x42, 0x42), Color.FromArgb(0xFF, 0xB3, 0x00)),
        _ => (Color.FromArgb(0x75, 0x75, 0x75), Color.White),
    };
}
