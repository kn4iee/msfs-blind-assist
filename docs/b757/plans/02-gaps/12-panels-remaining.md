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

Measured 2026-10-28 (`flight-12.md`) and read from the package's code (the XML and BlueBird's WASM), so the rows are fixed:

- Pressurization Auto Rate (node and state `B757_OVHD_AUTORATE_SWITCH`, label "Auto Rate", Pressurization panel): 0 MIN, 3.5 INDEX, 13 MAX. 3.5 is the cockpit's load value; BlueBird's code uses the whole number (3: a climb limit of 500 ft/min and a descent limit of 300; 0 is 50 and 30, 13 is 2,000 and 1,200), so INDEX at 3.5 shows the load value as INDEX.
- Display brightness, eight knobs in a new panel "Display brightness" under Forward panel, each written through the L:var BlueBird's brightness code reads (output = knob ÷ 40 × 1.5, never below 0.1; measured): positions 0 Minimum, 20 Half, 40 Full (0 dims a display to its minimum, it does not turn it off, so no "Off"; 40 is every knob's load value; the cockpit's mouse stops lower, at 30 or 20, but the code takes 0-40). Node → state var → label: `B757_ADI_1_LCD_KNOB` → `B757_PFD_1_SCREEN_KNOB` → Captain PFD; `B757_ADI_2_LCD_KNOB` → `B757_PFD_2_SCREEN_KNOB` → First Officer PFD; `B757_HSI_1_SCREEN_KNOB` → itself → Captain ND; `B757_HSI_2_SCREEN_KNOB` → First Officer ND; `B757_EICAS_1_SCREEN_KNOB` → Upper EICAS; `B757_EICAS_2_SCREEN_KNOB` → Lower EICAS; `B757_FMC_1_SCREEN_KNOB` → Captain CDU; `B757_FMC_2_SCREEN_KNOB` → First Officer CDU. The nodes named `B757_PFD_1/2_SCREEN_KNOB` stay out: their click writes `BBS757_RASTER_BRIGHTNESS_CA/_FO`, which nothing in the package reads. (A dark display also defeats the AI display reads.)
- Weather radar knobs (`B757_PED_WX_L/R_LARGE_SEL_KNOB`, `_SMALL_SEL_KNOB`): left out. Nothing in the package reads them (no gauge, no WASM code: the ND draws no weather radar), so no position has a meaning to name.
- DDRMI VOR/ADF switches: no change. The map already holds all four as toggles on `BBS_DDRMI_LEFT`, `_RIGHT`, `_LEFT_FO`, `_RIGHT_FO` (0 VOR, 1 ADF), and the click code works through the calculator, the F/O right one's stray `}` included (measured).
- Zone temperature selectors, in Air conditioning and bleed: `B757_OVHD_SEL_pack_Cockpt` → `B757_OVHD_FLT_DK_TEMP_SEL_KNOB_FRAME` → Flight Deck Temperature; `B757_OVHD_SEL_pack_FWD_cab` → `B757_OVHD_FWD_CAB_TEMP_SEL_KNOB_FRAME` → Forward Cabin Temperature; `B757_OVHD_SEL_pack_AFT_cab` → `B757_OVHD_AFT_CAB_TEMP_SEL_KNOB_FRAME` → Aft Cabin Temperature (the `…_ANIM_FRAME` var is derived from the knob frame every tick and must not be written). The readback `B757_OVHD_<zone>_ZONE_TEMP_SEL` is °C = 18 + (frame − 5) × 6 / 13 between frames 5 and 31 (frames 3-6 read 18, 30-33 read 30, 0-2 and 34-35 OFF; there is no AUTO). Positions, the frame nearest each whole degree: 0 OFF, 5 18 °C, 7 19 °C, 9 20 °C, 12 21 °C, 14 22 °C, 16 23 °C, 18 24 °C, 20 25 °C, 22 26 °C, 25 27 °C, 27 28 °C, 29 29 °C, 31 30 °C.
- The manual cabin altitude switch (`B757_OVHD_CABINALT_SWITCH`) stays excluded from the map: it is spring-loaded, and Task 3 gives it buttons; its curation note says so.
- Fix first: the Status panel's "Cabin Altitude" row reads `B757_DEBUG_PRESSURE_CABIN_ALT_FT`, which BlueBird's code declares and never writes (it reads 0); its key becomes `B757_TEST_CABIN_ALT`, the cabin altitude in feet BlueBird's WASM writes every tick (1,161 ft on the ground, measured).
- [ ] **Steps 1-5** (tests per added row); commit `feat(b757): auto rate, display brightness and the DDRMI switches on the panels`.

### Task 2: A live sweep of every panel item

