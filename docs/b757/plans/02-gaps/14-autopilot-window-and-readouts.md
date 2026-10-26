# BlueBird 757 Autopilot Window and More Readouts Implementation Plan (plan 14, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ctrl+P gains Speed Intervene, A/T Disconnect and the Disengage Bar, with Alt keys on every button; squawk, TCAS mode and altimeter changes from any source are announced; Alt+I reads the standby instruments.

**Architecture:** The autopilot window keeps its layout table and press paths (plan 05's files); the announcements are new Continuous rows in the readouts file (plan 06's) that use plan 01's `OnOwnSquawkSet` / `OnOwnAltimeterSet` hooks so the pilot's own sets are said once; Alt+I is one more AI display read on the instrument view that frames the standby instruments.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 4 (R57, R66, R70), 7; plans 05 and 06, whose files this plan takes over.

## Global Constraints

- Owned files (from plans 05 and 06): `Aircraft/BBS757/Bbs757AutopilotLayout.cs`, `Forms/BBS757/BBS757AutopilotWindow.cs`, `Aircraft/BBS757Definition.Autopilot.cs`, `Aircraft/BBS757Definition.Readouts.cs`, `Aircraft/BBS757/Bbs757Annunciators.cs`, `Aircraft/BBS757/Bbs757DisplayReads.cs`, and in `Services/GeminiService.cs` only a new `Standby757` display type and its prompt; tests `Bbs757McpWindowTests.cs`, `Bbs757AnnunciatorsTests.cs`, `Bbs757DisplayReadsTests.cs`.
- Variables added: 3 (the squawk, TCAS mode and altimeter watches).

## Review Focus

- The pilot's own squawk or altimeter set is said once (its read-back), not again by the background row (Task 2 test).
- Ctrl+P's keys stay unique letters after the three new rows (Task 1 test).

---

### Task 1: Ctrl+P parity

**Files:** `Bbs757AutopilotLayout.cs`, `Forms/BBS757/BBS757AutopilotWindow.cs`, `BBS757Definition.Autopilot.cs`; test `Bbs757McpWindowTests.cs`.

**Interfaces:**
- After V/S: Speed Intervene (`B757_MCP_AP_SPD_HOLD_SW`, state as in plan 05's Task 1), A/T Disconnect (`B757_AT_DISC_1`, button, no state), Autopilot Disengage Bar (the map's node labelled "Autopilot Disengage Bar", Up / Down, flipped from the read value).
- Letter keys (`Bbs757AutopilotLayout.AccessKeyFor(string node)`): CMD L `L`, CMD C `C`, CMD R `R`, A/T Arm `A`, Captain F/D `F`, F/O F/D `O`, THR `T`, SPD `S`, LNAV `N`, VNAV `V`, FLCH `H`, HDG HOLD `D`, ALT HOLD `E`, V/S `Y`, LOC `G`, APP `P`, Speed Intervene `I`, A/T Disconnect `X`, Disengage Bar `B`.
- [ ] **Steps 1-5** (tests `Autopilot_rows_in_order` (the sixteen, then the three), `Autopilot_keys_unique_and_letters`; rewrite the moved row-list tests); commit `feat(b757): Ctrl+P gains Speed Intervene, A/T disconnect, the disengage bar and Alt keys`.

### Task 2: Squawk, TCAS mode and altimeter changes

**Files:** `Bbs757Annunciators.cs`, `BBS757Definition.Readouts.cs` (implements plan 01's `OnOwnSquawkSet`, `OnOwnAltimeterSet`); test `Bbs757AnnunciatorsTests.cs`.

- Rows: `BBS757_SQUAWK_WATCH` → stock `TRANSPONDER CODE:1` (BCO16), "Squawk": `Bbs757Squawk.Confirmation`'s words ("Squawk 4743", decoded with `Bbs757Squawk.Decode`); `BBS757_TCAS_MODE_WATCH` → `B757_PED_TCAS_MODE_KNOB`, "TCAS Mode": "TCAS STBY", "TCAS ALT RPTG OFF", "TCAS XPNDR", "TCAS TA ONLY", "TCAS TA/RA"; `BBS757_ALTIMETER_WATCH` → stock `KOHLSMAN SETTING MB:1`, "Altimeter": `AltimeterPhrase.Sentence`'s words, spoken once the value rests (a baro knob turning). A resting value is never delivered again (rows arrive only on change), so use plan 05's `Bbs757RestingValue` (`Changed`, `TakeDue`, `RestMs` 1500) with a UI-thread timer in `Readouts.cs` (stopped by `ResetReadoutBaselines`); that release runs outside MainForm's Ctrl+M wrap and drops the phrase when `BBS757_ALTIMETER_WATCH` is in `BBS757DisabledMonitorVariablesSet` ([VAR-8]). The squawk and TCAS rows speak inside the update, where the wrap mutes them.
- `OnOwnSquawkSet(code)` arms the squawk row's echo; `OnOwnAltimeterSet()` arms the altimeter row's; their own read-backs (plan-01-frozen paths) are the one confirmation. A TCAS pick on the panel is a pick (the combo is read): `IsPickPending` and the echo window cover it.
- [ ] **Steps 1-5** (tests `Squawk_change_from_elsewhere_is_spoken`, `Own_squawk_set_not_repeated`, `Tcas_mode_words`, `Altimeter_spoken_once_it_rests_without_another_delivery`, `Muted_altimeter_row_is_dropped`, `Own_altimeter_set_not_repeated`, de-DE); commit `feat(b757): squawk, TCAS mode and altimeter changes announced`.

### Task 3: Alt+I reads the standby instruments

**Files:** `Aircraft/BBS757/Bbs757DisplayReads.cs`, `Services/GeminiService.cs` (one `DisplayType` member `Standby757` and its prompt in `GetPromptForDisplay`, the MD-11 wording style); test `Bbs757DisplayReadsTests.cs`.

- A row: the standby-instruments action (the one PMDG 737 uses for Alt+I) → `Standby757`, "Standby instruments", instrument view index 1 (the EICAS view frames the standby attitude, altimeter and airspeed, measured). The prompt says to read only the standby attitude indicator, the standby altimeter (with its baro setting) and the standby airspeed indicator, in their own units, and to ignore the EICAS screens and the MCP in the same picture.
- [ ] **Steps 1-5** (tests: the row, the prompt names the 757 and the three instruments; follow `Bbs757DisplayReadsTests` and `DisplayPromptTests`); commit `feat(b757): Alt+I reads the standby instruments`.

## In-sim checks (owner)

1. Ctrl+P: Alt+I Speed Intervene, Alt+X A/T Disconnect, Alt+B Disengage Bar.
2. A squawk change from another tool or the panel's TCAS knobs → "Squawk …"; a baro knob turned in the cockpit → "Altimeter …" once it rests; Alt+I reads the standby instruments.

## Handoff to plan 16

- **Pilot-facing:** "757: Ctrl+P gains Speed Intervene, A/T Disconnect and the Disengage Bar, with Alt keys on every button; squawk, TCAS mode and altimeter changes are announced (each in Ctrl+M); Alt+I reads the standby instruments." Changelog `feature` / `improvement`.
- **Hotkey guide:** Ctrl+P, the Spoken-on-change list, Alt+I.

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
