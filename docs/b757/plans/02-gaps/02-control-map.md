# BlueBird 757 Control Map Implementation Plan (plan 02, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fixes first: every 757 panel control reads with its real name and positions, the wrong labels and kinds are fixed, the emergency lights and the stab trim cutouts are guarded controls, the chronometers step the right way, controls the cockpit lacks are gone, and a test replays every control's click code. Then the essentials: the gear lever (with its OFF detent) and the flap lever on the panels, and Altitude Intervene kept or dropped per a flight check. (The remaining controls and the live sweep are plan 12, which takes these files over.)

**Architecture:** Curation and generator logic in `tools/b757-gen` (Python 3 stdlib) regenerate the embedded `MSFSBlindAssist/Resources/bbs757_control_map.json`; the C# side changes only to read it and in the map's own tests. The evidence for every name is BlueBird's published variable list (<https://support.bluebirdsim.com/docs/simvars>) or the click code itself, cited in the curation's `_notes`.

**Tech Stack:** Python 3 (`python -I`), unittest, C# xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#4, #5, #7), 3.3, 3.6, 3.7, 4 (R64, R65, R68); `docs/b757.md` "The control map".

## Global Constraints

- Needs the BlueBird package installed (the generator reads it): a contributor without it cannot regenerate the map.
- Never hand-edit `bbs757_control_map.json`: change `tools/b757-gen/curation.json` or the generator, then run `python -I tools/b757-gen/generate_b757_map.py`.
- `positions`, `labels` and the new keys are keyed by **node** (the control's `node`, e.g. `B757_MCP_AP_HDG_BNKLIM`), never its state var.
- A label never moves a control: names apply before `panel_order` / `control_order`.
- Panel names unique across sections; labels unique within a panel ([VAR-1]).
- `Bbs757RpnTests.Every_string_a_757_control_can_produce_stays_under_900_characters` keeps passing.
- Owned files (then passed to plan 12): `tools/b757-gen/**`, `Resources/bbs757_control_map.json`, `Aircraft/BBS757/Bbs757ControlMap.cs` (after plan 01), `tests/.../Bbs757ControlMapTests.cs`, `docs/b757/evidence/flight-02.md`.

## Review Focus

- No guarded control carries the vendor's gated click code (the old stab trim press began with the extractor's `[if …]` note), Task 3 test.
- Positions follow the control's real values in order, including non-contiguous ones (start selectors 1, 3, 4, 5, 6; warning indicators 0 and 11; chrono ET whatever the click code reaches), Task 1 test.
- A label fix never duplicates a label in its panel (the hydraulic pumps share one), Task 2 test.
- A variant suffix never doubles ("L Fuel Control (PW)" stays), Task 5 test.
- The gear lever's UP must never be sent on the ground by a test or the sweep (Task 7's in-sim check is airborne; plan 12's sweep denies it).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/02-control-map.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 02`, with the `Plan: 02` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: Name every position BlueBird names

**Files:** `tools/b757-gen/curation.json` (`positions`, `_notes`), the map; tests `tools/b757-gen/test_generate_b757_map.py`, `tests/MSFSBlindAssist.Tests/Bbs757ControlMapTests.cs`.