**Files:** Create `tools/b757-gen/PanelSweep/` (a C# console project modelled on `tools/CDUTest`, built on its own, never part of the solution, CORE-4), `tools/b757-gen/PanelSweep/denylist.txt`, `tools/b757-gen/PanelSweep/README.md`.

- Connects through SimConnect and MobiFlight, loads the embedded map (link `Bbs757ControlMap.cs`, `Bbs757Write.cs`, `Bbs757Rpn.cs` as source), and for each control not denied: reads its state, writes each other position the way the app does (`Bbs757Write.ToPosition`, one call per step, 400 ms apart), reads back after 1.5 s, restores the original, and writes `sweep-<date>.tsv` (node, label, position, written, read back, verdict). `--panel <name>` limits it; `--dry-run` prints the plan only.
- Which controls: the positional ones (toggle, switch, select, guard) with a state var and two or more positions. Buttons and held buttons are listed in the TSV as "skipped: button" (a press has no position to read back), and a guarded control and its guard as "skipped: guarded" (their writes need the app's guard sequence, `SetGuardedAsync`, which the tool does not link).
- Reads go through SimConnect data definitions of the L:vars (as the app reads them); writes go through the MobiFlight calculator as one-shot commands (as the app's `ExecuteCalculatorCodeUnique` sends them), never as an expression registered for reading: MobiFlight polls a registered expression, so a write inside one runs again on every poll (measured with the simconnect MCP, `flight-12.md`).
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
  Measured 2026-10-28 (Step 1, `flight-12.md`) and read from BlueBird's code, so the table is fixed: 0 is left, nose down and descend, 2 (7) is right, nose up and climb, as BlueBird's list says. The cabin switch's code uses only the side of 4 (5-8 all climb, 0-3 all descend), and 7 is what the cockpit's own click writes. Nothing returns a trim switch written 0 or 2 to 1 (only the cockpit's mouse release does), so a release that is lost leaves the trim running to its stop (measured twice: the rudder trim ran to −17 and to +16.7 units); the release is verified (below). The cabin switch is the other way round: its XML writes 4 back within about a second of an outside write, so the press writes `Hold` again every 500 ms while it holds. Rates (BlueBird's code; measured close): rudder 0.85 units/s (−17 to +17), aileron 1 unit/s (−11.6 to +11.6, no readable units), stabilizer 0.25 units/s at or below 100 kt (0.10 at 350 kt), the outflow valve's target 10 % per second; the alternate stab trim also needs hydraulic pressure and its cutouts NORMAL, so with neither it moves nothing (the readout then says the unchanged trim).
- Buttons per control: "Rudder Trim Left 1 s", "Rudder Trim Right 1 s", "Aileron Trim Left 1 s", "Aileron Trim Right 1 s", "Stab Trim Nose Down 1 s", "Stab Trim Nose Up 1 s" (alternate), "Cabin Climb 1 s", "Cabin Descend 1 s" (refused unless Mode Select reads MAN: "Set the pressurization mode to MAN first").
- `public static (string Hold, string Release) Codes(Bbs757SpringControl c, bool positive)`; the press writes `Hold`, writes it again every 500 ms, after 1000 ms in all (the release always runs, as `PressAsync`'s does) writes `Release`, then reads the switch back and, while it does not read its rest value, writes `Release` again (at most 3 writes in all; still off rest: "{name} switch did not return, check it"). Then, about 500 ms after the release (the readouts lag it), it reads the matching readout and speaks it:
  - rudder: `L:B757_PED_RUDDER_TRIM_INDICATOR` (units, −17 to +17, positive right; stock `RUDDER TRIM PCT` does not move): "Rudder trim 0.9 units right", "… left", "Rudder trim centered" under 0.05 units; one decimal, invariant;
  - aileron: `L:B757_FC_YOKE` (the yoke's rotation, 1.5 neutral, larger is right; stock `AILERON TRIM PCT` stays 0 and BlueBird publishes no aileron trim units): direction only, "Aileron trim right", "… left", "Aileron trim centered" within 0.01 of 1.5;
  - stab trim: the existing trim call-out's phrase (`Bbs757Readouts.StabTrimUnits` of stock `ELEVATOR TRIM POSITION`, `StabTrimUnitsCallout.Phrase`: "Trim 5.3");
  - cabin: the outflow valve, `L:BBS_CABIN_VALVE` (0 closed, 1 fully open): "Outflow valve 87 percent open" (the cabin altitude does not move on the ground, and the valve is what MAN drives).

- [x] **Step 1 (owner-in-sim):** measured 2026-10-28 by the controller with the owner's go-ahead, through the simconnect MCP; recorded in `flight-12.md`.
- [ ] **Step 2: Write the failing tests:** codes for each control and direction (invariant); `Release_always_runs` (a canceled lifetime still writes the release); `Release_is_resent_until_the_switch_rests` (a switch that still reads its hold value gets the release again, at most 3 writes, then the "did not return" phrase); `Cabin_buttons_refused_outside_man`; the four readout phrases, under de-DE as well (CLAUDE.md).
- [ ] **Steps 3-5:** implement, full suite, commit `feat(b757): trims and the manual cabin altitude from the panels`.

### Task 4: Every panel's controls in daily-use order (added 2026-10-28, the owner's instruction)

The owner: "The panel items should be ordered where most frequently every day items are listed first in tab order."

**Files:** `tools/b757-gen/curation.json` (`control_order`, and `panel_order` for the new Display brightness panel only), the map (regenerated); no generator code.

- The order, panel by panel, with each placement's tier and source, is the research in this plan's workspace (`order_data.py`, from BlueBird's User Procedures and Normal Checklist v1.01): tier 1, what the normal procedures move on every flight, in the order the flow first uses it, L/R pairs together; tier 2, set once in preflight, tests, display options; tier 3, non-normal, emergency, maintenance, with a guard just before the switch it covers (the emergency lights guard stays after its switch, which the app opens itself). Panels the research leaves unchanged keep their order.
- Only the controls inside a panel move. The panels of a section keep their order; the new Display brightness panel goes last in Forward panel (set once in preflight).
- App rows are not map nodes and are placed by their own code: the landing altitude field stays first in Pressurization; Task 3 puts the trim buttons first in Stabilizer trim (rudder, aileron, then the alternate stab trim) and the cabin buttons last in Pressurization.
- [ ] Regenerate, check every panel of the map against the research's order (a script in the workspace), run the generator tests and the full suite, commit `feat(b757): every panel's every-day controls first`.

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

- **Status:** claimed
- **Claimed by:** kn4iee (the owner), 2026-10-28
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** 2026-10-28; branched from `B757` at f12d85afa
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase; generator tests too)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
