# Task: Multi-account monitoring and switching for Claude Meter

## Goal
Extend Claude Meter so I can keep several Claude Code (CLI) accounts, watch every account's usage live in the tray panel, and switch the active account with one click at any time. Switching is always manual; the app never switches on its own. Claude Code CLI only (not Claude Desktop, not claude.ai web).

"Switching" means replacing the credentials Claude Code uses globally (`.credentials.json` + the `oauthAccount` block in `.claude.json`), so the CLI and the VS Code extension both use the new account after they are reopened.

Visual reference: the approved mockup (private artifact) at https://claude.ai/artifact/MSPQX59sVYANTpJYo1CqpY.

## Workflow
1. **Spike.** Done on 2026-10-11, see Spike findings. Items still open are listed under Open items.
2. **Plan.** Present: changes per existing file, new classes and responsibilities, the switch sequence, test list, and anything in this spec I should reconsider. Wait for my approval.
3. **Implement in phases**, one commit per phase, tests green at each step:
   1. Core: paths, JSON splice, identity, account store, file locks, capture verification (incl. profile client), auto-save, switch transaction (+ tests)
   2. Accounts runtime: token refresh for inactive accounts, per-account usage and scheduling, login expiry, reset inference (+ tests)
   3. UI: acrylic prototype first, then the owner-drawn panel, pinned widget mode, settings view, notifications; remove the context menu
   4. README and SPEC updates

## Spike findings (Claude Code 2.1.296, Windows 11)
- `CLAUDE_CONFIG_DIR` is not set. No legacy `<config-home>\.config.json`. The global config is `%USERPROFILE%\.claude.json`.
- `.claude.json` is about 100 KB, LF line endings, 2-space indent, no trailing newline, non-ASCII written raw (no `\u` escapes). It contains keys that differ only by case (for example two `projects` entries `D:/...` and `d:/...`), so any JSON handling must stay case-sensitive.
- `.credentials.json` is single-line JSON with top-level keys `claudeAiOauth` and `organizationUuid`.
  - `claudeAiOauth` keys: `accessToken`, `refreshToken`, `expiresAt`, `refreshTokenExpiresAt`, `scopes`, `subscriptionType`, `rateLimitTier`.
  - Access tokens live about 8 hours.
  - `refreshTokenExpiresAt` is present. It was not aligned with the last refresh time, which supports claude-swap's statement that only a fresh `/login` sets it (a token refresh does not extend it).
- `oauthAccount` has 20 keys, including `accountUuid`, `emailAddress`, `organizationUuid`, `organizationType`, `displayName`.
- `System.Security.Cryptography.ProtectedData` ships in the `Microsoft.WindowsDesktop.App.Ref` 10.0 reference pack, so DPAPI needs no package and no P/Invoke.
- The current code no longer sets `NotifyIcon.Text`; the themed hover panel replaced the tooltip.
- Lock protocol, read from the installed 2.1.296 binary (`bin\claude.exe`, a Bun build):
  - OAuth refresh: `proper-lockfile` on `<config-home>\.oauth_refresh.lock` (primary) and `<realpath(config-home)>.lock` (legacy), both `stale: 60000, update: 5000, realpath: false`. Primary first; on legacy contention it releases the primary and retries. Up to 5 attempts with 1–2 s jittered sleeps.
  - Global config: `<global-config-path>.lock`, `proper-lockfile` defaults (`stale: 10000`, update every 5 s).
  - **New since claude-swap's notes:** after taking both refresh locks, Claude Code writes an owner record `<config-home>\.oauth_refresh.lock.owner` (`pid`, `procStart`, `pidDomain`, `pidSpace`, `lockBirthtimeMs`, `legacyLockBirthtimeMs`). A waiter may take over the lock early only if that record exists, matches the lock directory's birthtime and mtime, and the holder PID is proven gone. Without a record ("no_owner_record") only the 60 s stale rule applies.
  - **New since claude-swap's notes:** every write to credential storage takes `<config-home>\.storage-write.lock` (`stale: 15000`, update every 7.5 s, 10 retries). This also covers `/login`.
  - Claude Code compares identities by (`accountUuid`, `organizationUuid`), the same key this spec uses.
  - On a 401, Claude Code re-reads the credentials file and adopts a different, non-expired access token it finds there.
  - Credential storage has a primary backend with a plaintext fallback. On this machine the plaintext `.credentials.json` is in use; a future version could move Windows credentials elsewhere.