**Interfaces:** position labels by node (value → label), in the control's value order:
- `B757_IRS_CENTRE_KNOB`: 0 L, 1 C, 2 R. `B757_IRS_LARGE_KNOB`: 0 TK/GS, 1 PPOS, 2 WIND, 3 HDG/STS.
- `B757_OVHD_WIPER_SWITCH`: 0 OFF, 1 LOW, 2 HIGH.
- `B757_OVHD_SEATBELTS_SWITCH`, `B757_OVHD_NO_SMOKING_SWITCH`: 0 OFF, 1 AUTO, 2 ON.
- `B757_OVHD_MODE_SELECT_SWITCH`: 0 AUTO 1, 1 AUTO 2, 2 MAN.
- `B757_OVHD_SEL_L_PACK_SWITCH`, `B757_OVHD_SEL_R_PACK_SWITCH`: 0 OFF, 1 AUTO, 2 STBY N, 3 STBY C, 4 STBY W.
- `B757_CABIN_CEILING`: 0 Off, 1 33%, 2 66%, 3 100%. `B757_CABIN_WINDOW`, `B757_CABIN_ENTRY`: 0 Off, 1 50%, 2 100%.
- `B757_OVHD_WARNING_IND_SWITCH`: 0 DIM, 11 BRT.
- `B757_MCP_AP_HDG_BNKLIM` (Bank Limit, state `BBS757_BANK_LIMITER`): 0 AUTO, 1 5, 2 10, 3 15, 4 20, 5 25.
- `B757_MAIN_knob_gears_altn_flaps`: 0 NORM, 1 UP, 2 1, 3 5, 4 15, 5 20, 6 25, 7 30.
- `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR`: 0 OFF, 1 ILS, 2 BCRS.
- `B757_MAIN_CHRONO_ET_SWITCH`, `B757_MAIN_CHRONO_ET_SWITCH_FO` (state `BBS_CHRONO_LEFT_SWITCH[_FO]`): 0 RUN, 1 HOLD, RESET at the value the click code reaches past HOLD (BlueBird's list says 3 with 2 unused; the cockpit tooltip says 2: read the generated cw/ccw and name what it lands on; a value the code never reaches is left out).
- `B757_MAIN_CHRONO_SET_SWITCH`, `B757_MAIN_CHRONO_SET_SWITCH_FO` (state `BBS_CHRONO_RIGHT_SWITCH[_FO]`): 0 RUN, 1 HOLD, 2 Slow slew, 3 Fast slew.
- `B757_OVHD_ENG_START_SWITCH` (Ignition): 0 1, 1 2, 2 BOTH.
- `B757_OVHD_L_ENG_START_SWITCH`, `B757_OVHD_R_ENG_START_SWITCH`: 1 GND, 3 AUTO, 4 OFF, 5 CONT, 6 FLT.
- `B757_PED_TCAS_MODE_KNOB`: 0 STBY, 1 ALT RPTG OFF, 2 XPNDR, 3 TA ONLY, 4 TA/RA.
- `B757_EICAS_LWR_THRUST-REF_KNOB`: 0 L, 1 BOTH, 2 R. `B757_ECAM_LRAUTO_KNOB`: 0 L, 1 AUTO, 2 R.
- `B757_VC_P61_SWITCH_2` (Flight Recorder, state `BBS757_FLT_REC_SWITCH`): 0 ON, 1 AUTO, 2 TEST.
- The P61 test switches `B757_VC_P61_SWITCH_3` … `_10` (find each node's label in the map): 1 Neutral on all; 0 / 2 per BlueBird's list: Equip Cooling "Upper test" / "Lower test"; Yaw Damp "Upper test" / "Lower test"; Wing AI Window Heat "Wing anti-ice test" / "Probe heat test"; Ground Prox Hyd Gen "Ground proximity test" / "Hydraulic generator test"; TO and Land Config "Takeoff config test" / "Landing config test"; Air Data Stall Left and Right "Air data test" / "Stall warning test". Duct Leak Fuel Qty: 1 Neutral, 0 and 2 numbered.
- `_notes`: "Position names from BlueBird's simvar list (support.bluebirdsim.com/docs/simvars, values checked against source 2026-10-21), read 2026-10-25."

- [ ] **Step 1: Write the failing tests:** Python `test_bluebird_position_names` (every node above, exact `(value, label)` list). C# `Positions_have_bluebirds_names` (Mode Select AUTO 1 / AUTO 2 / MAN; left start selector values 1, 3, 4, 5, 6 = GND, AUTO, OFF, CONT, FLT; TCAS value 2 XPNDR; alternate flaps value 7 "30") and `No_position_reads_Position_n_except_the_unpublished` (only the Duct Leak Fuel Qty test switch keeps "Position n").
- [ ] **Step 2: Run** `python -I -m unittest tools/b757-gen/test_generate_b757_map.py` and `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757ControlMapTests`. Expected: FAIL.
- [ ] **Step 3: Edit the curation**, regenerate.
- [ ] **Step 4: Run both.** Expected: PASS.
- [ ] **Step 5: Commit** `fix(b757): every position BlueBird names, named`.

### Task 2: Wrong labels and kinds

**Files:** as Task 1.

**Interfaces** (node → label / kind / positions):
- `B757_OVHD_SWITCH_GAURD` → "Emergency Lights Guard" (Closed / Open).
- The four hydraulic electric pumps (read each node's XML name and BlueBird's list) → "C1 ELEC Pump", "C2 ELEC Pump", "L ELEC Pump", "R ELEC Pump"; engine pumps stay "L ENG HYD Pumps", "R ENG HYD Pumps"; Hydraulics `control_order` keeps R, C, L for the electric pumps.
- "Xfeed Pumps FWD / Aft" → "Crossfeed FWD", "Crossfeed Aft".
- `B757_GAURD_11_SWITCH`, `B757_GAURD_12_SWITCH` → the names of the switches they cover (from the XML), else "Fire Guard 1" / "Fire Guard 2" with a `_notes` line.
- The Autopilot Disengage Bar → 0 Up, 1 Down. Thrust Ref Pull → 0 In, 1 Pulled.
- `B757_MCP_AP_SPD_HOLD_SW` ("AP SPD Hold") → "Speed Intervene".
- The APU fire handle → kind `toggle` on its pulled-state L:var (the engine handles' pattern), positions In / Pulled, so the generic path reads it before pressing (today a second press stows it).
- The 14 audio mic selects → kind `button` (each press selects that microphone and clears the others; "Off" is unreachable).

- [ ] **Steps 1-5** as Task 1 (tests `test_label_corrections`, `test_labels_unique_per_panel`; C# `Labels_name_what_the_control_is`, `Apu_fire_handle_shows_its_state`); commit `fix(b757): labels and kinds that match the cockpit`.

### Task 3: Guarded controls

**Files:** `tools/b757-gen/generate_b757_map.py`, `curation.json` (new key `guarded`; `include_nodes`, `node_panels`, `control_order`), the map; tests as Task 1.

**Interfaces:**
- Consumes plan 01's `Bbs757Control.Guard`, `GuardClosed`, `CloseSets` (JSON `guard`, `guard_closed`, `close_sets`).
- `guarded`: node → `{ "state_var", "positions": [labels], "guard", "guard_closed", "close_sets" }`; emitted as kind `switch` with its state var, positions and guard fields and **no** `press`, `release`, `cw`, `ccw` (plan 03's writer opens the guard and writes the state var). The guard keeps its own row.
- Rows: `B757_OVHD_PASS_OXY_Toggle_Switch_CLICK` → "Emergency Lights", panel "Overhead miscellaneous", first in its `control_order`, the guard right after; 0 OFF, 1 ARMED, 2 ON; guard `B757_OVHD_SWITCH_GAURD`, closed 0, close sets 1. `B757_Stab_trim_C_toggle_switch_CLICK` → "Stab Trim C Cutout", 0 NORMAL, 1 CUTOUT, guard `B757_AISLE_STAND_STAB_1_GAURD_SWITCH`, closed 0, close sets 0; `B757_Stab_trim_R_toggle_switch_CLICK` → "Stab Trim R Cutout", guard `B757_AISLE_STAND_STAB_2_GAURD_SWITCH`.
- A check: any control whose press, release, cw or ccw starts with `[` and is not in `guarded` raises `CurationError`.

- [ ] **Steps 1-5** (tests `test_guarded_controls`, `test_gate_note_never_reaches_click_code`, `test_gated_control_outside_guarded_stops_the_run`; C# `Guarded_controls_carry_their_guard`); commit `fix(b757): emergency lights and stab trim cutouts as guarded controls`. Until plan 03's writer lands (an early plan like this one), a guarded pick finds no click code, writes the state var, and BlueBird's guard refuses it: the combo snaps back. Nothing is worse than before.

### Task 4: Chronometer ET steps the right way

**Files:** generator, `curation.json` (new key `reverse_knobs`: nodes whose clockwise code lowers the value), the map; Python tests.

- `reverse_knobs`: `B757_MAIN_CHRONO_ET_SWITCH`, `B757_MAIN_CHRONO_ET_SWITCH_FO`: the emitted `cw` is the vendor's anticlockwise code and `ccw` its clockwise code, so `Bbs757Rpn.StepTo` (cw goes up) lands where it aims; a `reverse_knobs` node whose emitted `cw` still contains `--` raises `CurationError`.
- [ ] **Steps 1-5** (test `test_chrono_et_cw_raises_the_value`); commit `fix(b757): chronometer ET steps toward the pick`.

### Task 5: Only controls the cockpit has

**Files:** generator (reads the glTF node names of `BBS_752_PW_INTERIOR.gltf` and `BBS_752_RR_INTERIOR.gltf`; stop with `CurationError` if either is missing), the map; tests as Task 1.

- `def cockpit_presence(node: str, pw: set[str], rr: set[str]) -> str` → `"both" | "pw" | "rr" | "none"`. A control in neither is dropped and reported (expected: the ADF switches, WX R Test, the P6 display and event buttons, guards 13-15, the P61 panel guard, `B757_EICAS_COMPUTER_KNOB`). In one model only → " (PW)" / " (RR)" appended unless the label already ends with it. Curation `keep_without_node` keeps a control BlueBird's list proves real (empty unless needed).
- [ ] **Steps 1-5** (tests `test_cockpit_presence`, `test_variant_suffix_not_doubled`, `test_absent_controls_dropped`; C# `No_control_without_a_cockpit_node`); commit `fix(b757): the map holds only controls the cockpit has`.

### Task 6: Replay every control's click code in a test

**Files:** Create `tools/b757-gen/rpn_replay.py` (a small RPN interpreter for the subset the cockpit code uses: numbers, `(L:…)` / `(>L:…)` with optional units, `(>K:…)` recorded and ignored, `!`, `+`, `-`, `++`, `--`, `min`, `max`, `==`, `<`, `>`, `if{ } els{ }`, `d`, `%`; `O:` and `E:` reads as 0, `O:` writes recorded); test `tools/b757-gen/test_generate_b757_map.py`.

- `def replay(code: str, lvars: dict[str, float]) -> dict[str, float]`.
- Test `test_every_control_replays_to_its_positions`, over the generated map: for each toggle, guard and select, pressing from each position lands on another listed position of its state var; for each switch with cw / ccw, each step from each position lands on the next or previous listed position or stays at an end; no press, cw or ccw fails to parse; guarded controls are skipped (they have no code). A control that fails is reported by node with the code, and the test fails.
- [ ] **Steps 1-5**; commit `test(b757): replay every panel control's click code`.

### Task 7: Gear lever and flap lever on the panels

**Files:** `curation.json` (`include_nodes`, `node_panels`, `positions`, `control_order`), the map; tests as Task 1.

**Interfaces:**
- "Gear Lever" (node `B757_MAIN_Landing_Gear_Y`), state var `B757_GEAR_HANDLE_POS` (BlueBird: "for hardware, use B757_GEAR_HANDLE_POS detents"), positions 0 UP, 0.5 OFF, 1 DOWN, no click code (the generic path writes the state var), first in the Forward panel's "Landing gear" panel. The after-takeoff checklist's "gear UP, then OFF" needs OFF, which the simulator's gear keys cannot select.
- "Flap Lever" (node of the flap handle, XML 32845), state var `B757_FC_FLAP_LEVER`, positions 0 UP, 17 1, 41 5, 67 15, 95 20, 125 25, 150 30 (BlueBird's list), no click code, in the Pedestal's "Control stand" panel after Speedbrake Up.

- [ ] **Steps 1-5** (C# `Gear_and_flap_levers_are_on_the_panels`); commit `feat(b757): gear lever with its OFF detent and the flap lever on the panels`.
- In-sim (owner, airborne after takeoff only): Gear Lever UP, then OFF; Shift+G says gear up; Flap Lever 5 → "Flaps 5".

### Task 8 [in flight]: Altitude Intervene, or not

- In flight (owner, VNAV climb or descent with an altitude constraint ahead on LEGS): press the map's "ALT SEL Push" (`BBS757_ALTSEL_CLICK`) and see whether the constraint goes or VNAV changes level; snapshot before and after (`docs/b757/tools/lvar-snapshot`). Record in `docs/b757/evidence/flight-02.md`.
- Works: label it "Altitude Intervene" (C# test). Does nothing: `exclude_nodes` with a `_notes` line citing `flight-02.md`, a test that it is absent, and a line in this plan's handoff for plans 05 and 16.
- [ ] **Steps 1-5**; commit `fix(b757): altitude selector push per the flight check`.

## In-sim checks (owner)

1. The fixes: Overhead miscellaneous "Emergency Lights: ARMED" and its guard Closed (preset Before start); Mode Select "AUTO 1"; TCAS Mode cycles STBY, ALT RPTG OFF, XPNDR, TA ONLY, TA/RA and back; the electric pumps read C1, C2, L and R ELEC Pump and switch the right pumps (Alt+S's hydraulic page).
2. The essentials: as in Task 7 (airborne).

## Handoff to plan 16

- **Pilot-facing:** the fixes "The 757's panels use BlueBird's names for every switch position (seat belts AUTO, TCAS XPNDR and TA ONLY, engine start CONT and FLT, pack STBY settings, bank limit, alternate flaps and more), the hydraulic pumps carry their right names, the emergency lights switch is on the Overhead miscellaneous panel, and switches the 757's cockpit does not have are gone" (changelog `fix`); the essentials "Gear lever (UP, OFF, DOWN) on the Landing gear panel and the flap lever on the Control stand" (`feature`).
- **docs/b757.md:** the control map section gains `guarded`, `reverse_knobs`, `keep_without_node`, cockpit presence, the gate-note check and the replay test; BlueBird's list as the naming source.
- **Proposed rule line:** `- [B757-6] A guarded 757 control (`guarded` in the curation, `Bbs757Control.Guard` in the app) carries no click code: its vendor click is gated on the guard, so the generator emits only the state var and the guard fields and stops on any `[if …]` gate note left in a press, release, cw or ccw. Full: docs/invariants/b757.md#b757-6`
- **Removed controls:** from Task 5's run report.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started
- **Claimed by:** none
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** (date; the `B757` commit you branched from)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase; generator tests too)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
