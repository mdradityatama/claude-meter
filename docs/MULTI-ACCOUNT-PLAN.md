# Multi-account Claude Meter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep several Claude Code CLI accounts, show every account's live usage in the tray panel, and switch the active account with one click.

**Architecture:** New `Core` classes do the risky work behind small, testable seams: byte-exact JSON splicing, proper-lockfile-compatible directory locks, a DPAPI-encrypted account store, capture verification, auto-save and a switch transaction. An `AccountsRuntime` owns one `UsageService` per account and serializes every write through one async gate. The UI becomes one owner-drawn Fluent flyout (acrylic if the prototype proves it) with a pinned widget mode and no context menu.

**Tech Stack:** .NET 10 (`net10.0-windows`), WinForms, System.Text.Json (`Utf8JsonReader`), `ProtectedData` (DPAPI), DWM interop, xUnit.

**Spec:** `docs/MULTI-ACCOUNT-SPEC.md` (read it with this plan). Visual reference: mockup artboards `Main`, `Settings`, `States` at https://claude.ai/artifact/MSPQX59sVYANTpJYo1CqpY.

---

## Progress

This plan lives in the repo as `docs/MULTI-ACCOUNT-PLAN.md` (Task 0) so any later session can pick it up. **At the start of every session: read this section, then the first unchecked step.** After each finished step: tick its checkbox (`- [x]`) and update this table. Each phase commit includes this file.

| Phase | Status | Commit |
|---|---|---|
| 0. Plan in repo | done | c0dad18 |
| 1. Core | done | b6dc10c |
| 2. Accounts runtime | done | 3e11572 |
| 3. UI | done (manual tray check pending) | bf9b256 |
| 4. README and SPEC | done | 81ecb70 |
| 5. Merge and release | done (v2.0.0) | 570f08d |

**Next step:** User's manual tray check on the v2.0.0 build (hover, pin, switch with a second account). Open spike items: refreshTokenExpiresAt after Claude Code's next refresh; cached oauthAccount write-back after a switch.

**Decisions and findings during implementation** (append, newest last, one line each with date):

- 2026-10-11: Plan approved. Spec items 1–13 below are the agreed changes to the spec.
- 2026-10-11: Work happens on branch `multi-account` (user's choice); merge after the final review.
- 2026-10-11: Ruling: the executing-plans ledger scripts are missing from this install, so this Progress section is the ledger — cost if wrong: none, the record is the same.
- 2026-10-11: Task 1.0: profile schema confirmed (`account.uuid`, `account.email`, `organization.uuid`); login-expiry baseline recorded in the spec; spec items 1–13 folded into the spec. The after-refresh comparison stays an open item.
- 2026-10-11: Task 1.1: Ruling: FindMember takes the value reader as a `ref` parameter declared by the caller instead of `out` (C# ref-safety error CS8352) — cost if wrong: none, same behavior.
- 2026-10-11: Task 1.1: complete, JsonSpliceTests 15/15.
- 2026-10-11: Task 1.2: complete, full suite 96/96 (ClaudePathsTests, LoginFilesTests added; CredentialsTests removed with the old reader).
- 2026-10-11: Task 1.3: Ruling: `ClaudeJson.Credentials` uses a `$` raw string because the plan's `` form collides with the literal `}}` (CS9007) — cost if wrong: none.
- 2026-10-11: Task 1.3: complete, full suite 109/109 (added Remove and FindByCredentials tests beyond the plan list).
- 2026-10-11: Task 1.4: complete, full suite 118/118.
- 2026-10-11: Task 1.5: complete, full suite 137/137.
- 2026-10-11: Task 1.6: complete, full suite 152/152.
- 2026-10-11: Task 1.7: complete, full suite 169/169 (added Verified_unknown_active_account_is_saved_before_the_switch and Progress_reports_lock_wait_then_writing).
- 2026-10-11: Task 1.8: Ruling: the plaintext check uses the account's email string, not `Select-String '@'`, because random ciphertext bytes can contain '@' — cost if wrong: none.
- 2026-10-11: Task 1.8: live run verified: `accounts.dat` created as a DPAPI blob without email or token; no lock dirs or temp files left. Phase 1 committed.
- 2026-10-11: Task 2.1: complete, full suite 177/177.
- 2026-10-11: Task 2.2: complete, full suite 190/190 (added Holder_reads_claude_codes_files).
- 2026-10-11: Task 2.3: complete, full suite 201/201.
- 2026-10-11: Task 2.4: Ruling: settings-row test dates are relative to the test clock (1 Oct / 24 Oct) instead of the mockup's 13 Oct — cost if wrong: none, same rule.
- 2026-10-11: Task 2.4: complete, full suite 231/231.
- 2026-10-11: Task 2.5: complete, full suite 234/234.
- 2026-10-11: Task 2.6: Ruling: `TokenRefresher` blocks on the new `AccountStore.LastSaveFailed` instead of `HasUnsavedChanges`; the plan's flag was also true for ordinary pending usage updates mid-poll and silently blocked every 401 retry (found by `Inactive_401_with_refresh_retries_once_with_the_new_token`; regression test `Pending_unsaved_changes_do_not_block_refresh`) — cost if wrong: none, matches the spec's 'until the save succeeds'.
- 2026-10-11: Task 2.6: complete, full suite 251/251; live run: store reloads, LastKnownAt persisted, no lock dirs left. Phase 2 committed.
- 2026-10-11: Task 3.1: gate result: Transparency effects are off on this machine, so acrylic always shows DWM's fallback; GDI text breaks on a composited surface. User chose **solid Fluent** (no acrylic). Prototype stayed in the scratchpad; nothing committed from it.
- 2026-10-11: Task 3.1: Ruling: no `FlyoutWindow` base class and no DWM backdrop/`WM_NCACTIVATE` code — with a solid backdrop the window logic is small and lives in `MeterPanel`; text uses `TextRenderer` (ClearType, as today) and alpha tokens are pre-blended onto the panel color — cost if wrong: a later acrylic attempt re-adds the backdrop code.
- 2026-10-11: Task 3.1: Ruling: accent comes from `AccentPalette` index 1 (dark) and 4 (light), the WinUI mapping (AccentFillColorDefault = Light2 / Dark1); the spec's `#60CDFF`/`#005FB8` stay as the fallback when the value is missing — cost if wrong: accent shade slightly off.
- 2026-10-11: Tasks 3.2–3.5: Ruling: done as one change set (UI has no tests and the phase is one commit); `UsagePanel` was deleted in the same change instead of being kept compiling against a theme shim — cost if wrong: none.
- 2026-10-11: Tasks 3.2–3.5: Ruling: `CS_DROPSHADOW` gives the flyout its shadow; the settings action is `PanelAction.SettingsOpened` (the tray pins on it) — cost if wrong: cosmetic.
- 2026-10-11: Tasks 3.2–3.5: verified by screenshots of the real `MeterPanel` in a scratch harness (main, settings, inline confirm, 10-account overflow, empty; dark and light) and a 12 s smoke run of the app; full suite 241/241 (10 old panel/menu tests removed with the old panel). Hover, pin and a real switch need the user (only one account on this machine). Phase 3 committed.
- 2026-10-11: Task 4.1: README rewritten (multiple accounts, key decisions, troubleshooting); docs/SPEC.md Security and 401 rule now defer to MULTI-ACCOUNT-SPEC.md. Ruling: claude-swap is credited by name without a link, because no verified URL is known — cost if wrong: none.
- 2026-10-11: Task 4.1: refreshTokenExpiresAt unchanged at 2026-10-10 22:29Z, but Claude Code had not refreshed yet (expiresAt 2026-10-11 04:01Z); the item stays open. Phase 4 committed.
- 2026-10-11: Final review (fresh reviewer, Opus): no Critical, 5 Important, 14 Minor; verdict 'with fixes'.
- 2026-10-11: Final: fixed I1 (I/O errors escaping async handlers: crash dialog and polling stopped for good) — Unreadable_file_is_reported_busy_instead_of_throwing, Lock_file_system_error_is_reported_busy_and_releases_locks, Unreadable_login_file_fails_cleanly, Snapshot_without_a_splice_target_sends_nothing, Returned_scopes_are_skipped_when_the_snapshot_has_none RED→GREEN; tray reschedules polling in `finally` and wraps async handlers in a last-resort catch (UI, no unit test); suite 251/251.
- 2026-10-11: Final: fixed I2 (a lock already taken leaked when a later lock threw, blocking Claude Code until exit) — Failure_on_a_later_lock_releases_the_earlier_ones RED→GREEN; suite 251/251.
- 2026-10-11: Final: fixed I3 (byte-match skipped the 'A on disk before overwrite' save when an earlier save had failed) — Unsaved_snapshot_of_the_live_login_is_saved_before_the_switch_or_nothing_changes RED→GREEN; suite 251/251.
- 2026-10-11: Final: fixed I4 (failed-undo message told the user to switch to A, which the panel shows as Active) — Failed_undo_names_the_target_whose_switch_repairs_the_files RED→GREEN; spec step 7 corrected; suite 251/251.
- 2026-10-11: Final: fixed I5 (refresher could spend the token of the account Claude Code really holds when its files disagree) — Holder_is_unknown_when_claude_codes_files_disagree, No_refresh_while_claude_codes_files_disagree_within_one_organization RED→GREEN; spec Token refresh updated; suite 251/251.
- 2026-10-11: Final: Ruling: I4 is fixed by naming T in the message, not by allowing a repair switch to the oauthAccount identity — that account shows as Active without a Switch button, while switching to T is reachable and tested — cost if wrong: the user switches twice to get back to A.
- 2026-10-11: Final: Ruling: the tray's last-resort catch swallows exceptions silently (the app has no log) — cost if wrong: a future bug hides instead of showing a crash dialog.
- 2026-10-11: Final: Ruling: M5 not fixed — `File.Replace` failing with ERROR_UNABLE_TO_MOVE_REPLACEMENT can leave `.credentials.json` deleted and the temp removed; it needs a seam to test and is very rare — cost if wrong: Claude Code is logged out and needs /login (the previous account's snapshot is still on disk).
- 2026-10-11: Final: minor (deferred): M1 logged-out state shows no 'No credentials' row when saved accounts exist; M2 switch refreshes the target before the NoLogin/AlreadyActive checks; M3 no UI warning when the store can't be saved; M6 Exit during a switch can leave a half-finished state; M7 step 6 catch omits OperationCanceledException (latent, only None is passed); M8 record ToString prints tokens (nothing logs them); M9 AccountStore.Load IOException at startup crashes the app; M10 lock mtime race returns 1601 if the directory vanishes; M11 measure cache unbounded and relayout on every Changed; M12 several toasts in one poll replace each other; M13 byte-match auto-save path resets LoginState; M14 Review Focus 2 says a BOM is refused but it is kept (better).
- 2026-10-11: User chose to merge `multi-account` into master before the manual tray check and release it as v2.0.0; all other branches (`feat/tray-app`, `chore/rename-claude-meter`, `multi-account`) were already contained in master and were deleted locally and on origin. Suite 251/251 before the merge.

## Context

Today Claude Meter reads one access token from `.credentials.json` and shows one account's 5-hour/7-day usage. The user has several Claude Code logins (and wants Team/multi-org safety for other users). The spec (spike done 2026-10-11) defines how to save each login as an encrypted snapshot, poll every account, refresh tokens of inactive accounts, and swap Claude Code's global login files under Claude Code's own locks. This plan turns that spec into phases with one commit each, as the spec's Workflow requires.

## Spec items to reconsider (my decisions; approve or overrule)

1. **Check 4 must not compare the whole `.claude.json`.** Claude Code rewrites that 100 KB file constantly (startups, project stats), and auto-save does not hold the config lock. A whole-file byte compare would fail almost every capture. Decision: compare `.credentials.json` bytes plus the bytes of the `oauthAccount` value only.
2. **Save A to disk before overwriting `.credentials.json`.** The spec saves the store in step 8, after the overwrite. If that save fails or the app dies in between, A's only token copy is gone. Decision: when A needs capturing, upsert and save the store inside step 3; abort the switch if that save fails. Step 8 keeps only `LastActiveAt` bookkeeping.
3. **Byte-match rule.** Live credentials that byte-equal a stored snapshot need no capture and no profile call. This also makes a half-finished switch (crash between steps 5 and 6: credentials = T, `oauthAccount` = A) repairable by clicking Switch; otherwise check 2 would block every later capture and switch forever.
4. **Acrylic on a window that never takes focus.** Windows 11 draws system backdrops in full only for active windows; inactive windows get the fallback color. The panel is `WS_EX_NOACTIVATE`, so it is always inactive. Also, GDI text (`TextRenderer`) writes zero alpha and turns invisible on a composited surface. The prototype (Task 3.1) tests a `WM_NCACTIVATE` override and GDI+ grayscale text. If acrylic still fails, I stop and ask you: solid Fluent fallback (my recommendation, performance first) or the undocumented `SetWindowCompositionAttribute` API.
5. **"Keep inactive accounts live" off also skips the switch-time refresh of the target.** Strict reading of "only when the toggle is on". Claude Code refreshes T itself after the switch.
6. **`invalid_client` disables refresh until the app restarts** (in memory only). The spec does not say whether it persists.
7. **New README risk:** a refresh whose response is lost (timeout after the server already rotated the token) spends the refresh token; that account then needs `/login`. Rotating tokens make this unavoidable.
8. **Running Claude Code sessions may write their cached `oauthAccount` back after a switch** (not verified). Checks 1 and 2 already stop a wrong capture; the row then shows "Login files are changing; will retry" until Claude Code is reopened. No spec change; added to the observation list in Task 1.0.
9. **Reset inference rule:** show `≈0%`/"reset" when `resets_at` has passed and the data was fetched before `resets_at`. This also covers "live" data fetched minutes before a reset, which the spec's "while data is not live" misses.
10. **Unreadable `accounts.dat`** (other Windows user, corruption, newer format): rename it to `accounts.dat.unreadable-<UTC stamp>`, start empty, toast once. Never overwrite it.
11. **No signed-in Claude Code** (a file or `oauthAccount` missing): Switch is refused with "Claude Code has no signed-in account. Run /login in Claude Code first." Nothing is written.
12. **Toast copy follows the mockup's title + body pairs.** Two new toasts: "Switch not confirmed" (post-check mismatch) and "Saved accounts couldn't be read" (item 10).
13. **Commits:** one commit per phase, as the spec says (not one per task). No AI attribution trailers.

## Global Constraints

- .NET 10, `net10.0-windows`, WinForms, no third-party NuGet in the app, xUnit in tests, single instance, self-contained single-file exe.
- Encryption: DPAPI via `ProtectedData` with `DataProtectionScope.CurrentUser` and a constant app-specific entropy, behind an interface so tests use a fake.
- Tokens are sent only to `api.anthropic.com` (usage, profile) and `platform.claude.com` (token refresh). Redirects are disabled.
- The app never refreshes the account Claude Code currently holds.
- Never log, display, or persist a token outside `accounts.dat`. Exception messages never contain token text.
- Snapshots exist only in `%LOCALAPPDATA%\ClaudeMeter\accounts.dat`, DPAPI CurrentUser encrypted.
- Temp files for atomic writes are created next to their target and never left behind on failure.
- No plaintext credential copy on disk other than Claude Code's own `.credentials.json`. Rollback content lives in memory only.
- No network call while holding any of Claude Code's locks.
- Every mutation of Claude Code's files happens under Claude Code's own locks. Edits to Claude Code's JSON files are byte splices.
- UI: Windows 11 native look, one owner-drawn window, no child controls, no open/close animation, performance first.
- One commit per phase, tests green at each step. Commit messages carry no `Co-Authored-By`/`Claude-Session` lines.
- Store record updates follow one rule: `Find` → `with` → `Upsert` with no `await` in between, so concurrent async flows on the UI thread never overwrite each other with stale copies.

## Review Focus

1. **Undecryptable `accounts.dat`** (another Windows user, reinstall, corruption): expect the file set aside, an empty store, one toast, and no overwrite. Test in Task 1.3 (`Unreadable_store_is_set_aside_never_overwritten`).
2. **Duplicate `oauthAccount` key or a BOM in Claude's JSON:** expect the splice refused (`FormatException`, surfaced as "Login files are changing"), never an edit of the wrong occurrence. Tests in Task 1.1 (`Duplicate_name_on_the_path_is_refused`, `Leading_bom_is_skipped_and_kept`).
3. **Half-finished switch** (credentials = T, `oauthAccount` = A): expect Switch to repair it and A never lost. Test in Task 1.7 (`Half_finished_switch_is_repaired_by_switching_again`).
4. **Re-login of an expired account with `/login`:** expect `LoginState` back to Valid and the expiry warning re-armed. Tests in Task 1.6 (`Known_login_update_clears_expired_state`) and Task 2.4 (`New_login_expiry_value_re_arms_the_warning`).
5. **Claude Code logged out** (no credentials or no `oauthAccount`): expect "No credentials" on the active row, Switch refused, nothing written. Tests in Task 1.6 (`No_login_writes_nothing`) and Task 1.7 (`Switch_without_signed_in_claude_code_is_refused`).

## File map

**New, `src/ClaudeMeter/Core/`**

| File | Responsibility |
|---|---|
| `ClaudePaths.cs` | All paths (config home, credentials, global config, 4 lock dirs, app store, settings). Replaces `CredentialsLocator`. |
| `JsonSplice.cs` | Find a property value's byte span with `Utf8JsonReader`; replace exactly those bytes; encode string/number/array values. |
| `LoginFiles.cs` | `CredentialsInfo`, `OAuthAccount`, `AccountKey`, `LiveFiles`, `LiveLogin`: read and parse Claude Code's two files. Replaces `CredentialsReader`. |
| `AtomicFile.cs` | Temp file in the target folder + `File.Replace`/`File.Move`; temp always removed. |
| `DataProtector.cs` | `IDataProtector` + `DpapiProtector`. |
| `AccountStore.cs` | `StoredAccount`, `LoginState`, `AccountStore` (load, set aside, upsert, remove, save). |
| `DirectoryLocks.cs` | `ILockFileSystem`, `LockFileSystem` (Win32 `CreateDirectoryW`), `LockSpec`, `DirectoryLocks`, `HeldLock(s)`, `LockTimeoutException`, `ClaudeLocks`. |
| `ProfileClient.cs` | `IProfileClient`, `ProfileClient`, `ProfileParser`, `ProfileResult`, `ProfileCheck`. |
| `CaptureVerifier.cs` | Checks 1–3 of Capture verification as a pure function. |
| `AutoSaver.cs` | Capture the active login (insert / silent update / defer / reject). |
| `AccountSwitcher.cs` | The switch transaction, plus `ITokenRefresher` and `RefreshOutcome`. |
| `OAuthTokenClient.cs` | Phase 2. Token endpoint client + `TokenResponseParser`. |
| `TokenRefresher.cs` | Phase 2. Refresh inactive accounts, splice, save, error policy. |
| `AccountTokenSource.cs` | Phase 2. Live token while active, saved token (with refresh) while inactive. |
| `LoginExpiry.cs` | Phase 2. Login validity text/tone and toast decision. |
| `Notifications.cs` | Phase 2. Toast title/body copy. |
| `AppSettings.cs` | Phase 2. `settings.json` ("Keep inactive accounts live"). |
| `AccountsRuntime.cs` | Phase 2. Accounts, active identity, per-account usage, scheduling, auto-save/switch/remove, `OperationGate`. |
| `AppInfo.cs` | Phase 2. Version string for `User-Agent` and the Settings footer. |

**New, `src/ClaudeMeter/UI/`**: `ClaudeFilesWatcher.cs` (Phase 1), `FlyoutWindow.cs`, `FluentTheme.cs`, `MeterPanel.cs` (Phase 3).

**Changes per existing file**

| File | Change |
|---|---|
| `Core/CredentialsLocator.cs` | Deleted (Phase 1); `ClaudePaths` replaces it. |
| `Core/CredentialsReader.cs` | Deleted (Phase 1); `CredentialsInfo.Parse` + `LiveFiles.ReadAccessToken` replace it. |
| `Core/UsageService.cs` | Phase 2: takes an `IAccessTokenSource`; one renew + retry after a 401; `NotLive` path; `Seed` for last known usage. Old constructor kept, so existing tests stay. |
| `Core/UsageState.cs` | Phase 2: adds `UsageStatus.NotLive`. |
| `Core/DisplayFormatter.cs` | Phase 2: adds `AccountRow`, `SettingsRow`, `PlanBadge`, `Age`, reset inference. Phase 3: removes `Panel`, `PanelRow`, `PanelModel`, `MenuLines`. `Icon`, `LevelFor`, `FormatTime` stay. |
| `Core/RefreshPolicy.cs`, `UsageClient.cs`, `UsageParser.cs`, `UsageSnapshot.cs` | Unchanged. `RefreshPolicy` is reused for token-refresh backoff. |
| `UI/TrayApplicationContext.cs` | Phase 1: auto-save + watcher + "Added" toast. Phase 2: uses `AccountsRuntime`. Phase 3: no context menu, click toggles pin, new panel, all toasts, theme events. |
| `UI/UsagePanel.cs` | Deleted (Phase 3); `MeterPanel` replaces it. |
| `UI/SystemTheme.cs` | Phase 3: returns `FluentTheme` tokens (spec table + system accent + transparency flag). |
| `UI/NativeMethods.cs` | Phase 3: DWM backdrop/dark-mode attributes, `DwmExtendFrameIntoClientArea`, `MARGINS`, `WM_NCACTIVATE`. |
| `UI/Palette.cs`, `PanelPlacement.cs`, `StartupRegistration.cs`, `TrayIconBounds.cs`, `TrayIconRenderer.cs`, `Program.cs`, csproj | Unchanged. |
| `tests/.../Fakes.cs` | Fake clock gains timers; new fakes: protector, lock file system, profile client, token refresher, token client; usage client gains a per-token responder. |
| `tests/.../CredentialsTests.cs` | Replaced by `ClaudePathsTests.cs` + `LoginFilesTests.cs` (same cases moved). |
| `tests/.../DisplayFormatterTests.cs` | Phase 2 adds row tests; Phase 3 removes `Panel_*` and `Menu_*` tests. |
| `README.md`, `docs/SPEC.md`, `docs/MULTI-ACCOUNT-SPEC.md` | Phase 1 commits the spec with items 1–13 folded in; Phase 4 updates README and SPEC. |

## Switch sequence (final, with items 1–3 applied)

Account A active, target T stored and not expired. Everything below runs inside the runtime's `OperationGate`.

1. **Outside locks.** Refuse if T's login is expired. `ITokenRefresher.EnsureFreshAsync(T)` (refreshes if `expiresAt` is within 5 minutes, toggle and global disable permitting); `LoginExpired` aborts. Read live files → `before`. Refuse if no login, or if `before` is already T. If `before`'s credentials byte-match no stored snapshot, run the profile check on them.
2. Acquire OAuth refresh primary + legacy, then storage-write.
3. Re-read → `live`. `live` must equal `before` (credentials bytes + `oauthAccount` bytes), else abort "Login files are changing". If `live`'s credentials byte-match a stored snapshot: no capture. Otherwise run `CaptureVerifier` (Defer → "Current account isn't saved yet…", Reject → "Login files are changing…"), upsert A, and **save the store now**; save failure aborts with nothing changed.
4. Keep `live` credentials bytes in memory for rollback.
5. `AtomicFile.Write(.credentials.json, T.CredentialsJson)`.
6. Acquire config lock, re-read `.claude.json`, splice `T.OAuthAccountJson` over `oauthAccount`, atomic write, release.
7. If step 6 fails: write the rollback bytes back; report. If the rollback write also fails: "Couldn't finish the switch or undo it. Switch to <A> again from the panel." (A is stored, so the byte-match rule repairs it.)
8. `LastActiveAt` for T and A (re-found, not stale copies); `TrySave`. Release storage-write, then both refresh locks.
9. Post-check outside locks: credentials bytes = `T.CredentialsJson` and `oauthAccount` key = T, else `Unconfirmed` → warning toast and `AutoSaver.RequireProfile = true`.
10. Toast "Switched to <email>" with the body from the mockup.
11. Manual poll of all accounts (each subject to its own floor).

---

# Phase 0: Plan in the repo

### Task 0: Store the plan and spec for tracking

**Files:**
- Create: `docs/MULTI-ACCOUNT-PLAN.md` (this plan, verbatim)
- Add: `docs/MULTI-ACCOUNT-SPEC.md` (currently untracked)
- Create: memory `multi-account-plan.md` + one line in `MEMORY.md`

- [x] **Step 1:** Copy this plan to `docs/MULTI-ACCOUNT-PLAN.md`. Set Progress row 0 to "done" and **Next step** to "Task 1.0, Step 1".
- [x] **Step 2:** Save a project memory: "Multi-account work is tracked in `docs/MULTI-ACCOUNT-PLAN.md` (Progress section at the top); read it first when continuing" and add its pointer to `MEMORY.md`.
- [x] **Step 3: Commit**

```powershell
git add docs/MULTI-ACCOUNT-PLAN.md docs/MULTI-ACCOUNT-SPEC.md
git commit -m "Add the multi-account spec and implementation plan" -m "The plan carries a Progress section that is updated after every step, so work can continue in a later session."
```

A commit cannot contain its own hash, so each phase's hash is written into the table as part of the next commit.

**Tracking rule for every later task:** after each step, tick it in `docs/MULTI-ACCOUNT-PLAN.md`; when a phase is committed, set its row to "done" with the short hash, and set **Next step**. Any deviation from the plan (a changed signature, a gate result, a spike finding) gets one dated line under "Decisions and findings". Every phase commit below also stages `docs/MULTI-ACCOUNT-PLAN.md`.

---

# Phase 1: Core

Commands used throughout (repo root, PowerShell):
- Build: `dotnet build`
- One test class: `dotnet test --filter "FullyQualifiedName~<ClassName>"`
- All tests: `dotnet test`

### Task 1.0: Open items and spec update

**Files:** Modify `docs/MULTI-ACCOUNT-SPEC.md`

- [x] **Step 1: Confirm the profile schema (open item).** Prints key paths only, never values or the token:

```powershell
$t = (Get-Content "$env:USERPROFILE\.claude\.credentials.json" -Raw | ConvertFrom-Json).claudeAiOauth.accessToken
$r = Invoke-RestMethod https://api.anthropic.com/api/oauth/profile -Headers @{ Authorization = "Bearer $t"; 'anthropic-beta' = 'oauth-2025-04-20' }
function Keys($o, $p) { foreach ($n in $o.PSObject.Properties) { "$p$($n.Name)"; if ($n.Value -is [pscustomobject]) { Keys $n.Value "$p$($n.Name)." } } }
Keys $r ''
```
Expected: the list contains `account.uuid`, `account.email`, `organization.uuid`. If `anthropic-beta` is not needed or another key name appears, adjust `ProfileParser` (Task 1.5) to match.

- [x] **Step 2: Record the login-expiry baseline (open item).** Prints timestamps only:

```powershell
$o = (Get-Content "$env:USERPROFILE\.claude\.credentials.json" -Raw | ConvertFrom-Json).claudeAiOauth
"expiresAt=$([DateTimeOffset]::FromUnixTimeMilliseconds($o.expiresAt)) refreshTokenExpiresAt=$([DateTimeOffset]::FromUnixTimeMilliseconds($o.refreshTokenExpiresAt))"
```
Run again after Claude Code's next refresh (about 8 h later). Expected: `expiresAt` moves, `refreshTokenExpiresAt` does not.

- [x] **Step 3: Fold "Spec items to reconsider" 1–13 into the spec** (Capture verification step 4, Switch transaction steps 1/3/8, Token refresh, Usage reset inference, Notifications, Account store), and add item 8 to Open items. Record Step 1's result under Spike findings.

### Task 1.1: JsonSplice

**Files:**
- Create: `src/ClaudeMeter/Core/JsonSplice.cs`
- Test: `tests/ClaudeMeter.Tests/JsonSpliceTests.cs`

**Interfaces:**
- Produces: `JsonSplice.Find(ReadOnlySpan<byte> json, params string[] path) → (int Start, int Length)?`; `JsonSplice.Replace(ReadOnlySpan<byte> json, ReadOnlySpan<byte> newValue, params string[] path) → byte[]`; `EncodeString(string) → byte[]`, `EncodeNumber(long) → byte[]`, `EncodeStringArray(IReadOnlyList<string>) → byte[]`. Throws `FormatException` for invalid JSON, a non-object on the path, a duplicate target name, or (Replace) a missing path.

- [x] **Step 1: Write the failing tests**

```csharp
using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class JsonSpliceTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Replaces_only_the_value_bytes()
    {
        var json = U("""{"a": 1, "oauthAccount": {"x": "old"} ,"z":[1,2]}""");

        Assert.Equal(U("""{"a": 1, "oauthAccount": {"y":2} ,"z":[1,2]}"""), JsonSplice.Replace(json, U("""{"y":2}"""), "oauthAccount"));
    }

    [Fact]
    public void Span_of_a_string_includes_its_quotes()
    {
        var json = U("""{"claudeAiOauth":{"accessToken":"old","n":5}}""");

        var (start, length) = JsonSplice.Find(json, "claudeAiOauth", "accessToken")!.Value;

        Assert.Equal("\"old\"", Encoding.UTF8.GetString(json, start, length));
    }

    [Fact]
    public void Matches_names_case_sensitively()
    {
        var json = U("""{"projects":{"D:/x":{"n":1},"d:/x":{"n":2}},"OAuthAccount":0,"oauthAccount":1}""");

        Assert.Equal(
            U("""{"projects":{"D:/x":{"n":1},"d:/x":{"n":9}},"OAuthAccount":0,"oauthAccount":1}"""),
            JsonSplice.Replace(json, U("""{"n":9}"""), "projects", "d:/x"));
        Assert.Equal(
            U("""{"projects":{"D:/x":{"n":1},"d:/x":{"n":2}},"OAuthAccount":0,"oauthAccount":7}"""),
            JsonSplice.Replace(json, U("7"), "oauthAccount"));
    }

    [Fact]
    public void Keeps_raw_non_ascii_and_a_missing_trailing_newline()
    {
        var json = U("{\n  \"name\": \"Dimas Ü ✓\",\n  \"n\": 1\n}");

        Assert.Equal(U("{\n  \"name\": \"Dimas Ü ✓\",\n  \"n\": 22\n}"), JsonSplice.Replace(json, U("22"), "n"));
    }

    [Fact]
    public void Keeps_a_trailing_newline()
    {
        Assert.Equal(U("{\"n\": 2}\n"), JsonSplice.Replace(U("{\"n\": 1}\n"), U("2"), "n"));
    }

    [Fact]
    public void Replaces_a_nested_pretty_printed_object()
    {
        var json = U("{\n  \"a\": 1,\n  \"oauthAccount\": {\n    \"accountUuid\": \"x\"\n  },\n  \"b\": 2\n}");
        var value = U("{\n    \"accountUuid\": \"y\",\n    \"emailAddress\": \"t@example.com\"\n  }");

        Assert.Equal(
            U("{\n  \"a\": 1,\n  \"oauthAccount\": {\n    \"accountUuid\": \"y\",\n    \"emailAddress\": \"t@example.com\"\n  },\n  \"b\": 2\n}"),
            JsonSplice.Replace(json, value, "oauthAccount"));
    }

    [Fact]
    public void Leading_bom_is_skipped_and_kept()
    {
        byte[] json = [0xEF, 0xBB, 0xBF, .. U("""{"n":1}""")];

        Assert.Equal([0xEF, 0xBB, 0xBF, .. U("""{"n":2}""")], JsonSplice.Replace(json, U("2"), "n"));
    }

    [Fact]
    public void Missing_path_is_null() => Assert.Null(JsonSplice.Find(U("""{"a":{}}"""), "a", "b"));

    [Fact]
    public void Replace_throws_when_the_path_is_missing() =>
        Assert.Throws<FormatException>(() => JsonSplice.Replace(U("{}"), U("1"), "a"));

    [Fact]
    public void Duplicate_name_on_the_path_is_refused() =>
        Assert.Throws<FormatException>(() => JsonSplice.Find(U("""{"oauthAccount":{},"oauthAccount":{}}"""), "oauthAccount"));

    [Fact]
    public void Non_object_on_the_path_is_refused() =>
        Assert.Throws<FormatException>(() => JsonSplice.Find(U("""{"a":[1]}"""), "a", "b"));

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Invalid_documents_are_refused(string json) =>
        Assert.Throws<FormatException>(() => JsonSplice.Find(U(json), "a"));

    [Fact]
    public void Encoded_values_are_plain_json()
    {
        Assert.Equal("\"sk-ant_x+1\"", Encoding.UTF8.GetString(JsonSplice.EncodeString("sk-ant_x+1")));
        Assert.Equal("1791700000000", Encoding.UTF8.GetString(JsonSplice.EncodeNumber(1791700000000)));
        Assert.Equal("[\"a\",\"b\"]", Encoding.UTF8.GetString(JsonSplice.EncodeStringArray(["a", "b"])));
    }
}
```

- [x] **Step 2: Run, expect FAIL** (`JsonSplice` does not exist): `dotnet test --filter "FullyQualifiedName~JsonSpliceTests"`

- [x] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeMeter.Core;

/// <summary>
/// Replaces one property value in a JSON document without re-serializing it: every byte outside the
/// value's span stays identical. Names match case-sensitively; a duplicate target name is refused,
/// because JavaScript's JSON.parse would use the last one and the edit could hit the wrong copy.
/// </summary>
internal static class JsonSplice
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <returns>Start and length of the value (strings include their quotes), or null if absent.</returns>
    /// <exception cref="FormatException">Invalid JSON, a non-object on the path, or a duplicate name.</exception>
    public static (int Start, int Length)? Find(ReadOnlySpan<byte> json, params string[] path)
    {
        var offset = json.StartsWith(Bom) ? Bom.Length : 0;
        try
        {
            var reader = new Utf8JsonReader(json[offset..]);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new FormatException("JSON root is not an object.");

            (int Start, int Length) span = default;
            for (var i = 0; i < path.Length; i++)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new FormatException($"'{path[i - 1]}' is not an object.");
                if (!FindMember(ref reader, path[i], out var atValue, out span))
                    return null;
                reader = atValue;
            }

            return (span.Start + offset, span.Length);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Invalid JSON.", ex);
        }
    }

    /// <exception cref="FormatException">As <see cref="Find"/>, or the path is absent.</exception>
    public static byte[] Replace(ReadOnlySpan<byte> json, ReadOnlySpan<byte> newValue, params string[] path)
    {
        var (start, length) = Find(json, path) ?? throw new FormatException($"'{string.Join('.', path)}' not found.");
        return [.. json[..start], .. newValue, .. json[(start + length)..]];
    }

    public static byte[] EncodeString(string value) => JsonSerializer.SerializeToUtf8Bytes(value, Relaxed);

    public static byte[] EncodeNumber(long value) => Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));

    public static byte[] EncodeStringArray(IReadOnlyList<string> values) => JsonSerializer.SerializeToUtf8Bytes(values, Relaxed);

    /// <summary>Scans every member of the object at <paramref name="reader"/> so a duplicate is noticed.</summary>
    private static bool FindMember(ref Utf8JsonReader reader, string name, out Utf8JsonReader atValue, out (int Start, int Length) span)
    {
        var found = false;
        atValue = default;
        span = default;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var matches = reader.ValueTextEquals(name);
            reader.Read();
            var valueReader = reader;
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            if (!matches)
                continue;
            if (found)
                throw new FormatException($"Duplicate property '{name}'.");
            found = true;
            atValue = valueReader;
            span = (start, (int)reader.BytesConsumed - start);
        }

        return found;
    }
}
```

- [x] **Step 4: Run, expect PASS:** `dotnet test --filter "FullyQualifiedName~JsonSpliceTests"`. If `Span_of_a_string_includes_its_quotes` fails, `TokenStartIndex` excludes the opening quote on this runtime: subtract 1 for `JsonTokenType.String` and re-run.

### Task 1.2: ClaudePaths and login files

**Files:**
- Create: `src/ClaudeMeter/Core/ClaudePaths.cs`, `src/ClaudeMeter/Core/LoginFiles.cs`
- Delete: `src/ClaudeMeter/Core/CredentialsLocator.cs`, `src/ClaudeMeter/Core/CredentialsReader.cs`, `tests/ClaudeMeter.Tests/CredentialsTests.cs`
- Modify: `src/ClaudeMeter/UI/TrayApplicationContext.cs:36-39` (token reader)
- Test: `tests/ClaudeMeter.Tests/ClaudePathsTests.cs`, `tests/ClaudeMeter.Tests/LoginFilesTests.cs`

**Interfaces:**
- Consumes: `JsonSplice.Find`.
- Produces:
  - `record ClaudePaths(string ConfigHome, string Credentials, string GlobalConfig, string OAuthRefreshLock, string LegacyLock, string StorageWriteLock, string GlobalConfigLock, string AppData)` with `Store`, `Settings`, `static Resolve(Func<string,string?> env, Func<string,bool> fileExists, Func<string,string> realPath, string localAppData)`, `static RealPath(string)`, `static ForCurrentUser()`.
  - `readonly record struct AccountKey(string AccountUuid, string OrganizationUuid)`.
  - `record CredentialsInfo(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, DateTimeOffset? RefreshTokenExpiresAt, string? SubscriptionType, string? OrganizationUuid)` with `static Parse(byte[]) → CredentialsInfo?`.
  - `record OAuthAccount(AccountKey Key, string Email, byte[] Raw)` with `static Parse(byte[] globalConfig) → OAuthAccount?` (throws `FormatException` like `JsonSplice.Find`).
  - `record LiveFiles(byte[]? Credentials, byte[]? GlobalConfig)` with `static Read(ClaudePaths)`, `static ReadOrNull(string)`, `static ReadAccessToken(ClaudePaths) → string?`.
  - `record LiveLogin(byte[] CredentialsJson, CredentialsInfo Credentials, OAuthAccount Account)` with `static From(LiveFiles) → LiveLogin?` and `SameAs(LiveLogin) → bool` (credentials bytes and `oauthAccount` bytes equal).

- [x] **Step 1: Write the failing tests.** `ClaudePathsTests` (all use `realPath: p => p`, `localAppData: @"C:\Users\me\AppData\Local"`):
  - `Defaults_to_user_profile`: env has only `USERPROFILE=C:\Users\me`, no files. Expect `ConfigHome=C:\Users\me\.claude`, `Credentials=C:\Users\me\.claude\.credentials.json`, `GlobalConfig=C:\Users\me\.claude.json`, `OAuthRefreshLock=C:\Users\me\.claude\.oauth_refresh.lock`, `LegacyLock=C:\Users\me\.claude.lock`, `StorageWriteLock=C:\Users\me\.claude\.storage-write.lock`, `GlobalConfigLock=C:\Users\me\.claude.json.lock`, `Store=C:\Users\me\AppData\Local\ClaudeMeter\accounts.dat`, `Settings=...\ClaudeMeter\settings.json`.
  - `Blank_config_dir_is_ignored` (Theory `""`, `"   "`): same as default.
  - `Config_dir_moves_everything`: `CLAUDE_CONFIG_DIR=D:\cc`. Expect `ConfigHome=D:\cc`, `GlobalConfig=D:\cc\.claude.json`, `LegacyLock=D:\cc.lock`, `GlobalConfigLock=D:\cc\.claude.json.lock`.
  - `Legacy_config_json_wins_when_present`: `fileExists` returns true for `C:\Users\me\.claude\.config.json`. Expect `GlobalConfig` that path and `GlobalConfigLock` + `.lock`.
  - `Legacy_lock_uses_the_real_path`: `realPath` maps `C:\Users\me\.claude` to `E:\real\claude\`. Expect `LegacyLock=E:\real\claude.lock` (trailing separator trimmed).

  `LoginFilesTests` (moved and new cases):
  - `Parses_credentials`: full `ClaudeJson.Credentials(...)` (Task 1.3 helper; until then inline JSON) gives every field; `expiresAt` ms becomes the right `DateTimeOffset`.
  - `Credentials_without_usable_token_are_null` (Theory: `{}`, `{"claudeAiOauth":null}`, `{"claudeAiOauth":{"accessToken":""}}`, `{"claudeAiOauth":{"accessToken":5}}`, `not json`).
  - `Out_of_range_expiry_is_null`: `expiresAt: 9e18` gives `ExpiresAt == null`, no throw.
  - `Parses_oauth_account_and_keeps_raw_bytes`: config with `oauthAccount` at depth 1; `Raw` equals the exact value bytes; `Key == new AccountKey("acc-a","org-a")`; `Email == "a@example.com"`.
  - `Oauth_account_missing_or_without_uuids_is_null` (Theory: no `oauthAccount`; `oauthAccount` without `organizationUuid`; `oauthAccount: null`).
  - `Duplicate_oauth_account_throws_format_exception`.
  - `Missing_files_read_as_null`: `LiveFiles.Read` on an empty temp home gives both null; `LiveLogin.From` gives null; `ReadAccessToken` gives null.
  - `Same_as_ignores_other_config_changes`: two configs differing only in `numStartups` with the same `oauthAccount` → `SameAs` true; different `oauthAccount.displayName` → false; different credentials bytes → false.

- [x] **Step 2: Run, expect FAIL** (types missing).

- [x] **Step 3: Implement `ClaudePaths.cs`**

```csharp
namespace ClaudeMeter.Core;

