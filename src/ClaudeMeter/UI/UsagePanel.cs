using System.Drawing.Drawing2D;
using ClaudeMeter.Core;

namespace ClaudeMeter.UI;

/// <summary>
/// Borderless flyout shown while hovering the tray icon: 5-hour and 7-day usage with progress bars,
/// a status line, and Refresh / usage page links. Never takes focus from the active app.
/// </summary>
internal sealed class UsagePanel : Form
{
    private const int WS_EX_TOPMOST = 0x8;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int LogicalWidth = 300;
    private const int LogicalPadding = 16;

    private readonly LinkLabel _refreshLink;
    private readonly LinkLabel _usagePageLink;
    private ThemeColors _colors = SystemTheme.Current;
    private PanelFonts _fonts;
    private PanelModel? _model;

    public event EventHandler? RefreshRequested;
    public event EventHandler? UsagePageRequested;

    public UsagePanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;

        _fonts = new PanelFonts(DeviceDpi / 96f);
        _refreshLink = CreateLink("Refresh", () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _usagePageLink = CreateLink("Usage page ↗", () => UsagePageRequested?.Invoke(this, EventArgs.Empty));
        Controls.Add(_refreshLink);
        Controls.Add(_usagePageLink);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            // Topmost via the style (not the TopMost property, which would activate the window).
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowAt(Rectangle icon, PanelModel model)
    {
        _colors = SystemTheme.Current;
        ApplyTheme();
        SetModel(model);

        var workArea = Screen.FromRectangle(icon).WorkingArea;
        Location = PanelPlacement.Compute(icon, Size, workArea, Scale(8));
        if (!Visible)
            Show();
    }

    public void SetModel(PanelModel model)
    {
        _model = model;
        Size = new Size(Scale(LogicalWidth), LayOut(null));
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _fonts.Dispose();
        _fonts = new PanelFonts(DeviceDpi / 96f);
        _refreshLink.Font = _usagePageLink.Font = _fonts.Small;
        if (_model is not null)
            SetModel(_model);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        LayOut(e.Graphics);
    }

    /// <summary>
    /// Single pass used both to measure (g == null: positions the links, returns the height)
    /// and to paint, so layout and drawing cannot drift apart.
    /// </summary>
    private int LayOut(Graphics? g)
    {
        var pad = Scale(LogicalPadding);
        var width = Scale(LogicalWidth);
        var inner = width - 2 * pad;
        var y = pad;

        y += Write(g, "Claude usage", _fonts.Header, _colors.Text, pad, y) + Scale(12);
        Separator(g, y, width);
        y += Scale(14);

        if (_model is not null)
        {
            foreach (var row in new[] { _model.FiveHour, _model.SevenDay })
            {
                var labelHeight = Write(g, row.Label, _fonts.Label, _colors.Text, pad, y);
                WriteRight(g, row.Resets, _fonts.Small, _colors.SecondaryText, width - pad, y);
                y += labelHeight + Scale(4);

                var percentSize = Measure(row.Percent, _fonts.Big);
                Write(g, row.Percent, _fonts.Big, _colors.Text, pad, y);
                var usedSize = Measure("used", _fonts.Small);
                Write(g, "used", _fonts.Small, _colors.SecondaryText, pad + percentSize.Width + Scale(4), y + percentSize.Height - usedSize.Height - Scale(3));
                y += percentSize.Height + Scale(6);

                Bar(g, new Rectangle(pad, y, inner, Scale(4)), row.Fraction, Palette.ForLevel(row.Level).Background);
                y += Scale(4) + Scale(16);
            }
        }

        Separator(g, y - Scale(4), width);
        y += Scale(10);

        var statusHeight = Write(g, _model?.Status ?? "", _fonts.Small, _colors.SecondaryText, pad, y);
        if (g is null)
        {
            _usagePageLink.Location = new Point(width - pad - _usagePageLink.PreferredWidth, y);
            _refreshLink.Location = new Point(_usagePageLink.Left - Scale(12) - _refreshLink.PreferredWidth, y);
        }
        y += Math.Max(statusHeight, _refreshLink.PreferredHeight) + pad;

        return y;
    }

    private static Size Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);

    private static int Write(Graphics? g, string text, Font font, Color color, int x, int y)
    {
        if (g is not null)
            TextRenderer.DrawText(g, text, font, new Point(x, y), color, TextFormatFlags.NoPadding);
        return Measure(text, font).Height;
    }

    private static void WriteRight(Graphics? g, string text, Font font, Color color, int right, int y)
    {
        if (g is not null)
            TextRenderer.DrawText(g, text, font, new Point(right - Measure(text, font).Width, y), color, TextFormatFlags.NoPadding);
    }

    private void Separator(Graphics? g, int y, int width)
    {
        if (g is null)
            return;
        using var pen = new Pen(_colors.Separator);
        g.DrawLine(pen, 0, y, width, y);
    }

    private void Bar(Graphics? g, Rectangle track, double fraction, Color fill)
    {
        if (g is null)
            return;
        var radius = new Size(track.Height, track.Height);
        using var trackBrush = new SolidBrush(_colors.Track);
        g.FillRoundedRectangle(trackBrush, track, radius);

        if (fraction <= 0)
            return;
        var fillWidth = Math.Max(track.Height, (int)Math.Round(track.Width * fraction));
        using var fillBrush = new SolidBrush(fill);
        g.FillRoundedRectangle(fillBrush, new Rectangle(track.X, track.Y, fillWidth, track.Height), radius);
    }

    private void ApplyTheme()
    {
        BackColor = _colors.Background;
        foreach (var link in new[] { _refreshLink, _usagePageLink })
        {
            link.BackColor = _colors.Background;
            link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = _colors.Link;
        }

        var border = ColorTranslator.ToWin32(_colors.Border);
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    private LinkLabel CreateLink(string text, Action onClick)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Font = _fonts.Small,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            TabStop = false,
        };
        link.LinkClicked += (_, _) => onClick();
        return link;
    }

    private int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96f);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _fonts.Dispose();
        base.Dispose(disposing);
    }

    private sealed class PanelFonts(float scale) : IDisposable
    {
        public Font Header { get; } = new("Segoe UI Semibold", 14 * scale, GraphicsUnit.Pixel);
        public Font Label { get; } = new("Segoe UI Semibold", 13 * scale, GraphicsUnit.Pixel);
        public Font Big { get; } = new("Segoe UI", 22 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        public Font Small { get; } = new("Segoe UI", 12 * scale, GraphicsUnit.Pixel);

        public void Dispose()
        {
            Header.Dispose();
            Label.Dispose();
            Big.Dispose();
            Small.Dispose();
        }
    }
}
