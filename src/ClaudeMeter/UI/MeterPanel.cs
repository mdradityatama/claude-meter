using System.Drawing.Drawing2D;
using System.Globalization;
using ClaudeMeter.Core;

namespace ClaudeMeter.UI;

internal sealed record MeterPanelModel(
    IReadOnlyList<AccountRowModel> Rows,
    IReadOnlyList<SettingsRowModel> SettingsRows,
    bool Pinned,
    bool Refreshing,
    bool KeepInactiveLive,
    bool StartWithWindows,
    bool ShowResetDate,
    string Version);

internal abstract record PanelAction
{
    private PanelAction() { }

    public sealed record Refresh : PanelAction;
    public sealed record TogglePin : PanelAction;
    /// <summary>The settings view opened; a hover panel becomes pinned.</summary>
    public sealed record SettingsOpened : PanelAction;
    public sealed record Switch(AccountKey Key) : PanelAction;
    public sealed record Remove(AccountKey Key) : PanelAction;
    public sealed record ToggleKeepLive : PanelAction;
    public sealed record ToggleStartWithWindows : PanelAction;
    /// <summary>Switches every meter between the time left and the reset date.</summary>
    public sealed record ToggleResetDisplay : PanelAction;
    public sealed record OpenUsagePage : PanelAction;
    public sealed record Exit : PanelAction;
}

/// <summary>
/// The tray flyout: every account with usage bars, plus a settings view. One window, fully owner-drawn in a
/// single pass with its own hit-testing; it never takes focus. Repaints only on data (including the tray's update
/// when a countdown ticks), theme, DPI or hover changes, plus the refresh button's progress ring while a refresh runs.
/// </summary>
internal sealed class MeterPanel : Form
{
    private const int WS_EX_TOPMOST = 0x8;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int CS_DROPSHADOW = 0x20000;

    private const int LogicalWidth = 360;
    private const int HeaderHeight = 48;
    private const int Gap = 8;

    private const string GlyphRefresh = "";
    private const string GlyphSettings = "";
    private const string GlyphPin = "";
    private const string GlyphPinned = "";
    private const string GlyphBack = "";
    private const string GlyphWarning = "";
    private const string GlyphInfo = "";
    private const string GlyphOpen = "";

    private const string AddHint =
        "To add an account, run /login in Claude Code with it. Claude Meter saves it automatically. Don't run /logout first: it can revoke the saved login.";

    private readonly List<(Rectangle Bounds, Hit Hit)> _hits = [];
    private readonly Dictionary<(string, Font), Size> _measured = [];
    private readonly Dictionary<Color, SolidBrush> _brushes = [];
    private readonly Dictionary<Color, Pen> _pens = [];
    // Runs only while a refresh is in progress and the ring is on screen; each tick repaints the ring alone.
    private readonly System.Windows.Forms.Timer _spinTimer = new() { Interval = 16 };
    private FluentTheme _theme = SystemTheme.Current;
    private PanelFonts _fonts;
    private MeterPanelModel? _model;
    private Rectangle? _icon;
    private bool _settings;
    private AccountKey? _confirming;
    private Hit _hover;
    private Hit _pressed;
    private int _scroll;
    private int _contentHeight;
    private Rectangle _refreshButton;
    private long _spinStartedAt;
    // One width for both views, so the meters keep their length on a switch; 0 until measured for the current DPI.
    private int _resetColumnWidth;

