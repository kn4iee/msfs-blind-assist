# BlueBird 757 Readouts and Announcements Implementation Plan (plan 06, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fix first: the EXT PWR button reads back its state instead of "pressed". Then the essentials: Shift+1 to Shift+6 read the flap maneuver speeds and VREF, D and Shift+D read the distance to destination and to top of climb or descent, and the autoland status, altitude alert, configuration warning, external power, IRS alignment, engine start valves and doors are announced. (Squawk, TCAS mode and altimeter change announcements and Alt+I are plan 14, which takes these files over.)

**Architecture:** Flap speeds come from VREF30 measured on BlueBird's own APPROACH REF page at twelve weights and the standard 757 maneuver schedule (owner, Q5). D and Shift+D parse the CDU's PROG page 1 on the first officer's CDU through plan 01's `Bbs757CduPages`. The announcements are BlueBird's published lights and states, registered as Continuous rows in this plan's readouts file and spoken through the definition's `Bbs757ChangeTracker`.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#2, #12), 2, 3.5, 4 (R61, R66, R68), 8.

## Global Constraints

- Owned files (then passed to plan 14): `Aircraft/BBS757Definition.Readouts.cs` (after plan 01), `Aircraft/BBS757/Bbs757Readouts.cs`, `Bbs757McpLights.cs`, `Bbs757FlapSpeeds.cs` (new), `Bbs757Prog.cs` (new), `Bbs757Annunciators.cs` (new); tests `Bbs757ReadoutsTests.cs`, `Bbs757FlightReadinessTests.cs` (after plan 01), `Bbs757FlapSpeedsTests.cs`, `Bbs757ProgTests.cs`, `Bbs757AnnunciatorsTests.cs` (new); `docs/b757/evidence/flight-06.md`.
- VREF (pounds × 1000 → VREF 30 / VREF 25, measured): 140 109/111, 150 113/115, 160 117/119, 170 121/123, 180 125/127, 190 129/131, 200 133/135, 210 137/139, 220 140/142, 230 144/146, 240 148/150, 250 152/154; linear between, rounded; outside 140,000-250,000 lb unavailable.
- Schedule (owner, Q5): flaps up VREF30 + 80, 1 + 60, 5 + 40, 15 + 20, 20 + 20. Keys: Shift+1 `ReadSpeedGD` up, Shift+2 `ReadSpeedS` 1, Shift+3 `ReadSpeedF` 5, Shift+4 `ReadSpeedVLS` 15, Shift+5 `ReadSpeedVS` 20, Shift+6 `ReadSpeedVFE` VREF 30 and 25.
- Weight: `Bbs757Readouts.TotalWeightKey` (stock `TOTAL WEIGHT`, pounds) through `FlapManeuverSpeeds.AnnounceAsync`; sentences through `FlapManeuverSpeeds.Sentence`.
- PROG page 1 (measured): line 5 ` DEST`, line 6 `KMIA    568  0332Z   6.1`, line 7 ` ECON SPD         TO T/C`, line 8 `299/.785     0218Z/ 56NM`; "TO T/D" with its time and distance between top of climb and top of descent (assumed; Task 3 accepts either).
- Variables added: 20.

## Review Focus

