# Claude Meter

A tiny Windows 11 tray app that keeps your Claude plan usage in sight: the rolling **5-hour** limit and the **7-day** limit, with the time left until each resets, for every Claude Code account you use.

> Unofficial. Not affiliated with or endorsed by Anthropic.

## Why

Claude Pro and Max plans have two usage limits: a rolling 5-hour window and a weekly window. The only ways to check them are to open **claude.ai → Settings → Usage** or to run `/usage` in Claude Code. In practice you don't check, and you find out you're at the limit when Claude stops mid-task.

Claude Meter puts that number on your taskbar, where it's always visible. If you have more than one Claude Code account, it watches all of them and lets you switch with one click.

## What it does

- **Usage at a glance.** The tray icon shows the active account's 5-hour usage as a number, colored by how close you are to the limit:

  | Icon | Meaning |
  |---|---|
  | green `0`–`49` | plenty left |
  | yellow `50`–`79` | getting there |
  | red `80`–`99` | close to the limit |
  | red `F` | limit reached (100%) |
  | `!` | token expired, no credentials, or an error with no data yet |
  | `—` | not known yet |

- **Every account in one panel.** Hover the icon for a panel that lists each saved account with both limits, progress bars, the time left until each limit resets (click the bars to show the reset date and time instead), live or last-known status, and how long its login stays valid. The panel follows the Windows light/dark theme and accent color.
- **Pin it as a widget.** Left or right click the icon to pin the panel: it stays on top while you work in other apps and never takes focus. Click the icon again (or the pin button) to close it.
- **Switch accounts with one click.** Press **Switch** on any account to make it the one Claude Code uses (see [Multiple accounts](#multiple-accounts)).
- **Settings in the same panel.** The gear opens saved accounts (with **Remove**), **Keep inactive accounts live**, **Start with Windows**, the claude.ai usage page, and Exit.
- **Uses your Claude Code sign-in.** No extra login or API key. Accounts you sign in to with `/login` in Claude Code are saved automatically.
- **Light on the server.** Each account is checked every 10 minutes. Manual refreshes send at most one request per account per 5 minutes. On rate limits (HTTP 429) it backs off. If the network drops, it keeps showing the last data with its age.

## Requirements

- Windows 11 (x64)
- [Claude Code](https://code.claude.com/docs), signed in with a **Claude subscription** (Pro, Max or Team). An API-key login has no plan limits to show.

Nothing else. The release exe is self-contained, so no .NET runtime is needed.

## Install

1. **Download** `ClaudeMeter.exe` from the [latest release](https://github.com/mdradityatama/claude-meter/releases/latest). It is a single file of about 50 MB.

2. **Put it somewhere permanent**, for example `%LOCALAPPDATA%\Programs\ClaudeMeter\`, and double-click it.

   The exe isn't code-signed, so the first time Windows may show **"Windows protected your PC"**. Click **More info → Run anyway**.

3. **Make the icon visible.** Windows 11 hides new tray icons in the `^` overflow. Go to **Settings → Personalization → Taskbar → Other system tray icons** and turn on **Claude Meter**.

4. *(Optional)* To start automatically at sign-in, click the icon, open Settings (gear) and turn on **Start with Windows**.

### Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`).

```powershell
git clone https://github.com/mdradityatama/claude-meter.git
cd claude-meter
dotnet publish src/ClaudeMeter -c Release -o publish
```

This produces the same single self-contained file: `publish\ClaudeMeter.exe`.

### Uninstall

In Settings, turn off **Start with Windows** and click **Exit**. Then delete `ClaudeMeter.exe` and, if you want the saved accounts gone too, `%LOCALAPPDATA%\ClaudeMeter\`.

## Multiple accounts

- **Add an account** by running `/login` in Claude Code with it. Claude Meter saves it automatically (you get an "Added …" notification). There is no Add button.
- **Switch** from the panel. Claude Meter replaces Claude Code's global login (`.credentials.json` and the `oauthAccount` block in `.claude.json`) under Claude Code's own locks.
- **Reopen Claude Code after switching.** The CLI and the VS Code extension use the new account when they start. Running sessions may move to it at their next token refresh.
- **Never run `/logout` on an account you want to keep.** It can revoke the saved login. To add another account, just run `/login` with it.
- **Every login expires.** Each account's panel row shows how long its login stays valid. When it expires (or is about to), switch to that account and run `/login` in Claude Code again.
- **Remove** in Settings deletes Claude Meter's saved copy only. It never changes Claude Code.

## Troubleshooting

| You see | What to do |
|---|---|
| `!` and "Token expired, open Claude Code" | The active account's token is short-lived and only Claude Code refreshes it. Open Claude Code and run any prompt. The app picks up the new token within 10 minutes, or right away with **Refresh**. |
| `!` and "No credentials" | Sign in to Claude Code (`claude`, then `/login`). If you use a custom `CLAUDE_CONFIG_DIR`, the app respects it. |
| "Login expired" on an account | Switch to it and run `/login` in Claude Code with that account. |
| "Not saved yet: waiting for network to verify" | A new login is only saved after Anthropic confirms whose it is. It is saved at the next check once you're online. |
| "Login files are changing; will retry" | Claude Code was writing its login files at that moment. It clears by itself. If it stays after a switch, reopen Claude Code. |
| "Offline · last known …" | The last check failed (network or rate limit). The numbers are from the time shown and update on the next successful check. |
| "Couldn't switch accounts: Claude Code is refreshing its login" | Claude Code held its lock for a few seconds. Try again. |
| No icon at all | See step 3 of Install. |

## How it works

- **Data source.** The app calls `GET https://api.anthropic.com/api/oauth/usage`, the same endpoint Claude Code uses. It is **undocumented and may change or stop working without notice**. The parser reads only `five_hour` and `seven_day` and shows `—` for anything missing, so small format changes don't crash it.
- **Encrypted login snapshots.** To show and switch accounts that Claude Code is not using, the app keeps a copy of each account's login. The copies live only in `%LOCALAPPDATA%\ClaudeMeter\accounts.dat`, encrypted with Windows DPAPI for your Windows user. Tokens are never logged or displayed, and are only sent to `api.anthropic.com` (usage, profile) and `platform.claude.com` (token refresh), with redirects disabled.
- **Token refresh for inactive accounts only.** Claude Code stays the only party that refreshes the account it is using. The app refreshes the saved tokens of the *other* accounts so their usage stays live and dead logins show up early. This uses an unofficial endpoint and Claude Code's client id, so it could stop working; if Anthropic rejects it, the app stops refreshing and shows last-known usage. A refresh whose response gets lost (for example a timeout) can spend that account's refresh token, and that account then needs `/login` again. Turn **Keep inactive accounts live** off in Settings to disable background refresh completely.
- **Claude Code's own locks.** Before touching Claude Code's files, the app takes the same lock directories Claude Code uses (`.oauth_refresh.lock`, the legacy `.claude.lock`, `.storage-write.lock` and `.claude.json.lock`), so the two never write at the same time. It never makes network calls while holding them, and edits only the bytes of the values it replaces. Thanks to the claude-swap project for documenting the lock protocol.
- **Verified captures.** Before a login is saved under an account, the app checks that the credentials really belong to the identity in `.claude.json`, using Anthropic's profile endpoint and a byte-for-byte re-read under the locks. If that can't be confirmed, nothing is saved.
- **Logins expire.** Refreshing a token does not extend a login. Every account needs `/login` again when its login expiry passes; the panel and a notification warn you two days ahead.
- **Why WinForms.** `NotifyIcon` plus `ApplicationContext` is the smallest built-in way to make a windowless tray app on .NET, with no third-party packages. The panel is one owner-drawn window, so it stays light.

## Development

```powershell
dotnet test                          # unit tests (xUnit)
dotnet run --project src/ClaudeMeter # run from source
```

To release, push a version tag. The [Release workflow](.github/workflows/release.yml) runs the tests, builds the exe and publishes it as a GitHub Release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

```
src/ClaudeMeter/
  Core/   usage, accounts store, locks, auto-save, switching, token refresh, display formatting (no UI, unit tested)
  UI/     tray icon, the owner-drawn panel, theme, startup registration
tests/ClaudeMeter.Tests/
docs/SPEC.md                 original specification
docs/MULTI-ACCOUNT-SPEC.md   multi-account specification
docs/MULTI-ACCOUNT-PLAN.md   implementation plan and progress
```