/// <summary>Every path Claude Meter reads, writes or locks, resolved once at startup.</summary>
internal sealed record ClaudePaths(
    string ConfigHome,
    string Credentials,
    string GlobalConfig,
    string OAuthRefreshLock,
    string LegacyLock,
    string StorageWriteLock,
    string GlobalConfigLock,
    string AppData)
{
    public string Store => Path.Combine(AppData, "accounts.dat");

    public string Settings => Path.Combine(AppData, "settings.json");

    public static ClaudePaths ForCurrentUser() => Resolve(
        Environment.GetEnvironmentVariable,
        File.Exists,
        RealPath,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static ClaudePaths Resolve(Func<string, string?> env, Func<string, bool> fileExists, Func<string, string> realPath, string localAppData)
    {
        var configDir = env("CLAUDE_CONFIG_DIR");
        var hasConfigDir = !string.IsNullOrWhiteSpace(configDir);
        var profile = env("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configHome = hasConfigDir ? configDir! : Path.Combine(profile, ".claude");

        var legacyConfig = Path.Combine(configHome, ".config.json");
        var globalConfig = fileExists(legacyConfig)
            ? legacyConfig
            : Path.Combine(hasConfigDir ? configDir! : profile, ".claude.json");

        return new ClaudePaths(
            configHome,
            Path.Combine(configHome, ".credentials.json"),
            globalConfig,
            Path.Combine(configHome, ".oauth_refresh.lock"),
            Path.TrimEndingDirectorySeparator(realPath(configHome)) + ".lock",
            Path.Combine(configHome, ".storage-write.lock"),
            globalConfig + ".lock",
            Path.Combine(localAppData, "ClaudeMeter"));
    }

    /// <summary>Like Node's fs.realpath, which Claude Code uses for its legacy lock: the final link target, else the full path.</summary>
    public static string RealPath(string directory)
    {
        var info = new DirectoryInfo(directory);
        return info.Exists ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName : info.FullName;
    }
}
```

- [x] **Step 4: Implement `LoginFiles.cs`**

```csharp
using System.Text.Json;

namespace ClaudeMeter.Core;

/// <summary>Claude Code compares logins by this pair; one person can have several organizations.</summary>
internal readonly record struct AccountKey(string AccountUuid, string OrganizationUuid);

/// <summary>The fields Claude Meter reads from <c>.credentials.json</c>. Tokens go only to Anthropic.</summary>
internal sealed record CredentialsInfo(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    string? SubscriptionType,
    string? OrganizationUuid)
{
    /// <returns>Null if the content is not JSON or holds no usable access token.</returns>
    public static CredentialsInfo? Parse(byte[] content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || Text(oauth, "accessToken") is not { Length: > 0 } accessToken)
                return null;

            return new CredentialsInfo(
                accessToken,
                Text(oauth, "refreshToken"),
                Millis(oauth, "expiresAt"),
                Millis(oauth, "refreshTokenExpiresAt"),
                Text(oauth, "subscriptionType"),
                Text(root, "organizationUuid"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Millis(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var ms) && ms >= 0 && ms <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
}

/// <param name="Raw">The exact bytes of the <c>oauthAccount</c> value, used verbatim when switching.</param>
internal sealed record OAuthAccount(AccountKey Key, string Email, byte[] Raw)
{
    /// <returns>Null if <c>oauthAccount</c> is absent or lacks either UUID.</returns>
    /// <exception cref="FormatException">Invalid JSON or a duplicate <c>oauthAccount</c>.</exception>
    public static OAuthAccount? Parse(byte[] globalConfig)
    {
        if (JsonSplice.Find(globalConfig, "oauthAccount") is not { } span)
            return null;

        var raw = globalConfig.AsSpan(span.Start, span.Length).ToArray();
        using var doc = JsonDocument.Parse(raw);
        var e = doc.RootElement;
        return e.ValueKind == JsonValueKind.Object
            && CredentialsInfo.Text(e, "accountUuid") is { Length: > 0 } account
            && CredentialsInfo.Text(e, "organizationUuid") is { Length: > 0 } org
                ? new OAuthAccount(new AccountKey(account, org), CredentialsInfo.Text(e, "emailAddress") ?? "", raw)
                : null;
    }
}

/// <summary>Raw bytes of Claude Code's credentials file and global config.</summary>
internal sealed record LiveFiles(byte[]? Credentials, byte[]? GlobalConfig)
{
    /// <exception cref="IOException">A file exists but could not be read; treat as transient.</exception>
    public static LiveFiles Read(ClaudePaths paths) => new(ReadOrNull(paths.Credentials), ReadOrNull(paths.GlobalConfig));

    /// <summary>The active account's access token, re-read on every call (Claude Code refreshes it).</summary>
    public static string? ReadAccessToken(ClaudePaths paths) =>
        ReadOrNull(paths.Credentials) is { } bytes ? CredentialsInfo.Parse(bytes)?.AccessToken : null;

    public static byte[]? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>A signed-in Claude Code: both files present and parseable.</summary>
internal sealed record LiveLogin(byte[] CredentialsJson, CredentialsInfo Credentials, OAuthAccount Account)
{
    /// <returns>Null if either file is missing or holds no login.</returns>
    /// <exception cref="FormatException">The global config is invalid JSON or has a duplicate <c>oauthAccount</c>.</exception>
    public static LiveLogin? From(LiveFiles files) =>
        files.Credentials is { } credentials && files.GlobalConfig is { } config
        && CredentialsInfo.Parse(credentials) is { } info && OAuthAccount.Parse(config) is { } account
            ? new LiveLogin(credentials, info, account)
            : null;

    /// <summary>Same login bytes. The rest of the global config is ignored: Claude Code rewrites it constantly.</summary>
    public bool SameAs(LiveLogin other) =>
        CredentialsJson.AsSpan().SequenceEqual(other.CredentialsJson) && Account.Raw.AsSpan().SequenceEqual(other.Account.Raw);
}
```

- [x] **Step 5: Switch the tray to the new reader.** In `TrayApplicationContext` add a field `private readonly ClaudePaths _paths = ClaudePaths.ForCurrentUser();` and replace the token delegate with `() => LiveFiles.ReadAccessToken(_paths)`. Delete the two old Core files and `CredentialsTests.cs`.

- [x] **Step 6: Run, expect PASS:** `dotnet build` then `dotnet test`.

### Task 1.3: Atomic writes, DPAPI and the account store

**Files:**
- Create: `src/ClaudeMeter/Core/AtomicFile.cs`, `src/ClaudeMeter/Core/DataProtector.cs`, `src/ClaudeMeter/Core/AccountStore.cs`, `tests/ClaudeMeter.Tests/ClaudeHome.cs`
- Modify: `tests/ClaudeMeter.Tests/Fakes.cs` (add `FakeProtector`)
- Test: `tests/ClaudeMeter.Tests/AtomicFileTests.cs`, `tests/ClaudeMeter.Tests/AccountStoreTests.cs`

**Interfaces:**
- Consumes: `LiveLogin`, `AccountKey`, `CredentialsInfo`, `UsageSnapshot`.
- Produces:
  - `AtomicFile.Write(string path, ReadOnlySpan<byte> content)`.
  - `interface IDataProtector { byte[] Protect(byte[]); byte[] Unprotect(byte[]); }`, `DpapiProtector`.
  - `enum LoginState { Valid, Expired }`.
  - `record StoredAccount(AccountKey Key, string Email, string? Plan, byte[] CredentialsJson, byte[] OAuthAccountJson, DateTimeOffset AddedAt, DateTimeOffset? LastActiveAt, UsageSnapshot? LastKnownUsage, DateTimeOffset? LastKnownAt, LoginState LoginState, long? ExpiryWarnedFor)` with computed `Credentials`, `static Create(LiveLogin, DateTimeOffset)`, `WithLiveLogin(LiveLogin)`.
  - `enum StoreLoadResult { Empty, Loaded, SetAside }`.
  - `AccountStore(string path, IDataProtector)`: `Accounts` (AddedAt order), `HasUnsavedChanges`, `Find(AccountKey)`, `FindByCredentials(ReadOnlySpan<byte>)`, `Upsert(StoredAccount)`, `Remove(AccountKey) → bool`, `Load() → StoreLoadResult`, `TrySave() → bool`.
  - Test helpers `ClaudeHome` and `ClaudeJson` (used by Tasks 1.6, 1.7, 2.x).

- [x] **Step 1: Add test helpers.** `Fakes.cs` gains:

```csharp
/// <summary>Reversible and visibly not plaintext, so tests can assert nothing is stored in the clear.</summary>
internal sealed class FakeProtector : IDataProtector
{
    public byte[] Protect(byte[] plaintext) => [.. "P1"u8, .. plaintext.Select(b => (byte)(b ^ 0x5A))];

    public byte[] Unprotect(byte[] data) =>
        data.AsSpan().StartsWith("P1"u8)
            ? [.. data.Skip(2).Select(b => (byte)(b ^ 0x5A))]
            : throw new System.Security.Cryptography.CryptographicException("Not ours.");
}
```

`ClaudeHome.cs` (the fake lock pieces it references arrive in Task 1.4; add them to this file then):

```csharp
using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

/// <summary>A temp user profile with Claude Code's files, a fake clock and an account store.</summary>
internal sealed class ClaudeHome : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("claude-meter-tests").FullName;

    public ClaudeHome()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".claude"));
        Paths = ClaudePaths.Resolve(name => name == "USERPROFILE" ? _root : null, File.Exists, p => p, Path.Combine(_root, "LocalAppData"));
        Store = new AccountStore(Paths.Store, new FakeProtector());
    }

    public FakeTimeProvider Clock { get; } = new(Start);
    public ClaudePaths Paths { get; }
    public AccountStore Store { get; }

    public byte[] LiveCredentials => File.ReadAllBytes(Paths.Credentials);
    public byte[] LiveConfig => File.ReadAllBytes(Paths.GlobalConfig);

    public void WriteLive(byte[] credentials, byte[] globalConfig)
    {
        File.WriteAllBytes(Paths.Credentials, credentials);
        File.WriteAllBytes(Paths.GlobalConfig, globalConfig);
    }

    /// <summary>Stores an account as if auto-save had captured it, and saves the store.</summary>
    public StoredAccount Save(string account, string org, string email, byte[] credentials, DateTimeOffset addedAt)
    {
        var live = LiveLogin.From(new LiveFiles(credentials, ClaudeJson.GlobalConfig(account, org, email)))!;
        var stored = StoredAccount.Create(live, addedAt);
        Store.Upsert(stored);
        Assert.True(Store.TrySave());
        return stored;
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}

/// <summary>Claude Code's file shapes from the spike: single-line credentials; LF, 2-space, no trailing newline config.</summary>
internal static class ClaudeJson
{
    public static byte[] Credentials(string access, string refresh, DateTimeOffset expiresAt, string org, DateTimeOffset? loginExpiresAt = null, string plan = "pro") =>
        Encoding.UTF8.GetBytes(
            $$"""{"claudeAiOauth":{"accessToken":"{{access}}","refreshToken":"{{refresh}}","expiresAt":{{expiresAt.ToUnixTimeMilliseconds()}},"refreshTokenExpiresAt":{{(loginExpiresAt ?? expiresAt.AddDays(30)).ToUnixTimeMilliseconds()}},"scopes":["user:inference","user:profile"],"subscriptionType":"{{plan}}","rateLimitTier":"default_claude_ai","futureField":{"x":1}},"organizationUuid":"{{org}}"}""");

    public static byte[] GlobalConfig(string account, string org, string email, int startups = 12) =>
        Encoding.UTF8.GetBytes(string.Join("\n",
            "{",
            $"  \"numStartups\": {startups},",
            "  \"oauthAccount\": {",
            $"    \"accountUuid\": \"{account}\",",
            $"    \"emailAddress\": \"{email}\",",
            $"    \"organizationUuid\": \"{org}\",",
            "    \"displayName\": \"Dimas Ü ✓\"",
            "  },",
            "  \"projects\": {",
            "    \"D:/repo\": { \"allowedTools\": [] },",
            "    \"d:/repo\": { \"allowedTools\": [\"Bash\"] }",
            "  }",
            "}"));
}
```

- [x] **Step 2: Write the failing tests.**
  - `AtomicFileTests`:
    - `Creates_a_new_file` and `Replaces_an_existing_file` (content equals the written bytes; no `*.tmp` left in the folder).
    - `Failure_leaves_target_and_no_temp_file`: target set `ReadOnly`; `Write` throws `UnauthorizedAccessException`; target bytes unchanged; `Directory.GetFiles(dir, "*.tmp")` empty.
  - `AccountStoreTests` (use `ClaudeHome`):
    - `Round_trip_keeps_every_field_and_exact_bytes`: save two accounts with `LastKnownUsage`, `LastKnownAt`, `ExpiryWarnedFor`, `LoginState.Expired`; a new `AccountStore` on the same path `Load()` gives `Loaded` and equal fields; `CredentialsJson`/`OAuthAccountJson` `SequenceEqual` the originals (they contain `futureField`, raw `Ü ✓`, no trailing newline).
    - `Store_file_is_not_plaintext`: the bytes of `Paths.Store` do not contain `"a-refresh"`.
    - `Accounts_are_ordered_by_added_at`: insert with AddedAt day 3, day 1, day 2; order is 1, 2, 3; `Upsert` of the day-1 account keeps its position.
    - `Same_account_in_two_organizations_are_two_entries`.
    - `Missing_file_is_empty`: `Load()` gives `Empty`.
    - `Unreadable_store_is_set_aside_never_overwritten`: write garbage to `Paths.Store`; `Load()` gives `SetAside`; `Accounts` empty; a file `accounts.dat.unreadable-*` holds the garbage; `Paths.Store` no longer exists.
    - `Failed_save_keeps_unsaved_flag`: create a directory at `Paths.Store`; `Upsert` then `TrySave()` gives false and `HasUnsavedChanges` stays true.
    - `Dpapi_round_trip` (real `DpapiProtector`): `Unprotect(Protect(x)) == x` and the protected bytes differ from `x`.

- [x] **Step 3: Run, expect FAIL.**

- [x] **Step 4: Implement `AtomicFile.cs`**

```csharp
namespace ClaudeMeter.Core;

/// <summary>Write-then-replace in the target's folder, so readers see the old or the new file, never half of one.</summary>
internal static class AtomicFile
{
    private const int SharingViolation = unchecked((int)0x80070020);

    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                stream.Write(content);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(full))
                        File.Replace(temp, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    else
                        File.Move(temp, full);
                    return;
                }
                catch (IOException ex) when (ex.HResult == SharingViolation && attempt < 3)
                {
                    Thread.Sleep(50);   // a scanner or reader has the file open for a moment
                }
            }
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
```

- [x] **Step 5: Implement `DataProtector.cs`**

```csharp
using System.Security.Cryptography;

namespace ClaudeMeter.Core;

internal interface IDataProtector
{
    byte[] Protect(byte[] plaintext);

    /// <exception cref="CryptographicException">Not decryptable by this Windows user.</exception>
    byte[] Unprotect(byte[] data);
}

/// <summary>DPAPI, current user. The entropy ties the data to this app.</summary>
internal sealed class DpapiProtector : IDataProtector
{
    private static readonly byte[] Entropy = "ClaudeMeter.accounts.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
```

- [x] **Step 6: Implement `AccountStore.cs`**

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeMeter.Core;

public enum LoginState
{
    Valid,
    /// <summary>The token endpoint answered invalid_grant; only a new /login helps.</summary>
    Expired,
}

/// <param name="CredentialsJson">The whole <c>.credentials.json</c>; only refresh fields are ever spliced.</param>
/// <param name="OAuthAccountJson">The <c>oauthAccount</c> value from the global config, verbatim.</param>
/// <param name="ExpiryWarnedFor">The <c>refreshTokenExpiresAt</c> (ms) already warned about.</param>
internal sealed record StoredAccount(
    AccountKey Key,
    string Email,
    string? Plan,
    byte[] CredentialsJson,
    byte[] OAuthAccountJson,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastActiveAt,
    UsageSnapshot? LastKnownUsage,
    DateTimeOffset? LastKnownAt,
    LoginState LoginState,
    long? ExpiryWarnedFor)
{
    [JsonIgnore]
    public CredentialsInfo? Credentials => CredentialsInfo.Parse(CredentialsJson);

    public static StoredAccount Create(LiveLogin live, DateTimeOffset now) => new(
        live.Account.Key, live.Account.Email, live.Credentials.SubscriptionType, live.CredentialsJson, live.Account.Raw,
        AddedAt: now, LastActiveAt: now, LastKnownUsage: null, LastKnownAt: null, LoginState.Valid, ExpiryWarnedFor: null);

    /// <summary>A fresh capture of this identity; a new /login also clears an expired state.</summary>
    public StoredAccount WithLiveLogin(LiveLogin live) => this with
    {
        Email = live.Account.Email,
        Plan = live.Credentials.SubscriptionType ?? Plan,
        CredentialsJson = live.CredentialsJson,
        OAuthAccountJson = live.Account.Raw,
        LoginState = LoginState.Valid,
    };
}

internal enum StoreLoadResult
{
    Empty,
    Loaded,
    /// <summary>The file could not be read; it was renamed aside and the store starts empty.</summary>
    SetAside,
}

/// <summary>Saved accounts in one DPAPI-encrypted file, written atomically.</summary>
internal sealed class AccountStore(string path, IDataProtector protector)
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    private List<StoredAccount> _accounts = [];

    /// <summary>In <see cref="StoredAccount.AddedAt"/> order; this is the display order.</summary>
    public IReadOnlyList<StoredAccount> Accounts => _accounts;

    public bool HasUnsavedChanges { get; private set; }

    public StoredAccount? Find(AccountKey key) => _accounts.Find(a => a.Key == key);

    public StoredAccount? FindByCredentials(ReadOnlySpan<byte> credentials)
    {
        foreach (var account in _accounts)
        {
            if (account.CredentialsJson.AsSpan().SequenceEqual(credentials))
                return account;
        }

        return null;
    }

    public void Upsert(StoredAccount account)
    {
        _accounts = [.. _accounts.Where(a => a.Key != account.Key).Append(account).OrderBy(a => a.AddedAt)];
        HasUnsavedChanges = true;
    }

    public bool Remove(AccountKey key)
    {
        var removed = _accounts.RemoveAll(a => a.Key == key) > 0;
        HasUnsavedChanges |= removed;
        return removed;
    }

    public StoreLoadResult Load()
    {
        if (LiveFiles.ReadOrNull(path) is not { } data)
            return StoreLoadResult.Empty;

        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(protector.Unprotect(data), Options);
            if (file is not { Version: 1, Accounts: not null })
                throw new FormatException("Unknown store format.");
            _accounts = [.. file.Accounts.OrderBy(a => a.AddedAt)];
            return StoreLoadResult.Loaded;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or NotSupportedException)
        {
            // Never overwrite what can't be read. It stays encrypted, so keeping it is safe.
            File.Move(path, $"{path}.unreadable-{DateTime.UtcNow:yyyyMMddHHmmss}");
            return StoreLoadResult.SetAside;
        }
    }

    public bool TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.Write(path, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new StoreFile(1, _accounts), Options)));
            HasUnsavedChanges = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    private sealed record StoreFile(int Version, List<StoredAccount> Accounts);
}
```

- [x] **Step 7: Run, expect PASS:** `dotnet test --filter "FullyQualifiedName~AtomicFileTests|FullyQualifiedName~AccountStoreTests"`.

### Task 1.4: Claude Code's lock directories

**Files:**
- Create: `src/ClaudeMeter/Core/DirectoryLocks.cs`
- Modify: `tests/ClaudeMeter.Tests/Fakes.cs` (timer support in `FakeTimeProvider`, `FakeLockFileSystem`), `tests/ClaudeMeter.Tests/ClaudeHome.cs` (lock fields)
- Test: `tests/ClaudeMeter.Tests/DirectoryLocksTests.cs`, `tests/ClaudeMeter.Tests/LockFileSystemTests.cs`

**Interfaces:**
- Consumes: `ClaudePaths`.
- Produces:
  - `interface ILockFileSystem { bool TryCreateDirectory(string); DateTimeOffset? GetLastWriteTime(string); void SetLastWriteTime(string, DateTimeOffset); void DeleteDirectory(string); }`, `LockFileSystem`.
  - `record LockSpec(string Path, TimeSpan Stale)`, `LockTimeoutException`.
  - `DirectoryLocks(ILockFileSystem, TimeProvider, Func<TimeSpan, CancellationToken, Task> delay)`: `Timeout` (9 s), `TouchInterval` (3 s), `RetryDelay` (200 ms), `AcquireAsync(IReadOnlyList<LockSpec>, CancellationToken) → Task<HeldLocks>`, `internal TryAcquire(LockSpec) → HeldLock?`.
  - `HeldLock : IDisposable`, `HeldLocks : IDisposable` (reverse release).
  - `ClaudeLocks(ClaudePaths, DirectoryLocks)`: `AcquireRefreshAsync`, `AcquireStorageWriteAsync`, `AcquireGlobalConfigAsync` (each `→ Task<HeldLocks>`).
  - Tests: `FakeLockFileSystem` (`Log`, `HeldCount`, `AddForeign`, `RemoveForeign`, `Exists`, `AfterCreate`, `BeforeDelete`); `ClaudeHome.Locks` and `ClaudeHome.ClaudeLocks`.

- [x] **Step 1: Extend the fakes.** Replace `FakeTimeProvider` in `Fakes.cs` (existing tests keep working; they never create timers):

```csharp
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<FakeTimer> _timers = [];

    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Moves time forward, firing due timers in time order.</summary>
    public void Advance(TimeSpan by)
    {
        var end = Now + by;
        while (_timers.Where(t => t.DueAt <= end).MinBy(t => t.DueAt) is { } timer)
        {
            Now = timer.DueAt!.Value;
            timer.Fire();
        }
        Now = end;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class FakeTimer(FakeTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Now + dueTime;
            clock._timers.Remove(this);
            if (DueAt is not null)
                clock._timers.Add(this);
            return true;
        }

        public void Fire()
        {
            DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : DueAt + _period;
            if (DueAt is null)
                clock._timers.Remove(this);
            callback(state);
        }

        public void Dispose()
        {
            DueAt = null;
            clock._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>In-memory lock directories with an operation log ("create x", "touch x", "delete x" by file name).</summary>
internal sealed class FakeLockFileSystem(FakeTimeProvider clock) : ILockFileSystem
{
    private readonly Dictionary<string, DateTimeOffset> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ours = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Log { get; } = [];

    /// <summary>Runs after each successful create, e.g. to change files while a lock is held.</summary>
    public Action<string>? AfterCreate { get; set; }

    /// <summary>Runs before each delete, i.e. just before a lock is released.</summary>
    public Action<string>? BeforeDelete { get; set; }

    /// <summary>Locks this process holds right now.</summary>
    public int HeldCount => _ours.Count;

    public void AddForeign(string path, DateTimeOffset mtime) => _dirs[path] = mtime;

    public void RemoveForeign(string path) => _dirs.Remove(path);

    public bool Exists(string path) => _dirs.ContainsKey(path);

    public bool TryCreateDirectory(string path)
    {
        if (_dirs.ContainsKey(path))
            return false;
        _dirs[path] = clock.Now;
        _ours.Add(path);
        Log.Add($"create {Path.GetFileName(path)}");
        AfterCreate?.Invoke(path);
        return true;
    }

    public DateTimeOffset? GetLastWriteTime(string path) => _dirs.TryGetValue(path, out var t) ? t : null;

    public void SetLastWriteTime(string path, DateTimeOffset time)
    {
        if (!_dirs.ContainsKey(path))
            return;
        _dirs[path] = time;
        Log.Add($"touch {Path.GetFileName(path)}");
    }

    public void DeleteDirectory(string path)
    {
        BeforeDelete?.Invoke(path);
        if (_dirs.Remove(path))
            Log.Add($"delete {Path.GetFileName(path)}");
        _ours.Remove(path);
    }
}
```

`ClaudeHome` gains (constructor and properties):

```csharp
Locks = new FakeLockFileSystem(Clock);
ClaudeLocks = new ClaudeLocks(Paths, new DirectoryLocks(Locks, Clock, (d, _) => { Clock.Advance(d); return Task.CompletedTask; }));
// ...
public FakeLockFileSystem Locks { get; }
public ClaudeLocks ClaudeLocks { get; }
```

- [x] **Step 2: Write the failing tests.** `DirectoryLocksTests` (fields: `_clock = new FakeTimeProvider(Start)`, `_fs = new FakeLockFileSystem(_clock)`, `_locks = new DirectoryLocks(_fs, _clock, (d, _) => { _clock.Advance(d); return Task.CompletedTask; })`, `A = new LockSpec(@"C:\h\.claude\.oauth_refresh.lock", 60s)`, `B = new LockSpec(@"C:\h\.claude.lock", 60s)`):

```csharp
[Fact]
public async Task Acquire_creates_and_release_deletes()
{
    using (await _locks.AcquireAsync([A], default))
        Assert.True(_fs.Exists(A.Path));

    Assert.False(_fs.Exists(A.Path));
}

[Fact]
public async Task Young_foreign_lock_times_out_after_9_seconds_and_is_left_alone()
{
    _fs.AddForeign(A.Path, Start);

    await Assert.ThrowsAsync<LockTimeoutException>(() => _locks.AcquireAsync([A], default));

    Assert.True(_fs.Exists(A.Path));
    Assert.Empty(_fs.Log);
    Assert.InRange(_clock.Now - Start, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(9.2));
}

[Fact]
public void Lock_older_than_stale_is_taken_over()
{
    _fs.AddForeign(A.Path, Start - TimeSpan.FromSeconds(61));

    using var held = _locks.TryAcquire(A);

    Assert.NotNull(held);
    Assert.Equal(["delete .oauth_refresh.lock", "create .oauth_refresh.lock"], _fs.Log);
}

[Fact]
public void Lock_exactly_at_the_threshold_is_not_taken_over()
{
    _fs.AddForeign(A.Path, Start - TimeSpan.FromSeconds(60));

    Assert.Null(_locks.TryAcquire(A));
    Assert.Empty(_fs.Log);
}

[Fact]
public async Task Lock_is_released_when_the_holder_throws()
{
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
        using var held = await _locks.AcquireAsync([A], default);
        throw new InvalidOperationException();
    });

    Assert.False(_fs.Exists(A.Path));
}

[Fact]
public async Task Held_lock_is_touched_every_3_seconds_and_not_after_release()
{
    var held = await _locks.AcquireAsync([A], default);
    _clock.Advance(TimeSpan.FromSeconds(7));
    held.Dispose();
    _clock.Advance(TimeSpan.FromSeconds(7));

    Assert.Equal(["create .oauth_refresh.lock", "touch .oauth_refresh.lock", "touch .oauth_refresh.lock", "delete .oauth_refresh.lock"], _fs.Log);
}

[Fact]
public async Task Busy_second_lock_releases_the_first_and_retries()
{
    _fs.AddForeign(B.Path, Start);
    var waits = 0;
    var locks = new DirectoryLocks(_fs, _clock, (d, _) =>
    {
        if (++waits == 2)
            _fs.RemoveForeign(B.Path);
        _clock.Advance(d);
        return Task.CompletedTask;
    });

    using (await locks.AcquireAsync([A, B], default)) { }

    Assert.Equal(
        ["create .oauth_refresh.lock", "delete .oauth_refresh.lock", "create .oauth_refresh.lock", "delete .oauth_refresh.lock",
         "create .oauth_refresh.lock", "create .claude.lock", "delete .claude.lock", "delete .oauth_refresh.lock"],
        _fs.Log);
}
```

`LockFileSystemTests` (real file system, temp dir):
  - `Create_fails_when_the_directory_exists`: first `TryCreateDirectory` true, second false.
  - `Owner_record_is_never_touched`: create `<tmp>\.claude`, write `<tmp>\.claude\.oauth_refresh.lock.owner` with `{"pid":1}` and set its mtime to an hour ago; create a stale `<tmp>\.claude\.oauth_refresh.lock` (mtime 2 minutes ago). With real `LockFileSystem`, `TimeProvider.System`, `Task.Delay`: `ClaudeLocks.AcquireRefreshAsync` then dispose. Owner file bytes and `LastWriteTimeUtc` are unchanged; both lock directories are gone.

- [x] **Step 3: Run, expect FAIL.**

- [x] **Step 4: Implement `DirectoryLocks.cs`**

```csharp
using System.Runtime.InteropServices;

namespace ClaudeMeter.Core;

internal interface ILockFileSystem
{
    /// <returns>False if the directory already exists (or is being deleted).</returns>
    bool TryCreateDirectory(string path);

    /// <returns>Null if the directory does not exist.</returns>
    DateTimeOffset? GetLastWriteTime(string path);

    void SetLastWriteTime(string path, DateTimeOffset time);

    /// <summary>Removes an empty directory; no-op if it is missing.</summary>
    void DeleteDirectory(string path);
}

internal sealed class LockFileSystem : ILockFileSystem
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorAlreadyExists = 183;

    // Directory.CreateDirectory succeeds silently on an existing directory, so it cannot be a mutex.
    public bool TryCreateDirectory(string path)
    {
        if (CreateDirectoryW(path, IntPtr.Zero))
            return true;
        var error = Marshal.GetLastPInvokeError();
        return error is ErrorAlreadyExists or ErrorAccessDenied
            ? false
            : throw new IOException($"Cannot create lock directory {Path.GetFileName(path)} (error {error}).");
    }

    public DateTimeOffset? GetLastWriteTime(string path) =>
        Directory.Exists(path) ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;

    public void SetLastWriteTime(string path, DateTimeOffset time) => Directory.SetLastWriteTimeUtc(path, time.UtcDateTime);

    public void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);
}

/// <param name="Stale">A lock whose mtime is older than this may be taken over.</param>
internal sealed record LockSpec(string Path, TimeSpan Stale);

internal sealed class LockTimeoutException(string path)
    : Exception($"Timed out waiting for {System.IO.Path.GetFileName(path)}.");

/// <summary>
/// Locks compatible with npm proper-lockfile, which Claude Code uses: a lock is a directory and creating
/// it is the mutex. Held locks are touched so they never look stale. Never writes an owner record, so
/// Claude Code can only take a lock from us through the stale rule.
/// </summary>
internal sealed class DirectoryLocks(ILockFileSystem fs, TimeProvider clock, Func<TimeSpan, CancellationToken, Task> delay)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(9);
    public static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Takes all locks in order. If one is busy, the ones already taken are released and the attempt
    /// starts over, so a waiter never sits on a lock (Claude Code does this for its two refresh locks).
    /// </summary>
    /// <exception cref="LockTimeoutException">Not all locks were free within <see cref="Timeout"/>.</exception>
    public async Task<HeldLocks> AcquireAsync(IReadOnlyList<LockSpec> specs, CancellationToken cancellationToken)
    {
        var deadline = clock.GetUtcNow() + Timeout;
        while (true)
        {
            var held = new List<HeldLock>();
            foreach (var spec in specs)
            {
                if (TryAcquire(spec) is not { } one)
                    break;
                held.Add(one);
            }

            if (held.Count == specs.Count)
                return new HeldLocks(held);

            new HeldLocks(held).Dispose();
            if (clock.GetUtcNow() >= deadline)
                throw new LockTimeoutException(specs[held.Count].Path);
            await delay(RetryDelay, cancellationToken);
        }
    }

    /// <summary>One attempt, no waiting. Takes over only a lock older than its stale threshold.</summary>
    internal HeldLock? TryAcquire(LockSpec spec)
    {
        if (!fs.TryCreateDirectory(spec.Path))
        {
            if (fs.GetLastWriteTime(spec.Path) is not { } mtime || clock.GetUtcNow() - mtime <= spec.Stale)
                return null;
            fs.DeleteDirectory(spec.Path);
            if (!fs.TryCreateDirectory(spec.Path))
                return null;
        }

        return new HeldLock(spec.Path, fs, clock);
    }
}

internal sealed class HeldLock : IDisposable
{
    private readonly ILockFileSystem _fs;
    private readonly TimeProvider _clock;
    private readonly ITimer _toucher;
    private readonly Lock _sync = new();
    private bool _released;

    public HeldLock(string path, ILockFileSystem fs, TimeProvider clock)
    {
        Path = path;
        _fs = fs;
        _clock = clock;
        _toucher = clock.CreateTimer(_ => Touch(), null, DirectoryLocks.TouchInterval, DirectoryLocks.TouchInterval);
    }

    public string Path { get; }

    private void Touch()
    {
        lock (_sync)
        {
            if (_released)
                return;
            try
            {
                _fs.SetLastWriteTime(Path, _clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The next touch retries; the stale threshold is far longer than the interval.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_released)
                return;
            _released = true;
        }

        _toucher.Dispose();
        try
        {
            _fs.DeleteDirectory(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind, it turns stale and Claude Code takes it over.
        }
    }
}

/// <summary>Releases in reverse order of acquisition.</summary>
internal sealed class HeldLocks(IReadOnlyList<HeldLock> locks) : IDisposable
{
    public void Dispose()
    {
        for (var i = locks.Count - 1; i >= 0; i--)
            locks[i].Dispose();
    }
}

/// <summary>Claude Code's own locks, in its order: OAuth refresh (primary, legacy), storage write, global config.</summary>
internal sealed class ClaudeLocks(ClaudePaths paths, DirectoryLocks locks)
{
    private static readonly TimeSpan RefreshStale = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StorageWriteStale = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan GlobalConfigStale = TimeSpan.FromSeconds(10);

    public static ClaudeLocks ForSystem(ClaudePaths paths) =>
        new(paths, new DirectoryLocks(new LockFileSystem(), TimeProvider.System, (d, ct) => Task.Delay(d, ct)));

    public Task<HeldLocks> AcquireRefreshAsync(CancellationToken ct) =>
        locks.AcquireAsync([new(paths.OAuthRefreshLock, RefreshStale), new(paths.LegacyLock, RefreshStale)], ct);

    public Task<HeldLocks> AcquireStorageWriteAsync(CancellationToken ct) =>
        locks.AcquireAsync([new(paths.StorageWriteLock, StorageWriteStale)], ct);

    public Task<HeldLocks> AcquireGlobalConfigAsync(CancellationToken ct) =>
        locks.AcquireAsync([new(paths.GlobalConfigLock, GlobalConfigStale)], ct);
}
```

- [x] **Step 5: Run, expect PASS:** `dotnet test --filter "FullyQualifiedName~DirectoryLocksTests|FullyQualifiedName~LockFileSystemTests"`.

### Task 1.5: Profile client and capture verification

**Files:**
- Create: `src/ClaudeMeter/Core/ProfileClient.cs`, `src/ClaudeMeter/Core/CaptureVerifier.cs`
- Modify: `tests/ClaudeMeter.Tests/Fakes.cs` (add `FakeProfileClient`)
- Test: `tests/ClaudeMeter.Tests/ProfileParserTests.cs`, `tests/ClaudeMeter.Tests/CaptureVerifierTests.cs`

**Interfaces:**
- Consumes: `LiveLogin`, `StoredAccount`, `CredentialsInfo`.
- Produces:
  - `abstract record ProfileResult` with `Found(string AccountUuid, string? OrganizationUuid)` and `Unavailable(string Reason)`.
  - `interface IProfileClient { Task<ProfileResult> GetAsync(string accessToken, CancellationToken); }`, `ProfileClient : IProfileClient, IDisposable`, `ProfileParser.Parse(string) → ProfileResult`.
  - `ProfileCheck.RunAsync(IProfileClient, CredentialsInfo, TimeProvider, CancellationToken) → Task<ProfileResult>` (no request if the access token is expired).
  - `enum CaptureVerdict { Accept, Defer, Reject }`, `record CaptureDecision(CaptureVerdict Verdict, string? Reason)`, `CaptureVerifier.Evaluate(LiveLogin, IReadOnlyList<StoredAccount>, ProfileResult) → CaptureDecision`.
  - Test fake `FakeProfileClient` (`Result`, `Calls`, `OnCall` hook used to assert no locks are held).

- [x] **Step 1: Add the fake**

```csharp
internal sealed class FakeProfileClient : IProfileClient
{
    public ProfileResult Result { get; set; } = new ProfileResult.Unavailable("offline");
    public List<string> Calls { get; } = [];
    public Action? OnCall { get; set; }

    public Task<ProfileResult> GetAsync(string accessToken, CancellationToken cancellationToken)
    {
        OnCall?.Invoke();
        Calls.Add(accessToken);
        return Task.FromResult(Result);
    }
}
```

- [x] **Step 2: Write the failing tests.**
  - `ProfileParserTests`: `{"account":{"uuid":"a","email":"x"},"organization":{"uuid":"o"}}` → `Found("a","o")`; without `organization` → `Found("a", null)`; without `account.uuid` → `Unavailable`; `[]` and `not json` → `Unavailable`.
  - `CaptureVerifierTests` (live login for A built with `ClaudeJson`; stored accounts via `StoredAccount.Create`):
    - `Verified_new_login_is_accepted`: profile `Found("acc-a","org-a")`, empty store → Accept.
    - `Credentials_of_another_organization_are_rejected` (check 1): credentials `organizationUuid="org-x"` → Reject.
    - `Credentials_without_organization_skip_check_1`: remove the top-level field → Accept.
    - `Refresh_token_of_another_saved_account_is_rejected` (check 2): stored T has refresh `"r-1"`, live A has `"r-1"` → Reject.
    - `Own_old_snapshot_with_same_refresh_token_is_fine`: stored A has `"r-1"`, live A has `"r-1"` → Accept.
    - `Profile_account_mismatch_is_rejected` and `Profile_organization_mismatch_is_rejected` (check 3).
    - `Profile_without_organization_is_accepted`.
    - `Unverified_new_login_is_deferred`: `Unavailable` + empty store → Defer.
    - `Unverified_known_login_is_accepted`: `Unavailable` + A stored → Accept.
    - `Missing_refresh_token_is_rejected`.
    - `Profile_check_skips_the_request_for_an_expired_token`: `ProfileCheck.RunAsync` with `ExpiresAt` in the past → `Unavailable("token expired")`, `Calls` empty.

- [x] **Step 3: Run, expect FAIL.**

- [x] **Step 4: Implement `ProfileClient.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeMeter.Core;

internal abstract record ProfileResult
{
    private ProfileResult() { }

    public sealed record Found(string AccountUuid, string? OrganizationUuid) : ProfileResult;

    /// <summary>Expired token, 401, offline, or a response without <c>account.uuid</c>.</summary>
    public sealed record Unavailable(string Reason) : ProfileResult;
}

internal interface IProfileClient
{
    Task<ProfileResult> GetAsync(string accessToken, CancellationToken cancellationToken);
}

/// <summary>Asks Anthropic whose token this is. Never called while holding a lock.</summary>
internal sealed class ProfileClient : IProfileClient, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/profile");

    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public async Task<ProfileResult> GetAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK
                ? ProfileParser.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
                : new ProfileResult.Unavailable($"HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException)
        {
            return new ProfileResult.Unavailable("network error");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProfileResult.Unavailable("timeout");
        }
    }

    public void Dispose() => _http.Dispose();
}

internal static class ProfileParser
{
    public static ProfileResult Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object
                || CredentialsInfo.Text(account, "uuid") is not { Length: > 0 } accountUuid)
                return new ProfileResult.Unavailable("unexpected response");

            var org = root.TryGetProperty("organization", out var o) && o.ValueKind == JsonValueKind.Object
                ? CredentialsInfo.Text(o, "uuid")
                : null;
            return new ProfileResult.Found(accountUuid, org);
        }
        catch (JsonException)
        {
            return new ProfileResult.Unavailable("unexpected response");
        }
    }
}

internal static class ProfileCheck
{
    /// <summary>Capture verification step 3. Skips the request when the access token is already expired.</summary>
    public static Task<ProfileResult> RunAsync(IProfileClient client, CredentialsInfo credentials, TimeProvider clock, CancellationToken cancellationToken) =>
        credentials.ExpiresAt is { } expiresAt && expiresAt > clock.GetUtcNow()
            ? client.GetAsync(credentials.AccessToken, cancellationToken)
            : Task.FromResult<ProfileResult>(new ProfileResult.Unavailable("token expired"));
}
```

- [x] **Step 5: Implement `CaptureVerifier.cs`**

```csharp
namespace ClaudeMeter.Core;

internal enum CaptureVerdict
{
    Accept,
    /// <summary>A new identity that could not be verified yet; try again later.</summary>
    Defer,
    Reject,
}

internal sealed record CaptureDecision(CaptureVerdict Verdict, string? Reason = null);

/// <summary>Checks 1–3 of capture verification. Check 4, the locked re-read, belongs to the caller.</summary>
internal static class CaptureVerifier
{
    public static CaptureDecision Evaluate(LiveLogin live, IReadOnlyList<StoredAccount> stored, ProfileResult profile)
    {
        var key = live.Account.Key;
        var credentials = live.Credentials;

        if (credentials.RefreshToken is null)
            return Reject("no refresh token");
        if (credentials.OrganizationUuid is { } org && org != key.OrganizationUuid)
            return Reject("organization mismatch");
        if (stored.Any(a => a.Key != key && a.Credentials?.RefreshToken == credentials.RefreshToken))
            return Reject("token of another saved account");

        return profile switch
        {
            ProfileResult.Found found when found.AccountUuid != key.AccountUuid
                || (found.OrganizationUuid is { } profileOrg && profileOrg != key.OrganizationUuid) => Reject("profile mismatch"),
            ProfileResult.Found => new CaptureDecision(CaptureVerdict.Accept),
            _ => stored.Any(a => a.Key == key)
                ? new CaptureDecision(CaptureVerdict.Accept)
                : new CaptureDecision(CaptureVerdict.Defer, "unverified"),
        };

        static CaptureDecision Reject(string reason) => new(CaptureVerdict.Reject, reason);
    }
}
```

- [x] **Step 6: Run, expect PASS.**

### Task 1.6: Auto-save

**Files:**
- Create: `src/ClaudeMeter/Core/AutoSaver.cs`
- Test: `tests/ClaudeMeter.Tests/AutoSaverTests.cs`

**Interfaces:**
- Consumes: `ClaudePaths`, `ClaudeLocks`, `AccountStore`, `IProfileClient`, `ProfileCheck`, `CaptureVerifier`, `LiveFiles`, `LiveLogin`.
- Produces: `abstract record AutoSaveResult` with `NoLogin`, `Unchanged`, `Inserted(string Email)`, `Updated`, `Deferred(string Email)`, `Rejected(string Reason)`, `Busy`; `AutoSaver(ClaudePaths, ClaudeLocks, AccountStore, IProfileClient, TimeProvider)` with `RequireProfile { get; set; }` and `RunAsync(CancellationToken) → Task<AutoSaveResult>`.

- [x] **Step 1: Write the failing tests** (fixture: `_home = new ClaudeHome()`, `_profiles = new FakeProfileClient { OnCall = () => Assert.Equal(0, _home.Locks.HeldCount) }`, `_saver = new AutoSaver(_home.Paths, _home.ClaudeLocks, _home.Store, _profiles, _home.Clock)`):

```csharp
[Fact]
public async Task New_verified_login_is_inserted()
{
    var credentials = ClaudeJson.Credentials("a-access", "a-refresh", Start.AddHours(8), "org-a");
    _home.WriteLive(credentials, ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com"));
    _profiles.Result = new ProfileResult.Found("acc-a", "org-a");

    var result = await _saver.RunAsync(default);

    Assert.Equal(new AutoSaveResult.Inserted("a@example.com"), result);
    var saved = Assert.Single(_home.Store.Accounts);
    Assert.Equal(credentials, saved.CredentialsJson);
    Assert.False(_home.Store.HasUnsavedChanges);
    Assert.Equal(["create .oauth_refresh.lock", "create .claude.lock", "create .storage-write.lock",
                  "delete .storage-write.lock", "delete .claude.lock", "delete .oauth_refresh.lock"], _home.Locks.Log);
}
```
  Plus:
  - `Known_login_with_changed_credentials_is_updated_silently`: A stored with refresh `r-1`; live A with `r-2`; profile Found → `Updated`; stored bytes = live bytes.
  - `Known_login_update_clears_expired_state`: stored A has `LoginState.Expired`; live A has new credentials → `Updated` and `LoginState.Valid`.
  - `Unchanged_login_makes_no_request_and_takes_no_lock`: live bytes equal stored A → `Unchanged`, `Calls` empty, `Log` empty.
  - `Config_rewrite_elsewhere_does_not_count_as_a_change`: same as above but `numStartups` differs → `Unchanged`.
  - `Changed_oauth_account_with_same_credentials_updates_without_profile_call`: displayName differs → `Updated`, `Calls` empty.
  - `Profile_mismatch_writes_nothing`: `Found("acc-x","org-a")` → `Rejected`; store empty; `Log` empty.
  - `Files_changing_under_the_lock_write_nothing`: `_home.Locks.AfterCreate = p => { if (p == _home.Paths.StorageWriteLock) File.WriteAllBytes(_home.Paths.Credentials, other); }` → `Rejected`; store empty.
  - `Unverified_new_login_is_deferred`: `Unavailable` → `Deferred("a@example.com")`; store empty; no locks taken.
  - `Unverified_known_login_is_updated`: A stored, live rotated, `Unavailable` → `Updated`.
  - `Require_profile_rejects_unverified_known_login`: as above with `RequireProfile = true` → `Rejected`; after a later `Found` capture, `RequireProfile` is false.

  Test classes that use `ClaudeHome` declare `private static readonly DateTimeOffset Start = ClaudeHome.Start;` and dispose `_home` in `Dispose()`.
  - `Credentials_of_another_saved_account_are_rejected`: live credentials byte-equal stored T, `oauthAccount` says A → `Rejected`, no profile call.
  - `Busy_lock_is_reported_and_writes_nothing`: foreign young `StorageWriteLock` → `Busy`; store empty.
  - `No_login_writes_nothing`: credentials file missing → `NoLogin`; same when `oauthAccount` is absent.
  - `Duplicate_oauth_account_is_rejected`: config with two `oauthAccount` keys → `Rejected`.

- [x] **Step 2: Run, expect FAIL.**

- [x] **Step 3: Implement**

```csharp
namespace ClaudeMeter.Core;

internal abstract record AutoSaveResult
{
    private AutoSaveResult() { }

    public sealed record NoLogin : AutoSaveResult;
    public sealed record Unchanged : AutoSaveResult;
    public sealed record Inserted(string Email) : AutoSaveResult;
    public sealed record Updated : AutoSaveResult;
    /// <summary>New identity not verified yet: "Not saved yet: waiting for network to verify".</summary>
    public sealed record Deferred(string Email) : AutoSaveResult;
    /// <summary>A check failed: "Login files are changing; will retry".</summary>
    public sealed record Rejected(string Reason) : AutoSaveResult;
    /// <summary>Claude Code held a lock for the whole timeout; try at the next trigger.</summary>
    public sealed record Busy : AutoSaveResult;
}

/// <summary>Saves the active Claude Code login whenever it is new or its credentials changed, once verified.</summary>
internal sealed class AutoSaver(ClaudePaths paths, ClaudeLocks locks, AccountStore store, IProfileClient profiles, TimeProvider clock)
{
    /// <summary>Set after a switch post-check mismatch: only a profile-verified capture is accepted until one succeeds.</summary>
    public bool RequireProfile { get; set; }

    public async Task<AutoSaveResult> RunAsync(CancellationToken cancellationToken)
    {
        LiveLogin? live;
        try
        {
            live = LiveLogin.From(LiveFiles.Read(paths));
        }
        catch (FormatException)
        {
            return new AutoSaveResult.Rejected("unreadable global config");
        }

        if (live is null)
            return new AutoSaveResult.NoLogin();

        // Byte-match rule: credentials already saved need no verification.
        if (store.FindByCredentials(live.CredentialsJson) is { } owner)
        {
            if (owner.Key != live.Account.Key)
                return new AutoSaveResult.Rejected("credentials of another saved account");
            if (owner.OAuthAccountJson.AsSpan().SequenceEqual(live.Account.Raw))
                return new AutoSaveResult.Unchanged();
            store.Upsert(owner.WithLiveLogin(live));
            store.TrySave();
            return new AutoSaveResult.Updated();
        }

        var profile = await ProfileCheck.RunAsync(profiles, live.Credentials, clock, cancellationToken);
        if (RequireProfile && profile is not ProfileResult.Found)
            return new AutoSaveResult.Rejected("waiting for a verified capture");

        var decision = CaptureVerifier.Evaluate(live, store.Accounts, profile);
        if (decision.Verdict == CaptureVerdict.Defer)
            return new AutoSaveResult.Deferred(live.Account.Email);
        if (decision.Verdict == CaptureVerdict.Reject)
            return new AutoSaveResult.Rejected(decision.Reason!);

        // Check 4: the verified bytes must still be what Claude Code has, read under its locks.
        try
        {
            using var refreshLocks = await locks.AcquireRefreshAsync(cancellationToken);
            using var storageLock = await locks.AcquireStorageWriteAsync(cancellationToken);
            if (LiveLogin.From(LiveFiles.Read(paths)) is not { } again || !again.SameAs(live))
                return new AutoSaveResult.Rejected("files changed during capture");
        }
        catch (LockTimeoutException)
        {
            return new AutoSaveResult.Busy();
        }
        catch (FormatException)
        {
            return new AutoSaveResult.Rejected("unreadable global config");
        }

        if (profile is ProfileResult.Found)
            RequireProfile = false;

        var existing = store.Find(live.Account.Key);
        store.Upsert(existing?.WithLiveLogin(live) ?? StoredAccount.Create(live, clock.GetUtcNow()));
        store.TrySave();   // on failure the runtime retries on every poll
        return existing is null ? new AutoSaveResult.Inserted(live.Account.Email) : new AutoSaveResult.Updated();
    }
}
```

- [x] **Step 4: Run, expect PASS.**

### Task 1.7: Switch transaction

**Files:**
- Create: `src/ClaudeMeter/Core/AccountSwitcher.cs`
- Modify: `tests/ClaudeMeter.Tests/Fakes.cs` (add `FakeTokenRefresher`)
- Test: `tests/ClaudeMeter.Tests/AccountSwitcherTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1.1–1.6; `LoginExpiry.IsExpired` is defined here as a private helper until Task 2.4 moves it.
- Produces:
  - `enum RefreshOutcome { Fresh, Refreshed, LoginExpired, Unavailable }`; `interface ITokenRefresher { Task<RefreshOutcome> EnsureFreshAsync(AccountKey key, CancellationToken ct); }` (implemented in Task 2.2).
  - `enum SwitchStage { WaitingForLock, Writing }`.
  - `abstract record SwitchResult` with `Switched(string Email)`, `Unconfirmed(string Email)`, `Failed(string Message)`.
  - `AccountSwitcher(ClaudePaths, ClaudeLocks, AccountStore, IProfileClient, ITokenRefresher, TimeProvider)`: `SwitchAsync(AccountKey target, IProgress<SwitchStage>? progress, CancellationToken) → Task<SwitchResult>`; message constants `Busy`, `Changing`, `Unverified`, `NoLogin`, `TargetExpired`, `AlreadyActive`.

- [x] **Step 1: Add the fake**

```csharp
internal sealed class FakeTokenRefresher : ITokenRefresher
{
    public RefreshOutcome Outcome { get; set; } = RefreshOutcome.Fresh;
    public List<AccountKey> Calls { get; } = [];

    public Task<RefreshOutcome> EnsureFreshAsync(AccountKey key, CancellationToken cancellationToken)
    {
        Calls.Add(key);
        return Task.FromResult(Outcome);
    }
}
```

- [x] **Step 2: Write the failing tests.** Fixture: `_home`, `_profiles` (with the no-lock `OnCall` assert), `_refresher = new FakeTokenRefresher()`, `_switcher = new AccountSwitcher(_home.Paths, _home.ClaudeLocks, _home.Store, _profiles, _refresher, _home.Clock)`. Helper `Arrange()` stores A (old credentials `a-refresh-1`, added day −2) and T (`t-refresh`, added day −1), writes live A rotated (`a-refresh-2`) with `ClaudeJson.GlobalConfig("acc-a","org-a","a@example.com")`, sets `_profiles.Result = Found("acc-a","org-a")`, and returns `(t, liveA, configBefore)`.

```csharp
[Fact]
public async Task Switch_writes_the_target_and_saves_the_previous_login()
{
    var (t, liveA, configBefore) = Arrange();

    var result = await _switcher.SwitchAsync(t.Key, null, default);

    Assert.Equal(new SwitchResult.Switched("t@example.com"), result);
    Assert.Equal(t.CredentialsJson, _home.LiveCredentials);
    var (start, length) = JsonSplice.Find(configBefore, "oauthAccount")!.Value;
    Assert.Equal([.. configBefore[..start], .. t.OAuthAccountJson, .. configBefore[(start + length)..]], _home.LiveConfig);
    Assert.Equal(liveA, _home.Store.Find(new AccountKey("acc-a", "org-a"))!.CredentialsJson);
    Assert.Equal(Start, _home.Store.Find(t.Key)!.LastActiveAt);
}

[Fact]
public async Task Locks_are_taken_in_claude_codes_order_and_released_in_reverse()
{
    var (t, _, _) = Arrange();

    await _switcher.SwitchAsync(t.Key, null, default);

    Assert.Equal(
        ["create .oauth_refresh.lock", "create .claude.lock", "create .storage-write.lock", "create .claude.json.lock",
         "delete .claude.json.lock", "delete .storage-write.lock", "delete .claude.lock", "delete .oauth_refresh.lock"],
        _home.Locks.Log);
}

[Fact]
public async Task Busy_config_lock_restores_the_previous_credentials()
{
    var (t, liveA, configBefore) = Arrange();
    _home.Locks.AddForeign(_home.Paths.GlobalConfigLock, _home.Clock.Now);

    var result = await _switcher.SwitchAsync(t.Key, null, default);

    Assert.Equal(new SwitchResult.Failed(AccountSwitcher.Busy), result);
    Assert.Equal(liveA, _home.LiveCredentials);
    Assert.Equal(configBefore, _home.LiveConfig);
}
```
  Plus:
  - `Config_write_failure_restores_the_previous_credentials`: `File.SetAttributes(_home.Paths.GlobalConfig, FileAttributes.ReadOnly)` → `Failed`; credentials = `liveA`; config unchanged.
  - `Unverified_unknown_active_account_aborts_without_changes`: A not stored, `Unavailable` → `Failed(Unverified)`; both files and the store unchanged.
  - `Store_save_failure_aborts_before_touching_claude_files`: A not stored, profile Found, a directory created at `_home.Paths.Store` beforehand (use a fresh `AccountStore` on that path holding only T in memory) → `Failed`; credentials = `liveA`.
  - `Expired_target_is_refused_without_locks`: T `LoginState.Expired` → `Failed(TargetExpired)`; `_refresher.Calls` empty; `Log` empty. Same for T whose `refreshTokenExpiresAt` is in the past.
  - `Refresh_reporting_login_expired_aborts`: `_refresher.Outcome = LoginExpired` → `Failed(TargetExpired)`; no files touched.
  - `Files_changing_before_the_lock_abort`: `AfterCreate` on `StorageWriteLock` rewrites credentials → `Failed(Changing)`; credentials = the rewritten bytes (not T).
  - `Busy_refresh_lock_fails_cleanly`: foreign young `OAuthRefreshLock` → `Failed(Busy)`; files unchanged.
  - `Post_check_mismatch_is_unconfirmed`: `BeforeDelete` on `StorageWriteLock` rewrites credentials with other bytes → `Unconfirmed("t@example.com")`.
  - `Half_finished_switch_is_repaired_by_switching_again`: live credentials = `t.CredentialsJson`, config says A, A stored → switching to T gives `Switched`; config now holds T's `oauthAccount`; no profile call.
  - `Switch_without_signed_in_claude_code_is_refused`: credentials file deleted → `Failed(NoLogin)`; nothing written.
  - `Switch_to_the_active_account_is_refused`: target = A → `Failed(AlreadyActive)`.

- [x] **Step 3: Run, expect FAIL.**

- [x] **Step 4: Implement**

```csharp
namespace ClaudeMeter.Core;

internal enum RefreshOutcome
{
    /// <summary>The token is usable for at least five more minutes.</summary>
    Fresh,
    Refreshed,
    /// <summary>invalid_grant: only /login helps.</summary>
    LoginExpired,
    /// <summary>Not refreshed (toggle off, disabled, backoff, offline, or Claude Code holds it).</summary>
    Unavailable,
}

/// <summary>Refreshes the tokens of accounts the app owns, i.e. inactive ones.</summary>
internal interface ITokenRefresher
{
    Task<RefreshOutcome> EnsureFreshAsync(AccountKey key, CancellationToken cancellationToken);
}

internal enum SwitchStage
{
    WaitingForLock,
    Writing,
}

internal abstract record SwitchResult
{
    private SwitchResult() { }

    public sealed record Switched(string Email) : SwitchResult;
    /// <summary>Files were written, but re-reading them did not show the target.</summary>
    public sealed record Unconfirmed(string Email) : SwitchResult;
    public sealed record Failed(string Message) : SwitchResult;
}

/// <summary>
/// Replaces Claude Code's global login with a saved one, under Claude Code's locks. From the
/// credentials write on, Claude Code owns the target and the app owns the previous account.
/// </summary>
internal sealed class AccountSwitcher(ClaudePaths paths, ClaudeLocks locks, AccountStore store, IProfileClient profiles, ITokenRefresher refresher, TimeProvider clock)
{
    public const string Busy = "Claude Code is refreshing its login. Try again in a few seconds.";
    public const string Changing = "Login files are changing. Try again in a few seconds.";
    public const string Unverified = "Current account isn't saved yet (couldn't verify it). Check your connection and try again.";
    public const string NoLogin = "Claude Code has no signed-in account. Run /login in Claude Code first.";
    public const string TargetExpired = "This account's login expired. Run /login in Claude Code with it.";
    public const string AlreadyActive = "This account is already active.";

    public async Task<SwitchResult> SwitchAsync(AccountKey targetKey, IProgress<SwitchStage>? progress, CancellationToken cancellationToken)
    {
        // 1. Outside locks: a usable target, and the network check of the live login.
        if (store.Find(targetKey) is not { } target)
            return Fail("This account is no longer saved.");
        if (IsExpired(target))
            return Fail(TargetExpired);
        if (await refresher.EnsureFreshAsync(targetKey, cancellationToken) == RefreshOutcome.LoginExpired)
            return Fail(TargetExpired);

        LiveLogin? before;
        try
        {
            before = LiveLogin.From(LiveFiles.Read(paths));
        }
        catch (FormatException)
        {
            return Fail(Changing);
        }

        if (before is null)
            return Fail(NoLogin);
        if (before.Account.Key == targetKey)
            return Fail(AlreadyActive);

        var profile = store.FindByCredentials(before.CredentialsJson) is null
            ? await ProfileCheck.RunAsync(profiles, before.Credentials, clock, cancellationToken)
            : null;

        progress?.Report(SwitchStage.WaitingForLock);
        try
        {
            // 2.
            using var refreshLocks = await locks.AcquireRefreshAsync(cancellationToken);
            using var storageLock = await locks.AcquireStorageWriteAsync(cancellationToken);
            progress?.Report(SwitchStage.Writing);

            // 3. Attribute the live login to A; A must be on disk before its live copy is replaced.
            var live = LiveLogin.From(LiveFiles.Read(paths));
            if (live is null || !live.SameAs(before))
                return Fail(Changing);
            if (profile is not null)
            {
                var decision = CaptureVerifier.Evaluate(live, store.Accounts, profile);
                if (decision.Verdict != CaptureVerdict.Accept)
                    return Fail(decision.Verdict == CaptureVerdict.Defer ? Unverified : Changing);
                store.Upsert(store.Find(live.Account.Key)?.WithLiveLogin(live) ?? StoredAccount.Create(live, clock.GetUtcNow()));
                if (!store.TrySave())
                    return Fail("Couldn't save the current account, so nothing was changed.");
            }

            // 4–5.
            target = store.Find(targetKey)!;
            var rollback = live.CredentialsJson;
            AtomicFile.Write(paths.Credentials, target.CredentialsJson);

            // 6–7.
            try
            {
                using var configLock = await locks.AcquireGlobalConfigAsync(cancellationToken);
                var config = File.ReadAllBytes(paths.GlobalConfig);
                AtomicFile.Write(paths.GlobalConfig, JsonSplice.Replace(config, target.OAuthAccountJson, "oauthAccount"));
            }
            catch (Exception ex) when (ex is LockTimeoutException or IOException or UnauthorizedAccessException or FormatException)
            {
                try
                {
                    AtomicFile.Write(paths.Credentials, rollback);
                }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                {
                    return Fail($"Couldn't finish the switch or undo it. Switch to {live.Account.Email} again from the panel.");
                }

                return Fail(ex is LockTimeoutException ? Busy : "Couldn't update Claude Code's settings file. Nothing was changed.");
            }

            // 8.
            var now = clock.GetUtcNow();
            foreach (var key in new[] { targetKey, live.Account.Key })
            {
                if (store.Find(key) is { } account)
                    store.Upsert(account with { LastActiveAt = now });
            }
            store.TrySave();
        }
        catch (LockTimeoutException)
        {
            return Fail(Busy);
        }
        catch (FormatException)
        {
            return Fail(Changing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("Couldn't write Claude Code's login file. Nothing was changed.");
        }

        // 9. Post-check, outside locks.
        LiveLogin? after;
        try
        {
            after = LiveLogin.From(LiveFiles.Read(paths));
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            after = null;
        }

        return after is not null && after.Account.Key == targetKey && after.CredentialsJson.AsSpan().SequenceEqual(target.CredentialsJson)
            ? new SwitchResult.Switched(target.Email)
            : new SwitchResult.Unconfirmed(target.Email);

        static SwitchResult Fail(string message) => new SwitchResult.Failed(message);
    }

    private bool IsExpired(StoredAccount account) =>
        account.LoginState == LoginState.Expired || account.Credentials?.RefreshTokenExpiresAt <= clock.GetUtcNow();
}
```

Note on the outer `IOException` catch: it covers only failures before or at step 5 (atomic, so nothing changed). Steps 6–7 handle their own failures.

- [x] **Step 5: Run, expect PASS:** `dotnet test --filter "FullyQualifiedName~AccountSwitcherTests"`.

### Task 1.8: Wire auto-save into the app, then commit Phase 1

**Files:**
- Create: `src/ClaudeMeter/UI/ClaudeFilesWatcher.cs`
- Modify: `src/ClaudeMeter/UI/TrayApplicationContext.cs`

- [x] **Step 1: Watcher**

```csharp
using ClaudeMeter.Core;

namespace ClaudeMeter.UI;

/// <summary>Raises <see cref="Changed"/> on the UI thread ~2 s after Claude Code's login files stop changing.</summary>
internal sealed class ClaudeFilesWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 2000 };
    private readonly SynchronizationContext _ui = SynchronizationContext.Current!;

    public event EventHandler? Changed;

    public ClaudeFilesWatcher(ClaudePaths paths)
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        foreach (var file in new[] { paths.Credentials, paths.GlobalConfig })
        {
            var folder = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(folder))
                continue;
            var watcher = new FileSystemWatcher(folder, Path.GetFileName(file))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Renamed += OnEvent;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => _ui.Post(_ =>
    {
        _debounce.Stop();
        _debounce.Start();
    }, null);

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _debounce.Dispose();
    }
}
```

- [x] **Step 2: TrayApplicationContext.** Add fields `_store = new AccountStore(_paths.Store, new DpapiProtector())`, `_profiles = new ProfileClient()`, `_autoSaver`, `_watcher`, `bool _autoSaving`. In the constructor: `_store.Load()` (on `SetAside`, `ShowBalloonTip` "Saved accounts couldn't be read" with the Notifications body from item 12); `_autoSaver = new AutoSaver(_paths, ClaudeLocks.ForSystem(_paths), _store, _profiles, TimeProvider.System)`; `_watcher = new ClaudeFilesWatcher(_paths)` with `Changed += async (_, _) => await AutoSaveAsync()`. In `OnTimerTick` call `await AutoSaveAsync()` before `RefreshAsync`. Dispose `_watcher` and `_profiles`.

```csharp
private async Task AutoSaveAsync()
{
    if (_autoSaving)
        return;
    _autoSaving = true;
    try
    {
        if (await _autoSaver.RunAsync(CancellationToken.None) is AutoSaveResult.Inserted inserted)
            _notifyIcon.ShowBalloonTip(5000, $"Added {inserted.Email}", "Claude Meter saved this login. Switch accounts from the tray panel.", ToolTipIcon.None);
    }
    finally
    {
        _autoSaving = false;
    }
}
```

- [x] **Step 3: Verify.** `dotnet build`; `dotnet test` (all green). Run the app (`dotnet run --project src/ClaudeMeter`); within a minute a toast "Added <your email>" appears and `%LOCALAPPDATA%\ClaudeMeter\accounts.dat` exists and does not contain your email in plain text (`Select-String -Path "$env:LOCALAPPDATA\ClaudeMeter\accounts.dat" -Pattern '@' -SimpleMatch` finds nothing). The tray icon still shows usage.

- [x] **Step 4: Commit Phase 1**

```powershell
git add docs/MULTI-ACCOUNT-SPEC.md docs/MULTI-ACCOUNT-PLAN.md src tests
git commit -m "Save Claude Code logins as encrypted snapshots and add the switch transaction" -m "Core for multi-account support: byte-exact JSON splicing, Claude Code-compatible lock directories, DPAPI account store, capture verification against the profile endpoint, auto-save and the switch transaction with rollback. The app now saves the active login automatically; switching has no UI yet."
```

---

# Phase 2: Accounts runtime

### Task 2.1: Token endpoint client

**Files:**
- Create: `src/ClaudeMeter/Core/OAuthTokenClient.cs`, `src/ClaudeMeter/Core/AppInfo.cs`
- Modify: `tests/ClaudeMeter.Tests/Fakes.cs` (add `FakeOAuthTokenClient`)
- Test: `tests/ClaudeMeter.Tests/TokenResponseParserTests.cs`

**Interfaces:**
- Produces:
  - `AppInfo.Version` (informational version without `+commit`, e.g. `1.4.0`).
  - `abstract record TokenResult` with `Success(string AccessToken, TimeSpan ExpiresIn, string? RefreshToken, IReadOnlyList<string>? Scopes)`, `InvalidGrant`, `InvalidClient`, `Transient(RetryConditionHeaderValue? RetryAfter, string Reason)`.
  - `interface IOAuthTokenClient { Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken); }`, `OAuthTokenClient : IOAuthTokenClient, IDisposable`.
  - `TokenResponseParser.ParseSuccess(string) → TokenResult` and `TokenResponseParser.ParseError(string) → TokenResult`.
  - Test fake `FakeOAuthTokenClient` (`Results` queue, `RefreshTokensSeen`).

- [x] **Step 1: Write the failing tests** (`TokenResponseParserTests`):
  - `{"access_token":"a2","refresh_token":"r2","expires_in":28800,"scope":"user:inference user:profile"}` → `Success("a2", 8 h, "r2", ["user:inference","user:profile"])` (compare fields; `Scopes` with `Assert.Equal` on the list).
  - Without `refresh_token` and `scope` → `RefreshToken` and `Scopes` null.
  - `scope` given as a JSON array → same list.
  - Missing `access_token`, missing or non-positive `expires_in`, or not JSON → `Transient(null, "invalid response")`.
  - `ParseError`: `{"error":"invalid_grant"}` → `InvalidGrant`; `{"error":"invalid_client"}` → `InvalidClient`; `{"error":"invalid_request"}` and `not json` → `Transient(null, "HTTP 400")`.

- [x] **Step 2: Run, expect FAIL.**

- [x] **Step 3: Implement**

```csharp
// AppInfo.cs
using System.Reflection;

