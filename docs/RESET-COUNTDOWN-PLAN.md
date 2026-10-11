# Reset countdown Implementation Plan

**Goal:** Show the time left until each limit resets (5h and 7d) next to its meter, and let a click switch every meter to the reset date.

**Why:** The panel showed only the reset clock time ("14:30", "Wed 07:00"). The user could not see at a glance how much time was left.

**Spec:** the panel section of `docs/MULTI-ACCOUNT-SPEC.md`, updated in step 4.

---

## Progress

**At the start of every session: read this section, then the first unchecked step.** After each finished step: tick its checkbox (`- [x]`), update this table and commit this file with the step.

| Step | Status | Commit |
|---|---|---|
| 0. Branch and plan in repo | done | e6f9571 |
| 1. Core formatting | done | 9865cd6 |
| 2. Setting | done | 7abd2b0 |
| 3. UI | done (manual tray check pending) | (this commit) |
| 4. Docs | pending | |
| 5. Review and merge | pending | |

**Next step:** Step 4, docs.

**Decisions and findings during implementation** (append, newest last, one line each with date):

- 2026-10-11: Plan approved after a grilling session; the agreed decisions are below.
- 2026-10-11: Work happens on branch `reset-countdown`; merge after the final review.
- 2026-10-11: Step 1: complete, full suite 262/262. The panel shows the countdown from here on; the click to switch comes in step 3.
- 2026-10-11: Step 2: complete, full suite 265/265.
- 2026-10-11: Step 3: Ruling: the countdown timer aims at the next minute step of the nearest reset (`DisplayFormatter.UntilCountdownChanges`, at most a minute) instead of the wall-clock minute, because `resets_at` carries seconds (e.g. `17:20:00.089`) and a wall-clock tick would show a stale minute — cost if wrong: none, at most one extra repaint a minute.
- 2026-10-11: Step 3: Ruling: the column samples come from `DisplayFormatter.ResetColumnSamples`, so the formats live in one place — cost if wrong: none.
- 2026-10-11: Step 3: complete, full suite 268/268. Verified by screenshots of the real `MeterPanel` in a scratch harness (dark and light, both views, hover on active and inactive rows, en-ID and de-DE) and simulated clicks (meters toggle, status line does nothing, Switch still switches). Not verified: 125% DPI and a run of the real app, because the user's own instance holds the single-instance mutex.

---

## Agreed decisions

- Default view: countdown. A click switches to the reset date; another click switches back.
- One click switches all 5h and 7d meters in all accounts.
- The choice is saved in `settings.json` and survives restarts.
- Countdown: the 2 largest units, floored. An inner zero stays: "6d 23h", "2d 0h", "4h 12m", "5h 0m", "12m", "<1m". "reset" (inferred) and "—" (unknown) stay as before in both views.
- Date view: "14:30" if the reset is today, else "14 Oct 07:00". Only the month name follows the Windows region format ("Okt" on id-ID, "Okt." on de-DE). Day-month order and 24-hour time stay fixed. Other times in the panel (the status line) do not change.
- Click target: the whole meter block of an account row (both meter lines). Hover shows a subtle fill there, like the other panel controls. No tooltip, no hand cursor.
- The time column has one fixed width for both views, so the meter does not jump on a click.

## Technical decisions

- While the panel is visible, the tray rebuilds the panel model at each minute boundary, so the countdown ticks. The timer stops when the panel hides. Side effect: an inferred reset ("≈0% reset") appears within a minute of the reset instead of at the next poll.
- Column width = the widest of the countdown samples ("6d 23h", "23h 59m") and "28 <month> 23:59" for all 12 months in the current culture. It is computed once per DPI (Segoe UI digits are tabular). A region-format change while the app runs applies after a restart.
- The hover fill is blended onto the row's own fill (`RowFill` / `RowActive`), not onto the panel background. `SubtleHover` is pre-blended on the panel background and would look darker than an active row in dark mode.

## Steps

- [x] **0. Branch and plan in repo.** `git switch -c reset-countdown`; add this file; commit.

- [x] **1. Core formatting, test first** — `src/ClaudeMeter/Core/DisplayFormatter.cs`
  - New `Countdown(TimeSpan left)` with the rules above.
  - New `ResetDate(DateTimeOffset? at, TimeZoneInfo zone, DateTimeOffset now, CultureInfo culture)`: `"HH:mm"` today; else `"d MMM"` in `culture` plus `" HH:mm"` invariant; "—" if null.
  - `BarModel`: replace `Resets` with `Countdown` and `ResetDate`. Inferred gives "reset" in both, unknown gives "—" in both.
  - `AccountRow(view, zone, now)` gains `CultureInfo culture`. `FormatTime` stays for the status line.
  - Tests: `Countdown` theory (0 s, 59 s, 60 s, 59 m 59 s, 1 h, 4 h 12 m 40 s, 23 h 59 m, 24 h, 6 d 23 h 59 m); `ResetDate` for today, another day in InvariantCulture and id-ID, and null. Update the `BarModel` expectations in `AccountRowTests.cs`.

- [x] **2. Setting** — `src/ClaudeMeter/Core/AppSettings.cs`
  - Add `ShowResetDate` (default false = countdown) and `SetShowResetDate(bool)`. One private `Save()` writes both values.
  - Tests: default is countdown; save and reload; saving one setting keeps the other.

- [x] **3. UI**
  - `MeterPanel.cs`: `MeterPanelModel.ShowResetDate`, `PanelAction.ToggleResetDisplay`, `HitKind.ResetDisplay`; a hit area with hover / pressed fill over both meter lines of a row; `BarRow` writes the countdown or the date in the fixed-width column (cached, cleared on DPI change).
  - `FluentTheme.cs`: blend the hover / pressed tint onto a given color.
  - `TrayApplicationContext.cs`: keep `AppSettings` in a field; pass `CultureInfo.CurrentCulture` and `ShowResetDate` to the model; handle `ToggleResetDisplay`; a minute timer while the panel is visible.

- [ ] **4. Docs.** `README.md` features line; the panel row description in `docs/MULTI-ACCOUNT-SPEC.md`.

- [ ] **5. Review and merge.** Full test suite, code review, fix findings, merge to master. Ask the user before any release or version bump.

## Verification

- `dotnet test` passes; `dotnet build -c Release` adds no warnings.
- Screenshots of the real `MeterPanel` in a scratch harness: both views, dark and light, hover on active and inactive rows, 125% DPI. The meter has the same length in both views.
- Smoke run: a click switches every row; the view stays after the panel closes and after an app restart; `settings.json` has both keys; a pinned panel's countdown changes at the minute boundary.
