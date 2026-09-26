using System.Diagnostics;
using System.Reflection;
using ClaudeUsageTray.Core;
using Microsoft.Win32;

namespace ClaudeUsageTray.UI;

/// <summary>Owns the tray icon, its menu, the hover panel and the poll timer. No main window.</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string UsagePageUrl = "https://claude.ai/settings/usage";
    private const int HoverShowDelayMs = 300;
    private const int HoverHideDelayMs = 400;

    // NotifyIcon only opens its menu on right click; this is the same method it uses internally.
    private static readonly MethodInfo? ShowContextMenuMethod =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly UsageClient _client = new();
    private readonly UsageService _service;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _startWithWindowsItem;
    private readonly List<ToolStripItem> _infoItems = [];
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly UsagePanel _panel = new();
    // Windows reports no "mouse left the tray icon" event, so hover is tracked by polling the cursor.
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 100 };
    private long _hoverStartedAt;
    private long? _leftAt;
    private Point _lastHoverPoint;
    private Icon? _icon;

    public TrayApplicationContext()
    {
        _service = new UsageService(
            _client,
            () => CredentialsReader.ReadAccessToken(CredentialsLocator.ResolvePath(Environment.GetEnvironmentVariable)),
            new RefreshPolicy(TimeProvider.System));

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows", null, OnStartWithWindowsClick) { CheckOnClick = true };
        _menu.Items.AddRange(
        [
            new ToolStripSeparator(),
            new ToolStripMenuItem("Refresh", null, OnRefreshClick),
            new ToolStripMenuItem("Open claude.ai usage page", null, OnOpenUsagePageClick),
            _startWithWindowsItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        ]);
        _menu.Opening += OnMenuOpening;

        _notifyIcon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
        _notifyIcon.MouseMove += OnNotifyIconMouseMove;

        _panel.RefreshRequested += OnRefreshClick;
        _panel.UsagePageRequested += OnOpenUsagePageClick;
        _hoverTimer.Tick += OnHoverTick;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        UpdateTray();

        // First poll runs once the message loop is up.
        _timer.Tick += OnTimerTick;
        _timer.Interval = 1;
        _timer.Start();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        await _service.RefreshAsync(manual: false);
        UpdateTray();
        ScheduleNextPoll();
    }

    private async void OnRefreshClick(object? sender, EventArgs e)
    {
        await _service.RefreshAsync(manual: true);
        UpdateTray();
        ScheduleNextPoll();
    }

    private void ScheduleNextPoll()
    {
        _timer.Stop();
        _timer.Interval = (int)Math.Clamp(_service.TimeUntilNextPoll.TotalMilliseconds, 1000, int.MaxValue);
        _timer.Start();
    }

    private void UpdateTray()
    {
        var state = _service.State;

        var previous = _icon;
        _icon = TrayIconRenderer.Render(DisplayFormatter.Icon(state));
        _notifyIcon.Icon = _icon;
        previous?.Dispose();

        if (_panel.Visible)
            _panel.SetModel(CurrentPanelModel());
    }

    private PanelModel CurrentPanelModel() =>
        DisplayFormatter.Panel(_service.State, TimeZoneInfo.Local, DateTimeOffset.Now);

    private void OnNotifyIconMouseMove(object? sender, MouseEventArgs e)
    {
        _lastHoverPoint = Cursor.Position;
        if (!_hoverTimer.Enabled)
        {
            _hoverStartedAt = Environment.TickCount64;
            _hoverTimer.Start();
        }
    }

    private void OnHoverTick(object? sender, EventArgs e)
    {
        var cursor = Cursor.Position;
        var icon = TrayIconBounds.Get(_notifyIcon, _lastHoverPoint);
        var overIcon = icon.Contains(cursor);
        var now = Environment.TickCount64;

        if (!_panel.Visible)
        {
            if (!overIcon || _menu.Visible)
                _hoverTimer.Stop();
            else if (now - _hoverStartedAt >= HoverShowDelayMs)
                _panel.ShowAt(icon, CurrentPanelModel());
            return;
        }

        // Stay open while the cursor is on the icon or the panel (so its links can be clicked).
        if (overIcon || _panel.Bounds.Contains(cursor))
        {
            _leftAt = null;
            return;
        }

        _leftAt ??= now;
        if (now - _leftAt >= HoverHideDelayMs)
            HidePanel();
    }

    private void HidePanel()
    {
        _hoverTimer.Stop();
        _leftAt = null;
        _panel.Hide();
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        HidePanel();

        foreach (var item in _infoItems)
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }
        _infoItems.Clear();

        foreach (var line in DisplayFormatter.MenuLines(_service.State, TimeZoneInfo.Local, DateTimeOffset.Now))
            _infoItems.Add(new ToolStripMenuItem(line) { Enabled = false });

        for (var i = 0; i < _infoItems.Count; i++)
            _menu.Items.Insert(i, _infoItems[i]);

        _startWithWindowsItem.Checked = StartupRegistration.IsEnabled;
    }

    private void OnNotifyIconMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowContextMenuMethod?.Invoke(_notifyIcon, null);
    }

    private void OnStartWithWindowsClick(object? sender, EventArgs e) =>
        StartupRegistration.SetEnabled(_startWithWindowsItem.Checked);

    private static void OnOpenUsagePageClick(object? sender, EventArgs e) =>
        Process.Start(new ProcessStartInfo(UsagePageUrl) { UseShellExecute = true });

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => UpdateTray();

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _timer.Dispose();
            _hoverTimer.Dispose();
            _panel.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon?.Dispose();
            _client.Dispose();
        }

        base.Dispose(disposing);
    }
}