- From claude-swap (`src/claude_swap/claude_locks.py`, `oauth.py`, verified there against Claude Code 2.1.218):
  - Under the lock, Claude Code re-reads the credentials file; if it finds a different, non-expired credential, it skips its own refresh and uses that credential (consistent with the 2.1.296 code).
  - Token endpoint: `POST https://platform.claude.com/v1/oauth/token`, JSON body `grant_type=refresh_token`, `refresh_token`, `client_id=9d1c250a-e61b-44d9-88ed-5944d1962f5e`. Refresh tokens rotate (a used refresh token is spent).
  - Profile endpoint: `GET https://api.anthropic.com/api/oauth/profile` with the bearer token returns `account.uuid`, `account.email`, `organization.uuid`. It must not be called while holding any lock.
- Profile schema confirmed on 2026-10-11 (with `anthropic-beta: oauth-2025-04-20`): top-level `account`, `organization`, `application`, `enabled_plugins`; `account.uuid`, `account.email` and `organization.uuid` are present.
- Login-expiry baseline on 2026-10-11: `expiresAt` 2026-10-11 04:01:58Z, `refreshTokenExpiresAt` 2026-10-24 01:03:01Z. Compare after Claude Code's next refresh (see Open items).

## Key decisions
- **Identity is the pair (`accountUuid`, `organizationUuid`)** from `oauthAccount`. One person can have several organizations (for example Pro plus a Team seat), each with its own token and limits.
- **The app refreshes tokens of inactive accounts only.** Claude Code stays the only party that refreshes the active account. Purpose: live usage for every account and early detection of dead logins. A refresh does not extend a login; every account still needs `/login` again when its `refreshTokenExpiresAt` passes.
- **Exactly one owner per account at any time.** Claude Code owns the active account; the app owns every inactive one. Ownership moves only inside the switch transaction.
- **The app persists credentials**, only as its own DPAPI-encrypted snapshots.
- **Every mutation of Claude Code's files happens under Claude Code's own locks.** Network calls never happen while a lock is held.
- **Accounts are saved automatically.** The active account is captured whenever it is new or its credentials changed, but only after verification proves the credentials belong to the identity in `oauthAccount`. There is no Add button and no "Prepare new login" flow.
- **Edits to Claude Code's JSON files are byte splices.** Only the bytes of the replaced value change; every other byte of the file stays identical.

## Tech constraints (unchanged)
- .NET 10, `net10.0-windows`, WinForms, no third-party NuGet in the app, xUnit in tests, single instance, self-contained single-file exe.
- Encryption: DPAPI via `ProtectedData` with `DataProtectionScope.CurrentUser` and a constant app-specific entropy, behind an interface so tests use a fake.

## Files and paths
Resolve once via a `ClaudePaths` type (replaces `CredentialsLocator`):
- Config home: `%CLAUDE_CONFIG_DIR%` if set, else `%USERPROFILE%\.claude`.
- Credentials: `<config-home>\.credentials.json`.
- Global config: `<config-home>\.config.json` if it exists (legacy), else `(%CLAUDE_CONFIG_DIR% or %USERPROFILE%)\.claude.json`.
- Lock directories (see Locking).
- App store: `%LOCALAPPDATA%\ClaudeMeter\accounts.dat` (encrypted).
- App settings: `%LOCALAPPDATA%\ClaudeMeter\settings.json` (not secret: "Keep inactive accounts live"). "Start with Windows" stays in the registry as today.

## JSON splice
- One utility locates a property's value span with `Utf8JsonReader` and replaces exactly those bytes.
- Used for `oauthAccount` in the global config and for the refreshed fields inside a stored `CredentialsJson`.
- Case-sensitive property matching. No re-serialization of the rest of the document.

## Account store
- One DPAPI-encrypted file, written atomically (temp file in the same folder + `File.Replace`).
- Per account:
  - `AccountUuid`, `OrganizationUuid` (the key)
  - `Email`, `Plan` (from `subscriptionType`)
  - `CredentialsJson`: the entire `.credentials.json` content. Only the refresh fields (`accessToken`, `refreshToken`, `expiresAt`, `scopes`) are ever changed, by splice; unknown fields survive byte-for-byte.
  - `OAuthAccountJson`: the `oauthAccount` object from the global config, verbatim.
  - `AddedAt` (defines the display order), `LastActiveAt`
  - `LastKnownUsage` + `LastKnownAt`
  - `LoginState` (valid / expired by `invalid_grant`)
  - `ExpiryWarnedFor` (the `refreshTokenExpiresAt` value already warned about, so a new login re-arms the warning)
