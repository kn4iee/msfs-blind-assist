# BlueBird 757 Autopilot Window and More Readouts Implementation Plan (plan 14, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

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

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/14-autopilot-window-and-readouts.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 14`, with the `Plan: 14` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

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
- **Ctrl+P keys for the guide:** CMD L Alt+L, CMD C Alt+C, CMD R Alt+R, A/T Arm Alt+A, Captain F/D Alt+F, F/O F/D Alt+O, THR Alt+T, SPD Alt+S, LNAV Alt+N, VNAV Alt+V, FLCH Alt+H, HDG HOLD Alt+D, ALT HOLD Alt+E, V/S Alt+Y, LOC Alt+G, APP Alt+P, Speed Intervene Alt+I, A/T Disconnect Alt+X, Disengage Bar Alt+B. No letter is underlined; the screen reader says each button's key ("Alt+E") on focus.
- **Spoken words:** "Squawk 4743"; "TCAS STBY", "TCAS ALT RPTG OFF", "TCAS XPNDR", "TCAS TA ONLY", "TCAS TA/RA"; "Altimeter: 1020, 30.12" or "Altimeter standard" (the captain's, once the knob stops). Ctrl+M rows "Squawk", "TCAS Mode", "Altimeter". Ctrl+P's Speed Intervene and A/T Disconnect say "… pressed".
- **For plan 13:** the TCAS mode knob is announced from any source here (`BBS757_TCAS_MODE_WATCH`); leave it out of the cockpit watcher, or it is said twice.
- **Proposed rule line** (`.claude/rules/b757.md`): `- [B757-n] Never register a second Continuous row on TRANSPONDER CODE:1: the squawk watch is the squawk field's read-back row itself (Bbs757Annunciators.SquawkWatchKey == Bbs757Squawk.CodeKey); two continuous rows of one name shift every later batch slot ([VAR-7]).` (VarNameCollisionTests already fails on it; the line says why the keys are one.)
- **Frozen files:** none needed.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** landed on `kn4iee/B757` (2026-10-26); waiting on owner (the in-sim checks)
- **Claimed by:** Daniel Wolak (danielw97), 2026-10-26
- **Worked by:** Daniel Wolak with Claude, in the main checkout (no subagents: a small plan)
- **Started:** 2026-10-26, from `69e2d8432` (the claim); the code from `64610de34`
- **Landed:** 2026-10-26, the commit carrying this record (code `0ee6fa9c8` … the squawk fix)
- **Tests at landing:** full suite passed, 17,942, after rebasing onto `kn4iee/B757` at `64610de34`
- **Tasks:** 1, 2 and 3 done; none left
- **Waiting on the owner:** in-sim checks 1 and 2
- **In-sim results:** none run
- **Changed from the plan:**
  - Task 1: ALT HOLD (E), V/S (Y) and LOC (G) hold no free letter of their own, so an underlined caption letter (the PMDG windows' `&` mnemonic) cannot carry them. The window handles Alt+letter itself (`ProcessCmdKey`) and each button reports its key as its accessible keyboard shortcut, so NVDA says "Alt+E". Letters as the plan gave them. `AccessKeyFor` is on `Bbs757AutopilotLayout`, a copy beside plan 05's on `Bbs757McpWindowLayout`. Speed Intervene and A/T Disconnect have no light, so a Ctrl+P press says "… pressed" (CORE-7, the panel's words); the disengage bar is a switch, read on request with the F/D switches.
  - Task 2: the squawk watch is NOT a new `BBS757_SQUAWK_WATCH` row. A second continuous `TRANSPONDER CODE:1` broke `VarNameCollisionTests` ([VAR-7]), so the transponder panel's read-back row `BBS757_SQUAWK` (`Bbs757Squawk.CodeKey`) now rides the batch and speaks. Variables added: 2, not 3 (`BBS757_TCAS_MODE_WATCH`, `BBS757_ALTIMETER_WATCH`). A TCAS pick on the panel is kept silent by linking the knob to its watch (`LinkedStatusKey`: echo armed, settled on the read-back). The altimeter's own-set check also uses plan 05's `_mcpSets` / `_ownSetEchoUntil` for `BBS757_ALTIMETER_SET`, so a long Ctrl+B set stays covered.
  - `Bbs757FlightReadinessTests` (06 → 14): two asserts that the squawk row was silent now say it speaks and has its Ctrl+M row.
- **Rulings made while working:** Ctrl+P keys not in the label are handled by the window and announced as shortcuts rather than reassigned (Daniel with Claude; the owner may prefer other letters)
