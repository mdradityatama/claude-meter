using ClaudeMeter.Core;

namespace ClaudeMeter.UI;

/// <summary>
/// Windows 11 Fluent colors for the panel. The spec's semi-transparent tokens are blended onto the solid
/// panel color once, so painting needs no alpha and text can use ClearType.
/// </summary>
internal sealed record FluentTheme(
    bool Dark,
    Color Background,
    Color Border,
    Color Text,
    Color Text2,
    Color Text3,
    Color RowFill,
    Color RowActive,
    Color Badge,
    Color Track,
    Color ButtonFill,
    Color ButtonHover,
    Color ButtonPressed,
    Color ButtonBorder,
    Color SubtleHover,
    Color SubtlePressed,
    Color Ok,
    Color Caution,
    Color Critical,
    Color CriticalTint,
    Color Accent,
    Color OnAccent,
    Color ToggleOff)
{
    private static readonly Color DarkAccent = Color.FromArgb(0x60, 0xCD, 0xFF);
    private static readonly Color LightAccent = Color.FromArgb(0x00, 0x5F, 0xB8);

    public static FluentTheme Create(bool dark, Color? systemAccent)
    {
        var white = Color.White;
        var black = Color.Black;
        if (dark)
        {
            var bg = Color.FromArgb(0x26, 0x26, 0x28);
            var buttonFill = Mix(white, 0.07, bg);
            return new FluentTheme(
                Dark: true,
                Background: bg,
                Border: Mix(white, 0.09, bg),
                Text: white,
                Text2: Mix(white, 0.79, bg),
                Text3: Mix(white, 0.58, bg),
                RowFill: Mix(white, 0.035, bg),
                RowActive: Mix(white, 0.085, bg),
                Badge: Mix(white, 0.09, bg),
                Track: Mix(white, 0.14, bg),
                ButtonFill: buttonFill,
                ButtonHover: Mix(white, 0.04, buttonFill),
                ButtonPressed: Mix(black, 0.06, buttonFill),
                ButtonBorder: Mix(white, 0.10, bg),
                SubtleHover: Mix(white, 0.06, bg),
                SubtlePressed: Mix(white, 0.04, bg),
                Ok: Color.FromArgb(0x6C, 0xCB, 0x5F),
                Caution: Color.FromArgb(0xFC, 0xE1, 0x00),
                Critical: Color.FromArgb(0xFF, 0x99, 0xA4),
                CriticalTint: Mix(Color.FromArgb(0xFF, 0x99, 0xA4), 0.10, bg),
                Accent: systemAccent ?? DarkAccent,
                OnAccent: black,
                ToggleOff: Mix(white, 0.62, bg));
        }
        else
        {
            var bg = Color.FromArgb(0xF3, 0xF3, 0xF3);
            var buttonFill = Mix(white, 0.80, bg);
            return new FluentTheme(
                Dark: false,
                Background: bg,
                Border: Mix(black, 0.08, bg),
                Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
                Text2: Mix(black, 0.64, bg),
                Text3: Mix(black, 0.58, bg),
                RowFill: Mix(white, 0.50, bg),
                RowActive: Mix(white, 0.92, bg),
                Badge: Mix(black, 0.06, bg),
                Track: Mix(black, 0.10, bg),
                ButtonFill: buttonFill,
                ButtonHover: Mix(black, 0.03, buttonFill),
                ButtonPressed: Mix(black, 0.06, buttonFill),
                ButtonBorder: Mix(black, 0.10, bg),
                SubtleHover: Mix(black, 0.04, bg),
                SubtlePressed: Mix(black, 0.02, bg),
                Ok: Color.FromArgb(0x0F, 0x7B, 0x0F),
                Caution: Color.FromArgb(0x9D, 0x5D, 0x00),
                Critical: Color.FromArgb(0xC4, 0x2B, 0x1C),
                CriticalTint: Mix(Color.FromArgb(0xC4, 0x2B, 0x1C), 0.07, bg),
                Accent: systemAccent ?? LightAccent,
                OnAccent: white,
                ToggleOff: Mix(black, 0.60, bg));
        }
    }

    /// <summary>Bar fill: under 50 OK, 50–79 caution, 80 and above critical.</summary>
    public Color ForLevel(UsageLevel level) => level switch
    {
        UsageLevel.Green => Ok,
        UsageLevel.Yellow => Caution,
        UsageLevel.Red => Critical,
        _ => Track,
    };

    public Color For(Tone tone) => tone switch
    {
        Tone.Caution => Caution,
        Tone.Critical => Critical,
        _ => Text3,
    };

    /// <summary>
    /// <see cref="SubtleHover"/> or <see cref="SubtlePressed"/> over <paramref name="under"/> instead of the panel
    /// color, for controls drawn on a row fill.
    /// </summary>
    public Color SubtleOver(Color under, bool pressed) => Dark
        ? Mix(Color.White, pressed ? 0.04 : 0.06, under)
        : Mix(Color.Black, pressed ? 0.02 : 0.04, under);

    /// <summary><paramref name="color"/> at <paramref name="alpha"/> opacity over <paramref name="background"/>.</summary>
    public static Color Mix(Color color, double alpha, Color background) => Color.FromArgb(
        (int)Math.Round(background.R + (color.R - background.R) * alpha),
        (int)Math.Round(background.G + (color.G - background.G) * alpha),
        (int)Math.Round(background.B + (color.B - background.B) * alpha));
}