- Usage data is not secret but lives in the same file for simplicity.
- If the file cannot be decrypted or parsed (another Windows user, corruption, a newer format), rename it to `accounts.dat.unreadable-<UTC timestamp>`, start with an empty store, and show one toast. Never overwrite it.

## Locking (cooperate with Claude Code)
Claude Code uses npm `proper-lockfile`: a lock is a **directory**, and `mkdir` atomicity is the mutex.

| Lock | Path | Stale after |
|---|---|---|
| OAuth refresh (primary) | `<config-home>\.oauth_refresh.lock` | 60 s |
| Credentials (legacy) | `<config-home-parent>\<config-home-name>.lock`, i.e. `%USERPROFILE%\.claude.lock` | 60 s |
| Credential storage write | `<config-home>\.storage-write.lock` | 15 s |
| Global config | `<global-config-path>.lock`, i.e. `%USERPROFILE%\.claude.json.lock` | 10 s |

Rules:
- Order: OAuth refresh primary, then legacy, then storage-write (held only around reading for capture and writing `.credentials.json`), then global config. Release in reverse order.
- Never write, change or delete `.oauth_refresh.lock.owner`. The app writes no owner record, so Claude Code cannot take over the app's lock early; only the 60 s stale rule applies. When the app takes over a stale lock, any leftover owner record no longer matches the new lock's birthtime and is ignored by Claude Code.
- Acquire with a create that fails if the directory exists (Win32 `CreateDirectory` return value; `Directory.CreateDirectory` alone succeeds silently).
- Take over a lock only if its mtime is older than its staleness threshold. Never steal a younger lock.
- While holding, touch the directory mtime every ~3 s.
- Timeout ~9 s per lock, then fail cleanly with "Claude Code is refreshing its login. Try again in a few seconds." Files must be untouched on timeout.
- Credential locks: acquire primary, then legacy. Release in reverse order, always in `finally`.
- Keep hold times short (target under 1 s): no network calls while holding any lock.
- Inject the clock and filesystem operations so lock logic is unit-testable without real waiting.

## Capture verification
Before any live credentials are attributed to an account (auto-save, switch):
1. `organizationUuid` at the top level of `.credentials.json` must equal `oauthAccount.organizationUuid` (skip if the credentials file has no such field).
2. The live `refreshToken` must not equal the `refreshToken` of any *other* stored account.
3. Profile check, **outside all locks**: if the live access token is not expired, call the profile endpoint; `account.uuid` must equal `oauthAccount.accountUuid` and `organization.uuid` (when present) must equal `oauthAccount.organizationUuid`.
4. Under both OAuth refresh locks and the storage-write lock, re-read both files. The credentials file and the `oauthAccount` value must be byte-identical to what was verified; otherwise abort and retry later. The rest of the global config is not compared, because Claude Code rewrites it constantly.

**Byte-match rule:** live credentials that are byte-identical to a stored snapshot need no capture and no profile check. If that snapshot belongs to the identity in `oauthAccount`, only a changed `oauthAccount` value is saved; if it belongs to another account, nothing is captured.

If the profile check cannot run (expired token, offline, 401, schema without `account.uuid`), treat the capture as unverified:
- Auto-save of a **new** account waits for the next attempt.
- Updating an **already stored** account may proceed on checks 1, 2 and 4 (identity is already known; Claude Code may simply have rotated the token).

Any failed check: write nothing, show "Login files are changing; will retry" in the status line.

## Auto-save
- Triggers: every poll of the active account (10 min), and a `FileSystemWatcher` on `.credentials.json` and the global config, debounced ~2 s.
- New identity that passes verification: insert, toast "Added <email> to Claude Meter".
- Known identity with changed credentials: update its snapshot silently.
- The active account shows "Not saved yet: waiting for network to verify" while a new identity cannot be verified.