namespace ClaudeMeter.Core;

internal static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
```

```csharp
// OAuthTokenClient.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClaudeMeter.Core;

internal abstract record TokenResult
{
    private TokenResult() { }

    public sealed record Success(string AccessToken, TimeSpan ExpiresIn, string? RefreshToken, IReadOnlyList<string>? Scopes) : TokenResult;
    /// <summary>The refresh token is dead: the login expired or was revoked.</summary>
    public sealed record InvalidGrant : TokenResult;
    /// <summary>Anthropic rejected Claude Code's client id for us.</summary>
    public sealed record InvalidClient : TokenResult;
    /// <param name="Reason">Short, generic; never includes response or exception text.</param>
    public sealed record Transient(RetryConditionHeaderValue? RetryAfter, string Reason) : TokenResult;
}

internal interface IOAuthTokenClient
{
    Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>Claude Code's token endpoint, used only for accounts Claude Code does not hold.</summary>
internal sealed class OAuthTokenClient : IOAuthTokenClient, IDisposable
{
    private static readonly Uri Endpoint = new("https://platform.claude.com/v1/oauth/token");
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public OAuthTokenClient() => _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeMeter/{AppInfo.Version}");

    public async Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(Endpoint, body, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => TokenResponseParser.ParseSuccess(text),
                HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized => TokenResponseParser.ParseError(text),
                HttpStatusCode.TooManyRequests => new TokenResult.Transient(response.Headers.RetryAfter, "rate limited"),
                _ => new TokenResult.Transient(null, $"HTTP {(int)response.StatusCode}"),
            };
        }
        catch (HttpRequestException)
        {
            return new TokenResult.Transient(null, "network error");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TokenResult.Transient(null, "timeout");
        }
    }

    public void Dispose() => _http.Dispose();
}

