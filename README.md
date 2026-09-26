# Claude Meter

A tiny Windows 11 tray app that keeps your Claude plan usage in sight: the rolling **5-hour** limit and the **7-day** limit, with their reset times.

> Unofficial. Not affiliated with or endorsed by Anthropic.

## Why

Claude Pro and Max plans have two usage limits: a rolling 5-hour window and a weekly window. The only ways to check them are to open **claude.ai → Settings → Usage** or to run `/usage` in Claude Code. In practice you don't check, and you find out you're at the limit when Claude stops mid-task.

Claude Meter puts that number on your taskbar, where it's always visible.

## What it does

- **Usage at a glance.** The tray icon shows your 5-hour usage as a number, colored by how close you are to the limit:

  | Icon | Meaning |
  |---|---|
  | green `0`–`49` | plenty left |
  | yellow `50`–`79` | getting there |
  | red `80`–`99` | close to the limit |
  | red `F` | limit reached (100%) |
  | `!` | token expired, no credentials, or an error with no data yet |
  | `—` | not known yet |

- **Details on hover.** Hover the icon for a panel with both limits, progress bars, reset times in your local time, and when the data was last updated. The panel follows the Windows light/dark theme and has **Refresh** and **Usage page ↗** links.
- **Menu on click.** Left or right click for Refresh, the claude.ai usage page, **Start with Windows**, and Exit.
- **Uses your Claude Code sign-in.** No extra login or API key. It reads the token Claude Code already stores and re-reads it on every check, so when Claude Code refreshes it, the app picks up the new one automatically.
- **Light on the server.** It checks every 10 minutes. Manual refreshes send at most one request per 5 minutes. On rate limits (HTTP 429) it backs off. If the network drops, it keeps showing the last data, marked as stale.

## Requirements

- Windows 11 (x64)
- [Claude Code](https://code.claude.com/docs), signed in with a **Claude subscription** (Pro or Max). An API-key login has no plan limits to show.

Nothing else. The release exe is self-contained, so no .NET runtime is needed.

## Install

1. **Download** `ClaudeUsageTray.exe` from the [latest release](https://github.com/mdradityatama/claude-meter/releases/latest). It is a single file of about 50 MB.

2. **Put it somewhere permanent**, for example `%LOCALAPPDATA%\Programs\ClaudeMeter\`, and double-click it.

   The exe isn't code-signed, so the first time Windows may show **"Windows protected your PC"**. Click **More info → Run anyway**.

3. **Make the icon visible.** Windows 11 hides new tray icons in the `^` overflow. Go to **Settings → Personalization → Taskbar → Other system tray icons** and turn on **ClaudeUsageTray**.

4. *(Optional)* To start automatically at sign-in, click the icon and check **Start with Windows**.

### Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`).

```powershell
git clone https://github.com/mdradityatama/claude-meter.git
cd claude-meter
dotnet publish src/ClaudeUsageTray -c Release -o publish
```

This produces the same single self-contained file: `publish\ClaudeUsageTray.exe`.

### Uninstall

Uncheck **Start with Windows**, choose **Exit**, then delete the folder `%LOCALAPPDATA%\Programs\ClaudeMeter`.

## Troubleshooting

| You see | What to do |
|---|---|
| `!` and "Token expired, open Claude Code" | The sign-in token is short-lived and only Claude Code refreshes it. Open Claude Code and run any prompt. The app picks up the new token within 10 minutes, or right away with **Refresh**. |
| `!` and "No credentials" | Sign in to Claude Code (`claude`, then `/login`). If you use a custom `CLAUDE_CONFIG_DIR`, the app respects it. |
| "Stale since …" | The last check failed (network or rate limit). The numbers shown are from the time listed and update on the next successful check. |
| Refresh does nothing | Manual refreshes are limited to one request per 5 minutes. Within that window you see cached data. |
| No icon at all | See step 4 of Install. |

## How it works

- **Data source.** The app calls `GET https://api.anthropic.com/api/oauth/usage`, the same endpoint Claude Code uses. It is **undocumented and may change or stop working without notice**. The parser reads only `five_hour` and `seven_day` and shows `—` for anything missing, so small format changes don't crash it.
- **Your token.** The app reads it from `%USERPROFILE%\.claude\.credentials.json` (or `%CLAUDE_CONFIG_DIR%`). It is never logged, stored, copied or displayed, and it is only ever sent to `api.anthropic.com`, with redirects disabled.
- **No token refresh.** The token belongs to Claude Code. Refreshing it here would rotate it behind Claude Code's back and could sign it out, so the app only reads it.
- **Why WinForms.** `NotifyIcon` plus `ApplicationContext` is the smallest built-in way to make a windowless tray app on .NET, with no third-party packages.

## Development

```powershell
dotnet test                              # unit tests (xUnit)
dotnet run --project src/ClaudeUsageTray # run from source
```

To release, push a version tag. The [Release workflow](.github/workflows/release.yml) runs the tests, builds the exe and publishes it as a GitHub Release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

```
src/ClaudeUsageTray/
  Core/   parsing, credentials, HTTP client, refresh policy, display formatting (no UI, unit tested)
  UI/     tray icon, hover panel, menu, startup registration
tests/ClaudeUsageTray.Tests/
docs/SPEC.md   original specification
```