- A weight outside the table (251,000 lb heavy, or a bad read) says "Flap speeds unavailable", never an extrapolated number (Task 2 test).
- PROG opening on page 2 or 3: the app presses PREV until page 1 (at most two presses) rather than saying the CDU did not answer (Task 3 test).
- An IRS switched off also puts its ALIGN light out: only a light going out with that IRS in NAV says "aligned" (Task 4 test).
- A door animating through 1-99 % speaks only at the ends (Task 4 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [x] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/06-readouts-announcements.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [x] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 06`, with the `Plan: 06` trailer.
- [x] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: EXT PWR reads back its state

**Files:** `Aircraft/BBS757Definition.Readouts.cs` (`AddReadoutVariables`, `AddReadoutPanels`, `ProcessReadoutUpdate`, `LinkedStatusKey`, `LinkedPhrase`); create `Aircraft/BBS757/Bbs757Annunciators.cs`; test `Bbs757AnnunciatorsTests.cs`.

**Interfaces:**
- `record Bbs757AnnunciatorRow(string Key, string LVar, string Label, Func<double, double?, string?> Phrase, string? Panel)` and `Bbs757Annunciators.Rows`, starting with:
  - `Bbs757SharedKeys.ExtPwrOn` → `B757_OVHD_EXT_PWR_BUTTON_ON`, "External Power On": 1 "External power on", 0 "External power off"; panel "Electrical".
  - `Bbs757SharedKeys.ExtPwrAvail` → `B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL`, "External Power Available": 1 "External power available", 0 "External power not available"; panel "Electrical".
  Each row: LVar, Continuous, `IsAnnounced`, a Ctrl+M row named by its label, a read-only status row on its panel.
- `LinkedStatusKey("B757_OVHD_EXT_PWR_BUTTON")` → `Bbs757SharedKeys.ExtPwrOn`; `LinkedPhrase(ExtPwrOn, v)` → "EXT PWR on" / "EXT PWR off". A press then reads back "EXT PWR on" and the row's own change is that press's echo.
- `ProcessReadoutUpdate`: each row through `_changes.Observe(key, phrase, NowMs())` when its phrase is non-null.

- [x] **Step 1: Write the failing tests:** `Ext_pwr_press_reads_back` (link and phrases); `Ext_pwr_rows_on_electrical` (both rows registered as above, on the Electrical display); `Ext_pwr_change_announced_once`.
- [x] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757AnnunciatorsTests`. Expected: FAIL.
- [x] **Steps 3-4:** implement; full suite PASS.
- [x] **Step 5: Commit** `fix(b757): EXT PWR reads back on or off, external power on the Electrical panel`.

### Task 2: Flap maneuver speeds (Shift+1 to Shift+6)

**Files:** Create `Aircraft/BBS757/Bbs757FlapSpeeds.cs`; modify `BBS757Definition.Readouts.cs` (implements plan 01's `HandleReadoutHotkey`); test `Bbs757FlapSpeedsTests.cs`.

**Interfaces:**
  ```csharp
  public static class Bbs757FlapSpeeds
  {
      public static int? Vref30(double? grossWeightLbs);
      public static int? Vref25(double? grossWeightLbs);
      public static string Describe(double? grossWeightLbs, int scheduleIndex);   // 0-4 maneuver speeds; 5 "VREF 30 124 knots, VREF 25 126 knots, calculated"
  }
  ```
  `HandleReadoutHotkey`: the six actions → `FlapManeuverSpeeds.AnnounceAsync(simConnect, announcer, Bbs757Readouts.TotalWeightKey, "BBS757", lbs => Bbs757FlapSpeeds.Describe(lbs, index))`; `handled = true`.

- [x] **Steps 1-5** (tests `Vref_at_the_measured_points`, `Vref_between_points` (178,600 → 124 / 126, the FMC's own), `Maneuver_speeds_at_178600` (204, 184, 164, 144, 144), `Outside_the_table_is_unavailable`, `Sentences_under_de_DE`); commit `feat(b757): flap maneuver speeds and VREF on Shift+1 to Shift+6`.

### Task 3: D and Shift+D from PROG

**Files:** Create `Aircraft/BBS757/Bbs757Prog.cs`; modify `BBS757Definition.Readouts.cs`; test `Bbs757ProgTests.cs`.

**Interfaces:**
  ```csharp
  public sealed record Bbs757ProgDest(string Ident, int Miles, string? EtaZulu);   // "KMIA", 568, "0332"
  public sealed record Bbs757ProgTop(string Kind, int Miles, string? EtaZulu);     // "T/C" or "T/D"
  public static class Bbs757Prog
  {
      public static bool IsProgress(Bbs757CduScreen s);          // Bbs757CduTitle.PageName ends with "PROGRESS"
      public static Bbs757ProgDest? Destination(Bbs757CduScreen s);
      public static Bbs757ProgTop? TopOf(Bbs757CduScreen s);
      public static string DestinationPhrase(Bbs757ProgDest? d); // "568 miles to destination KMIA, ETA 03:32 Zulu"
      public static string TopPhrase(Bbs757ProgTop? t);          // "Top of climb in 56 miles, 02:18 Zulu" / "Top of descent in …" / "No top of climb or descent ahead"
      public const string NoCdu = "The first officer's CDU did not answer.";
  }
  ```
  `HandleReadoutHotkey`: `ReadDistanceToDest` (D) and `ReadDistanceToTOD` (Shift+D): `CduPages.ShowPageAsync(2, "PROG", IsProgress, ct)`; while `PageCount` is not page 1, `PressKeysAsync(2, ["PREV"])` (at most twice); parse; speak; null → `NoCdu`; `handled = true`.

- [x] **Steps 1-5** (tests from session 8's lines: `Destination_from_prog`, `Top_of_climb_from_prog`, `Top_of_descent_variant`, `No_top_line`, `Page_two_is_progress_but_not_page_one`, `Phrases`); commit `feat(b757): D and Shift+D read the CDU's PROG page`.

### Task 4: Essential announcements

**Files:** `Bbs757Annunciators.cs`, `BBS757Definition.Readouts.cs`; test `Bbs757AnnunciatorsTests.cs`.

**Interfaces:** rows added to `Bbs757Annunciators.Rows` (as Task 1):

| Key | L:var | Label (Ctrl+M) | Phrase | Panel |
|---|---|---|---|---|
| `BBS757_ASA_UPPER` | `B757_MAIN_L_AUTOLAND_1_BUTTON_ON` | Autoland Status Upper | 0 "LAND 3", 2 "LAND 2", 1 silent | Status |
| `BBS757_ASA_LOWER` | `B757_MAIN_L_AUTOLAND_2_BUTTON_ON` | Autoland Status Lower | 0 "NO LAND 3", 2 "NO AUTOLAND", 1 silent | Status |
| `BBS757_ALT_ALERT` | `BBS757_CA_ALTIMETER_ALT_ALERT_LIGHT` | Altitude Alert | 1 "Altitude alert", 0 silent | — |
| `BBS757_CONFIG_WARNING` | `B757_MAIN_CONFIG_1_DECAL` | Configuration Warning | 1 "Configuration warning", 0 silent | — |
| `BBS757_IRS_L_ALIGN`, `_C_`, `_R_` | `B757_OVHD_IRS_L_ALIGN_DECAL`, `_C_`, `_R_` | IRS L / C / R Aligned | 1 → 0 with that IRS at NAV (2): "IRS L aligned"; else silent | — |
| `BBS757_IRS_L_SWITCH_WATCH`, `_C_`, `_R_` | `B757_IRS_L_SWITCH`, `_C_`, `_R_` | (silent, `ExcludeFromMonitorManager`) | — | — |
| `BBS757_L_START_VALVE`, `BBS757_R_START_VALVE` | `B757_OVHD_L_ENG_START_VALVE_DECAL`, `_R_` | Left / Right Start Valve | 1 "Left start valve open", 0 "… closed" | Engines |
| `BBS757_DOOR_L1` … `_R3` | `B757_DOORS_PAX_L1` … `_R3` (0-100) | Door L1 … R3 | ≥ 90 "Door L1 open", ≤ 5 "Door L1 closed", between silent | — |
| `BBS757_DOOR_CARGO_FWD`, `_AFT` | `B757_DOORS_CARGO_FWD`, `_AFT` (0-200) | Forward / Aft Cargo Door | ≥ 180 "… open", ≤ 10 "… closed" | — |

The IRS rows read the matching switch row's last delivered value (kept in a small dictionary). The autoland rows are confirmed on the owner's first autoland (record in `flight-06.md`).

- [x] **Steps 1-5** (tests `Asa_phrases`, `Irs_aligned_only_in_nav`, `Door_ends_only`, `Rows_are_announced_and_muteable`, `Variable_ceiling_holds`; full suite with `VarNameCollisionTests`, `DefAnnounceMuteSetsTests`); commit `feat(b757): autoland status, alerts, IRS, start valves and doors announced`.

## In-sim checks (owner)

1. The fix: press EXT PWR with the GPU connected → "EXT PWR on", and again → "EXT PWR off".
2. The essentials: Shift+3 at the gate → "Flaps 5 maneuver speed … knots, calculated" (APPROACH REF VREF30 + 40); D and Shift+D; IRS L from OFF to NAV → "IRS L aligned" when done; engine start → "Left start valve open", then "closed"; an autoland → "LAND 3".

## Handoff to plan 16

- **Pilot-facing:** the fix "757: EXT PWR says on or off when pressed; external power available and on are announced and shown on the Electrical panel" (`fix`); the essentials "Shift+1 to Shift+5 read the flap maneuver speeds (flaps up, 1, 5, 15, 20) from BlueBird's own VREF at your weight, Shift+6 VREF 30 and 25; D and Shift+D read the distance and ETA to the destination and to top of climb or descent from the first officer's CDU PROG page; announced now: autoland status, altitude alert, configuration warning, each IRS aligned, the engine start valves and the doors (each in Ctrl+M)" (`feature`).
- **Proposed rule line:** `- [B757-9] 757 flap maneuver speeds come from `Bbs757FlapSpeeds`' VREF30 table, measured on BlueBird's APPROACH REF at 140,000-250,000 lb; never extrapolate outside it and never compute VREF from a generic formula. Full: docs/invariants/b757.md#b757-9`
- **Proposed rule line (found while working):** `- [B757-n] A `Bbs757Annunciators` row's silent state is a `Bbs757Annunciators.Quiet` phrase (tracked, never spoken), never null: `Bbs757ChangeTracker` takes the first phrase after an untracked state for a baseline, so the next spoken state would be lost. A linked row's read-back (`LinkedPhrase`) and its own phrase must be the same words, or the read-back's rebaseline re-speaks the press. Full: docs/invariants/b757.md#b757-n`
- **The pages:** `HotkeyGuides/BBS757_Hotkeys.txt` and `guide/hotkey-reference.md` (Shift+1 to Shift+6, D, Shift+D for the 757; D and Shift+D leave the first officer's CDU on PROG page 1), the 757 chapter of the guide (the announcements and their Ctrl+M rows), `docs/b757.md` (the readouts and the announced rows), `Services/DefAnnounceMuteSets.cs`'s summary (it lists the 757's call-outs; frozen to plan 16).
- **Tracker baselines after a flight load (from the review, not fixed):** `ResetReadoutBaselines` clears every row's baseline on a flight load, but SimConnectManager delivers a batch value only when it changes; unless the load re-registers the variables (which clears its value cache), a row that did not change is never delivered again, and its first change after the load becomes the silent baseline (an engine start after loading cold and dark: "Left start valve open" not said, "closed" said). This is the existing design for flaps, gear and the MCP lights too, so it is plan 16's (or the dev group's) to rule on: seed the tracker from `GetCachedVariableValue` after a reset, or confirm a flight load re-registers. In-sim check 2's start valve is the place to hear it.
- **Deferred from the review (minor):** D then Shift+D pressed quickly can interleave PREV presses across the CDU page service's gate (one says the CDU did not answer); `Bbs757Prog.NoCdu` also covers "keys already queued" and "PREV did not reach page 1"; the altitude alert light may flash and repeat "Altitude alert" (listen in flight); a door mid-travel when the app connects has no baseline, so its first end is silent; `Bbs757Annunciators.ElectricalPanel` and `BBS757Definition.GroundPowerPanel` are two constants for "Electrical" (one can go); an IRS light going out at NAV for another reason (power loss) says aligned.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** landed on `B757` (2026-10-26); waiting on owner (the in-sim checks)
- **Claimed by:** Daniel Wolak (danielw97), 2026-10-26
- **Worked by:** Daniel Wolak with Claude (Opus 5.5), inline (superpowers:executing-plans), no per-task subagents; one whole-plan review by a fresh Opus 5.5 subagent and one fix pass
- **Started:** 2026-10-26, from `cf1dfce94` (claim e8c904670, first flight's evidence d97d89f51); Tasks 1-4 worked on `B757` at a6743e9ed (plan 07 landed)
- **Landed:** 2026-10-26; last code commit 697dcbde0. `git log --grep "^Plan: 06"` lists the plan's commits.
- **Tests at landing:** full suite passed, 17,443 (17,350 before Task 1); `MSFSBlindAssist.sln` builds with no new warning
- **Tasks:** all done.
  - Task 1, EXT PWR: 69ef4c8aa.
  - Task 2, flap speeds: e04ac42f9.
  - Task 3, D and Shift+D: 17bc983a4; its tests made async 6f0639af9 (xUnit1031).
  - Task 4, the announcements: 43fb69cec.
  - Whole-plan review (no Critical; three Important): 697dcbde0 (an EXT PWR press arms its AVAIL light's echo; a flight load keeps the rows' raw values; the autoland status comment no longer says confirmed). The third Important's wider half (tracker baselines after a flight load) and seven minors are in the handoff.
- **Waiting on the owner:** in-sim checks 1 and 2, and: whether AVAIL goes out when EXT PWR is ON (check 1); the autoland status values cold and dark, unpowered, armed and after the autoland (check 2's autoland, recorded in `flight-06.md`), since the rows' values are the plan's, unmeasured; the first start valve "open" after a flight load (the tracker baselines note in the handoff).
- **In-sim results:** none run yet.
- **Changed from the plan:**
  - The EXT PWR ON row says "EXT PWR on" / "EXT PWR off", the read-back's own words, not "External power on" / "off": the read-back rebaselines the row with `LinkedPhrase`, so different words would announce every press twice.
  - `Bbs757AnnunciatorRow` gains `States` (the status display's words), `RelatedKey` (an IRS's switch row) and `Silent` (the switch rows: no Ctrl+M row); `Bbs757Annunciators.ElectricalPanel`, `ExtPwrButtonNode`, `ExtPwrPhrase`, `Quiet` and `IsSpoken` added.
  - A state the plan's table keeps silent (a blank autoland status line, an alert going out, an IRS aligning or its light out off NAV) is a `Quiet` phrase, tracked and never spoken, not null: with null the next spoken state would be taken for a baseline and lost (an altitude alert would never be said).
  - Variables added: 22, not 20 (the plan's own table lists 22); the profile stays under 900.
  - Added: `Bbs757Prog.ReadPageOneAsync` and `IsPageOne` (the page flow, tested with a fake CDU); `Bbs757Prog.DestinationPhrase(null)` says "No destination on the progress page"; `BBS757Definition.FlapSpeedIndex`; the test `A_ground_power_window_run_is_heard_once_with_both_rows_present` (plan 07's handoff); de-DE and sv-SE tests for every spoken phrase.
  - `Bbs757FlightReadinessTests.The_engines_panel_shows_both_engines_beside_its_switches` expects the two start valves after the engine values on the Engines display.
- **Rulings made while working:** (question; answer; who decided. Claude's rulings are the owner's to reverse.)
  - Work in worktrees or the main copy? The main copy on `B757`; Daniel.
  - The EXT PWR phrases, the `Quiet` states, the record's fields, the variable count: as in "Changed from the plan"; Claude.
  - The ASA rows' L:vars drive the annunciator's animation (BlueBird's tooltips), so a resting 0 could show LAND 3 with nothing armed: kept the plan's values, flagged unconfirmed for the owner's autoland; Claude.
  - Tracker baselines after a flight load (review): left to plan 16, as it is the design for every 757 call-out; Claude.