internal static class TokenResponseParser
{
    public static TokenResult ParseSuccess(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || CredentialsInfo.Text(root, "access_token") is not { Length: > 0 } accessToken
                || !root.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt64(out var seconds) || seconds <= 0)
                return Invalid();

            IReadOnlyList<string>? scopes = root.TryGetProperty("scope", out var scope) ? scope.ValueKind switch
            {
                JsonValueKind.String => scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                JsonValueKind.Array => [.. scope.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!)],
                _ => null,
            } : null;

            return new TokenResult.Success(accessToken, TimeSpan.FromSeconds(seconds), CredentialsInfo.Text(root, "refresh_token"), scopes);
        }
        catch (JsonException)
        {
            return Invalid();
        }

        static TokenResult Invalid() => new TokenResult.Transient(null, "invalid response");
    }

    public static TokenResult ParseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return (doc.RootElement.ValueKind == JsonValueKind.Object ? CredentialsInfo.Text(doc.RootElement, "error") : null) switch
            {
                "invalid_grant" => new TokenResult.InvalidGrant(),
                "invalid_client" => new TokenResult.InvalidClient(),
                _ => new TokenResult.Transient(null, "HTTP 400"),
            };
        }
        catch (JsonException)
        {
            return new TokenResult.Transient(null, "HTTP 400");
        }
    }
}
```

Fake:

```csharp
internal sealed class FakeOAuthTokenClient : IOAuthTokenClient
{
    public Queue<TokenResult> Results { get; } = new();
    public List<string> RefreshTokensSeen { get; } = [];

