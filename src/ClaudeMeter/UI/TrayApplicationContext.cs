using System.Diagnostics;
using ClaudeMeter.Core;
using Microsoft.Win32;

namespace ClaudeMeter.UI;

/// <summary>
/// Owns the tray icon, the panel (shown on hover, pinned on click), the poll timer and the accounts runtime.
/// No main window, no context menu.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string UsagePageUrl = "https://claude.ai/settings/usage";
    private const int HoverShowDelayMs = 300;
    private const int HoverHideDelayMs = 400;
    // A refresh inside every account's manual floor sends nothing and returns at once; the ring still shows this long.
    private const int MinRefreshFeedbackMs = 600;

    private readonly ClaudePaths _paths = ClaudePaths.ForCurrentUser();
    private readonly UsageClient _client = new();
    private readonly ProfileClient _profiles = new();
    private readonly OAuthTokenClient _tokens = new();
    private readonly AccountsRuntime _runtime;
    private readonly StoreLoadResult _loaded;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly MeterPanel _panel = new();
    // Windows reports no "mouse left the tray icon" event, so hover is tracked by polling the cursor.
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 100 };
    private readonly ClaudeFilesWatcher _watcher;
    private long _hoverStartedAt;
    private long? _leftAt;
    private Point _lastHoverPoint;
    private Icon? _icon;
    private bool _pinned;
    private bool _refreshing;
    private bool _started;

    public TrayApplicationContext()
    {
        var store = new AccountStore(_paths.Store, new DpapiProtector());
        _loaded = store.Load();
        _runtime = new AccountsRuntime(
            _paths, store, AppSettings.Load(_paths.Settings), _client, _profiles, _tokens, ClaudeLocks.ForSystem(_paths), TimeProvider.System);

        _notifyIcon = new NotifyIcon { Visible = true };
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
        _notifyIcon.MouseMove += OnNotifyIconMouseMove;

        _runtime.Changed += (_, _) => UpdateTray();
        _runtime.Notify += (_, toast) => _notifyIcon.ShowBalloonTip(5000, toast.Title, toast.Body, ToolTipIcon.None);
        _panel.ActionRequested += OnPanelAction;
        _hoverTimer.Tick += OnHoverTick;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        UpdateTray();

        _watcher = new ClaudeFilesWatcher(_paths);
        _watcher.Changed += async (_, _) => await RunSafelyAsync(_runtime.OnClaudeFilesChangedAsync);

        // First poll runs once the message loop is up.
        _timer.Tick += OnTimerTick;
        _timer.Interval = 1;
        _timer.Start();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        try
        {
            if (_started)
            {
                await RunSafelyAsync(() => _runtime.PollAsync(manual: false, CancellationToken.None));
            }
            else
            {
                _started = true;
                await RunSafelyAsync(() => _runtime.StartAsync(_loaded));
            }
        }
        finally
        {
            // Polling must never stop, whatever happened in this round.
            UpdateTray();
            ScheduleNextPoll();
        }
    }

    /// <summary>
    /// Last resort for async event handlers: an exception there would show WinForms' crash dialog and leave the
    /// tray frozen. Expected failures are handled where they happen; this only keeps the app alive.
    /// </summary>
    private static async Task RunSafelyAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }

    private void ScheduleNextPoll()
    {
        _timer.Stop();
        _timer.Interval = (int)Math.Clamp(_runtime.TimeUntilNextDue.TotalMilliseconds, 1000, int.MaxValue);
        _timer.Start();
    }

    private void UpdateTray()
    {
        var previous = _icon;
        _icon = TrayIconRenderer.Render(DisplayFormatter.Icon(_runtime.ActiveUsage));
        _notifyIcon.Icon = _icon;
        previous?.Dispose();

        if (_panel.Visible)
            _panel.SetModel(CurrentModel());
    }

    private MeterPanelModel CurrentModel()
    {
        var zone = TimeZoneInfo.Local;
        var now = DateTimeOffset.Now;
        var accounts = _runtime.Accounts;
        return new MeterPanelModel(
            [.. accounts.Select(a => DisplayFormatter.AccountRow(a, zone, now))],
            [.. accounts.Where(a => a.IsSaved).Select(a => DisplayFormatter.SettingsRow(a, zone, now))],
            _pinned,
            _refreshing,
            _runtime.KeepInactiveLive,
            StartupRegistration.IsEnabled,
            AppInfo.Version);
    }

    private void OnNotifyIconMouseMove(object? sender, MouseEventArgs e)
    {
        if (_pinned)
            return;
        _lastHoverPoint = Cursor.Position;
        if (!_hoverTimer.Enabled)
        {
            _hoverStartedAt = Environment.TickCount64;
            _hoverTimer.Start();
        }
    }

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (_pinned)
        {
            _hoverTimer.Stop();
            return;
        }

        var cursor = Cursor.Position;
        var icon = TrayIconBounds.Get(_notifyIcon, _lastHoverPoint);
        var overIcon = icon.Contains(cursor);
        var now = Environment.TickCount64;

        if (!_panel.Visible)
        {
            if (!overIcon)
                _hoverTimer.Stop();
            else if (now - _hoverStartedAt >= HoverShowDelayMs)
                ShowPanel(icon);
            return;
        }

        // Stay open while the cursor is on the icon or the panel (so its buttons can be clicked).
        if (overIcon || _panel.Bounds.Contains(cursor))
        {
            _leftAt = null;
            return;
        }

        _leftAt ??= now;
        if (now - _leftAt >= HoverHideDelayMs)
            HidePanel();
    }

    /// <summary>Left or right click pins the panel as a widget; a click on a pinned panel's icon closes it.</summary>
    private void OnNotifyIconMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right))
            return;
        if (_pinned)
        {
            HidePanel();
            return;
        }

        Pin();
        ShowPanel(TrayIconBounds.Get(_notifyIcon, Cursor.Position));
    }

    private void ShowPanel(Rectangle icon)
    {
        _panel.SetModel(CurrentModel());
        _panel.ShowAt(icon);
    }

    private void Pin()
    {
        _pinned = true;
        _hoverTimer.Stop();
        _leftAt = null;
    }

    private void HidePanel()
    {
        _hoverTimer.Stop();
        _leftAt = null;
        _pinned = false;
        _panel.Hide();
        _panel.ResetView();
    }

    private async void OnPanelAction(object? sender, PanelAction action)
    {
        switch (action)
        {
            case PanelAction.Refresh:
                _refreshing = true;
                _panel.SetModel(CurrentModel());
                var minimumFeedback = Task.Delay(MinRefreshFeedbackMs);
                await RunSafelyAsync(() => _runtime.PollAsync(manual: true, CancellationToken.None));
                await minimumFeedback;
                _refreshing = false;
                ScheduleNextPoll();
                break;
            case PanelAction.TogglePin:
                if (_pinned)
                    HidePanel();
                else
                    Pin();
                break;
            case PanelAction.SettingsOpened:
                Pin();
                break;
            case PanelAction.Switch switchTo:
                await RunSafelyAsync(() => _runtime.SwitchAsync(switchTo.Key));
                ScheduleNextPoll();
                break;
            case PanelAction.Remove remove:
                await RunSafelyAsync(() => _runtime.RemoveAsync(remove.Key));
                break;
            case PanelAction.ToggleKeepLive:
                _runtime.SetKeepInactiveLive(!_runtime.KeepInactiveLive);
                break;
            case PanelAction.ToggleStartWithWindows:
                StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled);
                break;
            case PanelAction.OpenUsagePage:
                Process.Start(new ProcessStartInfo(UsagePageUrl) { UseShellExecute = true });
                break;
            case PanelAction.Exit:
                ExitThread();
                return;
        }

        if (_panel.Visible)
            _panel.SetModel(CurrentModel());
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => UpdateTray();

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            _panel.ApplyTheme(SystemTheme.Current);
    }

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
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _timer.Dispose();
            _hoverTimer.Dispose();
            _watcher.Dispose();
            _panel.Dispose();
            _notifyIcon.Dispose();
            _icon?.Dispose();
            _client.Dispose();
            _profiles.Dispose();
            _tokens.Dispose();
        }

        base.Dispose(disposing);
    }
}
