# A350 Display Windows at Fenix Parity Implementation Plan (plan 26)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each A350 display key opens a live window of that display with the cockpit controls that change it under the list, as the Fenix's display windows and the PMDG 737's do: Alt+P the PFD, Alt+N the ND with every function of it in one place, Alt+E the E/WD, Alt+S the SD, Alt+I the ISIS, Alt+Shift+N the SND (owner, 2026-11-13).

**Architecture:** One base window, `A350DisplayWindow`, is the A350's `FenixChannelDisplayForm`: one live list named "<display> display", read from the background monitor's latest draw every second and reconciled in place (the row the pilot is on followed by its leading words), one status row while the display is dark or unread, silent, F5 and Escape, the controls after the list. Its rows come from a pure `A350DisplayRows`; its controls are the panels' own rows (`IniBuildsA350Definition.PressRow`, `TryDescribeControlState`), as the A350's FCU windows already are, through a small `IA350WindowRows` seam the tests fake. Each display is a class of its own (`ShowTrackedWindow` keeps one window per type). The ND keeps its own window (`A350NdWindow`: the ND, the airport map, BTV, Enter clicks, map drag), which moves to Alt+N and gains the EFIS buttons.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit; the A350 capture fixtures in `tests/MSFSBlindAssist.Tests/A350/Fixtures`.

**Spec:** the owner's words of 2026-11-13 (below), and the Fenix's display windows as built: `Forms/Fenix/FenixChannelDisplayForm.cs` (the list, `RowsFor`, `RowToFollow`, `StartLive`, `RefreshNow`, `AddControlAfterList`, `BuildKeyRow`, `HandleWindowKey`), `FenixChannelNdForm.cs` (state buttons and combos that follow the display, `FollowsTheNd`, `PickSettleMs`), `FenixChannelSdForm.cs`, `FenixChannelEwdForm.cs`, `FenixChannelIsisForm.cs`, `FenixWindowRegistry.Channel*.cs` and `FenixA320Definition.Channel*.cs` (the keys and the windows list); the PMDG 737's `PMDG737PfdForm`, `PMDG737NavDisplayForm` and `PMDG737EngineDisplaysForm` (the PFD's own controls under its list).

- "the displays there are very much different, both when it comes to how the windows appear, and, for example, the ND in the Fenix is an actual window with all the buttons and all that ... Could you replicate that for the A350? And change all the hotkeys as well, for example for feature parity?"
- "remove control shift b, cause the whole point is to have the nd plus all its functions in one place, at least in the a350. and for the snd do maybe alt shift n as the key"
- "really explore how it's done for the fenix in particular so you have the basics, and adapt for the a350"
- Agreed the same day: the one-shot reads of Alt+P, Alt+N, Alt+S and Alt+I go, as on the Fenix (the key opens the window).

## Global Constraints