    public Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RefreshTokensSeen.Add(refreshToken);
        return Task.FromResult(Results.Dequeue());
    }
}
```

- [x] **Step 4: Run, expect PASS.**

### Task 2.2: Token refresher for inactive accounts

**Files:**
- Create: `src/ClaudeMeter/Core/TokenRefresher.cs`
- Test: `tests/ClaudeMeter.Tests/TokenRefresherTests.cs`

**Interfaces:**
- Consumes: `AccountStore`, `IOAuthTokenClient`, `JsonSplice`, `RefreshPolicy`, `ClaudePaths`, `LiveFiles`, `ITokenRefresher`, `RefreshOutcome`.
- Produces:
  - `record ClaudeHolder(AccountKey? ActiveKey, string? LiveRefreshToken)` with `Holds(StoredAccount) → bool` and `static Read(ClaudePaths) → ClaudeHolder?` (null if the files can't be read or parsed).
  - `TokenRefresher(AccountStore, IOAuthTokenClient, Func<ClaudeHolder?> readHolder, Func<bool> keepLive, TimeProvider) : ITokenRefresher` with `Margin` (5 min), `CanRefresh`, `GloballyDisabled`, `event EventHandler? Disabled`, `NeedsRefresh(StoredAccount)`, `EnsureFreshAsync`, `ForceRefreshAsync(AccountKey, CancellationToken)`.
  - Not reentrant by design: callers serialize through `OperationGate` (Task 2.6).

- [x] **Step 1: Write the failing tests.** Fixture: `_home`; T stored with credentials expiring at `Start + 2 min` (`t-access-1`, `t-refresh-1`, `refreshTokenExpiresAt` day +20); `_holder = new ClaudeHolder(new AccountKey("acc-a","org-a"), "a-refresh")`; `_tokens = new FakeOAuthTokenClient()`; `_keepLive = true`; `_refresher = new TokenRefresher(_home.Store, _tokens, () => _holder, () => _keepLive, _home.Clock)`.

```csharp
[Fact]
public async Task Refresh_splices_new_tokens_and_keeps_every_other_byte()
{
    var before = _home.Store.Find(T)!.CredentialsJson;
    _tokens.Results.Enqueue(new TokenResult.Success("t-access-2", TimeSpan.FromHours(8), "t-refresh-2", ["user:inference"]));

    Assert.Equal(RefreshOutcome.Refreshed, await _refresher.EnsureFreshAsync(T, default));

    var expected = JsonSplice.Replace(before, JsonSplice.EncodeString("t-access-2"), "claudeAiOauth", "accessToken");
    expected = JsonSplice.Replace(expected, JsonSplice.EncodeNumber((Start + TimeSpan.FromHours(8)).ToUnixTimeMilliseconds()), "claudeAiOauth", "expiresAt");
    expected = JsonSplice.Replace(expected, JsonSplice.EncodeString("t-refresh-2"), "claudeAiOauth", "refreshToken");
    expected = JsonSplice.Replace(expected, JsonSplice.EncodeStringArray(["user:inference"]), "claudeAiOauth", "scopes");
    Assert.Equal(expected, _home.Store.Find(T)!.CredentialsJson);
    Assert.False(_home.Store.HasUnsavedChanges);
}
```
  Plus:
  - `Refresh_token_and_scopes_are_kept_when_not_returned`: `Success("t-access-2", 8 h, null, null)` → `refreshToken` still `t-refresh-1`, `scopes` unchanged; `refreshTokenExpiresAt` unchanged (`Credentials.RefreshTokenExpiresAt` equal before/after).
  - `Fresh_token_is_not_refreshed`: T expires at `Start + 6 min` → `Fresh`; no request.
  - `Invalid_grant_marks_the_login_expired`: → `LoginExpired`; stored `LoginState.Expired` saved; the next call returns `LoginExpired` with no request.
  - `Invalid_client_disables_refresh_for_all_accounts_once`: → `Unavailable`; `GloballyDisabled`; `Disabled` raised once; a second account's refresh sends no request and raises nothing.
  - `Transient_error_backs_off`: `Transient(null, "network error")` → `Unavailable`; an immediate second call sends no request; after `RefreshPolicy.Backoff(1)` (20 min) a request is sent again.
  - `Active_account_is_never_refreshed`: `_holder = new(T, null)` → `Unavailable`; no request.
  - `Account_whose_refresh_token_is_live_is_never_refreshed`: `_holder = new(A, "t-refresh-1")` → `Unavailable`; no request.
  - `Unknown_holder_blocks_refresh`: `readHolder` returns null → `Unavailable`; no request.
  - `Toggle_off_sends_nothing`: `_keepLive = false` → `Unavailable`; no request.
  - `Failed_save_blocks_further_refreshes`: make the store unwritable (directory at `Paths.Store` after loading); first refresh → `Refreshed` with the new token in memory and `HasUnsavedChanges`; advance 10 min; second (forced) → `Unavailable`, no request; restore writability, `TrySave()`, advance 10 min → a forced refresh sends again. (Advancing past `RefreshPolicy.PollInterval` keeps the policy from masking the save check.)
  - `Force_refresh_ignores_the_margin`: T expires in 6 h; `ForceRefreshAsync` → request sent.

- [x] **Step 2: Run, expect FAIL.**

- [x] **Step 3: Implement**

```csharp
namespace ClaudeMeter.Core;