    public MeterPanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = _theme.Background;
        _fonts = new PanelFonts(DeviceDpi / 96f);
        _spinTimer.Tick += (_, _) => Invalidate(_refreshButton);
    }

    public event EventHandler<PanelAction>? ActionRequested;

    public bool InSettings => _settings;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            // Topmost via the style (not the TopMost property, which would activate the window).
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    public void ShowAt(Rectangle icon)
    {
        _icon = icon;
        Relayout();
        if (!Visible)
            Show();
    }

    public void SetModel(MeterPanelModel model)
    {
        _model = model;
        if (_confirming is { } key && !model.SettingsRows.Any(r => r.Key == key))
            _confirming = null;
        Relayout();
    }

    /// <summary>Back to the accounts view; used when the panel closes.</summary>
    public void ResetView()
    {
        _settings = false;
        _confirming = null;
        _scroll = 0;
    }

    public void ApplyTheme(FluentTheme theme)
    {
        _theme = theme;
        BackColor = theme.Background;
        ClearPaintCache();
        if (IsHandleCreated)
            ApplyWindowAttributes();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowAttributes();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateSpinTimer();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _fonts.Dispose();
        _fonts = new PanelFonts(DeviceDpi / 96f);
        _measured.Clear();
        _resetColumnWidth = 0;
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        LayOut(e.Graphics);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHover(HitAt(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _pressed = default;
        SetHover(default);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        _pressed = HitAt(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _pressed;
        _pressed = default;
        Invalidate();
        var hit = HitAt(e.Location);
        if (e.Button == MouseButtons.Left && hit.Kind != HitKind.None && hit == pressed)
            Activate(hit);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var max = Math.Max(0, _contentHeight - ClientSize.Height);
        var scroll = Math.Clamp(_scroll - e.Delta * Scale(48) / 120, 0, max);
        if (scroll == _scroll)
            return;
        _scroll = scroll;
        Invalidate();
    }

    private void Activate(Hit hit)
    {
        switch (hit.Kind)
        {
            case HitKind.Settings:
                _settings = true;
                _scroll = 0;
                Relayout();
                Raise(new PanelAction.SettingsOpened());
                break;
            case HitKind.Back:
                ResetView();
                Relayout();
                break;
            case HitKind.Remove:
                _confirming = hit.Key;
                Relayout();
                break;
            case HitKind.CancelRemove:
                _confirming = null;
                Relayout();
                break;
            case HitKind.ConfirmRemove:
                _confirming = null;
                Raise(new PanelAction.Remove(hit.Key!.Value));
                break;
            case HitKind.Refresh:
                Raise(new PanelAction.Refresh());
                break;
            case HitKind.Pin:
                Raise(new PanelAction.TogglePin());
                break;
            case HitKind.Switch:
                Raise(new PanelAction.Switch(hit.Key!.Value));
                break;
            case HitKind.KeepLive:
                Raise(new PanelAction.ToggleKeepLive());
                break;
            case HitKind.Startup:
                Raise(new PanelAction.ToggleStartWithWindows());
                break;
            case HitKind.ResetDisplay:
                Raise(new PanelAction.ToggleResetDisplay());
                break;
            case HitKind.UsagePage:
                Raise(new PanelAction.OpenUsagePage());
                break;
            case HitKind.Exit:
                Raise(new PanelAction.Exit());
                break;
        }
    }

    private void Raise(PanelAction action) => ActionRequested?.Invoke(this, action);

    private void SetHover(Hit hit)
    {
        if (hit == _hover)
            return;
        _hover = hit;
        Invalidate();
    }

    private Hit HitAt(Point point)
    {
        foreach (var (bounds, hit) in _hits)
        {
            if (bounds.Contains(point))
                return hit;
        }

        return default;
    }

    /// <summary>Measures, resizes, keeps the panel anchored to the icon, and repaints.</summary>
    private void Relayout()
    {
        _contentHeight = LayOut(null);
        var height = _contentHeight;
        if (_icon is { } icon)
        {
            var workArea = Screen.FromRectangle(icon).WorkingArea;
            height = Math.Min(height, workArea.Height - 2 * Scale(Gap));
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _contentHeight - height));
            Size = new Size(Scale(LogicalWidth), height);
            Location = PanelPlacement.Compute(icon, Size, workArea, Scale(Gap));
        }
        else
        {
            Size = new Size(Scale(LogicalWidth), height);
        }

        UpdateSpinTimer();
        Invalidate();
    }

    private bool ShowsSpinner => Visible && !_settings && _model?.Refreshing == true;

    private void UpdateSpinTimer()
    {
        if (ShowsSpinner == _spinTimer.Enabled)
            return;
        if (ShowsSpinner)
        {
            _spinStartedAt = Environment.TickCount64;
            _spinTimer.Start();
        }
        else
        {
            _spinTimer.Stop();
        }
    }

    /// <summary>
    /// Single pass used both to measure (g == null) and to paint, so layout and drawing cannot drift apart.
    /// The body scrolls under a fixed header. Returns the full content height.
    /// </summary>
    private int LayOut(Graphics? g)
    {
        _hits.Clear();
        var width = Scale(LogicalWidth);
        var header = Scale(HeaderHeight);
        int body;
        if (g is null)
        {
            body = Body(null, header - _scroll, width);
        }
        else
        {
            var state = g.Save();
            g.SetClip(new Rectangle(0, header, width, Math.Max(0, ClientSize.Height - header)));
            body = Body(g, header - _scroll, width);
            g.Restore(state);
        }

        Header(g, width);
        return header + body;
    }

    private int Body(Graphics? g, int top, int width) => _settings ? SettingsBody(g, top, width) : MainBody(g, top, width);

    private void Header(Graphics? g, int width)
    {
        var button = Scale(32);
        var top = Scale(8) + (Scale(36) - button) / 2;
        if (_settings)
        {
            var back = new Rectangle(Scale(8), top, button, button);
            IconButton(g, back, GlyphBack, new Hit(HitKind.Back), _theme.Text2, filled: false);
            Write(g, "Settings", _fonts.Title, _theme.Text, new Rectangle(back.Right + Scale(4), top, width - back.Right - Scale(16), button), TextFormatFlags.VerticalCenter);
            return;
        }

        var pin = new Rectangle(width - Scale(8) - button, top, button, button);
        var settings = pin with { X = pin.X - Scale(2) - button };
        var refresh = settings with { X = settings.X - Scale(2) - button };
        var pinned = _model?.Pinned == true;
        _refreshButton = refresh;
        Write(g, "Claude Meter", _fonts.Title, _theme.Text, new Rectangle(Scale(16), top, refresh.X - Scale(16), button), TextFormatFlags.VerticalCenter);
        // While refreshing, the button shows a progress ring and takes no clicks.
        if (_model?.Refreshing == true)
            ProgressRing(g, refresh);
        else
            IconButton(g, refresh, GlyphRefresh, new Hit(HitKind.Refresh), _theme.Text2, filled: false);
        IconButton(g, settings, GlyphSettings, new Hit(HitKind.Settings), _theme.Text2, filled: false);
        IconButton(g, pin, pinned ? GlyphPinned : GlyphPin, new Hit(HitKind.Pin), pinned ? _theme.Accent : _theme.Text2, filled: pinned);
    }

    private int MainBody(Graphics? g, int top, int width)
    {
        var x = Scale(8);
        var w = width - Scale(16);
        var y = top + Scale(4);
        var rows = _model?.Rows ?? [];
        if (rows.Count == 0)
            y += Wrapped(g, "No Claude Code login found yet. Run /login in Claude Code.", _fonts.Small, _theme.Text3, new Rectangle(x + Scale(8), y + Scale(8), w - Scale(16), 0)) + Scale(16);

        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                y += Scale(4);
            y += AccountRow(g, rows[i], x, y, w);
        }

        return y + Scale(8) - top;
    }

    private int AccountRow(Graphics? g, AccountRowModel row, int x, int y, int w)
    {
        var height = Scale(8 + 32 + 6 + 38 + 6 + 16 + 10);
        if (g is not null)
        {
            FillRound(g, row.IsActive ? _theme.RowActive : _theme.RowFill, new Rectangle(x, y, w, height), Scale(6));
            if (row.IsActive)
                FillRound(g, _theme.Accent, new Rectangle(x, y + Scale(18), Scale(3), Scale(16)), Scale(2));
        }

        var left = x + Scale(16);
        var right = x + w - Scale(12);
        var line = new Rectangle(left, y + Scale(8), right - left, Scale(32));

        int actionLeft;
        switch (row.Action)
        {
            case RowAction.Active:
                actionLeft = Label(g, "Active", _theme.Accent, right, line);
                break;
            case RowAction.LoginExpired:
                actionLeft = Label(g, "Login expired", _theme.Critical, right, line);
                break;
            default:
                var text = row.Action == RowAction.Switch ? "Switch" : "Switching…";
                var buttonWidth = Measure(text, _fonts.Body).Width + Scale(28);
                var button = new Rectangle(right - buttonWidth, line.Y, buttonWidth, line.Height);
                Button(g, button, text, new Hit(HitKind.Switch, row.Key), enabled: row.Action == RowAction.Switch, _theme.Text, _fonts.Body);
                actionLeft = button.X;
                break;
        }

        var badgeWidth = row.Plan is { } plan ? Measure(plan, _fonts.Badge).Width + Scale(12) : 0;
        var emailMax = actionLeft - Scale(8) - left - (badgeWidth > 0 ? badgeWidth + Scale(8) : 0);
        var emailWidth = Math.Max(0, Math.Min(Measure(row.Email, _fonts.Title).Width, emailMax));
        Write(g, row.Email, _fonts.Title, _theme.Text, new Rectangle(left, line.Y, emailWidth, line.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (row.Plan is { } badge)
        {
            var bounds = new Rectangle(left + emailWidth + Scale(8), line.Y + (line.Height - Scale(16)) / 2, badgeWidth, Scale(16));
            if (g is not null)
                FillRound(g, _theme.Badge, bounds, Scale(4));
            Write(g, badge, _fonts.Badge, _theme.Text2, bounds, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        var gridTop = line.Bottom + Scale(6);
        // A click anywhere on the meters switches every meter between the time left and the reset date.
        var meters = new Rectangle(x + Scale(8), gridTop - Scale(3), w - Scale(16), Scale(38 + 6));
        var metersHit = new Hit(HitKind.ResetDisplay, row.Key);
        AddHit(meters, metersHit);
        if (g is not null && _hover == metersHit)
            FillRound(g, _theme.SubtleOver(row.IsActive ? _theme.RowActive : _theme.RowFill, _pressed == metersHit), meters, Scale(4));
        BarRow(g, row.FiveHour, left, gridTop, right - left);
        BarRow(g, row.SevenDay, left, gridTop + Scale(22), right - left);

        var statusTop = gridTop + Scale(38 + 6);
        var loginWidth = 0;
        if (row.Login.Length > 0)
        {
            var caution = row.LoginTone == Tone.Caution;
            var color = caution ? _theme.Caution : _theme.Text3;
            var font = caution ? _fonts.SmallSemibold : _fonts.Small;
            var textWidth = Measure(row.Login, font).Width;
            var glyph = caution ? Scale(12 + 4) : 0;
            loginWidth = textWidth + glyph;
            var loginLeft = right - loginWidth;
            if (caution)
                Write(g, GlyphWarning, _fonts.Icon12, color, new Rectangle(loginLeft, statusTop, Scale(12), Scale(16)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Write(g, row.Login, font, color, new Rectangle(loginLeft + glyph, statusTop, textWidth, Scale(16)), TextFormatFlags.VerticalCenter);
        }

        var statusWidth = right - left - loginWidth - (loginWidth > 0 ? Scale(8) : 0);
        Write(g, row.Status, _fonts.Small, _theme.For(row.StatusTone), new Rectangle(left, statusTop, statusWidth, Scale(16)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        return height;
    }

    /// <summary>12 px semibold text right-aligned in the row's first line; returns its left edge.</summary>
    private int Label(Graphics? g, string text, Color color, int right, Rectangle line)
    {
        var width = Measure(text, _fonts.SmallSemibold).Width;
        var left = right - Scale(6) - width;
        Write(g, text, _fonts.SmallSemibold, color, new Rectangle(left, line.Y, width, line.Height), TextFormatFlags.VerticalCenter);
        return left - Scale(6);
    }

    private void BarRow(Graphics? g, BarModel bar, int x, int y, int w)
    {
        var row = Scale(16);
        var label = new Rectangle(x, y, Scale(20), row);
        var resetWidth = ResetColumnWidth();
        var resets = new Rectangle(x + w - resetWidth, y, resetWidth, row);
        var percent = new Rectangle(resets.X - Scale(8 + 36), y, Scale(36), row);
        var track = new Rectangle(label.Right + Scale(8), y + (row - Scale(4)) / 2, percent.X - Scale(8) - label.Right - Scale(8), Scale(4));

        Write(g, bar.Label, _fonts.Small, _theme.Text2, label, TextFormatFlags.VerticalCenter);
        if (g is not null)
        {
            FillRound(g, _theme.Track, track, Scale(2));
            if (bar.Fraction > 0)
                FillRound(g, _theme.ForLevel(bar.Level), track with { Width = Math.Max(Scale(4), (int)Math.Round(track.Width * bar.Fraction)) }, Scale(2));
        }

        Write(g, bar.Percent, _fonts.SmallSemibold, bar.Inferred ? _theme.Text3 : _theme.Text, percent, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        Write(g, _model?.ShowResetDate == true ? bar.ResetDate : bar.Countdown, _fonts.Small, _theme.Text3, resets, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    private int ResetColumnWidth()
    {
        if (_resetColumnWidth == 0)
            _resetColumnWidth = DisplayFormatter.ResetColumnSamples(CultureInfo.CurrentCulture).Max(text => Measure(text, _fonts.Small).Width);
        return _resetColumnWidth;
    }

    private int SettingsBody(Graphics? g, int top, int width)
    {
        var x = Scale(8);
        var w = width - Scale(16);
        var y = top;

        Write(g, "Accounts", _fonts.SmallSemibold, _theme.Text2, new Rectangle(x + Scale(8), y + Scale(8), w - Scale(16), Scale(16)), TextFormatFlags.VerticalCenter);
        y += Scale(8 + 16 + 4);

        foreach (var row in _model?.SettingsRows ?? [])
            y += SettingsAccount(g, row, x, y, w) + Scale(4);

        var hintLeft = x + Scale(8 + 14 + 8);
        Write(g, GlyphInfo, _fonts.Icon14, _theme.Text2, new Rectangle(x + Scale(8), y + Scale(9), Scale(14), Scale(14)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        y += Scale(8) + Wrapped(g, AddHint, _fonts.Small, _theme.Text2, new Rectangle(hintLeft, y + Scale(8), x + w - Scale(8) - hintLeft, 0)) + Scale(4);

        y += Separator(g, x, y + Scale(6), w) + Scale(12);
        y += ToggleRow(g, "Keep inactive accounts live", "Refreshes their tokens in the background", _model?.KeepInactiveLive == true, new Hit(HitKind.KeepLive), x, y, w);
        y += ToggleRow(g, "Start with Windows", null, _model?.StartWithWindows == true, new Hit(HitKind.Startup), x, y, w);
        y += LinkRow(g, "Open claude.ai usage page", new Hit(HitKind.UsagePage), x, y, w);
        y += Separator(g, x, y + Scale(2), w) + Scale(8);

        var exitWidth = Measure("Exit", _fonts.Body).Width + Scale(28);
        var exit = new Rectangle(x + w - Scale(8) - exitWidth, y, exitWidth, Scale(32));
        Write(g, $"Version {_model?.Version}", _fonts.Small, _theme.Text3, new Rectangle(x + Scale(8), y, exit.X - x - Scale(16), Scale(32)), TextFormatFlags.VerticalCenter);
        Button(g, exit, "Exit", new Hit(HitKind.Exit), enabled: true, _theme.Text, _fonts.Body);
        y += Scale(32) + Scale(8);
        return y - top;
    }

    private int SettingsAccount(Graphics? g, SettingsRowModel row, int x, int y, int w)
    {
        if (_confirming == row.Key)
        {
            var text = $"Remove {row.Email} from Claude Meter? Claude Code is not changed."
                + (row.IsActive ? " It's the active account, so Claude Meter saves it again at the next check." : "");
            var inner = new Rectangle(x + Scale(12), y + Scale(8), w - Scale(20), 0);
            var textHeight = MeasureWrapped(text, _fonts.Small, inner.Width);
            var height = Scale(8) + textHeight + Scale(8) + Scale(32) + Scale(8);
            if (g is not null)
                FillRound(g, _theme.CriticalTint, new Rectangle(x, y, w, height), Scale(6));
            Wrapped(g, text, _fonts.Small, _theme.Text, inner);

            var buttonsTop = y + Scale(8) + textHeight + Scale(8);
            var removeWidth = Measure("Remove", _fonts.Title).Width + Scale(28);
            var cancelWidth = Measure("Cancel", _fonts.Body).Width + Scale(28);
            var remove = new Rectangle(x + w - Scale(8) - removeWidth, buttonsTop, removeWidth, Scale(32));
            var cancel = new Rectangle(remove.X - Scale(8) - cancelWidth, buttonsTop, cancelWidth, Scale(32));
            Button(g, cancel, "Cancel", new Hit(HitKind.CancelRemove, row.Key), enabled: true, _theme.Text, _fonts.Body);
            Button(g, remove, "Remove", new Hit(HitKind.ConfirmRemove, row.Key), enabled: true, _theme.Critical, _fonts.Title);
            return height;
        }

        var rowHeight = Scale(8 + 20 + 16 + 8);
        if (g is not null)
            FillRound(g, _theme.RowFill, new Rectangle(x, y, w, rowHeight), Scale(6));
        var buttonWidth = Measure("Remove", _fonts.Body).Width + Scale(28);
        var button = new Rectangle(x + w - Scale(8) - buttonWidth, y + (rowHeight - Scale(32)) / 2, buttonWidth, Scale(32));
        Button(g, button, "Remove", new Hit(HitKind.Remove, row.Key), enabled: true, _theme.Text, _fonts.Body);
        var textLeft = x + Scale(12);
        var textWidth = button.X - Scale(8) - textLeft;
        Write(g, row.Email, _fonts.Title, _theme.Text, new Rectangle(textLeft, y + Scale(8), textWidth, Scale(20)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        Write(g, row.Sub, _fonts.Small, _theme.For(row.SubTone), new Rectangle(textLeft, y + Scale(28), textWidth, Scale(16)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        return rowHeight;
    }

    private int ToggleRow(Graphics? g, string label, string? sub, bool on, Hit hit, int x, int y, int w)
    {
        var height = Scale(6 + 20 + (sub is null ? 0 : 16) + 6);
        var bounds = new Rectangle(x, y, w, height);
        AddHit(bounds, hit);
        if (g is not null && _hover == hit)
            FillRound(g, _pressed == hit ? _theme.SubtlePressed : _theme.SubtleHover, bounds, Scale(4));

        var track = new Rectangle(x + w - Scale(8 + 40), y + (height - Scale(20)) / 2, Scale(40), Scale(20));
        var textWidth = track.X - Scale(12) - (x + Scale(8));
        Write(g, label, _fonts.Body, _theme.Text, new Rectangle(x + Scale(8), y + Scale(6), textWidth, Scale(20)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (sub is not null)
            Write(g, sub, _fonts.Small, _theme.Text3, new Rectangle(x + Scale(8), y + Scale(26), textWidth, Scale(16)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (g is not null)
        {
            var knob = Scale(12);
            var knobBounds = new Rectangle(on ? track.Right - Scale(5) - knob : track.X + Scale(4), track.Y + (track.Height - knob) / 2, knob, knob);
            if (on)
                FillRound(g, _theme.Accent, track, track.Height / 2);
            else
                DrawRound(g, _theme.ToggleOff, track, track.Height / 2);
            FillRound(g, on ? _theme.OnAccent : _theme.ToggleOff, knobBounds, knob / 2);
        }

        return height;
    }

    private int LinkRow(Graphics? g, string text, Hit hit, int x, int y, int w)
    {
        var height = Scale(10 + 20 + 10);
        var bounds = new Rectangle(x, y, w, height);
        AddHit(bounds, hit);
        if (g is not null && _hover == hit)
            FillRound(g, _pressed == hit ? _theme.SubtlePressed : _theme.SubtleHover, bounds, Scale(4));
        Write(g, text, _fonts.Body, _theme.Text, new Rectangle(x + Scale(8), y + Scale(10), w - Scale(16 + 14 + 8), Scale(20)), TextFormatFlags.VerticalCenter);
        Write(g, GlyphOpen, _fonts.Icon14, _theme.Text, new Rectangle(x + w - Scale(8 + 14), y + Scale(10), Scale(14), Scale(20)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return height;
    }

    /// <returns>The height it takes, 1 px.</returns>
    private int Separator(Graphics? g, int x, int y, int w)
    {
        if (g is not null)
            g.FillRectangle(Brush(_theme.Border), x + Scale(8), y, w - Scale(16), 1);
        return 1;
    }

    private void IconButton(Graphics? g, Rectangle bounds, string glyph, Hit hit, Color color, bool filled)
    {
        _hits.Add((bounds, hit));
        if (g is null)
            return;
        Color? fill = _hover == hit ? (_pressed == hit ? _theme.SubtlePressed : _theme.SubtleHover) : filled ? _theme.ButtonFill : null;
        if (fill is { } f)
            FillRound(g, f, bounds, Scale(4));
        Write(g, glyph, _fonts.Icon16, color, bounds, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Indeterminate Fluent progress ring: a 16 px accent arc that turns once a second while it grows and shrinks.</summary>
    private void ProgressRing(Graphics? g, Rectangle bounds)
    {
        if (g is null)
            return;
        var stroke = Scale(2);
        var size = Scale(16) - stroke;
        var ring = new Rectangle(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2, size, size);
        var seconds = (Environment.TickCount64 - _spinStartedAt) / 1000.0;
        var start = (float)(seconds * 360 % 360) - 90;
        var sweep = (float)(40 + 220 * (0.5 - 0.5 * Math.Cos(seconds * Math.PI)));
        using var pen = new Pen(_theme.Accent, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, ring, start, sweep);
    }

    private void Button(Graphics? g, Rectangle bounds, string text, Hit hit, bool enabled, Color textColor, Font font)
    {
        if (enabled)
            AddHit(bounds, hit);
        if (g is null)
            return;
        var fill = !enabled ? _theme.ButtonFill
            : _hover == hit ? (_pressed == hit ? _theme.ButtonPressed : _theme.ButtonHover)
            : _theme.ButtonFill;
        FillRound(g, fill, bounds, Scale(4));
        DrawRound(g, _theme.ButtonBorder, bounds, Scale(4));
        Write(g, text, font, enabled ? textColor : _theme.Text3, bounds, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Body hits only count where the body is visible under the header.</summary>
    private void AddHit(Rectangle bounds, Hit hit)
    {
        var visible = Rectangle.Intersect(bounds, new Rectangle(0, Scale(HeaderHeight), Scale(LogicalWidth), Math.Max(0, ClientSize.Height - Scale(HeaderHeight))));
        if (!visible.IsEmpty)
            _hits.Add((visible, hit));
    }

    private Size Measure(string text, Font font)
    {
        if (!_measured.TryGetValue((text, font), out var size))
            _measured[(text, font)] = size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return size;
    }

    private static int MeasureWrapped(string text, Font font, int width) =>
        TextRenderer.MeasureText(text, font, new Size(width, 0), TextFormatFlags.NoPadding | TextFormatFlags.WordBreak).Height;

    /// <summary>Word-wrapped text in <paramref name="bounds"/> (height ignored); returns the height used.</summary>
    private static int Wrapped(Graphics? g, string text, Font font, Color color, Rectangle bounds)
    {
        var height = MeasureWrapped(text, font, bounds.Width);
        if (g is not null)
            TextRenderer.DrawText(g, text, font, bounds with { Height = height }, color, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        return height;
    }

    private static void Write(Graphics? g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags)
    {
        if (g is not null && bounds.Width > 0)
            TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    private void FillRound(Graphics g, Color color, Rectangle bounds, int radius) =>
        g.FillRoundedRectangle(Brush(color), bounds, new Size(radius * 2, radius * 2));

    private void DrawRound(Graphics g, Color color, Rectangle bounds, int radius) =>
        g.DrawRoundedRectangle(Pen(color), bounds with { Width = bounds.Width - 1, Height = bounds.Height - 1 }, new Size(radius * 2, radius * 2));

    private SolidBrush Brush(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
            _brushes[color] = brush = new SolidBrush(color);
        return brush;
    }

    private Pen Pen(Color color)
    {
        if (!_pens.TryGetValue(color, out var pen))
            _pens[color] = pen = new Pen(color);
        return pen;
    }

    private void ClearPaintCache()
    {
        foreach (var brush in _brushes.Values)
            brush.Dispose();
        foreach (var pen in _pens.Values)
            pen.Dispose();
        _brushes.Clear();
        _pens.Clear();
    }

    private void ApplyWindowAttributes()
    {
        var corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var dark = _theme.Dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var border = ColorTranslator.ToWin32(_theme.Border);
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    private int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96f);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spinTimer.Dispose();
            _fonts.Dispose();
            ClearPaintCache();
        }

        base.Dispose(disposing);
    }

    private enum HitKind
    {
        None,
        Refresh,
        Settings,
        Pin,
        Back,
        Switch,
        Remove,
        ConfirmRemove,
        CancelRemove,
        KeepLive,
        Startup,
        UsagePage,
        Exit,
        ResetDisplay,
    }

    private readonly record struct Hit(HitKind Kind, AccountKey? Key = null);

    /// <summary>Fonts for one DPI; Segoe UI Variable and Segoe Fluent Icons with Windows 10 fallbacks.</summary>
    private sealed class PanelFonts(float scale) : IDisposable
    {
        public Font Title { get; } = Create("Segoe UI Variable Text Semibold", "Segoe UI Semibold", 14, scale);
        public Font Body { get; } = Create("Segoe UI Variable Text", "Segoe UI", 14, scale);
        public Font Small { get; } = Create("Segoe UI Variable Text", "Segoe UI", 12, scale);
        public Font SmallSemibold { get; } = Create("Segoe UI Variable Text Semibold", "Segoe UI Semibold", 12, scale);
        public Font Badge { get; } = Create("Segoe UI Variable Text", "Segoe UI", 11, scale);
        public Font Icon16 { get; } = Create("Segoe Fluent Icons", "Segoe MDL2 Assets", 16, scale);
        public Font Icon14 { get; } = Create("Segoe Fluent Icons", "Segoe MDL2 Assets", 14, scale);
        public Font Icon12 { get; } = Create("Segoe Fluent Icons", "Segoe MDL2 Assets", 12, scale);

        private static Font Create(string family, string fallback, float px, float scale)
        {
            var font = new Font(family, px * scale, GraphicsUnit.Pixel);
            if (font.Name == family)
                return font;
            font.Dispose();
            return new Font(fallback, px * scale, GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            foreach (var font in new[] { Title, Body, Small, SmallSemibold, Badge, Icon16, Icon14, Icon12 })
                font.Dispose();
        }
    }
}
