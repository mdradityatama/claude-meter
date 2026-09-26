using Microsoft.Win32;

namespace ClaudeUsageTray.UI;

internal sealed record ThemeColors(Color Background, Color Border, Color Separator, Color Text, Color SecondaryText, Color Track, Color Link);

/// <summary>Follows the Windows system (taskbar) theme, like the built-in tray flyouts.</summary>
internal static class SystemTheme
{
    private static readonly ThemeColors Dark = new(
        Background: Color.FromArgb(0x2B, 0x2B, 0x2B),
        Border: Color.FromArgb(0x45, 0x45, 0x45),
        Separator: Color.FromArgb(0x3D, 0x3D, 0x3D),
        Text: Color.FromArgb(0xFF, 0xFF, 0xFF),
        SecondaryText: Color.FromArgb(0xC5, 0xC5, 0xC5),
        Track: Color.FromArgb(0x45, 0x45, 0x45),
        Link: Color.FromArgb(0x60, 0xCD, 0xFF));

    private static readonly ThemeColors Light = new(
        Background: Color.FromArgb(0xF9, 0xF9, 0xF9),
        Border: Color.FromArgb(0xD5, 0xD5, 0xD5),
        Separator: Color.FromArgb(0xE5, 0xE5, 0xE5),
        Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
        SecondaryText: Color.FromArgb(0x5F, 0x5F, 0x5F),
        Track: Color.FromArgb(0xE0, 0xE0, 0xE0),
        Link: Color.FromArgb(0x00, 0x5F, 0xB8));

    public static ThemeColors Current
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int light && light != 0 ? Light : Dark;
        }
    }
}