/// <summary>What Claude Code holds right now, read fresh from its files.</summary>
internal sealed record ClaudeHolder(AccountKey? ActiveKey, string? LiveRefreshToken)
{
    /// <summary>True if Claude Code uses this account, by identity or because its refresh token is the live one.</summary>
    public bool Holds(StoredAccount account) =>
        account.Key == ActiveKey || (LiveRefreshToken is not null && account.Credentials?.RefreshToken == LiveRefreshToken);

    /// <returns>Null if the files can't be read; then nothing may be refreshed.</returns>
    public static ClaudeHolder? Read(ClaudePaths paths)
    {
        try
        {
            var files = LiveFiles.Read(paths);
            return new ClaudeHolder(
                files.GlobalConfig is { } config ? OAuthAccount.Parse(config)?.Key : null,
                files.Credentials is { } credentials ? CredentialsInfo.Parse(credentials)?.RefreshToken : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Refreshes the saved tokens of inactive accounts so their usage stays live. Never touches the
/// account Claude Code holds. Callers serialize calls through the runtime's gate.
/// </summary>
internal sealed class TokenRefresher(AccountStore store, IOAuthTokenClient client, Func<ClaudeHolder?> readHolder, Func<bool> keepLive, TimeProvider clock) : ITokenRefresher
{
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly Dictionary<AccountKey, RefreshPolicy> _backoff = [];

    public bool GloballyDisabled { get; private set; }

    public bool CanRefresh => keepLive() && !GloballyDisabled;

    /// <summary>Raised once when Anthropic rejects the client id.</summary>
    public event EventHandler? Disabled;

    public bool NeedsRefresh(StoredAccount account) =>
        account.Credentials?.ExpiresAt is not { } expiresAt || expiresAt - clock.GetUtcNow() <= Margin;

    public Task<RefreshOutcome> EnsureFreshAsync(AccountKey key, CancellationToken cancellationToken) => RefreshAsync(key, force: false, cancellationToken);

    /// <summary>After a 401: refresh even if the token looks valid.</summary>
    public Task<RefreshOutcome> ForceRefreshAsync(AccountKey key, CancellationToken cancellationToken) => RefreshAsync(key, force: true, cancellationToken);

    private async Task<RefreshOutcome> RefreshAsync(AccountKey key, bool force, CancellationToken cancellationToken)
    {
        if (store.Find(key) is not { } account)
            return RefreshOutcome.Unavailable;
        if (account.LoginState == LoginState.Expired)
            return RefreshOutcome.LoginExpired;
        if (!force && !NeedsRefresh(account))
            return RefreshOutcome.Fresh;
        if (!CanRefresh || store.HasUnsavedChanges || account.Credentials?.RefreshToken is not { } refreshToken)
            return RefreshOutcome.Unavailable;
        if (readHolder() is not { } holder || holder.Holds(account))
            return RefreshOutcome.Unavailable;

        var policy = Backoff(key);
        if (!policy.TryBeginRequest(manual: false))
            return RefreshOutcome.Unavailable;

        var result = await client.RefreshAsync(refreshToken, cancellationToken);

        // Re-find: the record may have changed while the request was out.
        if (store.Find(key) is not { } current || !ReferenceEquals(current.CredentialsJson, account.CredentialsJson))
            return RefreshOutcome.Unavailable;

        switch (result)
        {
            case TokenResult.Success success:
                policy.OnSuccess();
                var json = Splice(current.CredentialsJson, "accessToken", JsonSplice.EncodeString(success.AccessToken));
                json = Splice(json, "expiresAt", JsonSplice.EncodeNumber((clock.GetUtcNow() + success.ExpiresIn).ToUnixTimeMilliseconds()));
                if (success.RefreshToken is { } rotated)
                    json = Splice(json, "refreshToken", JsonSplice.EncodeString(rotated));
                if (success.Scopes is { } scopes)
                    json = Splice(json, "scopes", JsonSplice.EncodeStringArray(scopes));
                store.Upsert(current with { CredentialsJson = json });
                store.TrySave();   // if this fails, HasUnsavedChanges blocks further refreshes until a save succeeds
                return RefreshOutcome.Refreshed;

            case TokenResult.InvalidGrant:
                store.Upsert(current with { LoginState = LoginState.Expired });
                store.TrySave();
                return RefreshOutcome.LoginExpired;

            case TokenResult.InvalidClient:
                if (!GloballyDisabled)
                {
                    GloballyDisabled = true;
                    Disabled?.Invoke(this, EventArgs.Empty);
                }
                return RefreshOutcome.Unavailable;

            case TokenResult.Transient transient:
                policy.OnRateLimited(transient.RetryAfter);
                return RefreshOutcome.Unavailable;

            default:
                throw new InvalidOperationException("Unknown token result.");
        }
    }

    private static byte[] Splice(byte[] json, string field, byte[] value) => JsonSplice.Replace(json, value, "claudeAiOauth", field);

    private RefreshPolicy Backoff(AccountKey key) =>
        _backoff.TryGetValue(key, out var policy) ? policy : _backoff[key] = new RefreshPolicy(clock);
}
```

Note: `RefreshPolicy.TryBeginRequest(manual: false)` also limits refreshes to one per 10 minutes per account, which is far below the 8-hour token life.

- [x] **Step 4: Run, expect PASS.**

### Task 2.3: Per-account token source for UsageService

**Files:**
- Create: `src/ClaudeMeter/Core/AccountTokenSource.cs`
- Modify: `src/ClaudeMeter/Core/UsageService.cs`, `src/ClaudeMeter/Core/UsageState.cs`, `tests/ClaudeMeter.Tests/Fakes.cs` (`FakeUsageClient.Responder`)
- Test: `tests/ClaudeMeter.Tests/UsageServiceTests.cs` (add cases), `tests/ClaudeMeter.Tests/AccountTokenSourceTests.cs`

**Interfaces:**
- Consumes: `TokenRefresher`, `AccountStore`, `OperationGate` (defined here, used by Task 2.6).
- Produces:
  - `UsageStatus.NotLive` (inactive account without a usable token: show last known usage).
  - `abstract record TokenLookup` with `Found(string AccessToken)`, `NoCredentials`, `NotLive`.
  - `interface IAccessTokenSource { Task<TokenLookup> GetAsync(CancellationToken); Task<string?> RenewAsync(string rejectedToken, CancellationToken); }`.
  - `UsageService(IUsageClient, IAccessTokenSource, RefreshPolicy)` (old `Func<string?>` constructor kept) and `UsageService.Seed(UsageSnapshot, DateTimeOffset)`.
  - `OperationGate` with `EnterAsync(CancellationToken) → Task<IDisposable>`.
  - `AccountTokenSource(AccountKey key, Func<AccountKey?> activeKey, Func<string?> readLiveToken, AccountStore store, TokenRefresher refresher, OperationGate gate, TimeProvider clock) : IAccessTokenSource`.

- [x] **Step 1: Fake usage client responder.** In `FakeUsageClient` add `public Func<string, FetchResult>? Responder { get; set; }` and use it when set: `return Task.FromResult(Responder?.Invoke(accessToken) ?? Results.Dequeue());`.

- [x] **Step 2: Write the failing tests.**
  - `UsageServiceTests` additions (with a small `FakeTokenSource` in the test file: `Lookup`, `Renewed`, `RenewCalls`):
    - `Unauthorized_renews_once_and_retries_with_the_new_token`: first token `old` → `Unauthorized`; `Renewed = "new"` → `Success`. Expect `TokensSeen == ["old","new"]`, status `Ok`, one `RenewAsync` call.
    - `Unauthorized_without_renewal_is_token_expired`: `Renewed = null` → `TokenExpired`; one fetch.
    - `Not_live_sends_nothing_and_keeps_data`: after an `Ok` poll, `Lookup = NotLive`, advance 10 min → status `NotLive`, snapshot kept, no new request.
    - `Seed_shows_last_known_usage_until_the_first_fetch`: `Seed(snapshot, Start - 2 h)` → state has the snapshot, `LastUpdated = Start - 2 h`, status `Loading`.
  - `AccountTokenSourceTests` (`ClaudeHome`, real `TokenRefresher` with `FakeOAuthTokenClient`, `OperationGate`):
    - `Active_account_uses_the_live_token_and_never_renews`: `activeKey` = key; live token `"live"` → `Found("live")`; `RenewAsync` → null; no token request.
    - `Inactive_account_uses_its_saved_token`: expires in 6 h → `Found(saved)`; no token request.
    - `Inactive_near_expiry_is_refreshed_first`: expires in 2 min, token client returns `t-access-2` → `Found("t-access-2")`.
    - `Without_refresh_polling_stops_at_expires_at`: `keepLive` false, token expired → `NotLive`.
    - `Without_refresh_polling_stops_after_a_401`: `keepLive` false, `RenewAsync("t-access-1")` → null; next `GetAsync` → `NotLive` although `expiresAt` is ahead.
    - `With_refresh_a_401_refreshes_once`: `RenewAsync` → forced refresh → `"t-access-2"`.
    - `Expired_login_is_not_live`: `LoginState.Expired` → `NotLive`; no token request.

- [x] **Step 3: Run, expect FAIL.**

- [x] **Step 4: Change `UsageState.cs`** (add the enum member after `NoCredentials`):

```csharp
    /// <summary>Inactive account without a usable token; the snapshot is the last known usage.</summary>
    NotLive,
```

- [x] **Step 5: Change `UsageService.cs`**

```csharp
namespace ClaudeMeter.Core;

internal abstract record TokenLookup
{
    private TokenLookup() { }

    public sealed record Found(string AccessToken) : TokenLookup;
    public sealed record NoCredentials : TokenLookup;
    public sealed record NotLive : TokenLookup;
}

internal interface IAccessTokenSource
{
    /// <exception cref="IOException">A credentials file could not be read; treated as transient.</exception>
    Task<TokenLookup> GetAsync(CancellationToken cancellationToken);

    /// <summary>After a 401: a new token to retry once with, or null.</summary>
    Task<string?> RenewAsync(string rejectedToken, CancellationToken cancellationToken);
}

/// <summary>
/// Gets a token, calls the endpoint when <see cref="RefreshPolicy"/> allows it, and folds the
/// result into <see cref="State"/>. All state transitions happen here.
/// </summary>
internal sealed class UsageService(IUsageClient client, IAccessTokenSource tokens, RefreshPolicy policy)
{
    private bool _inFlight;

    public UsageService(IUsageClient client, Func<string?> readAccessToken, RefreshPolicy policy)
        : this(client, new DelegateTokenSource(readAccessToken), policy)
    {
    }

    public UsageState State { get; private set; } = UsageState.Initial;

    public TimeSpan TimeUntilNextPoll => policy.TimeUntilNextPoll;

    /// <summary>Shows saved usage until the first fetch completes.</summary>
    public void Seed(UsageSnapshot snapshot, DateTimeOffset fetchedAt)
    {
        if (State.Snapshot is null)
            State = State with { Snapshot = snapshot, LastUpdated = fetchedAt };
    }

    // RefreshAsync unchanged.

    private async Task<UsageState> FetchAsync(CancellationToken cancellationToken)
    {
        TokenLookup lookup;
        try
        {
            // Re-read on every poll so a token refreshed by Claude Code is picked up.
            lookup = await tokens.GetAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(UsageStatus.Error, "credentials file unreadable");
        }

        string token;
        switch (lookup)
        {
            case TokenLookup.Found found:
                token = found.AccessToken;
                break;
            case TokenLookup.NotLive:
                return State with { Status = UsageStatus.NotLive, Error = null };
            default:
                return Fail(UsageStatus.NoCredentials, "no credentials");
        }

        var result = await client.FetchAsync(token, cancellationToken);
        if (result is FetchResult.Unauthorized && await tokens.RenewAsync(token, cancellationToken) is { } renewed)
            result = await client.FetchAsync(renewed, cancellationToken);

        // The existing switch over result follows unchanged.
    }

    private sealed class DelegateTokenSource(Func<string?> read) : IAccessTokenSource
    {
        public Task<TokenLookup> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<TokenLookup>(read() is { } token ? new TokenLookup.Found(token) : new TokenLookup.NoCredentials());

        public Task<string?> RenewAsync(string rejectedToken, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
```

(`DelegateTokenSource.GetAsync` throws synchronously when `read` throws; the `await` inside the `try` still catches it, so `Unreadable_credentials_file_is_treated_as_transient_error` keeps passing.)

- [x] **Step 6: Implement `AccountTokenSource.cs`**

```csharp
namespace ClaudeMeter.Core;

/// <summary>Serializes everything that writes the store or Claude Code's files across awaits.</summary>
internal sealed class OperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}

/// <summary>
/// Token for one account's usage polls: Claude Code's live token while the account is active (never
/// refreshed by the app), otherwise the saved token, refreshed when "Keep inactive accounts live" allows.
/// </summary>
internal sealed class AccountTokenSource(
    AccountKey key,
    Func<AccountKey?> activeKey,
    Func<string?> readLiveToken,
    AccountStore store,
    TokenRefresher refresher,
    OperationGate gate,
    TimeProvider clock) : IAccessTokenSource
{
    private string? _rejected;

    public async Task<TokenLookup> GetAsync(CancellationToken cancellationToken)
    {
        if (activeKey() == key)
            return readLiveToken() is { } live ? new TokenLookup.Found(live) : new TokenLookup.NoCredentials();

        if (refresher.CanRefresh && store.Find(key) is { } stale && refresher.NeedsRefresh(stale))
        {
            using (await gate.EnterAsync(cancellationToken))
                await refresher.EnsureFreshAsync(key, cancellationToken);
        }

        if (store.Find(key) is not { LoginState: LoginState.Valid } account || account.Credentials is not { } credentials)
            return new TokenLookup.NotLive();

        var usable = credentials.ExpiresAt > clock.GetUtcNow() && credentials.AccessToken != _rejected;
        return usable ? new TokenLookup.Found(credentials.AccessToken) : new TokenLookup.NotLive();
    }

    public async Task<string?> RenewAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        if (activeKey() == key)
            return null;   // Claude Code owns this token

        _rejected = rejectedToken;
        if (!refresher.CanRefresh)
            return null;

        RefreshOutcome outcome;
        using (await gate.EnterAsync(cancellationToken))
            outcome = await refresher.ForceRefreshAsync(key, cancellationToken);
        return outcome == RefreshOutcome.Refreshed ? store.Find(key)?.Credentials?.AccessToken : null;
    }
}
```

- [x] **Step 7: Run, expect PASS** (`UsageServiceTests` all, old and new; `AccountTokenSourceTests`).

### Task 2.4: Login expiry, row formatting, reset inference, toast copy

**Files:**
- Create: `src/ClaudeMeter/Core/LoginExpiry.cs`, `src/ClaudeMeter/Core/Notifications.cs`
- Modify: `src/ClaudeMeter/Core/DisplayFormatter.cs` (add row models), `src/ClaudeMeter/Core/AccountSwitcher.cs` (use `LoginExpiry.IsExpired`, drop the private helper)
- Test: `tests/ClaudeMeter.Tests/LoginExpiryTests.cs`, `tests/ClaudeMeter.Tests/AccountRowTests.cs`

**Interfaces:**
- Consumes: `StoredAccount`, `UsageState`, `UsageWindow`, `AccountKey`, `SwitchStage`.
- Produces:
  - `enum Tone { Muted, Caution, Critical }`; `record LoginInfo(string Text, Tone Tone, bool Expired)`.
  - `LoginExpiry.IsExpired(StoredAccount, DateTimeOffset)`, `Describe(DateTimeOffset? loginExpiresAt, LoginState, DateTimeOffset? lastActiveAt, bool isActive, DateTimeOffset now) → LoginInfo`, `WarningDue(StoredAccount, DateTimeOffset) → string?` (the "in N days" text), `InDays(TimeSpan) → string`.
  - `record Notification(string Title, string Body)`; `Notifications.Switched/SwitchUnconfirmed/SwitchFailed/Added/LoginExpiring/RefreshStopped/StoreSetAside`.
  - `record AccountView(AccountKey Key, string Email, string? Plan, bool IsActive, bool IsSaved, UsageState Usage, LoginInfo Login, SwitchStage? Switching, bool SwitchedHere, string? CaptureWarning, DateTimeOffset? LoginExpiresAt)` (built by the runtime, Task 2.6).
  - `enum RowAction { Active, Switch, Switching, LoginExpired }`; `record BarModel(string Label, string Percent, string Resets, double Fraction, UsageLevel Level, bool Inferred)`; `record AccountRowModel(AccountKey Key, string Email, string? Plan, bool IsActive, RowAction Action, BarModel FiveHour, BarModel SevenDay, string Status, Tone StatusTone, string Login, Tone LoginTone)`; `record SettingsRowModel(AccountKey Key, string Email, string Sub, Tone SubTone, bool IsActive)`.
  - `DisplayFormatter.AccountRow(AccountView, TimeZoneInfo, DateTimeOffset now) → AccountRowModel`, `SettingsRow(AccountView, TimeZoneInfo, DateTimeOffset) → SettingsRowModel`, `PlanBadge(string?) → string?`, `Age(TimeSpan) → string`.

- [x] **Step 1: Write the failing tests.**
  - `LoginExpiryTests` (`now = Start`):
    - `Describe(now + 13.5 d)` → `("Login valid 13 days", Muted, false)`.
    - `now + 3 d + 1 min` → `"Login valid 3 days"`, Muted; `now + 3 d` → `"Login expires in 3 days"`, Caution (boundary: 3 days or less is caution).
    - `now + 1.5 d` → `"Login expires in 1 day"`; `now + 0.5 d` → `"Login expires today"`.
    - Past date → `("Login expired", Critical, true)`; future date with `LoginState.Expired` → expired too.
    - Missing date, inactive, last active 15 d ago → `("Last active 15 days ago", Caution)`; 14 d → Muted; 0 d → `"Last active today"`; active with missing date → `""`.
    - `WarningDue`: 2 d left → `"in 2 days"`; 2 d + 1 min → null; `ExpiryWarnedFor` equal to the value → null; `New_login_expiry_value_re_arms_the_warning`: warned for the old value, new `refreshTokenExpiresAt` 1 d ahead → `"in 1 day"`; past → null; `LoginState.Expired` → null.
  - `AccountRowTests` (UTC+7 zone and `Now` as in `DisplayFormatterTests`):
    - `Active_live_row`: Ok state fetched at `Now` → Action `Active`, Status `"Live · updated 09:00"` Muted, bars `("5h","42%","14:30",0.42,Green,false)` and `("7d", ...)`.
    - `Inactive_offline_row_shows_age`: Error `"network error"` with snapshot fetched 2 h ago → `"Offline · last known 2 h ago"`; Error `"rate limited"` → `"Rate limited · last known 2 h ago"`; Error without snapshot → `"Network error"`.
    - `Not_live_row_shows_last_known_age`: NotLive, fetched 3 days ago → `"Last known 3 days ago"`; NotLive without snapshot → `"Not live"`.
    - `Reset_inference_at_the_boundary`: five-hour `resets_at = Now`, fetched before → `("5h","≈0%","reset",0,Green,true)`; `resets_at = Now + 1 s` → normal; fetched at or after `resets_at` → normal.
    - `Expired_login_row`: `Login.Expired` → Action `LoginExpired`, Status `"Run /login in Claude Code with this account"` Critical, Login text `""`.
    - `Switching_row`: `Switching = WaitingForLock` → Action `Switching`, Status `"Waiting for Claude Code's lock…"`; `Writing` → `"Switching…"`.
    - `Unsaved_active_row`: `IsSaved = false`, `CaptureWarning = "Not saved yet: waiting for network to verify"` → Status that text, Caution.
    - `Token_expired_after_a_switch`: active, `TokenExpired`, `SwitchedHere` → `"Token expired, open Claude Code. If it asks you to log in, run /login with this account"` Caution; without `SwitchedHere` → `"Token expired, open Claude Code"`.
    - `Plan_badge`: `pro`→`Pro`, `max`→`Max`, `claude_team`→`Team`, null→null.
    - `Settings_row_sub_line`: active Pro valid until 24 Oct → `"Pro · active · login valid until 24 Oct"` Muted; Max ≤ 3 days → `"Max · login expires 13 Oct"` Caution; expired → `"Pro · login expired"` Critical.
    - `Age_format`: 30 s → `"just now"`, 15 min → `"15 min ago"`, 2 h → `"2 h ago"`, 47 h → `"47 h ago"`, 3 d → `"3 days ago"`.

- [x] **Step 2: Run, expect FAIL.**

- [x] **Step 3: Implement `LoginExpiry.cs`**

```csharp
namespace ClaudeMeter.Core;

internal enum Tone
{
    Muted,
    Caution,
    Critical,
}

internal sealed record LoginInfo(string Text, Tone Tone, bool Expired);

/// <summary>Every login ends at refreshTokenExpiresAt and needs /login again; refreshing does not extend it.</summary>
internal static class LoginExpiry
{
    private static readonly TimeSpan CautionWindow = TimeSpan.FromDays(3);
    private static readonly TimeSpan ToastWindow = TimeSpan.FromDays(2);
    private static readonly TimeSpan IdleWarning = TimeSpan.FromDays(14);

    public static bool IsExpired(StoredAccount account, DateTimeOffset now) =>
        account.LoginState == LoginState.Expired || account.Credentials?.RefreshTokenExpiresAt <= now;

    public static LoginInfo Describe(DateTimeOffset? loginExpiresAt, LoginState state, DateTimeOffset? lastActiveAt, bool isActive, DateTimeOffset now)
    {
        if (state == LoginState.Expired || loginExpiresAt <= now)
            return new LoginInfo("Login expired", Tone.Critical, Expired: true);

        if (loginExpiresAt is { } at)
        {
            var left = at - now;
            return left > CautionWindow
                ? new LoginInfo($"Login valid {Days((int)left.TotalDays)}", Tone.Muted, false)
                : new LoginInfo($"Login expires {InDays(left)}", Tone.Caution, false);
        }

        if (isActive || lastActiveAt is not { } last)
            return new LoginInfo("", Tone.Muted, false);

        var idle = now - last;
        var ago = (int)idle.TotalDays == 0 ? "today" : $"{Days((int)idle.TotalDays)} ago";
        return new LoginInfo($"Last active {ago}", idle > IdleWarning ? Tone.Caution : Tone.Muted, false);
    }

    /// <returns>"in N days" when a toast is due (2 days or less, once per refreshTokenExpiresAt), else null.</returns>
    public static string? WarningDue(StoredAccount account, DateTimeOffset now) =>
        account.LoginState == LoginState.Valid
        && account.Credentials?.RefreshTokenExpiresAt is { } at
        && at > now && at - now <= ToastWindow
        && account.ExpiryWarnedFor != at.ToUnixTimeMilliseconds()
            ? InDays(at - now)
            : null;

    public static string InDays(TimeSpan left) => (int)left.TotalDays switch
    {
        0 => "today",
        var days => $"in {Days(days)}",
    };

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";
}
```

- [x] **Step 4: Implement `Notifications.cs`**

```csharp
namespace ClaudeMeter.Core;

internal sealed record Notification(string Title, string Body);

/// <summary>Toast copy, as in the approved mockup.</summary>
internal static class Notifications
{
    public static Notification Switched(string email) =>
        new($"Switched to {email}", "Reopen Claude Code to use it. Running sessions may move to this account at their next token refresh.");

    public static Notification SwitchUnconfirmed(string email) =>
        new("Switch not confirmed", $"Claude Code's login files don't show {email}. Claude Meter saves no logins until it can verify one.");

    public static Notification SwitchFailed(string reason) => new("Couldn't switch accounts", reason);

    public static Notification Added(string email) =>
        new($"Added {email}", "Claude Meter saved this login. Switch accounts from the tray panel.");

    public static Notification LoginExpiring(string email, string inDays) =>
        new($"{email}: login expires {inDays}", "Switch to it and run /login in Claude Code to renew.");

    public static Notification RefreshStopped { get; } =
        new("Background refresh stopped", "Anthropic rejected the token refresh. Inactive accounts now show last known usage.");

    public static Notification StoreSetAside { get; } =
        new("Saved accounts couldn't be read", "Claude Meter kept the old file and starts fresh. Accounts are saved again as you use them.");
}
```

(Task 1.8's inline "Added" toast text switches to `Notifications.Added` here.)

- [x] **Step 5: Add row formatting to `DisplayFormatter.cs`** (new records next to `PanelRow`; `Panel`/`MenuLines` stay until Phase 3):

```csharp
internal sealed record AccountView(
    AccountKey Key, string Email, string? Plan, bool IsActive, bool IsSaved, UsageState Usage, LoginInfo Login,
    SwitchStage? Switching, bool SwitchedHere, string? CaptureWarning, DateTimeOffset? LoginExpiresAt);

internal enum RowAction { Active, Switch, Switching, LoginExpired }

/// <param name="Inferred">The window reset after the data was fetched: shown as ≈0%.</param>
internal sealed record BarModel(string Label, string Percent, string Resets, double Fraction, UsageLevel Level, bool Inferred);

internal sealed record AccountRowModel(
    AccountKey Key, string Email, string? Plan, bool IsActive, RowAction Action,
    BarModel FiveHour, BarModel SevenDay, string Status, Tone StatusTone, string Login, Tone LoginTone);

internal sealed record SettingsRowModel(AccountKey Key, string Email, string Sub, Tone SubTone, bool IsActive);
```

```csharp
    public static AccountRowModel AccountRow(AccountView view, TimeZoneInfo zone, DateTimeOffset now)
    {
        var usage = view.Usage;
        var action = view.Switching is not null ? RowAction.Switching
            : view.IsActive ? RowAction.Active
            : view.Login.Expired ? RowAction.LoginExpired
            : RowAction.Switch;

        var (status, tone) = view switch
        {
            { Switching: SwitchStage.WaitingForLock } => ("Waiting for Claude Code's lock…", Tone.Muted),
            { Switching: not null } => ("Switching…", Tone.Muted),
            { Login.Expired: true } => ("Run /login in Claude Code with this account", Tone.Critical),
            { CaptureWarning: { } warning } => (warning, Tone.Caution),
            _ => UsageStatusLine(view, zone, now),
        };

        return new AccountRowModel(
            view.Key, view.Email, PlanBadge(view.Plan), view.IsActive, action,
            Bar("5h", usage.Snapshot?.FiveHour, usage.LastUpdated, zone, now),
            Bar("7d", usage.Snapshot?.SevenDay, usage.LastUpdated, zone, now),
            status, tone,
            view.Login.Expired ? "" : view.Login.Text, view.Login.Tone);
    }

    private static (string, Tone) UsageStatusLine(AccountView view, TimeZoneInfo zone, DateTimeOffset now)
    {
        var usage = view.Usage;
        var age = usage.LastUpdated is { } at ? Age(now - at) : null;
        return usage.Status switch
        {
            UsageStatus.Ok => ($"Live · updated {FormatTime(usage.LastUpdated, zone, now)}", Tone.Muted),
            UsageStatus.Loading => ("Loading…", Tone.Muted),
            UsageStatus.TokenExpired when view.IsActive => view.SwitchedHere
                ? ("Token expired, open Claude Code. If it asks you to log in, run /login with this account", Tone.Caution)
                : ("Token expired, open Claude Code", Tone.Caution),
            UsageStatus.NoCredentials => ("No credentials, sign in to Claude Code", Tone.Caution),
            UsageStatus.NotLive or UsageStatus.TokenExpired => (age is null ? "Not live" : $"Last known {age}", Tone.Muted),
            _ when usage.Snapshot is not null && age is not null => ($"{ErrorLabel(usage.Error)} · last known {age}", Tone.Muted),
            _ => (Capitalize(usage.Error ?? "error"), Tone.Muted),
        };
    }

    private static string ErrorLabel(string? error) =>
        error is "network error" or "timeout" ? "Offline" : Capitalize(error ?? "error");

    /// <summary>A reset that passed after the data was fetched is shown as ≈0% with "reset".</summary>
    private static BarModel Bar(string label, UsageWindow? window, DateTimeOffset? fetchedAt, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (window?.ResetsAt is { } resetsAt && resetsAt <= now && (fetchedAt is null || fetchedAt < resetsAt))
            return new BarModel(label, "≈0%", "reset", 0, UsageLevel.Green, Inferred: true);

        var resets = FormatTime(window?.ResetsAt, zone, now);
        if (Round(window?.Utilization) is not int percent)
            return new BarModel(label, Unknown, resets, 0, UsageLevel.Unknown, false);

        var level = percent >= 100 ? UsageLevel.Red : LevelFor(percent);
        return new BarModel(label, $"{percent}%", resets, Math.Clamp(window!.Utilization!.Value / 100, 0, 1), level, false);
    }

    public static SettingsRowModel SettingsRow(AccountView view, TimeZoneInfo zone, DateTimeOffset now)
    {
        var parts = new List<string>();
        if (PlanBadge(view.Plan) is { } plan)
            parts.Add(plan);
        if (view.IsActive)
            parts.Add("active");

        var date = view.LoginExpiresAt is { } at ? TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM", CultureInfo.InvariantCulture) : null;
        parts.Add(view.Login switch
        {
            { Expired: true } => "login expired",
            { Tone: Tone.Caution } when date is not null => $"login expires {date}",
            _ when date is not null => $"login valid until {date}",
            _ => view.Login.Text.ToLowerInvariant(),
        });

        return new SettingsRowModel(view.Key, view.Email, string.Join(" · ", parts.Where(p => p.Length > 0)), view.Login.Tone, view.IsActive);
    }

    public static string? PlanBadge(string? subscriptionType) => subscriptionType switch
    {
        null or "" => null,
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(subscriptionType.Replace("claude_", "").Replace('_', ' ')),
    };

    public static string Age(TimeSpan age) => age switch
    {
        _ when age < TimeSpan.FromMinutes(1) => "just now",
        _ when age < TimeSpan.FromHours(1) => $"{(int)age.TotalMinutes} min ago",
        _ when age < TimeSpan.FromHours(48) => $"{(int)age.TotalHours} h ago",
        _ => $"{(int)age.TotalDays} days ago",
    };
```

- [x] **Step 6: Run, expect PASS** (`LoginExpiryTests`, `AccountRowTests`, existing `DisplayFormatterTests`, `AccountSwitcherTests`).

### Task 2.5: App settings

**Files:**
- Create: `src/ClaudeMeter/Core/AppSettings.cs`
- Test: `tests/ClaudeMeter.Tests/AppSettingsTests.cs`

**Interfaces:**
- Produces: `AppSettings` with `static Load(string path) → AppSettings`, `KeepInactiveLive` (default true), `SetKeepInactiveLive(bool) → bool` (false if the save failed; the value still applies for this session).

- [x] **Step 1: Write the failing tests:** missing file → `KeepInactiveLive` true; `not json` → true; `SetKeepInactiveLive(false)` then `Load` again → false; file content is `{"keepInactiveLive":false}`.

- [x] **Step 2: Run, expect FAIL.**

- [x] **Step 3: Implement**

```csharp
using System.Text.Json;

namespace ClaudeMeter.Core;

/// <summary>Non-secret preferences in <c>settings.json</c>. "Start with Windows" stays in the registry.</summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;

    private AppSettings(string path, bool keepInactiveLive)
    {
        _path = path;
        KeepInactiveLive = keepInactiveLive;
    }

    public bool KeepInactiveLive { get; private set; }

    public static AppSettings Load(string path)
    {
        try
        {
            var file = LiveFiles.ReadOrNull(path) is { } bytes ? JsonSerializer.Deserialize<SettingsFile>(bytes, Options) : null;
            return new AppSettings(path, file?.KeepInactiveLive ?? true);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings(path, keepInactiveLive: true);
        }
    }

    public bool SetKeepInactiveLive(bool value)
    {
        KeepInactiveLive = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFile.Write(_path, JsonSerializer.SerializeToUtf8Bytes(new SettingsFile(value), Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record SettingsFile(bool? KeepInactiveLive);
}
```

- [x] **Step 4: Run, expect PASS.**

### Task 2.6: Accounts runtime, wiring, commit Phase 2

**Files:**
- Create: `src/ClaudeMeter/Core/AccountsRuntime.cs`
- Modify: `src/ClaudeMeter/UI/TrayApplicationContext.cs`
- Test: `tests/ClaudeMeter.Tests/AccountsRuntimeTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces `AccountsRuntime`:
  - `AccountsRuntime(ClaudePaths paths, AccountStore store, AppSettings settings, IUsageClient usage, IProfileClient profiles, IOAuthTokenClient tokens, ClaudeLocks locks, TimeProvider clock)`.
  - `event EventHandler? Changed`, `event EventHandler<Notification>? Notify`.
  - `IReadOnlyList<AccountView> Accounts` (saved accounts in AddedAt order, then the unsaved active login if any).
  - `UsageState ActiveUsage` (for the tray icon), `TimeSpan TimeUntilNextDue`, `bool KeepInactiveLive`.
  - `Task StartAsync(StoreLoadResult loaded)` (seed last known usage, toast on `SetAside`, first poll), `Task PollAsync(bool manual, CancellationToken)`, `Task OnClaudeFilesChangedAsync()`, `Task SwitchAsync(AccountKey)`, `Task RemoveAsync(AccountKey)`, `void SetKeepInactiveLive(bool)`.

- [x] **Step 1: Write the failing tests** (`ClaudeHome`; `FakeUsageClient` with a `Responder` keyed by token; `FakeProfileClient`; `FakeOAuthTokenClient`; A live and stored, T stored):
  - `First_poll_fetches_every_account_with_its_own_token`: `TokensSeen` contains A's live token and T's saved token, once each.
  - `Each_account_keeps_its_own_schedule`: T's token gets `RateLimited(45 min)`, A gets `Success`. Advance 10 min, poll → only A fetched; `TimeUntilNextDue` = 10 min afterwards (A) while T waits until minute 45.
  - `Manual_refresh_respects_each_accounts_floor`: poll, advance 5 min − 1 s, `PollAsync(manual: true)` → nothing sent; advance 1 s → both sent.
  - `Inactive_account_without_refresh_stops_after_expires_at`: settings off, T's token expired → T's view status `NotLive`; T's token never sent; T's last known usage (stored) is shown.
  - `Inactive_401_without_refresh_stops_polling`: settings off, T → `Unauthorized`; next due poll sends nothing for T.
  - `Inactive_401_with_refresh_retries_once_with_the_new_token`: T → `Unauthorized` for `t-access-1`, `Success` for `t-access-2`; token client returns `t-access-2` → T's view `Ok`.
  - `Active_account_is_never_refreshed`: A's live token gets `Unauthorized` → no token-client request; A's view `TokenExpired`.
  - `New_login_is_added_with_a_toast`: store empty, profile Found → `Notify` raised once with `Notifications.Added("a@example.com")`; `Accounts` has A with `IsSaved`.
  - `Unverified_new_login_shows_not_saved_yet`: profile Unavailable → A's view `IsSaved = false`, `CaptureWarning = "Not saved yet: waiting for network to verify"`.
  - `Last_known_usage_is_persisted`: after an Ok poll, a new runtime on the same store shows T's snapshot before its first fetch.
  - `Expiry_toast_once_per_value`: T's `refreshTokenExpiresAt` 1 day ahead → one `LoginExpiring` toast over two polls; `ExpiryWarnedFor` saved.
  - `Switch_toasts_and_moves_the_active_marker`: `SwitchAsync(T)` → `Notifications.Switched("t@example.com")`; T's view `IsActive`; A's view not; order of `Accounts` unchanged.
  - `Failed_switch_toasts_the_reason`: foreign young refresh lock → `Notifications.SwitchFailed(AccountSwitcher.Busy)`.
  - `Remove_deletes_only_the_snapshot`: `RemoveAsync(T)` → T gone from store and views; Claude Code files unchanged.
  - `Invalid_client_toasts_once`: T's refresh returns `InvalidClient` twice over two polls → one `RefreshStopped` toast.

- [x] **Step 2: Run, expect FAIL.**

- [x] **Step 3: Implement**

```csharp
namespace ClaudeMeter.Core;

/// <summary>
/// Everything account-related the tray shows and does. Runs on the UI thread. Multi-step writes
/// (auto-save, switch, token refresh) go through one <see cref="OperationGate"/>; usage fetches do not.
/// </summary>
internal sealed class AccountsRuntime
{
    /// <summary>Monitor key while Claude Code has no identity (logged out): its usage shows "No credentials".</summary>
    private static readonly AccountKey NoAccount = new("", "");

    private readonly ClaudePaths _paths;
    private readonly AccountStore _store;
    private readonly AppSettings _settings;
    private readonly IUsageClient _usageClient;
    private readonly TimeProvider _clock;
    private readonly OperationGate _gate = new();
    private readonly AutoSaver _autoSaver;
    private readonly AccountSwitcher _switcher;
    private readonly TokenRefresher _refresher;
    private readonly Dictionary<AccountKey, UsageService> _usage = [];

    private OAuthAccount? _active;
    private CredentialsInfo? _activeCredentials;
    private string? _captureWarning;
    private AccountKey? _switching;
    private SwitchStage? _switchStage;
    private AccountKey? _switchedTo;

    public AccountsRuntime(ClaudePaths paths, AccountStore store, AppSettings settings, IUsageClient usage, IProfileClient profiles, IOAuthTokenClient tokens, ClaudeLocks locks, TimeProvider clock)
    {
        _paths = paths;
        _store = store;
        _settings = settings;
        _usageClient = usage;
        _clock = clock;
        _refresher = new TokenRefresher(store, tokens, () => ClaudeHolder.Read(paths), () => settings.KeepInactiveLive, clock);
        _refresher.Disabled += (_, _) => Notify?.Invoke(this, Notifications.RefreshStopped);
        _autoSaver = new AutoSaver(paths, locks, store, profiles, clock);
        _switcher = new AccountSwitcher(paths, locks, store, profiles, _refresher, clock);
    }

    public event EventHandler? Changed;

    public event EventHandler<Notification>? Notify;

    public bool KeepInactiveLive => _settings.KeepInactiveLive;

    private AccountKey ActiveKey => _active?.Key ?? NoAccount;

    public UsageState ActiveUsage => Usage(ActiveKey).State;

    public TimeSpan TimeUntilNextDue
    {
        get
        {
            var next = PollableKeys().Select(k => Usage(k).TimeUntilNextPoll).DefaultIfEmpty(RefreshPolicy.PollInterval).Min();
            return next < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : next;
        }
    }

    public IReadOnlyList<AccountView> Accounts
    {
        get
        {
            var now = _clock.GetUtcNow();
            var views = _store.Accounts.Select(a => View(a, now)).ToList();
            if (_active is { } active && _store.Find(active.Key) is null)
            {
                views.Add(new AccountView(
                    active.Key, active.Email, _activeCredentials?.SubscriptionType, IsActive: true, IsSaved: false,
                    Usage(active.Key).State,
                    LoginExpiry.Describe(_activeCredentials?.RefreshTokenExpiresAt, LoginState.Valid, null, isActive: true, now),
                    Switching: null, SwitchedHere: false, _captureWarning, _activeCredentials?.RefreshTokenExpiresAt));
            }

            return views;
        }
    }

    public async Task StartAsync(StoreLoadResult loaded)
    {
        if (loaded == StoreLoadResult.SetAside)
            Notify?.Invoke(this, Notifications.StoreSetAside);
        foreach (var account in _store.Accounts)
        {
            if (account.LastKnownUsage is { } snapshot && account.LastKnownAt is { } at)
                Usage(account.Key).Seed(snapshot, at);
        }

        await PollAsync(manual: false, CancellationToken.None);
    }

    public async Task PollAsync(bool manual, CancellationToken cancellationToken)
    {
        ReadActive();
        if (manual || Usage(ActiveKey).TimeUntilNextPoll == TimeSpan.Zero)
            await AutoSaveAsync(cancellationToken);

        foreach (var key in PollableKeys())
        {
            var service = Usage(key);
            if (!manual && service.TimeUntilNextPoll > TimeSpan.Zero)
                continue;

            var before = service.State;
            var after = await service.RefreshAsync(manual, cancellationToken);
            if (after.Status == UsageStatus.Ok && !ReferenceEquals(after.Snapshot, before.Snapshot) && _store.Find(key) is { } saved)
                _store.Upsert(saved with { LastKnownUsage = after.Snapshot, LastKnownAt = after.LastUpdated });
        }

        WarnExpiringLogins();
        if (_store.HasUnsavedChanges)
            _store.TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task OnClaudeFilesChangedAsync()
    {
        ReadActive();
        await AutoSaveAsync(CancellationToken.None);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SwitchAsync(AccountKey key)
    {
        if (_switching is not null)
            return;

        _switching = key;
        _switchStage = SwitchStage.WaitingForLock;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            SwitchResult result;
            using (await _gate.EnterAsync(CancellationToken.None))
                result = await _switcher.SwitchAsync(key, new InlineProgress(stage => { _switchStage = stage; Changed?.Invoke(this, EventArgs.Empty); }), CancellationToken.None);

            switch (result)
            {
                case SwitchResult.Switched switched:
                    _switchedTo = key;
                    Notify?.Invoke(this, Notifications.Switched(switched.Email));
                    break;
                case SwitchResult.Unconfirmed unconfirmed:
                    _autoSaver.RequireProfile = true;
                    Notify?.Invoke(this, Notifications.SwitchUnconfirmed(unconfirmed.Email));
                    break;
                case SwitchResult.Failed failed:
                    Notify?.Invoke(this, Notifications.SwitchFailed(failed.Message));
                    break;
            }
        }
        finally
        {
            _switching = null;
            _switchStage = null;
        }

        await PollAsync(manual: true, CancellationToken.None);
    }

    public async Task RemoveAsync(AccountKey key)
    {
        using (await _gate.EnterAsync(CancellationToken.None))
        {
            if (_store.Remove(key))
                _store.TrySave();
        }

        if (key != ActiveKey)
            _usage.Remove(key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetKeepInactiveLive(bool on)
    {
        _settings.SetKeepInactiveLive(on);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task AutoSaveAsync(CancellationToken cancellationToken)
    {
        AutoSaveResult result;
        using (await _gate.EnterAsync(cancellationToken))
            result = await _autoSaver.RunAsync(cancellationToken);

        _captureWarning = result switch
        {
            AutoSaveResult.Deferred => "Not saved yet: waiting for network to verify",
            AutoSaveResult.Rejected => "Login files are changing; will retry",
            AutoSaveResult.Busy => _captureWarning,
            _ => null,
        };
        if (result is AutoSaveResult.Inserted inserted)
            Notify?.Invoke(this, Notifications.Added(inserted.Email));
        ReadActive();
    }

    /// <summary>Who Claude Code says is signed in; keeps the last answer if the files are mid-write.</summary>
    private void ReadActive()
    {
        try
        {
            var files = LiveFiles.Read(_paths);
            _active = files.GlobalConfig is { } config ? OAuthAccount.Parse(config) : null;
            _activeCredentials = files.Credentials is { } credentials ? CredentialsInfo.Parse(credentials) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
        }

        if (_switchedTo is { } switched && switched != ActiveKey)
            _switchedTo = null;
    }

    private IEnumerable<AccountKey> PollableKeys()
    {
        var now = _clock.GetUtcNow();
        yield return ActiveKey;
        foreach (var account in _store.Accounts)
        {
            if (account.Key != ActiveKey && !LoginExpiry.IsExpired(account, now))
                yield return account.Key;
        }
    }

    private UsageService Usage(AccountKey key)
    {
        if (!_usage.TryGetValue(key, out var service))
        {
            var source = new AccountTokenSource(key, () => ActiveKey, () => LiveFiles.ReadAccessToken(_paths), _store, _refresher, _gate, _clock);
            _usage[key] = service = new UsageService(_usageClient, source, new RefreshPolicy(_clock));
        }

        return service;
    }

    private AccountView View(StoredAccount account, DateTimeOffset now)
    {
        var isActive = account.Key == ActiveKey;
        var loginExpiresAt = isActive ? _activeCredentials?.RefreshTokenExpiresAt : account.Credentials?.RefreshTokenExpiresAt;
        return new AccountView(
            account.Key, account.Email, account.Plan, isActive, IsSaved: true, Usage(account.Key).State,
            LoginExpiry.Describe(loginExpiresAt, isActive ? LoginState.Valid : account.LoginState, account.LastActiveAt, isActive, now),
            _switching == account.Key ? _switchStage : null,
            SwitchedHere: _switchedTo == account.Key,
            isActive ? _captureWarning : null,
            loginExpiresAt);
    }

    private void WarnExpiringLogins()
    {
        var now = _clock.GetUtcNow();
        foreach (var account in _store.Accounts.ToList())
        {
            if (LoginExpiry.WarningDue(account, now) is not { } inDays)
                continue;
            _store.Upsert(account with { ExpiryWarnedFor = account.Credentials!.RefreshTokenExpiresAt!.Value.ToUnixTimeMilliseconds() });
            Notify?.Invoke(this, Notifications.LoginExpiring(account.Email, inDays));
        }
    }

    /// <summary>Reports synchronously on the caller's thread (Progress&lt;T&gt; would post asynchronously).</summary>
    private sealed class InlineProgress(Action<SwitchStage> report) : IProgress<SwitchStage>
    {
        public void Report(SwitchStage value) => report(value);
    }
}
```

- [x] **Step 4: Wire into the tray.** `TrayApplicationContext` replaces `_service`, `_autoSaver`, `_autoSaving` and the Phase 1 `AutoSaveAsync` with one `_runtime`:
  - Construction: `var loaded = _store.Load(); _runtime = new AccountsRuntime(_paths, _store, AppSettings.Load(_paths.Settings), _client, _profiles, _tokens = new OAuthTokenClient(), ClaudeLocks.ForSystem(_paths), TimeProvider.System);` with `_runtime.Changed += (_, _) => UpdateTray();` and `_runtime.Notify += (_, n) => _notifyIcon.ShowBalloonTip(5000, n.Title, n.Body, ToolTipIcon.None);`. Keep `loaded` in a field for the first tick.
  - First tick: `await _runtime.StartAsync(_loaded)`; later ticks `await _runtime.PollAsync(manual: false, CancellationToken.None)`. `OnRefreshClick`: `PollAsync(manual: true, ...)`. Watcher: `await _runtime.OnClaudeFilesChangedAsync()`.
  - `ScheduleNextPoll` uses `_runtime.TimeUntilNextDue`; `UpdateTray` and `CurrentPanelModel` use `_runtime.ActiveUsage`; `OnMenuOpening` uses `_runtime.ActiveUsage` for `MenuLines`.
  - Dispose `_tokens`.

- [x] **Step 5: Verify.** `dotnet build`; `dotnet test` (all green). Run the app: the icon and the old panel show the active account as before; after `/login` with a second account in Claude Code, a toast "Added <email>" appears; `accounts.dat` grows.

- [x] **Step 6: Commit Phase 2**

```powershell
git add docs/MULTI-ACCOUNT-PLAN.md src tests
git commit -m "Track usage for every saved account and keep inactive logins live" -m "One UsageService per account with its own schedule and backoff. Inactive accounts use their saved token, refreshed in the background when allowed; the active account stays owned by Claude Code. Adds login-expiry tracking, reset inference, toasts and the accounts runtime behind the existing panel."
```

---

# Phase 3: UI

No automated UI tests (spec). Each task ends with a manual check on this machine in dark and light theme, at 100% and 150% scale.

### Task 3.1: Acrylic prototype (decision gate)

**Files:**
- Create: `src/ClaudeMeter/UI/FlyoutWindow.cs` (kept as the panel's base class if the gate passes)
- Modify: `src/ClaudeMeter/UI/NativeMethods.cs`

- [x] **Step 1: Interop.** Add to `NativeMethods`:

```csharp
public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
public const int DWMSBT_NONE = 1;
public const int DWMSBT_TRANSIENTWINDOW = 3;
public const int WM_NCACTIVATE = 0x0086;

[StructLayout(LayoutKind.Sequential)]
public struct MARGINS
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;
}

[DllImport("dwmapi.dll")]
public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
```

- [x] **Step 2: `FlyoutWindow`**: borderless, `ShowWithoutActivation`, `WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (moved from `UsagePanel`), `DoubleBuffered`, `AutoScaleMode.None`. `ApplyBackdrop(bool dark, bool acrylic, Color solid, Color border)` sets dark mode, round corners, border color, and either `DWMSBT_TRANSIENTWINDOW` + `DwmExtendFrameIntoClientArea(-1,-1,-1,-1)` (`BackColor = Color.Black`, paint background with `Graphics.Clear(Color.Transparent)`) or `DWMSBT_NONE` + solid `BackColor`. `WndProc` override: on `WM_NCACTIVATE` force `m.WParam = 1` when acrylic is on, so DWM keeps drawing the active backdrop. A protected `bool Acrylic` tells subclasses which text path to use.

- [x] **Step 3: Throwaway test page.** Temporarily make `ShowAt` of the current panel use `FlyoutWindow` with a hard-coded 360×300 page: title, a line of 12 px secondary text, a 4 px bar, drawn with GDI+ (`TextRenderingHint.AntiAliasGridFit`, `SmoothingMode.AntiAlias`).

- [x] **Step 4: Gate checks** (hover the tray icon while another app has focus):
  1. The backdrop is blurred/tinted, not the flat fallback color, while the panel is inactive. Try without the `WM_NCACTIVATE` override first, then with it.
  2. Text is crisp and fully opaque in dark and light theme; no black fringes.
  3. No flicker when the page repaints on hover changes.
  4. With Settings › Personalization › Colors › Transparency effects off, the panel is solid.
  5. Corners rounded, 1 px border, shadow present.
  6. `Segoe UI Variable Text` and `Segoe UI Variable Text Semibold` resolve (else fall back to `Segoe UI` / `Segoe UI Semibold`); `Segoe Fluent Icons` glyphs E72C (Refresh), E713 (Settings), E718 (Pin), E840 (Pinned), E72B (Back), E7BA (Warning), E946 (Info), E8A7 (Open in new) render.
  7. Accent palette: with the default blue accent, `AccentPalette` index 1 reads `#60CDFF` and index 4 reads `#005FB8`. If not, find the indices that do and use those.
- [x] **Step 5: Decide.** All pass → keep `FlyoutWindow` with acrylic and GDI+ text. Check 1 fails with and without the override → **stop and ask the user** (solid Fluent fallback recommended). Record the outcome under Spike findings in the spec. Remove the throwaway page.

### Task 3.2: Fluent theme tokens

**Files:**
- Create: `src/ClaudeMeter/UI/FluentTheme.cs`
- Modify: `src/ClaudeMeter/UI/SystemTheme.cs`

**Interfaces:**
- Produces: `record FluentTheme(bool Dark, bool Transparency, Color Text, Color Text2, Color Text3, Color RowFill, Color RowActive, Color Badge, Color Track, Color ButtonFill, Color ButtonBorder, Color SubtleHover, Color Ok, Color Caution, Color Critical, Color CriticalTint, Color Accent, Color OnAccent, Color ToggleOff, Color PanelSolid, Color PanelBorder)`; `SystemTheme.Current → FluentTheme` (replaces `ThemeColors`); `FluentTheme.ForLevel(UsageLevel) → Color` (bar fills: Green → Ok, Yellow → Caution, Red → Critical, else Track); `FluentTheme.For(Tone) → Color`.

- [x] **Step 1: Implement** with the spec table and the mockup values:

| Token | Dark | Light |
|---|---|---|
| Text / Text2 / Text3 | `#FFFFFF` / white 79% / white 58% | `#1A1A1A` / black 64% / black 58% |
| RowFill / RowActive | white 3.5% / white 8.5% | white 50% / white 92% |
| Badge / Track | white 9% / white 14% | black 6% / black 10% |
| ButtonFill / ButtonBorder / SubtleHover | white 7% / white 10% / white 6% | white 80% / black 10% / black 4% |
| Ok / Caution / Critical | `#6CCB5F` / `#FCE100` / `#FF99A4` | `#0F7B0F` / `#9D5D00` / `#C42B1C` |
| CriticalTint | `#FF99A4` 10% | `#C42B1C` 7% |
| Accent (default) / OnAccent | `#60CDFF` / `#000000` | `#005FB8` / `#FFFFFF` |
| ToggleOff | white 62% | black 60% |
| PanelSolid / PanelBorder | `#262628` / white 9% | `#F3F3F3` / black 8% |

  `Dark` from `SystemUsesLightTheme` (as today); `Transparency` from `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency` (missing = on); `Accent` from `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent\AccentPalette` (32 bytes, RGBA per entry, indices from Task 3.1 check 7), default when missing.

- [x] **Step 2: Build.** `UsagePanel` still compiles against `SystemTheme.Current` by mapping its old fields (`Background = PanelSolid`, `Border = PanelBorder`, `Separator = PanelBorder`, `Text`, `SecondaryText = Text2`, `Track`, `Link = Accent`) until Task 3.5 deletes it.

### Task 3.3: MeterPanel main view

**Files:**
- Create: `src/ClaudeMeter/UI/MeterPanel.cs`

**Interfaces:**
- Consumes: `FlyoutWindow`, `FluentTheme`, `AccountRowModel`, `SettingsRowModel`, `PanelPlacement`.
- Produces:
  - `record MeterPanelModel(IReadOnlyList<AccountRowModel> Rows, IReadOnlyList<SettingsRowModel> SettingsRows, bool Pinned, bool KeepInactiveLive, bool StartWithWindows, string Version)`.
  - `abstract record PanelAction` with `Refresh`, `OpenSettings`, `TogglePin`, `Switch(AccountKey)`, `Remove(AccountKey)`, `ToggleKeepLive`, `ToggleStartWithWindows`, `OpenUsagePage`, `Exit`.
  - `MeterPanel : FlyoutWindow` with `ShowAt(Rectangle icon)`, `SetModel(MeterPanelModel)`, `ApplyTheme(FluentTheme)`, `bool InSettings`, `event EventHandler<PanelAction>? ActionRequested`.

- [x] **Step 1: Structure.** One `LayOut(Graphics? g)` pass both measures (g null) and paints, as `UsagePanel` does today, so layout and drawing cannot drift. The pass rebuilds `List<(Rectangle Bounds, Hit Hit)> _hits`, where `Hit` is a record of kind + optional `AccountKey`. `OnMouseMove`: hit-test; if the hovered hit changed, `Invalidate()`. `OnMouseLeave`: clear hover. `OnMouseDown`/`OnMouseUp`: pressed state; on up over the same hit, raise `ActionRequested` (or handle view-local hits: Settings, Back, ask/cancel remove). No timers, no animation. Paint only on `SetModel`, theme/DPI change, or hover/pressed change.
- [x] **Step 2: Caches.** `PanelFonts(float scale)` (Title/Email 14 px semibold, Body 14 px, Small 12 px, SmallSemibold 12 px, Badge 11 px, Icons 16 px and 12 px `Segoe Fluent Icons`) rebuilt on DPI change; brushes and pens per theme in one `PanelBrushes(FluentTheme)` disposed on theme change; text measurements in `Dictionary<(string, FontKind), Size>` cleared on either change. Text through GDI+ `DrawString` with `StringFormat` (`NoWrap`, `EllipsisCharacter`) when acrylic, `TextRenderer` when solid (Task 3.1 decides).
- [x] **Step 3: Main view layout (logical px, from the mockup).** Width 360. Header 48 high, padding 8/8/4/16: "Claude Meter" 14 semibold; three 32×32 icon buttons (gap 2): Refresh, Settings, Pin (pinned: accent glyph E840 on ButtonFill; unpinned: E718 in Text2). List padding 4/8/8, rows 4 apart. Row: radius 6, padding 8/12/10/16, RowFill (active: RowActive + 3×16 accent pill at left 0, top 18, radius 2). Line 1 (min height 32): email 14 semibold with ellipsis + plan badge (11 px, padding 0 6, radius 4, Badge fill, Text2); right side "Active" (12 semibold, Accent) / Switch button (32 high, padding 0 14, 14 px, radius 4, ButtonFill + ButtonBorder) / disabled "Switching…" button / "Login expired" (12 semibold, Critical). Gap 6. Grid rows "5h" and "7d": label column 20 (Text2), bar 1fr (4 px, radius 2, Track; fill `ForLevel`), percent column 36 right-aligned semibold (Text3 when `Inferred`), reset column 64 right-aligned Text3; column gap 8, row gap 6. Gap 6. Status line: left status (`For(StatusTone)`, Text3 for Muted, ellipsis), right login text (Caution: semibold + E7BA 12 px glyph).
- [x] **Step 4: Overflow.** Height = content height, clamped to work area height − 2 × gap. If clamped, the account list scrolls with `OnMouseWheel` (Windows routes wheel input to the window under the cursor by default) and clips to its area.
- [x] **Step 5: Manual check:** with 1, 3 and 8 accounts (8 via a temporary fake model), both themes, 100%/150%: matches the `Main` and `States` artboards; hover fills appear only on buttons; no flicker.

### Task 3.4: Settings view

**Files:** Modify `src/ClaudeMeter/UI/MeterPanel.cs`

- [x] **Step 1: Layout (from the `Settings` artboard).** Header: Back button (E72B) + "Settings". "Accounts" label (12 semibold, Text2, padding 8/8/4). Rows (padding 8/8/8/12, radius 6, RowFill): email 14 semibold + sub line 12 px (`For(SubTone)`, Text3 for Muted), Remove button. Confirming row: CriticalTint fill, text "Remove **<email>** from Claude Meter? Claude Code is not changed." plus, for the active row, " It's the active account, so Claude Meter saves it again at the next check."; buttons Cancel and Remove (Critical text). Only one row confirms at a time; switching views cancels it. Hint with E946: "To add an account, run /login in Claude Code with it. Claude Meter saves it automatically. Don't run /logout first: it can revoke the saved login." Separator. Toggles (40×20 track, 12 px knob; on: Accent fill, OnAccent knob; off: ToggleOff stroke and knob): "Keep inactive accounts live" with sub "Refreshes their tokens in the background", and "Start with Windows". Link row "Open claude.ai usage page" with E8A7. Separator. Footer: "Version {AppInfo.Version}" (Text3) and Exit button.
- [x] **Step 2: Manual check** against the artboard, both themes; the inline confirmation works without any dialog.

### Task 3.5: Tray behavior, notifications, cleanup, commit Phase 3

**Files:**
- Modify: `src/ClaudeMeter/UI/TrayApplicationContext.cs`, `src/ClaudeMeter/Core/DisplayFormatter.cs`, `tests/ClaudeMeter.Tests/DisplayFormatterTests.cs`
- Delete: `src/ClaudeMeter/UI/UsagePanel.cs`

- [x] **Step 1: Remove the context menu**: `_menu`, `_startWithWindowsItem`, `_infoItems`, `ShowContextMenuMethod`, `OnMenuOpening`, the `ContextMenuStrip` assignment and `using System.Reflection`. `NotifyIcon.Text` stays unset.
- [x] **Step 2: Pin.** Field `bool _pinned`. `MouseUp` (left or right): if pinned → unpin and hide; else pin and `ShowAt(icon)`. `OnHoverTick` returns immediately while pinned. `ActionRequested`: `TogglePin` unpins and hides; `OpenSettings` from a hover panel pins it; `Refresh` → `PollAsync(manual: true)`; `Switch(key)` → `_runtime.SwitchAsync(key)`; `Remove(key)` → `RemoveAsync`; `ToggleKeepLive` → `SetKeepInactiveLive(!current)`; `ToggleStartWithWindows` → `StartupRegistration.SetEnabled(!IsEnabled)`; `OpenUsagePage` → existing handler; `Exit` → `ExitThread()`. After each action, `SetModel(CurrentModel())`.
- [x] **Step 3: Model.** `CurrentModel()` maps `_runtime.Accounts` through `DisplayFormatter.AccountRow` and `SettingsRow` with `TimeZoneInfo.Local` and `DateTimeOffset.Now`; `Pinned = _pinned`; `KeepInactiveLive = _runtime.KeepInactiveLive`; `StartWithWindows = StartupRegistration.IsEnabled`; `Version = AppInfo.Version`. `_runtime.Changed` → icon update + `SetModel` if visible.
- [x] **Step 4: Theme.** `SystemEvents.UserPreferenceChanged` (categories `General`, `Color`, `VisualStyle`) → `_panel.ApplyTheme(SystemTheme.Current)`; unsubscribe in `Dispose`.
- [x] **Step 5: Cleanup.** Delete `UsagePanel.cs`; remove `DisplayFormatter.Panel`, `PanelRow`, `PanelModel`, `MenuLines` and the old `ThemeColors` mapping; delete tests `Panel_rows_for_ok_state`, `Panel_rows_show_dash_and_empty_bar_when_unknown`, `Panel_bar_is_clamped_to_full`, `Panel_status_line`, `Menu_lines_for_ok_state`, `Menu_lines_for_token_expired_with_old_data` (their cases are covered by `AccountRowTests`; add `Bar_is_clamped_to_full` there for the 130% case).
- [ ] **Step 6: Manual end-to-end check** (see Verification) then `dotnet test`.
- [x] **Step 7: Commit Phase 3**

```powershell
git add docs/MULTI-ACCOUNT-PLAN.md docs/MULTI-ACCOUNT-SPEC.md src tests
git commit -m "Replace the tray menu with a Fluent multi-account panel" -m "One owner-drawn flyout lists every saved account with live usage, login validity and a Switch button. Clicking the tray icon pins the panel as a widget; settings, removal and toggles live in the same window. The context menu and tooltip are gone; notifications use balloon tips."
```

---

# Phase 4: README and SPEC

### Task 4.1: Documentation, commit Phase 4

**Files:** Modify `README.md`, `docs/SPEC.md`, `docs/MULTI-ACCOUNT-SPEC.md`

- [x] **Step 1: README "Multiple accounts" section:** add an account by running `/login` in Claude Code with it (saved automatically); switch from the panel; reopen Claude Code after switching; never `/logout` an account you want to keep (it can revoke the saved login); run `/login` again when a login expires.
- [x] **Step 2: README key decisions:** why the app stores DPAPI-encrypted credential snapshots (and only in `accounts.dat`); why it refreshes tokens of inactive accounts only, its risks (unofficial endpoint, Claude Code's client id, a lost refresh response spends the token) and the "Keep inactive accounts live" switch; why it takes Claude Code's lock directories (credit claude-swap for documenting the protocol); why captures are verified against the profile endpoint; limitation: every login expires at `refreshTokenExpiresAt` and needs `/login` again. Update the existing "never refreshes the OAuth token" decision accordingly; update the features list (no context menu; pin; switch).
- [x] **Step 3: `docs/SPEC.md` Security:** replace "Never ... persist, copy ... the access token" and "Do NOT implement any OAuth refresh flow" with a pointer to `MULTI-ACCOUNT-SPEC.md` Security, so the two specs no longer contradict.
- [x] **Step 4: `docs/MULTI-ACCOUNT-SPEC.md`:** close the Open items with their results (profile schema, `refreshTokenExpiresAt` observation, acrylic outcome, cached `oauthAccount` write-back observation if seen).
- [x] **Step 5: Commit Phase 4**

```powershell
git add README.md docs
git commit -m "Document multiple accounts and the new security model"
```

---

## Verification (end to end, after Phase 3)

1. `dotnet test` — all green. `dotnet publish src/ClaudeMeter -c Release -o publish` succeeds.
2. Start the app with one Claude Code login: toast "Added <email>"; the panel lists it as Active with live bars.
3. In Claude Code run `/login` with a second account: within ~2 s a second "Added" toast; both rows show live usage; the first row shows Switch.
4. Click Switch on the first row: row shows "Switching…" briefly, then toast "Switched to …"; only the Active marker moves; `git diff --no-index` of a copy of `%USERPROFILE%\.claude.json` taken before vs. after shows only the `oauthAccount` block changed. Reopen Claude Code: `/status` shows the switched account.
5. Pin by clicking the icon; focus another app; the panel stays on top and never takes focus; hover does nothing; click the icon again to close.
6. Settings: toggle "Keep inactive accounts live" off/on (persists across restart in `settings.json`); Remove with inline confirmation on an inactive row (Claude Code files untouched); Exit closes the app.
7. Security spot checks: `Select-String` for `@` and for the start of your refresh token in `accounts.dat` and `settings.json` finds nothing; no `*.tmp` files remain in `%USERPROFILE%`, `.claude` or `%LOCALAPPDATA%\ClaudeMeter`; no `.oauth_refresh.lock`, `.storage-write.lock` or `.claude.json.lock` directories remain after a switch.
8. Turn Transparency effects off: the panel turns solid. Switch Windows to light theme: the panel follows without restart.

## Execution

Recommended: **Native** (I implement each task in this session, one commit per phase, then one fresh reviewer checks the whole branch). The tasks share many exact interfaces (`LiveLogin`, `StoredAccount`, `ClaudeLocks`) and the spec asks for one commit per phase, so per-task subagents would mostly re-read the same context. The alternative is Subagent-driven (fresh implementer + reviewer per task), more thorough and more expensive.
