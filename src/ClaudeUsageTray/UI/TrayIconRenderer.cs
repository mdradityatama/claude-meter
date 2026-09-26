using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ClaudeUsageTray.Core;

namespace ClaudeUsageTray.UI;

/// <summary>Draws the icon face (number, "F", "!" or "—" on a colored square) at the current DPI.</summary>
internal static class TrayIconRenderer
{
    /// <returns>An icon that owns its handle; the caller disposes it.</returns>
    public static Icon Render(IconFace face)
    {
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, NativeMethods.GetDpiForSystem());
        if (size <= 0)
            size = 16;

        var (background, foreground) = Palette.ForLevel(face.Level);

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var backgroundBrush = new SolidBrush(background);
            var radius = Math.Max(2, size / 5);
            g.FillRoundedRectangle(backgroundBrush, new Rectangle(0, 0, size, size), new Size(radius, radius));

            using var font = FitFont(g, face.Text, size);
            var format = StringFormat.GenericTypographic;
            var textSize = g.MeasureString(face.Text, font, PointF.Empty, format);
            using var textBrush = new SolidBrush(foreground);
            g.DrawString(face.Text, font, textBrush, (size - textSize.Width) / 2, (size - textSize.Height) / 2, format);
        }

        // GetHicon creates an unmanaged icon; clone it into an Icon that owns its own handle,
        // then destroy the original so no GDI handle leaks.
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static Font FitFont(Graphics g, string text, int size)
    {
        for (var px = size * 0.8f; px > 4; px -= 0.5f)
        {
            var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            if (measured.Width <= size && measured.Height <= size)
                return font;
            font.Dispose();
        }

        return new Font("Segoe UI", 4, FontStyle.Bold, GraphicsUnit.Pixel);
    }
}
