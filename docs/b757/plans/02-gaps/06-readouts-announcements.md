# BlueBird 757 Readouts and Announcements Implementation Plan (plan 06, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

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

### Task 1: EXT PWR reads back its state

**Files:** `Aircraft/BBS757Definition.Readouts.cs` (`AddReadoutVariables`, `AddReadoutPanels`, `ProcessReadoutUpdate`, `LinkedStatusKey`, `LinkedPhrase`); create `Aircraft/BBS757/Bbs757Annunciators.cs`; test `Bbs757AnnunciatorsTests.cs`.

**Interfaces:**
- `record Bbs757AnnunciatorRow(string Key, string LVar, string Label, Func<double, double?, string?> Phrase, string? Panel)` and `Bbs757Annunciators.Rows`, starting with:
  - `Bbs757SharedKeys.ExtPwrOn` → `B757_OVHD_EXT_PWR_BUTTON_ON`, "External Power On": 1 "External power on", 0 "External power off"; panel "Electrical".
  - `Bbs757SharedKeys.ExtPwrAvail` → `B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL`, "External Power Available": 1 "External power available", 0 "External power not available"; panel "Electrical".
  Each row: LVar, Continuous, `IsAnnounced`, a Ctrl+M row named by its label, a read-only status row on its panel.
- `LinkedStatusKey("B757_OVHD_EXT_PWR_BUTTON")` → `Bbs757SharedKeys.ExtPwrOn`; `LinkedPhrase(ExtPwrOn, v)` → "EXT PWR on" / "EXT PWR off". A press then reads back "EXT PWR on" and the row's own change is that press's echo.
- `ProcessReadoutUpdate`: each row through `_changes.Observe(key, phrase, NowMs())` when its phrase is non-null.

- [ ] **Step 1: Write the failing tests:** `Ext_pwr_press_reads_back` (link and phrases); `Ext_pwr_rows_on_electrical` (both rows registered as above, on the Electrical display); `Ext_pwr_change_announced_once`.
- [ ] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757AnnunciatorsTests`. Expected: FAIL.
- [ ] **Steps 3-4:** implement; full suite PASS.
- [ ] **Step 5: Commit** `fix(b757): EXT PWR reads back on or off, external power on the Electrical panel`.

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

- [ ] **Steps 1-5** (tests `Vref_at_the_measured_points`, `Vref_between_points` (178,600 → 124 / 126, the FMC's own), `Maneuver_speeds_at_178600` (204, 184, 164, 144, 144), `Outside_the_table_is_unavailable`, `Sentences_under_de_DE`); commit `feat(b757): flap maneuver speeds and VREF on Shift+1 to Shift+6`.

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

- [ ] **Steps 1-5** (tests from session 8's lines: `Destination_from_prog`, `Top_of_climb_from_prog`, `Top_of_descent_variant`, `No_top_line`, `Page_two_is_progress_but_not_page_one`, `Phrases`); commit `feat(b757): D and Shift+D read the CDU's PROG page`.

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

- [ ] **Steps 1-5** (tests `Asa_phrases`, `Irs_aligned_only_in_nav`, `Door_ends_only`, `Rows_are_announced_and_muteable`, `Variable_ceiling_holds`; full suite with `VarNameCollisionTests`, `DefAnnounceMuteSetsTests`); commit `feat(b757): autoland status, alerts, IRS, start valves and doors announced`.

## In-sim checks (owner)

1. The fix: press EXT PWR with the GPU connected → "EXT PWR on", and again → "EXT PWR off".
2. The essentials: Shift+3 at the gate → "Flaps 5 maneuver speed … knots, calculated" (APPROACH REF VREF30 + 40); D and Shift+D; IRS L from OFF to NAV → "IRS L aligned" when done; engine start → "Left start valve open", then "closed"; an autoland → "LAND 3".

## Handoff to plan 16

- **Pilot-facing:** the fix "757: EXT PWR says on or off when pressed; external power available and on are announced and shown on the Electrical panel" (`fix`); the essentials "Shift+1 to Shift+5 read the flap maneuver speeds (flaps up, 1, 5, 15, 20) from BlueBird's own VREF at your weight, Shift+6 VREF 30 and 25; D and Shift+D read the distance and ETA to the destination and to top of climb or descent from the first officer's CDU PROG page; announced now: autoland status, altitude alert, configuration warning, each IRS aligned, the engine start valves and the doors (each in Ctrl+M)" (`feature`).
- **Proposed rule line:** `- [B757-9] 757 flap maneuver speeds come from `Bbs757FlapSpeeds`' VREF30 table, measured on BlueBird's APPROACH REF at 140,000-250,000 lb; never extrapolate outside it and never compute VREF from a generic formula. Full: docs/invariants/b757.md#b757-9`

## Execution record

Kept in this file only, so no other plan's push ever touches it. Claim the plan by filling in **Claimed by** and
setting **Status** to claimed, and push that one change before starting; if the push stops on a conflict in these
lines, someone claimed it first. Fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started
- **Claimed by:** none
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** (date; the `B757` commit you branched from)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
