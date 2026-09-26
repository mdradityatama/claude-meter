# Task: Windows 11 system tray app that shows my Claude plan usage limits

## Goal
A tiny, always-running tray app (notification area, bottom-right of the taskbar) that shows my Claude Pro usage: the rolling 5-hour window and the 7-day window, with reset times. Personal tool, Windows 11 only.

## Workflow
1. Do NOT write code yet. First, make one real request to the endpoint below using my local credentials and print the raw JSON response (never print the token). Model the response from what actually comes back, not from assumptions.
2. Then present a short plan: project structure, classes and responsibilities, and anything in this spec you disagree with. Wait for my approval before implementing.

## Tech constraints
- .NET 10 (LTS), `net10.0-windows`, WinForms with `NotifyIcon` and an `ApplicationContext` (no main window).
- No third-party NuGet packages in the app. xUnit only in the test project.
- Single instance (named Mutex).
- Publish as framework-dependent single-file exe for win-x64.

## Data source (unofficial, used by Claude Code itself)
- Credentials: `%USERPROFILE%\.claude\.credentials.json` → `claudeAiOauth.accessToken`. Respect `CLAUDE_CONFIG_DIR` if set.
- Request: `GET https://api.anthropic.com/api/oauth/usage`
  - `Authorization: Bearer <accessToken>`
  - `anthropic-beta: oauth-2025-04-20`
- Expected fields: `five_hour` and `seven_day`, each with `utilization` (0–100) and `resets_at` (ISO timestamp). Other fields may exist or be null. Parse tolerantly: a missing or null field shows "—", never crashes.

## Behavior rules
- Poll every 10 minutes. Manual "Refresh" must never cause more than one request per 5 minutes (return cached data otherwise).
- On HTTP 429: honor `Retry-After` if present, otherwise exponential backoff capped at 30 minutes.
- On HTTP 401: show a "token expired, open Claude Code" state. Do NOT implement any OAuth refresh flow; Claude Code owns the token. Re-read the credentials file on every poll so a token refreshed by Claude Code is picked up automatically.
- Network errors: keep last known data, mark it stale with its timestamp.
- Convert `resets_at` to local time for display.

## Security (non-negotiable)
- Never log, persist, copy, or display the access token.
- The token is only ever sent to `api.anthropic.com`.

## Tray UI
- Icon: render the 5-hour utilization as a number on the icon. Color: green < 50, yellow 50–79, red ≥ 80. Show "!" on error and "—" when unknown.
  - Size the icon for the current DPI (don't hardcode 16×16).
  - Avoid GDI handle leaks: every icon created via `Icon.FromHandle` must be released with `DestroyIcon`, and the previous icon disposed on each update.
- Tooltip (`NotifyIcon.Text`, max 127 chars): e.g. `5h 42% · reset 14:30 | 7d 18% · reset Tue 07:00`.
- Context menu (left and right click):
  - Info lines (disabled items): 5h %, 7d %, reset times, last updated time / stale / error state
  - Refresh
  - Open claude.ai usage page (`https://claude.ai/settings/usage`)
  - Start with Windows (checkable toggle, `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)
  - Exit

## Tests (critical logic only, no UI tests)
- JSON parsing: full response, missing fields, null fields, unexpected extra fields.
- Utilization → color/state mapping, including boundaries (49/50, 79/80).
- Backoff calculation and the 5-minute manual refresh floor (inject a clock; no real waiting).
- Credentials path resolution with and without `CLAUDE_CONFIG_DIR`.

## README
Short: overview, project structure, how to build/run/publish, and key decisions:
- why the unofficial endpoint and the risk that it may change without notice
- why the app never refreshes the OAuth token
- why WinForms
- note that Windows 11 hides new tray icons: enable it in Settings → Personalization → Taskbar → Other system tray icons