- The index's "Shared conventions" apply; [A350-1] above all: a window reads draws and presses only the panels' own rows.
- Every control in a window is a panel row: pressed with `PressRow` (a two-position row with `PressToggleFromWindow`), labelled "<label>: <state>" from `TryDescribeControlState` (the bare label while unread, never a guessed state), and armed as the window's own echo (`ArmWindowEcho`, the row and the lamps its state reads) so the generic path does not say what the button's new text says ([CORE-7]). No window writes a variable itself.
- The windows keep the displays' colours (the A350 rule for every display but the MFD, plan 25).
- The Fenix's window behaviour: the list first in the tab order and focused on open, row 0 selected on the first open only, the row followed through a refresh, one status row "<display>: not available, <reason>" (reasons: "display off", "not read yet", "nothing shown", "read failed", and for the SND "not fitted"), F5 once per press, Escape closes unless a combo's list is open, the previous window brought back on close, nothing spoken on open, refresh or close.
- Keys (output mode): Alt+P PFD, Alt+N ND, Alt+E E/WD, Alt+S SD, Alt+I ISIS, Alt+Shift+N SND; the one-shot reads go; Ctrl+Shift+B no longer opens anything on the A350 (MainForm's fallback names Alt+N). Ctrl+W, D, Shift+D, Shift+1 to Shift+6 stay as they are.
- Shared code is touched only where a key needs it: one `HotkeyAction` member and its registration (Alt+Shift+N), and MainForm's two fallback sentences.
- Offline only: every press sequence is a panel row already flown; the windows' feel and the draw load go on `docs/a350/owner-steps.md` for one live check.
- Every commit carries `Plan: 26` and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; pushed to `gus` as a plain fast-forward, never a rebase.

## Review Focus

- A display with no draw yet, dark, or (the SND) not fitted: one status row, and its controls still press their rows (a dark display's panel still works) (Task 2 tests).
- The first officer's side: the side box reads side 1 and every row key swaps LEFT for RIGHT (Task 3, Task 4 tests).
- A press, then a refresh from a draw older than the press: the list never steps back to the older draw (Task 2 test, the `A350ShownCapture` rule).
- A row whose state is unread: its button says its bare label (Task 2 test).
- A window press that lights a lamp the A350 announces: heard once (Task 2 test: the row's lamps armed).

---

### Task 0 [controller]: Claim this plan

As every plan's Task 0: check the remote, set the record below, commit `docs(a350): claim plan 26` with the `Plan: 26` trailer and push before Task 1.

### Task 1: The rows (`A350DisplayRows`)

**Files:** Create `MSFSBlindAssist/Aircraft/A350/Display/A350DisplayRows.cs`; test `tests/MSFSBlindAssist.Tests/A350/A350DisplayRowsTests.cs`.

**Interfaces:**
- Produces: `public static IReadOnlyList<string> A350DisplayRows.For(string display, A350DrawCapture? capture, bool dark, Func<A350DrawCapture, IReadOnlyList<string>> read, Action<string>? report = null)`: dark → `"<display>: not available, display off"`; no capture → `", not read yet"`; `read` throws → `", read failed"` (reported once per distinct message as `"read failed: <type>: <message>"`); no rows or a null row → `", nothing shown"`; else the rows. Never empty.
- Produces: `public static bool A350DisplayRows.IsStatusRow(string display, IReadOnlyList<string> rows)`.
- Produces: `public static int A350DisplayRows.RowToFollow(IReadOnlyList<string> rows, string? followed, int currentIndex)`: the Fenix's rule (exact text, else most leading words shared, at least one, nearest on a tie; -1 for none).

- [ ] **Step 1:** Failing tests: each status reason; dark wins over a capture; a throwing read reports once; `RowToFollow` on "Altitude -63 feet, STD" → "Altitude -64 feet, STD", an exact row nearest the cursor, no shared word → -1, null → -1; the status rows the same under de-DE.
- [ ] **Step 2:** Run; they fail (no type).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `--filter FullyQualifiedName~A350DisplayRowsTests`; all pass.
- [ ] **Step 5:** Commit `feat(a350): a display window's rows, as the Fenix's (plan 26)`.

### Task 2: The base window (`A350DisplayWindow`) and its row controls

**Files:** Create `MSFSBlindAssist/Forms/A350/A350DisplayWindow.cs`, `MSFSBlindAssist/Forms/A350/IA350WindowRows.cs`; modify `MSFSBlindAssist/Aircraft/IniBuildsA350Definition.Hotkeys.cs` (an adapter `A350WindowRows` implementing the seam, and `ArmWindowPressEcho`); test `tests/MSFSBlindAssist.Tests/A350/A350DisplayWindowTests.cs`.

**Interfaces:**
- Produces: `public interface IA350WindowRows { string? State(string row); void Press(string row, double value = 1); void Toggle(string row); }` (the adapter: `TryDescribeControlState`, `ArmWindowPressEcho` then `PressRow`, `PressToggleFromWindow`).
- Produces: `public sealed record A350DisplayFeed(Func<int, A350DrawCapture?> Latest, Func<int, Task<A350DrawCapture?>> DrawNow, Func<int, bool> Dark, Func<A350DrawCapture, int, IReadOnlyList<string>> Read)`.
- Produces: `public sealed record A350WindowKey(string Label, Func<int, string> Row, bool Toggle = false)` (a row per side).
- Produces: `internal sealed class A350KeyRow : FlowLayoutPanel` with `(IReadOnlyList<A350WindowKey> keys, IA350WindowRows rows, Func<int> side)`, `void RefreshStates()` and `IReadOnlyList<Button> Buttons`: one button per key, "<label>: <state>", a click pressing (or toggling) the side's row; the base window and `A350NdWindow` both use it.
- Produces: `public class A350DisplayWindow : Form` with `(string display, A350DisplayFeed feed, IA350WindowRows rows, int sides = 1)`; `public void ShowForm()`; `internal void StartLive()`; `internal void RefreshNow(A350DrawCapture? fresh = null)`; `protected void AddControlAfterList(Control c)`; `protected A350KeyRow AddKeyRow(IReadOnlyList<A350WindowKey> keys)` (refreshed each tick); `protected virtual bool HandleWindowKey(Keys keyData)`; `protected virtual void OnRefresh()`; `internal int Side`; `internal IReadOnlyList<Button> KeyButtons`; a press refreshes from a fresh draw 600 ms later, and a tick never shows a draw older than the one shown (`A350ShownCapture`).
- Produces: `internal void IniBuildsA350Definition.ArmWindowPressEcho(string rowKey)`: arms the row and every lamp variable its state is read from.

- [ ] **Step 1:** Failing tests (the window built and driven with `StartLive`/`RefreshNow`, as `FenixChannelWindowTests`): the list is named "<display> display" and comes first in the tab order; the first start selects row 0, a second does not move the pilot; a refresh keeps the row by its words; dark, unread and not-fitted give one status row and the row followed comes back after it; with two sides the side box reads side 1 and its key rows press the RIGHT row; a key button reads "LS: On", its bare label while unread, presses its row once per click and a toggle through `Toggle`; a fresh draw is not replaced by an older latest one; `ArmWindowPressEcho` arms the EFIS CSTR row's lamp (`WindowEchoActive` on it).
- [ ] **Step 2:** Run; they fail.
- [ ] **Step 3:** Implement (the Fenix base's members, the A350's feed and seam).
- [ ] **Step 4:** Run `--filter FullyQualifiedName~A350`; all pass.
- [ ] **Step 5:** Commit `feat(a350): the display window, as the Fenix's (plan 26)`.

### Task 3: The PFD window (Alt+P)

**Files:** Create `MSFSBlindAssist/Forms/A350/A350PfdWindow.cs`; modify `IniBuildsA350Definition.Hotkeys.cs` (Alt+P opens it); `MSFSBlindAssist/Aircraft/A350/A350Hotkeys.cs` (`DisplayWindows`); test `tests/MSFSBlindAssist.Tests/A350/A350PfdWindowTests.cs`.

**Interfaces:**
- Produces: `internal static readonly IReadOnlyList<A350WindowKey> A350PfdWindow.Keys`: FD (`A350_FCU_FD`, toggle), LS (`A350_EFIS_LS_LEFT|RIGHT`, toggle), VV (`A350_EFIS_VV_*`, toggle), Baro STD (`A350_EFIS_QNH_PRESSURE_SELECTOR_*_PUSH`), Baro QNH (`..._PULL`), Baro unit (`A350_EFIS_QNH_UNIT_SELECTOR_*`); then a QNH box that applies on Enter through the panel's typed QNH row of that side (`A350TypedEntry`), as the panel's field.
- Produces: `public static readonly IReadOnlyDictionary<HotkeyAction, string> A350Hotkeys.DisplayWindows` (ReadDisplayPFD "PFD", ReadDisplayND "ND", ReadDisplayUpperECAM "E/WD", ReadDisplayLowerECAM "SD", ReadDisplayISIS "ISIS", ShowStandbyNavDisplay "SND").
- Rows: the side's PFD as its status display reads it (`ReadDisplay` of "Captain PFD" / "First Officer PFD"), two sides.

- [ ] **Step 1:** Failing tests: the keys per side (LEFT, RIGHT; FD shared); the rows of an EFIS fixture equal the status display's lines; `DisplayWindows` maps each key; a dark PFD's status row.
- [ ] **Step 2-4:** Run, implement, run the A350 tests.
- [ ] **Step 5:** Commit `feat(a350): the PFD window on Alt+P, its EFIS and FD controls under it (plan 26)`.

### Task 4: The ND window on Alt+N, every ND function in it

**Files:** Modify `MSFSBlindAssist/Forms/A350/A350NdWindow.cs`, `IniBuildsA350Definition.Hotkeys.cs` (Alt+N opens it; ShowOANS no longer handled), `MSFSBlindAssist/MainForm.Hotkeys.cs` (ShowOANS's fallback: "The airport map is only on the A380; on the A350 it is in the ND window, output Alt+N."); test `tests/MSFSBlindAssist.Tests/A350/A350NdWindowTests.cs` (new or the existing ND tests).

**Interfaces:**
- Produces: `internal static IReadOnlyList<A350WindowKey> A350NdWindow.EfisKeys`: CSTR, WPT, VORD, NDB, ARPT (`A350_EFIS_TSK_1..5_*`), VOR 1, WX, TERR, TRAF, VOR 2 (`A350_EFIS_BSK_1..5_*`), TAXI (`A350_EFIS_TAXI_*`, toggle), labelled from the panel's lamps, in an `A350KeyRow`.
- The window's tab order becomes the Fenix's: the list, the text box, mode and range, the EFIS keys, the BTV row, the side box. Its own Enter, drag, BTV and refresh stay as they are.

- [ ] **Step 1:** Failing tests: `EfisKeys` per side (LEFT, RIGHT; every EFIS panel row not on the PFD window is on one of the two windows: a test over the panel's rows); the list first in the tab order; `DisplayWindows` holds ReadDisplayND and not ShowOANS.
- [ ] **Step 2-4:** Run, implement, run.
- [ ] **Step 5:** Commit `feat(a350): the ND window on Alt+N with every EFIS button, Ctrl+Shift+B retired (plan 26)`.

### Task 5: The E/WD and SD windows (Alt+E, Alt+S)

**Files:** Create `MSFSBlindAssist/Forms/A350/A350EwdWindow.cs`, `A350SdWindow.cs`; modify `IniBuildsA350Definition.Hotkeys.cs` (Alt+E, Alt+S); tests `A350EwdWindowTests.cs`, `A350SdWindowTests.cs`.

**Interfaces:**
- E/WD rows: `A350EwdText.Build(capture, sdPage)` of the CTR draw; keys: T.O CONFIG, CLEAR left, CLEAR right, RCL LAST, RCL ALL, EMER CANC guard (toggle), EMER CANC, ABN, DEFRD, CHECKLIST left, CHECKLIST right, MORE, DISPCH, wheel left up/down, wheel right up/down (`A350_ECAM_*`, the wheels `#INC`/`#DEC`).
- SD rows: `A350SdText.Build(capture, sdPage)`; keys: ENG, APU, BLEED, COND, PRESS, DOOR, EL/AC, EL/DC, FUEL, WHEEL, HYD, F/CTL, ALL, STS, VIDEO, SD knob push, SD knob up, SD knob down.
- Every row of the ECAM control panels is on exactly one of the two windows (test).

- [ ] **Step 1:** Failing tests: the key lists; the ECP coverage test; rows from the CTR fixtures equal the E/WD and SD readers' lines.
- [ ] **Step 2-4:** Run, implement, run.
- [ ] **Step 5:** Commit `feat(a350): the E/WD and SD windows with the ECAM control panel's keys (plan 26)`.

### Task 6: The ISIS and SND windows (Alt+I, Alt+Shift+N)

**Files:** Create `MSFSBlindAssist/Forms/A350/A350IsisWindow.cs`, `A350SndWindow.cs`; modify `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` (`HotkeyAction.ShowStandbyNavDisplay`, output Alt+Shift+N), `MainForm.Hotkeys.cs` (its fallback: "The standby navigation display window is only on the A350."), `IniBuildsA350Definition.Hotkeys.cs`; tests `A350IsisWindowTests.cs`.

**Interfaces:**
- ISIS rows: the SFD's (`A350GroundDisplays.ReadSfd`); keys: STD/CHANGE, LS/DIR TO, MENU, Minus, MODE, Plus, knob up, knob down (`A350_ISIS_*_1`).
- SND rows: `ReadSnd`; the same keys on `_2`; not fitted (`A350Options` "Standby navigation display") → its status row.

- [ ] **Step 1:** Failing tests: the key lists (every ISIS and SND row covered); the not-fitted status row; `DisplayWindows` holds ShowStandbyNavDisplay.
- [ ] **Step 2-4:** Run, implement, run.
- [ ] **Step 5:** Commit `feat(a350): the ISIS window on Alt+I and the SND window on Alt+Shift+N (plan 26)`.

### Task 7: The windows list, the old reads, the rule and the docs

**Files:** `IniBuildsA350Definition.Hotkeys.cs` (Ctrl+Shift+F lists PFD, ND, E/WD, SD, ISIS, SND, MFD, RMP, Mailbox, FAP, Printer, each naming its key; the one-shot `AnnounceDisplay` and the SD sentence read removed); `.claude/rules/inibuilds-a350.md` and `docs/invariants/inibuilds-a350.md` ([A350-17]); `docs/inibuilds-a350.md`; `guide/airbus-aircraft.md`; `guide/hotkey-reference.md`; `MSFSBlindAssist/HotkeyGuides/iniBuilds_A350_Hotkeys.txt`; `docs/a350/owner-steps.md`; this plan's record; test `A350WindowsListTests` (the entries, in that order).

- [ ] **Step 1:** [A350-17], at most 400 characters: an A350 display window's control is the panel's own row, pressed through `PressRow` or `PressToggleFromWindow`, its label from `TryDescribeControlState`, armed as the window's echo (`ArmWindowPressEcho`); a window never writes a variable itself and says nothing on open, refresh or close.
- [ ] **Step 2:** The guides and docs: the keys, each window's list and controls; Ctrl+Shift+B gone from the A350.
- [ ] **Step 3:** Owner steps: one live check (each window opens on its key, follows its display, each control presses; the SND on Alt+Shift+N; the draw load with three windows open).
- [ ] **Step 4:** Run the suite with `ClaudeRulesHookTests` and `ClaudeContextBudgetTests`; all pass.
- [ ] **Step 5:** Commit `docs(a350): the display windows at Fenix parity (plan 26)`.

### Task 8: Whole-plan review

A fresh reviewer on the most capable model; the fix pass test-first; the full suite; push.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** claimed
- **Claimed by:** Francesco Tissera, with Claude, 2026-11-13
- **Worked by:** Claude (Opus 5.5), inline (superpowers:executing-plans)
- **Started:** 2026-11-13, from `ef6bcac8d`
- **Landed:** 2026-11-13, offline; final review (a fresh reviewer): no critical finding, the important ones fixed test-first
- **Tests at landing:** the suite without `ClaudeRulesHookTests` 22,205/22,205; `ClaudeRulesHookTests` 203/203
- **Tasks:** 1 `36338d9` (`A350DisplayRows`), 2 `17af37d` (`A350DisplayWindow`, `A350KeyRow`, `A350WindowRows`), 3 `c2be48f` (PFD,
  Alt+P), 4 `85b3032` (ND on Alt+N with the EFIS buttons, Ctrl+Shift+B retired), 5 `ab51450` (E/WD and SD, Alt+E and Alt+S), 6
  `4bf4dad` (ISIS on Alt+I, SND on Alt+Shift+N), 7 the windows list, [A350-17] and the docs
- **Waiting on the owner:** the live check, `owner-steps.md` 2.4 item 5
- **In-sim results:** none: built offline
- **Changed from the plan:** no `ArmWindowPressEcho`: the row write path already marks a pressed control's lamps as the pilot's own, so
  the window arms only the row; a typed box (`A350TypedBox`) for the PFD's and the ISIS's QNH; the ISIS window carries its panel's
  rows (six keys and the typed QNH, no knob up and down); a display not fitted is a fifth status reason; each one-shot read went with its
  key's window; after the final review, presses close together read one fresh draw, each side keeps its own fresh draw, the buttons
  refresh on every tick, and Escape spares an open combo list in the ND window too
- **Rulings made while working:** the A350's own copy of the Fenix's `RowToFollow`; the side box right after the list; the E/WD
  window keeps all the Alt+E text showed; the SD page selector (the page keys as a combo) is on neither window; the ND window's rows
  named "EFIS" and "BTV controls"