## Switch transaction
One click on **Switch** for account T, while account A is active. T is in the store and its login is not expired.
1. Outside locks: if T's access token is expired or within 5 minutes of `expiresAt`, refresh T first (the app still owns T), but only when background refresh is allowed (toggle on, not globally disabled); otherwise Claude Code refreshes T itself after the switch. `invalid_grant` marks T as login expired and aborts. If Claude Code has no signed-in account (a file or `oauthAccount` missing), abort with "Claude Code has no signed-in account. Run /login in Claude Code first." Then run the profile check (Capture verification step 3) on A's live credentials, unless the byte-match rule applies.
2. Acquire both OAuth refresh locks, then the storage-write lock.
3. Re-read the live credentials and the active identity; they must match step 1 (Capture verification step 4). If the byte-match rule applies, nothing is captured. Otherwise attribute them to A (Capture verification): update A's `CredentialsJson` if A is stored, or insert A if it passed the profile check in step 1; otherwise abort with "Current account isn't saved yet (couldn't verify it). Check your connection and try again." **Save the store now**: A's snapshot must be on disk before its only live copy is overwritten. If this save fails, abort with nothing changed.
4. Keep the current `.credentials.json` content in memory for rollback (never a plaintext copy on disk).
5. Write T's `CredentialsJson` to `.credentials.json` atomically. Keep the storage-write lock until step 7 is settled, so a rollback write is covered too.
6. Acquire the config lock. Re-read the global config, splice T's `OAuthAccountJson` over `oauthAccount`, write atomically. Release the config lock.
7. If step 6 fails, restore the in-memory content to `.credentials.json` and report the error. If the restore also fails, report "Couldn't finish the switch or undo it. Switch to <A> again from the panel." (A is stored, so the byte-match rule lets that switch repair the files.)
8. Save the store (`LastActiveAt` updates). Release the storage-write lock, then both OAuth refresh locks.
9. Post-check: re-read both files and confirm they now hold T. On mismatch, show a warning toast and do not capture anything until the next verified capture.
10. Toast: "Switched to <email>. Reopen Claude Code to use it. Running sessions may move to this account at their next token refresh."
11. Trigger an immediate usage fetch for all accounts.

From step 5 on, Claude Code owns T and the app owns A.

## Removing accounts
- In Settings: **Remove** turns the row into an inline confirmation ("Remove <email> from Claude Meter? Claude Code is not changed." · Cancel · Remove). No modal dialog.
- Remove deletes the snapshot from the store only. It never touches Claude Code's files. Removing the active account is allowed; auto-save will capture it again on the next poll, so Settings says so for that row.

## Token refresh (inactive accounts)
- Only when "Keep inactive accounts live" is on (default on) and refresh is not globally disabled.
- When: the account is inactive, its access token is expired or within 5 minutes of `expiresAt`, and a poll for it is due (or it is a switch target, see Switch step 1).
- Right before sending, re-read the active identity; never refresh the account Claude Code currently holds.
- One refresh in flight per account. A switch to that account waits for it.
- Request: `POST https://platform.claude.com/v1/oauth/token`, JSON `{grant_type: "refresh_token", refresh_token, client_id}`, 10 s timeout, redirects disabled, `User-Agent: ClaudeMeter/<version>`.
- Response: splice `accessToken`, `expiresAt` (now + `expires_in`), `refreshToken` (only if returned), `scopes` (only if returned) into the snapshot. Do not change `refreshTokenExpiresAt`. Save the store immediately.
- If the save fails: keep the new token in memory, retry the save on every poll, show a warning, and send no further refresh for that account until the save succeeds.
- Errors:
  - `invalid_grant`: the login is dead. Mark it expired; stop refreshing and polling it.
  - `invalid_client`: the client is rejected. Disable refresh for all accounts until the app restarts, toast once "Background refresh stopped", fall back to last-known usage.
  - 429, 5xx, network: transient, back off with the existing `RefreshPolicy` rules.
- Known risk: if a refresh response is lost after the server already rotated the refresh token (for example a timeout), that refresh token is spent and the account needs `/login` again.

## Usage per account
- One `UsageService` + `RefreshPolicy` per account (rate limits are per token). 10-minute poll, 5-minute manual floor, 429 backoff. One shared timer schedules the earliest due poll.
- **Active account**: unchanged behavior. Token read fresh from `.credentials.json` on every poll. Never refreshed by the app.
- **Inactive accounts**: stored access token, refreshed as above. A 401 triggers one refresh and one retry.
- **Without refresh** (toggle off or globally disabled): poll inactive accounts only until `expiresAt` or a 401, then show last known usage with its age.
- **Reset inference**: for any window whose `resets_at` is in the past and later than the time its data was fetched, show `≈0%` with "reset" in place of the time (this also covers live data fetched shortly before a reset).
- **After a switch**, if the new active account returns 401: show the existing "token expired, open Claude Code" state, plus "If Claude Code asks you to log in, run `/login` with this account."
- "Refresh" in the header refreshes every pollable account, each subject to its own manual floor.

## Login expiry
- From each snapshot's `refreshTokenExpiresAt`:
  - More than 3 days left: "Login valid N days" (muted).
  - 3 days or less: "Login expires in N days" (caution color, warning glyph).
  - Past, or `invalid_grant`: "Login expired" in place of Switch, status "Run /login in Claude Code with this account" (critical color). Switch is disabled.
- One toast per account and `refreshTokenExpiresAt` value when 2 days or less remain: "<email>: login expires in 2 days. Switch to it and run /login in Claude Code to renew."
- If the field is missing: show "Last active N days ago", highlighted above 14 days.

## UI
Follow the approved mockup. Windows 11 native look, performance first.

**Tray**
- Icon: unchanged, shows the active account's 5-hour usage.
- No tooltip (`NotifyIcon.Text` stays unset).
- Hover: shows the panel after 300 ms and hides it 400 ms after the cursor leaves (as today). The panel never takes focus.
- Left or right click: toggles **pinned**. A pinned panel is a widget: it stays on top while I work in other apps, never takes focus, and closes only on another icon click or the pin button. Hover does nothing while pinned.
- Remove the context menu (`ContextMenuStrip` and the reflection call to `ShowContextMenu`).

**Main view**
- Header: "Claude Meter", Refresh, Settings (gear), Pin (filled in the accent color when pinned). Opening Settings from a hover panel pins it.
- One uniform row per account, in `AddedAt` order. Nothing reorders and nothing moves on switch; only the Active marker moves.
- Row: email (ellipsis) + plan badge; right side is either "Active" (accent text, plus a 3×16 px accent pill on the row's left edge and a stronger row fill), a **Switch** button, or "Login expired"; 5h and 7d bars with percentage and local reset time; a status line with live/last-known/switching/error text on the left and login validity on the right.

**Settings view** (same window, back arrow)
- Accounts: email, plan, login validity, Remove with inline confirmation.
- Hint: "To add an account, run /login in Claude Code with it. Claude Meter saves it automatically. Don't run /logout first: it can revoke the saved login."
- Toggles: "Keep inactive accounts live" (Refreshes their tokens in the background), "Start with Windows".
- Link: "Open claude.ai usage page".
- Footer: version and Exit.

**Notifications** (balloon tips, shown as Windows toasts; title and body as in the mockup)
- "Switched to <email>" / "Reopen Claude Code to use it. Running sessions may move to this account at their next token refresh."
- "Added <email>" / "Claude Meter saved this login. Switch accounts from the tray panel."
- "<email>: login expires in N days" / "Switch to it and run /login in Claude Code to renew."
- "Couldn't switch accounts" / <reason>
- "Background refresh stopped" / "Anthropic rejected the token refresh. Inactive accounts now show last known usage."
- "Switch not confirmed" / "Claude Code's login files don't show <email>. Claude Meter saves no logins until it can verify one." (Switch step 9)
- "Saved accounts couldn't be read" / "Claude Meter kept the old file and starts fresh. Accounts are saved again as you use them." (Account store)

**Visual system**
- Backdrop: DWM acrylic (`DWMWA_SYSTEMBACKDROP_TYPE` = transient window) with dark-mode attribute set per theme; solid fallback when transparency effects are off or the backdrop is unavailable. Prove it on a borderless popup in a prototype before building the panel.
- Rounded corners (`DWMWCP_ROUND`), 1 px border, flyout shadow. 360 px logical width.
- Type: Segoe UI Variable (14 px titles and emails, 12 px secondary). Icons: Segoe Fluent Icons glyphs.
- Colors follow the system theme (existing `SystemTheme`) and the system accent color; update on theme change.

| Token | Dark | Light |
|---|---|---|
| Text | `#FFFFFF` | `#1A1A1A` |
| Secondary text | white 79% | black 64% |
| Tertiary text | white 58% | black 58% |
| Row fill / active row | white 3.5% / 8.5% | white 50% / 92% |
| Bar track | white 14% | black 10% |
| OK / caution / critical | `#6CCB5F` / `#FCE100` / `#FF99A4` | `#0F7B0F` / `#9D5D00` / `#C42B1C` |
| Accent (default) | `#60CDFF` | `#005FB8` |

Bar thresholds stay as today: under 50 OK, 50–79 caution, 80 and above critical. The tray icon keeps its own palette.

**Performance**
- One window, fully owner-drawn in a single paint pass; no child controls (the `LinkLabel`s go away). Own hit-testing for buttons.
- Repaint only on data change or when the hovered/pressed control changes.
- Fonts, brushes and measurements cached per DPI and theme.
- No open/close animation in v1.
- Keyboard navigation is out of scope (the panel never takes focus).

## Security (non-negotiable)
- Tokens are sent only to `api.anthropic.com` (usage, profile) and `platform.claude.com` (token refresh). Redirects are disabled.
- The app never refreshes the account Claude Code currently holds.
- Never log, display, or persist a token outside `accounts.dat`.
- Snapshots exist only in `%LOCALAPPDATA%\ClaudeMeter\accounts.dat`, DPAPI CurrentUser encrypted.
- Temp files for atomic writes are created next to their target and never left behind on failure.
- No plaintext credential copy on disk other than Claude Code's own `.credentials.json`. Rollback content lives in memory only.
- No network call while holding any of Claude Code's locks.

## Tests (critical logic only, no UI tests)
- Paths: with/without `CLAUDE_CONFIG_DIR`, legacy `.config.json` present/absent.
- JSON splice: only the target value's bytes change; keys that differ only by case; non-ASCII; no trailing newline; nested target.
- Store: round-trip through a fake protector; `CredentialsJson` and `OAuthAccountJson` preserved byte-for-byte including unknown fields; order by `AddedAt`.
- Locks: acquire/release, takeover only past staleness, timeout leaves files untouched, release on exception, primary → legacy → storage-write → config order, owner record never touched.
- Capture verification: each check passing and failing; byte-identical re-read; unverified new account deferred; stored account updated on checks 1, 2, 4.
- Auto-save: insert with toast, silent update, no write on mismatch.
- Switch: happy path writes both files and refreshes A's snapshot; config write failure rolls back credentials; unverified unknown active account aborts without changes; only `oauthAccount` changes in the global config; expired target is refused; post-check mismatch warns.
- Refresh: fields spliced; refresh token kept when not returned; `refreshTokenExpiresAt` untouched; `invalid_grant` marks expired; `invalid_client` disables globally; transient errors back off; active account never refreshed; failed save blocks further refreshes.
- Usage: per-account scheduling; inactive polling without refresh stops after `expiresAt`/401; reset inference at the `resets_at` boundary.
- Login expiry: valid / ≤3 days / expired states; one toast per `refreshTokenExpiresAt` value; fallback to last-active age above 14 days.

## README and SPEC
Add a short "Multiple accounts" section: add accounts by running `/login` in Claude Code (saved automatically), switching from the panel, reopening Claude Code after switching, never `/logout` an account you want to keep, and periodic `/login` when a login expires. Update key decisions:
- why the app stores encrypted credential snapshots
- why it refreshes tokens of inactive accounts only, its risks (unofficial endpoint, Claude Code's client id), and the "Keep inactive accounts live" switch
- why it takes Claude Code's lock directories, with credit to claude-swap for documenting the protocol
- why captures are verified against the profile endpoint
- limitation: every login expires at `refreshTokenExpiresAt` and needs `/login` again

Also update `docs/SPEC.md`'s Security section so it no longer contradicts this spec.

## Open items
- Observe `refreshTokenExpiresAt` before and after Claude Code's next token refresh to confirm a refresh does not change it (baseline recorded in Spike findings).
- ~~Phase 1, first step: call the profile endpoint once and print only its key names to confirm the schema.~~ Done 2026-10-11, see Spike findings.
- Phase 3, first step: acrylic backdrop on a borderless, non-activating popup. Windows draws system backdrops in full only for active windows, and this panel never activates; test a `WM_NCACTIVATE` override, and draw text with GDI+ (GDI text has no alpha on a composited surface). If acrylic still fails, ask before choosing between the solid fallback and the undocumented `SetWindowCompositionAttribute`.
- Observe whether a Claude Code session that was open during a switch later writes its cached `oauthAccount` back into the global config. Checks 1 and 2 already stop a wrong capture in that case.
