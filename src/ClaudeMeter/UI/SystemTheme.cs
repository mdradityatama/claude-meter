using Microsoft.Win32;

namespace ClaudeMeter.UI;

/// <summary>Follows the Windows system (taskbar) theme and accent color, like the built-in tray flyouts.</summary>
internal static class SystemTheme
{
    // AccentPalette holds Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3 (RGBA each). WinUI uses
    // Light2 for accent text on dark and Dark1 on light.
    private const int DarkAccentIndex = 1;
    private const int LightAccentIndex = 4;

    public static FluentTheme Current
    {
        get
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var dark = personalize?.GetValue("SystemUsesLightTheme") is not int light || light == 0;

            using var accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            Color? accentColor = accent?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 28
                ? FromPalette(palette, dark ? DarkAccentIndex : LightAccentIndex)
                : null;

            return FluentTheme.Create(dark, accentColor);
        }
    }

    private static Color FromPalette(byte[] palette, int index) =>
        Color.FromArgb(palette[index * 4], palette[index * 4 + 1], palette[index * 4 + 2]);
}
