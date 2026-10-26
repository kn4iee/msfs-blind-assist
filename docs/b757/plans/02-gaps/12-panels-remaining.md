# BlueBird 757 Remaining Panels Implementation Plan (plan 12, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The panel controls the map still leaves out become usable (pressurization auto rate, display brightness, the DDRMI switches, weather radar and pack temperature where a read shows their meaning), the spring-loaded trims and manual cabin altitude get buttons, and a live sweep checks every panel item in the sim.

**Architecture:** Generator curation for the controls a combo can drive (a new `direct_positions` key: positions written through the state var, no click code); app buttons for the spring-loaded knobs (hold off centre for a set time, always release, read the result); a C# console that writes every non-denied control through the app's own write planner and reads it back.

**Tech Stack:** Python 3 (`python -I`), unittest, C# 13 / .NET 10, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 3.7, 4 (R70); plan 02 (the map, the replay test) and plan 03 (the writers), whose files this plan takes over.

## Global Constraints

- Owned files (from plans 02 and 03): `tools/b757-gen/**` (the sweep tool in `tools/b757-gen/PanelSweep/`), `Resources/bbs757_control_map.json`, `Aircraft/BBS757/Bbs757ControlMap.cs`, `Aircraft/BBS757Definition.Overhead.cs`, `Aircraft/BBS757/Bbs757SpringControls.cs` (new), `Aircraft/BBS757/Bbs757Write.cs`, `Aircraft/BBS757/Bbs757Rpn.cs`; tests `Bbs757ControlMapTests.cs`, `Bbs757WriteTests.cs`, `Bbs757SpringControlsTests.cs` (new); `docs/b757/evidence/flight-12.md`.
- Needs the BlueBird package installed to regenerate the map.
- Never hand-edit `bbs757_control_map.json`; curation is keyed by node.
- Variables added: up to 8 (the spring buttons) plus the new map rows.

## Review Focus

