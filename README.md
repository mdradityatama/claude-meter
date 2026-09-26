# Claude Usage Tray

A tiny Windows 11 tray app that shows your Claude plan usage: the rolling 5-hour window and the 7-day window, with their reset times. The icon shows the 5-hour percentage (green < 50, yellow 50–79, red ≥ 80, `F` at 100%, `!` on error, `—` when unknown). Hover it for a usage panel (progress bars, reset times, status, Refresh and usage page links; it follows the Windows light/dark theme), or click it (left or right) for the menu: details, Refresh, the claude.ai usage page, "Start with Windows" and Exit.

Personal tool, Windows 11 only. The original spec is in [docs/SPEC.md](docs/SPEC.md).

## Project structure

```
src/ClaudeUsageTray/
  Program.cs                 single-instance Mutex, starts the tray context
  Core/                      no UI dependencies, unit tested
    UsageParser.cs           tolerant JSON parsing (five_hour / seven_day only)
    CredentialsLocator.cs    %CLAUDE_CONFIG_DIR% or %USERPROFILE%\.claude\.credentials.json
    CredentialsReader.cs     reads claudeAiOauth.accessToken, re-read on every poll
    UsageClient.cs           GET https://api.anthropic.com/api/oauth/usage
    RefreshPolicy.cs         10-min poll, 5-min manual floor, 429 backoff
    UsageService.cs          ties it together and owns all state transitions
    DisplayFormatter.cs      icon text/color, panel model, menu lines
  UI/
    TrayApplicationContext.cs  NotifyIcon, menu, poll timer, hover tracking
    UsagePanel.cs              hover flyout (custom-painted, never takes focus)
    TrayIconRenderer.cs        DPI-sized icon drawing, GDI handle hygiene
    StartupRegistration.cs     HKCU\...\CurrentVersion\Run
tests/ClaudeUsageTray.Tests/ xUnit tests for Core
```

## Build, run, publish

Requires the .NET 10 SDK.

```powershell
dotnet test
dotnet run --project src/ClaudeUsageTray
dotnet publish src/ClaudeUsageTray -c Release -o publish
```

`publish/ClaudeUsageTray.exe` is a single framework-dependent file (win-x64). It needs the .NET 10 Desktop Runtime installed.

Windows 11 hides new tray icons by default. To keep it visible, go to **Settings → Personalization → Taskbar → Other system tray icons** and switch it on.

## Key decisions

- **Unofficial endpoint.** `/api/oauth/usage` is what Claude Code itself uses. It is not documented and may change or disappear without notice. The response already carries many unrelated, codename-style fields, so the parser reads only `five_hour` and `seven_day` and treats anything missing or malformed as unknown (`—`) instead of failing.
- **No token refresh.** The OAuth token belongs to Claude Code. Refreshing it here would rotate the refresh token behind Claude Code's back and could log it out. The app re-reads the credentials file on every poll instead, so a token refreshed by Claude Code is picked up automatically. On HTTP 401 it shows "token expired, open Claude Code". Tokens are short-lived, so expect this state when Claude Code hasn't run for a while.
- **Token handling.** The token is never logged, stored, copied or displayed. It is only sent to `api.anthropic.com`, and redirects are disabled so it can't follow one elsewhere.
- **Polite polling.** The app polls every 10 minutes. Manual Refresh sends a request only if the previous one was at least 5 minutes ago; otherwise it shows cached data. HTTP 429 honors `Retry-After`, or backs off 20 → 30 minutes (capped). On network errors the last data stays on screen, marked stale with its timestamp.
- **Hover panel instead of a tooltip.** The native tray tooltip is plain text capped at 127 chars, so the app shows its own borderless flyout and leaves `NotifyIcon.Text` empty (otherwise both would appear). Windows sends no "mouse left the icon" event, so while hovering the app polls the cursor against the icon rectangle from `Shell_NotifyIconGetRect`. That needs `NotifyIcon`'s private `_window`/`_id` fields via reflection; if a future WinForms renames them, it falls back to a small area around the cursor.
- **Why WinForms.** `NotifyIcon` + `ApplicationContext` is the smallest built-in way to make a windowless tray app on .NET, with no third-party packages. WPF and WinUI have no built-in tray icon.