- A spring knob's hold is always released, even across a profile switch or a lost link (Task 3 test).
- The sweep never writes a denied control (Task 2: the denylist is checked before any write, and `--dry-run` shows the plan).
- A `direct_positions` row never carries click code (Task 1 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/12-panels-remaining.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 12`, with the `Plan: 12` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The remaining controls a combo can drive

**Files:** generator (new curation key `direct_positions`: node → `{ "state_var", "positions": [[value, label] …] }`, emitted with no click code, so the generic path writes the state var), `curation.json`, the map; tests as Task 1.

- Pressurization Auto Rate (`B757_OVHD_AUTORATE_SWITCH`, 0-13 in tenths): 0 MIN, 3.5 INDEX, 13 MAX.
- Display brightness, each CRT knob (`B757_PFD_1_SCREEN_KNOB` state `BBS757_RASTER_BRIGHTNESS_CA`, `_2` `_FO`, `B757_EICAS_1/2_SCREEN_KNOB`, `B757_HSI_1/2_SCREEN_KNOB`, `B757_FMC_1/2_SCREEN_KNOB`): Off (0), Dim (the range's middle), Bright (its maximum), in a new panel "Display brightness" under Forward panel. (A dark display also defeats the AI display reads.)
- Weather radar left and right gain/tilt (`B757_PED_WX_L_LARGE_SEL_KNOB`, `_R_`, 0-20): name the positions only after an owner-in-sim read of what each value does (radar display through Alt+N); until then leave them out.
- DDRMI VOR/ADF switches (`BBS_DDRMI_LEFT`, `_RIGHT`, `_LEFT_FO`, `_RIGHT_FO`: 0 VOR, 1 ADF, BlueBird's list) as toggles written through their state var, in the DDRMI panels.
- Pack temperature selectors (`B757_OVHD_SEL_pack_Cockpt`, `_FWD_cab`, `_AFT_cab`, state `…_TEMP_SEL_KNOB_ANIM_FRAME`): only if an owner-in-sim read shows what the frames mean (temperature, AUTO); else leave them out and say so in the handoff.
- [ ] **Steps 1-5** (tests per added row); commit `feat(b757): auto rate, display brightness and the DDRMI switches on the panels`.

### Task 2: A live sweep of every panel item

**Files:** Create `tools/b757-gen/PanelSweep/` (a C# console project modelled on `tools/CDUTest`, built on its own, never part of the solution, CORE-4), `tools/b757-gen/PanelSweep/denylist.txt`, `tools/b757-gen/PanelSweep/README.md`.

- Connects through SimConnect and MobiFlight, loads the embedded map (link `Bbs757ControlMap.cs`, `Bbs757Write.cs`, `Bbs757Rpn.cs` as source), and for each control not denied: reads its state, writes each other position the way the app does (`Bbs757Write.ToPosition`, one call per step, 400 ms apart), reads back after 1.5 s, restores the original, and writes `sweep-<date>.tsv` (node, label, position, written, read back, verdict). `--panel <name>` limits it; `--dry-run` prints the plan only.
- `denylist.txt` (never written): fire handles and bottles, cargo fire, stab trim cutouts, battery and its guard, APU selector, IRS selectors, fuel control switches, engine start selectors, generator drive disconnects, RAT, passenger oxygen, emergency lights, the gear lever, the flap lever, the parking brake, the guards of all of these.
- README: owner present, on the ground, engines off, APU running; read the TSV; failures go into this plan's handoff (labels and positions for plan 16 to list, writer faults for a follow-up).
- [ ] Build it (`dotnet build tools/b757-gen/PanelSweep -c Debug -p:Platform=x64`), run `--dry-run`, commit `feat(b757): a live sweep of the 757's panel items`. The owner runs it once and attaches the TSV's failures to this plan's handoff.

### Task 3: Spring-loaded knobs: trims and the manual cabin altitude

**Files:** Create `MSFSBlindAssist/Aircraft/BBS757/Bbs757SpringControls.cs`; modify `BBS757Definition.Overhead.cs` (the buttons' variables and panels through plan 01's `AddOverheadVariables` / `AddOverheadPanels`, their presses through `HandleOverheadSet`); test `Bbs757SpringControlsTests.cs`.

**Interfaces:**
- Spring controls (rest value, left/down value, right/up value; the package audit's evidence: dropped rows under the banner "RUDDER TRIM_LIMIT" and `B757_OVHD_CABINALT_SWITCH`):
  | Key | L:var | Rest | Left / down | Right / up | Panel |
  |---|---|---|---|---|---|
  | `BBS757_RUDDER_TRIM` | `B757_PEDESTAL_RUDDER_TRIM_KNOB` | 1 | 0 | 2 | Stabilizer trim |
  | `BBS757_AILERON_TRIM` | `BBS_AILERON_TRIM_SWITCH` | 1 | 0 | 2 | Stabilizer trim |
  | `BBS757_ALTN_STAB_TRIM` | `BBS_ELEV_TRIM_ALTERNATE` | 1 | 0 | 2 | Stabilizer trim |
  | `BBS757_CABIN_ALT_MANUAL` | `B757_OVHD_CABINALT_SWITCH` | 4 | 0 | 7 | Pressurization |
  Which end is left/nose-down/descend is unmeasured: this task's first step measures each on the ground (rudder and aileron trim through stock `RUDDER TRIM PCT` / `AILERON TRIM PCT`, alternate stab trim through `ELEVATOR TRIM POSITION` with hydraulics, cabin altitude in MAN mode through the cabin altitude row) and records it in `flight-12.md`; the table is then fixed.
- Buttons per control: "Rudder Trim Left 1 s", "Rudder Trim Right 1 s", "Aileron Trim Left 1 s", "Aileron Trim Right 1 s", "Stab Trim Nose Down 1 s", "Stab Trim Nose Up 1 s" (alternate), "Cabin Climb 1 s", "Cabin Descend 1 s" (refused unless Mode Select reads MAN: "Set the pressurization mode to MAN first").
- `public static (string Hold, string Release) Codes(Bbs757SpringControl c, bool positive)`; the press writes `Hold`, waits 1000 ms (the release always runs, as `PressAsync`'s does), writes `Release`, then reads the matching readout and speaks it: "Rudder trim 2.5 units left" (from `RUDDER TRIM PCT` scaled per the in-sim measurement), "Aileron trim …", the stab trim through the existing trim call-out, "Cabin altitude …" from the existing cabin altitude row.

- [ ] **Step 1 (owner-in-sim):** measure each control's direction and the trim readouts' scale; record in `flight-12.md`.
- [ ] **Step 2: Write the failing tests:** codes for each control and direction (invariant); `Release_always_runs` (a canceled lifetime still writes the release); `Cabin_buttons_refused_outside_man`.
- [ ] **Steps 3-5:** implement, full suite, commit `feat(b757): trims and the manual cabin altitude from the panels`.

## In-sim checks (owner)

1. The new panel rows (Auto Rate, display brightness, DDRMI) move their controls; Rudder Trim Right 1 s → "Rudder trim … right"; Cabin Climb in MAN.
2. Run the sweep once and attach its failures to this plan's handoff.

## Handoff to plan 16

- **Pilot-facing:** "757: Auto rate, display brightness and the DDRMI switches on the panels; rudder, aileron and alternate stab trim buttons and manual cabin altitude climb and descend on their panels." Changelog `feature`.
- **docs/b757.md:** `direct_positions`, the spring buttons, the sweep tool.
- **The sweep's report:** failures to fix, with the control and what it read back.

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
