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

- [x] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/12-panels-remaining.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [x] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 12`, with the `Plan: 12` trailer.
- [x] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

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
- [x] **Steps 1-5** (tests per added row); commit `feat(b757): auto rate, display brightness and the zone temperatures on the panels` (DDRMI needed no change).

### Task 2: A live sweep of every panel item

**Files:** Create `tools/b757-gen/PanelSweep/` (a C# console project modelled on `tools/CDUTest`, built on its own, never part of the solution, CORE-4), `tools/b757-gen/PanelSweep/denylist.txt`, `tools/b757-gen/PanelSweep/README.md`.

- Connects through SimConnect and MobiFlight, loads the embedded map (link `Bbs757ControlMap.cs`, `Bbs757Write.cs`, `Bbs757Rpn.cs` as source), and for each control not denied: reads its state, writes each other position the way the app does (`Bbs757Write.ToPosition`, one call per step, 400 ms apart), reads back after 1.5 s, restores the original, and writes `sweep-<date>.tsv` (node, label, position, written, read back, verdict). `--panel <name>` limits it; `--dry-run` prints the plan only.
- Which controls: the positional ones (toggle, switch, select, guard) with a state var and two or more positions. Buttons and held buttons are listed in the TSV as "skipped: button" (a press has no position to read back), and a guarded control and its guard as "skipped: guarded" (their writes need the app's guard sequence, `SetGuardedAsync`, which the tool does not link).
- Reads go through SimConnect data definitions of the L:vars (as the app reads them); writes go through the MobiFlight calculator as one-shot commands (as the app's `ExecuteCalculatorCodeUnique` sends them), never as an expression registered for reading: MobiFlight polls a registered expression, so a write inside one runs again on every poll (measured with the simconnect MCP, `flight-12.md`).
- `denylist.txt` (never written): fire handles and bottles, cargo fire, stab trim cutouts, battery and its guard, APU selector, IRS selectors, fuel control switches, engine start selectors, generator drive disconnects, RAT, passenger oxygen, emergency lights, the gear lever, the flap lever, the parking brake, the guards of all of these.
- README: owner present, on the ground, engines off, APU running; read the TSV; failures go into this plan's handoff (labels and positions for plan 16 to list, writer faults for a follow-up).
- [x] Build it (`dotnet build tools/b757-gen/PanelSweep -c Debug -p:Platform=x64`), run `--dry-run`, commit `feat(b757): a live sweep of the 757's panel items`.
- [x] **[owner]** Run it once and attach the TSV's failures to this plan's handoff (in-sim check 6; run 2026-10-28 with the owner present, the failures under "The sweep's report").

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
- Buttons per control, the side first (VAR-1; ruled while working, see the record): "Left Rudder Trim 1 s", "Right Rudder Trim 1 s", "Left Aileron Trim 1 s", "Right Aileron Trim 1 s", "Nose Down Stab Trim 1 s", "Nose Up Stab Trim 1 s" (alternate), "Climb Cabin 1 s", "Descend Cabin 1 s" (refused unless Mode Select reads MAN: "Set the pressurization mode to MAN first").
- `public static (string Hold, string Release) Codes(Bbs757SpringControl c, bool positive)`; the press writes `Hold`, writes it again every 500 ms, after 1000 ms in all (the release always runs, as `PressAsync`'s does) writes `Release`, then reads the switch back and, while it does not read its rest value, writes `Release` again (at most 3 writes in all, each read back 250 ms after it; still off rest: "{name} switch did not return; the trim may keep running", for the cabin switch "Cabin altitude switch did not return; check the outflow valve", then the release is retried in the background every 2 s for a minute, and "{name} switch returned" if it comes back). Then, about 500 ms after the release (the readouts lag it), it reads the matching readout and speaks it:
  - rudder: `L:B757_PED_RUDDER_TRIM_INDICATOR` (units, −17 to +17, positive right; stock `RUDDER TRIM PCT` does not move): "Rudder trim 0.9 units right", "… left", "Rudder trim centered" under 0.05 units; one decimal, invariant;
  - aileron: `L:B757_FC_YOKE` (the yoke's rotation, 1.5 neutral, larger is right; stock `AILERON TRIM PCT` stays 0 and BlueBird publishes no aileron trim units): direction only, "Aileron trim right", "… left", "Aileron trim centered" within 0.01 of 1.5;
  - stab trim: the existing trim call-out's phrase (`Bbs757Readouts.StabTrimUnits` of stock `ELEVATOR TRIM POSITION`, `StabTrimUnitsCallout.Phrase`: "Trim 5.3");
  - cabin: the outflow valve, `L:BBS_CABIN_VALVE` (0 closed, 1 fully open): "Outflow valve 87 percent open" (the cabin altitude does not move on the ground, and the valve is what MAN drives).

- [x] **Step 1 (owner-in-sim):** measured 2026-10-28 by the controller with the owner's go-ahead, through the simconnect MCP; recorded in `flight-12.md`.
- [x] **Step 2: Write the failing tests:** codes for each control and direction (invariant); `Release_always_runs` (a canceled lifetime still writes the release); `Release_is_resent_until_the_switch_rests` (a switch that still reads its hold value gets the release again, at most 3 writes, then the "did not return" phrase); `Cabin_buttons_refused_outside_man`; the four readout phrases, under de-DE as well (CLAUDE.md).
- [x] **Steps 3-5:** implement, full suite, commit `feat(b757): trims and the manual cabin altitude from the panels`.

### Task 4: Every panel's controls in daily-use order (added 2026-10-28, the owner's instruction)

The owner: "The panel items should be ordered where most frequently every day items are listed first in tab order."

**Files:** `tools/b757-gen/curation.json` (`control_order`, and `panel_order` for the new Display brightness panel only), the map (regenerated); no generator code.

- The order, panel by panel, with each placement's tier and source, is the research in this plan's workspace (`order_data.py`, from BlueBird's User Procedures and Normal Checklist v1.01): tier 1, what the normal procedures move on every flight, in the order the flow first uses it, L/R pairs together; tier 2, set once in preflight, tests, display options; tier 3, non-normal, emergency, maintenance, with a guard just before the switch it covers (the emergency lights guard stays after its switch, which the app opens itself). Panels the research leaves unchanged keep their order.
- Only the controls inside a panel move. The panels of a section keep their order; the new Display brightness panel goes last in Forward panel (set once in preflight).
- App rows are not map nodes and are placed by their own code: the landing altitude field stays first in Pressurization; Task 3 puts the trim buttons first in Stabilizer trim (rudder, aileron, then the alternate stab trim) and the cabin buttons last in Pressurization.
- [x] Regenerate, check every panel of the map against the research's order (a script in the workspace), run the generator tests and the full suite, commit `feat(b757): every panel's every-day controls first`.

### Task 5: The cockpit tests behave as the cockpit's (added 2026-10-28, the owner's instruction)

The owner: "Test in sim and revise accordingly." Measured in flight the same day (`flight-12.md`, "The fire test, the P61
test switches and the master lights"): the fire, CVR and squib tests show only while their button is held, the app's
0.2 s press gives a blip at most; the P61 test switches return to neutral by themselves at once, so one write runs no
test, and a test runs only while its position is written again about every 30 ms; the lamp test toggles; the master
warning and caution buttons work (one press clears both sides and silences the bell) and stay as they are.

**Task 5a (map).** Files: generator, `curation.json`, the map, `test_generate_b757_map.py`, `Bbs757ControlMapTests.cs`.
- A new curation key `hold_ms` (node → milliseconds) makes a button the rules keep a held button (kind `held`) with that
  hold, its press and release unchanged. Checked like the other keys (a node the package lacks, a control that is not a
  button with both a press and a release, a node also under kinds, a hold under 500 or over 10,000 ms: errors).
- Held 3,000 ms: the fire tests (`B757_PED_FIRE_ENG_BUTTON`, `B757_PED_FIRE_CARGO_BUTTON`, `B757_PED_FIRE_WHLWELL_BUTTON`),
  the CVR test (`B757_OVHD_SW_CVR_TEST`) and the squib test (`B757_P6_SQUIBS_BUTTON2`), measured; and, the same for one
  rule (a test is held until it shows), the TCAS test (`B757_PED_TCAS_MODE_KNOB_TEST_CLICK`), the takeoff check
  (`B757_MAIN_TOCHK_BUTTON`), the four oxygen tests (`OXY_CAPT_TEST_SWITCH001`, `OXY_FO_TEST_SWITCH1`,
  `OXY_OBSV_TEST_SWITCH`, `OXY_P61_TEST_SWITCH`), the autoland status tests (`B757_MAIN_L/R_AUTOLAND_1/2_BUTTON`, not the
  resets), the weather radar test (`B757_PED_WX_L_TEST_BUTTON`), the ILS test (`B757_PED_ILS_TEST_BUTTON`) and the three
  radio panel tests (`B757_PED_NAV_1/2/3_TEST_BUTTON`). Their labels stay as they are; the hold and the
  release go through the app's existing held-button path (`Bbs757Write.PressFor`, `PressAsync`).
- The eight P61 test switches (`B757_VC_P61_SWITCH_3` to `_10`) leave the map (`exclude_nodes`, with a note: they are
  spring-loaded with an instant return, Task 5b gives them buttons). Their state vars stay readable by the app (5b).

**Task 5b (app).** Files: `Bbs757SpringControls.cs`, `BBS757Definition.Overhead.cs`, `Bbs757SpringControlsTests.cs`
(and `Bbs757LandingAltitudeTests.cs` only if a list it pins changes).
- The spring controls gain a hold length and a re-write gap per control: the trims and the cabin switch as today
  (1,000 ms, 500 ms); the P61 tests 3,000 ms, re-written every 30 ms (measured: 30 ms holds steadily, 100 ms drops out).
  One button per test position, released to neutral (1) with the same verified release; after the release the press
  says "{test} done" (the lights and warnings it raised are the cockpit watcher's to say, plan 13).
- Buttons, in the P61 panel where each switch was, the discriminator first: Takeoff Config Test 3 s (`BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW` 0),
  Landing Config Test 3 s (2); Left Air Data Test 3 s (`BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW` 0), Left Stall Warning Test 3 s (2);
  Right Air Data Test 3 s, Right Stall Warning Test 3 s (`…_RIGHT_…`); Ground Proximity Test 3 s
  (`BBS757_GRNDPROX_HYDGEN_P61_TEST_SW` 0), Hydraulic Generator Test 3 s (2); Upper Equipment Cooling Test 3 s
  (`BBS757_EQUIP_COOLING_P61_TEST_SW` 0), Lower Equipment Cooling Test 3 s (2); Upper Yaw Damper Test 3 s
  (`BBS757_YAW_DAMP_P61_TEST_SW` 0), Lower Yaw Damper Test 3 s (2); Upper Duct Leak Fuel Quantity Test 3 s
  (`BBS757_DUCT_LEAK_FUEL_QTY_P61_TEST_SW` 0), Lower Duct Leak Fuel Quantity Test 3 s (2; BlueBird names neither position);
  Wing Anti-ice Test 3 s (`BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW` 0), Probe Heat Test 3 s (2).
- The lamp test (the map's Lamp Test, formerly Warning Test, `B757_OVHD_WARNING_TEST_BUTTON`) is pressed as today, then read back:
  `L:OVHD_TEST_LTS` 1 says "Lamp test on", 0 "Lamp test off" (it latches: a second press turns it off).
- [x] 5a: tests first, then the curation and generator, regenerate, full suite, commit `feat(b757): the cockpit test buttons hold as the cockpit's`.
- [x] 5b: tests first (the P61 hold re-written at 30 ms for 3 s, released to 1; "{test} done"; the lamp test's two phrases), full suite, commit `feat(b757): the P61 tests and the lamp test from their buttons`.

### Task 6: The tests say what they test (added 2026-10-28, the owner's instruction)

The owner: "The tests are not clearly labeled." The labels, by curation `labels`, from BlueBird's procedures where they
name the test (User Procedures: "FIRE/OVHT - ENG", "FIRE/OVHT - ENG/APU/CARGO", "FIRE/OVHT - WHL WELL", "SQUIB TEST",
"ASA TEST 1 … TEST 2", "Press the CONFIG (T/O CHK) button near RECALL") and from the measured effect (flight-12.md):
- `B757_PED_FIRE_ENG_BUTTON` Engine Fire Test; `B757_PED_FIRE_CARGO_BUTTON` Engine, APU and Cargo Fire Test;
  `B757_PED_FIRE_WHLWELL_BUTTON` Wheel Well Fire Test; `B757_P6_SQUIBS_BUTTON2` Squib Test;
  `B757_OVHD_WARNING_TEST_BUTTON` Lamp Test; `B757_PED_TCAS_MODE_KNOB_TEST_CLICK` TCAS Test;
  `B757_PED_WX_L_TEST_BUTTON` Weather Radar Test; `B757_PED_NAV_1/2/3_TEST_BUTTON` Radio Test (one per radio tuning panel);
  `B757_MAIN_L/R_AUTOLAND_1_BUTTON` Autoland Status Test 1, `_2_` Autoland Status Test 2, `_3_` Autoland Status Reset;
  `OXY_OBSV_TEST_SWITCH` First Observer Oxygen Test, `OXY_P61_TEST_SWITCH` Second Observer Oxygen Test;
  `B757_MAIN_TOCHK_BUTTON` Takeoff Config Check, and it leaves `hold_ms` (BlueBird: "press and release"), a plain button again.
- Added later the same day (the owner): the Cockpit Door's positions (`B757_COCKPIT_DOOR`, Other > Flight deck door) read
  Closed (0) and Open (1) instead of Off and On (BlueBird's XML: the door animation runs from 0 to 1, a disabled block names
  0 closed and the full value open, and an open door on the ground lights the lock-fail light).
- [x] The door's positions: tests first, regenerate, commit `fix(b757): the cockpit door says closed or open`.
- [x] Tests first, regenerate, full suite, commit `fix(b757): the cockpit tests say what they test`.

### Task 7: The panels follow the cockpit (added 2026-10-28, the owner's decision)

The owner asked whether the panels put each control where it is in the cockpit or are made up, "other than places
where it makes sense such as engine fuel and start levers being together"; the audit (`docs/b757/evidence/panel-audit-12.md`:
every map node placed by its 3D position in BlueBird's interior models and named from the Flight Guide's cockpit
pictures) found 34 of 55 panels real, 7 sensible groupings and the rest code buckets or misplaced. The owner chose
"Follow the cockpit", the brightness knobs on their real panels and the fire tests kept with the fire handles.

**Files:** `curation.json` (`panels`, `node_panels`, `panel_order`, `control_order`, `labels` where a panel move needs
a clearer label), the map, `test_generate_b757_map.py`, `Bbs757ControlMapTests.cs`; `Bbs757SpringControls.cs`,
`BBS757Definition.Overhead.cs`, `Bbs757SpringControlsTests.cs` (the trim buttons' panel only).
- The structure is the audit's "Proposed structure" (every map node placed once; sections and panel names as there),
  with the owner's answers: the fire and overheat tests stay in Fire protection after the handles (not a panel of
  their own); the eight brightness knobs go to their real panels (Captain EFIS: PFD Brightness and ND Brightness;
  First officer EFIS the same; EICAS control: Upper EICAS Brightness, Lower EICAS Brightness; a "CDU brightness" pair
  under Pedestal: Captain CDU, First Officer CDU), so the Display brightness panel goes. The audit's other open
  questions take its proposals (Equipment cooling its own panel, G/S inhibit with caution cancel and recall, each glareshield
  clock switch with its pilot's chronometer, the flight deck door alone, IRS one panel, the panels of a section in
  cockpit order, the section name Other kept).
- Inside each panel the controls keep Task 4's every-day-first order: a panel's controls are ordered by their place in
  Task 4's order (its tiers), each guard just before the switch it covers (the emergency lights guard after its switch,
  as before); a control whose panel is new takes its place by its Task 4 tier.
- The app's rudder and aileron trim buttons and their readout rows move to a new "Rudder and aileron trim" panel
  (Pedestal, at the aft aisle stand); the alternate stab trim buttons stay in Stabilizer trim.
- Code that names panels by string keeps working: no panel it names is renamed (`TransponderPanel`, `McpPanel`,
  `EnginesPanel`, `GroundPowerPanel` "Electrical", `ElectricalPanel`, `PressurizationPanel`, P61 panel).
- [x] Tests first, then curation, regenerate, full suite, commit `feat(b757): the panels follow the cockpit`.

### Task 8: Only the loaded engine type's controls (added 2026-10-28, the owner's decision)

The owner: the Engines panel "seems to show both and looks very cluttered". The sim's title names the variant
("BlueBird 757-200 RR", read 2026-10-28). The owner approved changing the 757 profile's core file
(`BBS757Definition.cs`, frozen by plan 01) for it.

**Files:** the generator (each control gets `variant`: "pw", "rr" or null, from the cockpit presence the run already
reads), the map, `test_generate_b757_map.py`, `Bbs757ControlMap.cs` (the field), `BBS757Definition.cs` (the filter),
tests `Bbs757ControlMapTests.cs` and a new `Bbs757VariantTests.cs`.
- The variant comes from the aircraft's title: " PW" or " RR" in it, case-insensitive; anything else, or no title yet,
  is unknown, and an unknown variant shows every control (as today).
- The panels leave out the other variant's controls (today: the PW and RR fuel control switches, the RR electronic
  engine control switches and their guards). When the variant becomes known or changes (a flight or aircraft load,
  a reconnect), the section lists are rebuilt in place (a panel the variant empties drops out), and each panel's
  controls are decided from the live title when the panel is built (`IsPanelControlVisible`), so the next panel shown is
  right; nothing else (variables, writes) changes. Right after a fresh connection the profile has no title until the
  first panel is built, so a PW's first section list can still name the RR-only panel: a MainForm call at connect and
  switch (as the MD-11's `Attach`) would close that, outside plan 12's files (handoff).
- [x] Tests first (the title parsing, the filter, an unknown variant showing all, a change of variant), full suite,
  commit `feat(b757): the panels show the loaded engine type's controls only`.

### Task 9: The panel lists follow the preflight flow (added 2026-10-28, the owner's instruction)

The owner asked whether the panel lists follow the preflight and checklist flow (ruling R37 ordered them by the manual's
preflight flow; Task 7's split panels took their source's place in cockpit order) and said "reorder".

**Files:** `curation.json` (`panel_order` only), the map, `test_generate_b757_map.py`, `Bbs757ControlMapTests.cs`.
- Each section's panels are ordered by where BlueBird's Simplified Procedures (BlueBird_757_User_Procedures_v1.01.pdf,
  PDF pages 34-62, cold and dark to securing) first act on or check one of the panel's controls: the page, then the
  line on that page. A panel the flow never touches follows the ones it does, in its present order.
- The Pedestal keeps its start (Control stand, Engines, TCAS and transponder), which plan 01's frozen test pins.
- The derivation (each panel's first mention, page and line, with the quoted words) is a script in this plan's
  workspace, and its table goes into `docs/b757/evidence/panel-order-12.md` as a short section.
- [x] Tests first, regenerate, full suite, commit `feat(b757): the panel lists follow the preflight flow`.

### Task 10: The Engines panel, engine by engine (added 2026-10-28, the owner's instruction)

The owner: "I wanted engine 1 start then fuel, then engine 2's controls. Right now they have the start levers, then the
fuel levers." `control_order["Engines"]`: Ignition (both engines), L ENG Start, L Fuel Control (PW), L Fuel Control (RR),
R ENG Start, R Fuel Control (PW), R Fuel Control (RR) — the loaded type's pair shows (Task 8), so on an RR: Ignition,
L ENG Start, L Fuel Control, R ENG Start, R Fuel Control. This replaces ruling R37's order for this panel.
- [x] Test first, regenerate, full suite, commit `fix(b757): the Engines panel lists engine 1's start and fuel, then engine 2's`. Plan 01's frozen `Bbs757DefinitionTests` Engines test was updated to the new order (its expected array only), as the owner's instruction replaces ruling R37's order it pinned.

### Task 11: The side windows, sun visors, shades, seats and stowage (added 2026-10-28, the owner's instruction)

The owner asked whether the furniture and the windows can be reached, and said "Yes add". BlueBird draws them all as
clickable cockpit parts; the map left them out (banners MISC PARTS, COCKPIT CAPTAIN CHAIR, COCKPIT FIRST OFFICER CHAIR and
RIGHT WINDOW RAIN EFFECTS are excluded, and the armrests, sun visor bases and floor box are excluded nodes). Measured on the
ground 2026-10-28 (`flight-12.md`, "Windows and furniture"): every variable below holds a written value and goes back.

**Files:** `curation.json`, the generator only where a seat needs it, the map, `test_generate_b757_map.py`,
`Bbs757ControlMapTests.cs`, `docs/b757/evidence/panel-order-12.md` (one line for the two new panels).
- Two new panels in Other, after Forward attendant panel, in this order:
  - **Windows and sun visors:** Left Side Window, Right Side Window (`L2win_handle`, `R2win_handle`: `B757_L/R_COCKPIT_WINDOW`
    0 Closed, 1 Open; BlueBird closes both above 35 kt ground speed); Left Sun Visor, Right Sun Visor (`B757_SUNVISOR_L/R_base`:
    `B757_SUNVISOR_L/R_vis` 0 Hidden, 1 Shown, the tooltip "Toggle Sunvisor visibility"); Left/Right Sun Visor Position
    (`B757_SUNVISOR_L/R_move`, a drag lever `B757_SUNVISOR_L/R` 0-36); the four window shades (`L2_SHADE_GUIDE`,
    `L3_SHADE_GUIDE`, `R2_SHADE_GUIDE`, `R3_SHADE_GUIDE`: Left/Right Sliding Window Shade for L2/R2, Left/Right Aft Window
    Shade for L3/R3; an aft shade moves only while its side window is closed and is put up while it is open).
  - **Seats and stowage:** Captain Seat, First Officer Seat (`B757_CAPT_SEAT` / `B757_FO_SEAT` 0 Forward, 100 Aft, from the
    XML's MOVE_FORWARD/MOVE_BACK; the seat levers' clicks write only `O:` vars, which the sim's calculator cannot set, so the
    app writes the seat var to the picked position); the captain's and first officer's armrests (`…_SEAT_ARM_REST_1/2`) and
    headrests (`…_SEAT_HEAD_REST_1`, a drag lever 0-36); Jumpseat 1 and 2 (`B757_JUMP_1/2_SEAT_BASE`) and their armrests
    (`B757_JUMPSEAT_1/2_SEAT_ARM_REST_1/2`); Captain and First Officer Chart Holder (`B757_CAPTAIN_MAIN_CHART`,
    `B757_FO_MAIN_CHART`); Floor Box (`B757_FLOOR_BOX`); Stowage Door 1-3 (`B757_STOWAGE_DOOR_1` 0-34, `_2`/`_3` 0-36, drag
    levers, tooltip "Sliding door").
- Position labels: from BlueBird's code or tooltip where it says (the windows, the visors' visibility, the seats); else
  from the interior model's animation where the geometry decides it (which armrest is inboard, which way a shade, visor,
  headrest or door moves); else the labels in the brief, and each such label is listed in `flight-12.md` as not verified.
- [x] Tests first (the two panels, their order, each control's state var and positions, the seats written through their
  state var with no click code), regenerate, full suite, commit `feat(b757): the side windows, sun visors, shades, seats
  and stowage on the panels`.

### Task 12: The final review's fixes (2026-10-28)

The whole-branch review of Tasks 5-10 (`docs/b757/evidence/review-12-tasks-5-10.md`): "With fixes", no Critical.
- I1: the two Duct Leak Fuel Quantity Test buttons go (nothing in BlueBird reads `BBS757_DUCT_LEAK_FUEL_QTY_P61_TEST_SW`:
  not the WASM, no XML but its own switch, and nothing changed when it was held on the ground with BBS Nest running).
- M1: Fire protection lists the fire handles first and the three fire tests after them, as the option the owner picked
  said ("Keep with fire handles": after the handles), so a key pressed on the panel's first control during a fire is no test
  bell.
- M4: the two untrue doc comments (`BBS757Definition.cs` on the panel order, `Bbs757SpringControls.cs` on one second).
- Recommendation 3: the background release starts before the "did not return" warning is spoken.
- M2, M3, M5: this file's record, handoff and in-sim check 1 (the controller).
- [x] Tests first where behaviour changes, full suite, commit `fix(b757): the final review's fixes`; the Task 12 review's minors in `fix(b757): the fire tests keep their preflight order`.

## In-sim checks (owner)

1. On the ground, powered: Overhead > Pressurization, Auto Rate to MAX, then INDEX; Pedestal > Captain EFIS, PFD Brightness to Half (the PFD dims), then Full; Overhead > Air conditioning and bleed, Flight Deck Temperature to 20 °C. Each section's panel list opens with its first panel of the day (Overhead: Electrical; Glareshield: MCP; Forward panel: Brakes; Pedestal: Control stand), and inside a panel the every-day controls come first (Electrical: Battery Guard, Battery, EXT PWR, …: each guard just before its switch). On the RR, Pedestal > Engines lists only the RR fuel control switches.
2. Pedestal > Rudder and aileron trim: the status display reads Rudder Trim "centered". Press "Right Rudder Trim 1 s": "Rudder trim 0.9 units right" (about); "Left Rudder Trim 1 s" brings it back. Pedestal > Stabilizer trim: with no hydraulic pressure, "Nose Up Stab Trim 1 s" says "Stab trim did not move"; with C1 ELEC Pump on, it moves and the trim call-out says "Trim …".
3. Overhead > Pressurization with Mode Select AUTO 1: "Climb Cabin 1 s" says "Set the pressurization mode to MAN first". Mode Select MAN: "Descend Cabin 1 s" says "Outflow valve 90 percent open" (about), "Climb Cabin 1 s" opens it again; Mode Select back to AUTO 1.
4. The cockpit tests on the ground: Fire protection > Engine Fire Test (held 3 s: the bell and the engine fire lights; Glareshield > Master warning and caution clears the bell), Overhead > Indicator lights and door lock > Lamp Test ("Lamp test on", again "Lamp test off"), P61 panel > Takeoff Config Test 3 s, Ground Proximity Test 3 s and Left Stall Warning Test 3 s ("… done"; the warning, GPWS voice or stick shaker while it holds), the other P61 tests and the Radio Test, Oxygen, TCAS and Autoland Status tests once each (what each shows; whether the Radio Test lets go). Other > Flight deck door > Cockpit Door to Open, then Closed.
5. In cruise (about 350 kt), with hydraulics: "Nose Up Stab Trim 1 s" is heard (the trim call-out's "Trim …"); if nothing at all is said, note it (the final review's open question: a small move the call-out keys past).
6. The sweep: owner present, on the ground, engines off, APU running; run `tools/b757-gen/PanelSweep` (its README) once and attach the TSV's failures to the handoff below.

## Handoff to plan 16

- **Pilot-facing:** "757: the panels now follow the real cockpit (the made-up groupings are gone, each guard sits with its switch), each section's panel list follows the preflight flow (Overhead opens with Electrical, the Forward panel with Brakes, the Pedestal with the control stand and the engines), and inside a panel the every-day controls come first; the Engines panel lists Ignition, then each engine's start selector and fuel control, and only the loaded engine type's (PW or RR) fuel switches. New on the panels: Auto Rate, the brightness of all eight displays (on their EFIS, EICAS and CDU panels), the flight deck, forward and aft cabin temperatures; buttons hold the rudder, aileron and alternate stab trim and the manual cabin altitude switch for one second and say the result, with the trims and the outflow valve shown on their panels; the cockpit tests say what they test, hold for 3 seconds as in the cockpit, the P61 tests are buttons, the lamp test says on or off, the cockpit door says Closed or Open, and Fire protection lists the fire handles before its tests. New in Other: Windows and sun visors (both side windows, the sun visors and their positions, the four window shades) and Seats and stowage (both seats forward or aft, the armrests and headrests, the jumpseats, the chart holders, the floor box and the stowage doors). The Status panel's cabin altitude now reads the real cabin altitude." Changelog `feature` (the cabin altitude row a `fix`); say that a Ctrl+M mute set on a renamed or split panel ("Switches: <panel>") has to be set again.
- **docs/b757.md:** `direct_positions` (a knob the rules drop, written through its state var); the spring buttons (hold 1 s, the hold re-written every 500 ms, the release verified and retried, why: a lost release runs a trim to its stop); the readout rows; the panel order and its evidence (`docs/b757/evidence/panel-order-12.md`, with the research's open questions: ten controls whose meaning is unconfirmed, the judgment calls); the sweep tool; that the weather radar knobs are left out because nothing reads them, and the duct leak / fuel quantity test switch because nothing reads it; a `levers` entry may also take in a control whose click writes only `O:` vars (the seats, written through `B757_CAPT_SEAT` / `B757_FO_SEAT`); the two Other panels of Task 11.
- **Rule candidates (`.claude/rules/b757.md`):** "A spring switch the app holds (`Bbs757SpringControls`) is always released and the release read back and resent: nothing in BlueBird returns an outside 0 or 2 to rest (measured: a lost release ran the rudder trim to its stop)." "The PFD brightness is `B757_PFD_1/2_SCREEN_KNOB`, written by the ADI knobs; never the raster vars `BBS757_RASTER_BRIGHTNESS_*`, which nothing reads."
- **Frozen or other plans' files:** `guide/boeing-aircraft-and-the-md-11.md:400` says the Electrical panel starts with "Battery and STBY Power" (now Battery and EXT PWR) and needs the new rows, buttons and readouts (CORE-19); `docs/development.md` should list `tools/b757-gen/PanelSweep` with the other standalone tools; `docs/b757/plans/02-gaps/02-control-map.md:130` gives the flap lever's old place (now right after the Parking Brake). Also stale for the Engines panel's new order (Task 10: Ignition, then each engine's start selector and fuel control) and the regrouped, flow-ordered panels (Tasks 7-9): `MSFSBlindAssist/HotkeyGuides/BBS757_Hotkeys.txt:33-36`, `guide/boeing-aircraft-and-the-md-11.md:400-410`, `docs/b757.md:52`. Ctrl+M's "Switches: <panel>" rows are keyed by panel name (`Bbs757CockpitAnnouncements.cs`), so a mute on a renamed or split panel is lost: say so in the changelog. Other plans' texts that name panels that no longer exist: `docs/b757/plans/02-gaps/03-panel-writes.md:132` (pilot-facing: the emergency lights are on "Emergency lights and passenger oxygen", not "the Overhead miscellaneous panel"), `02-control-map.md:95`, `:146` (an in-sim step naming "Overhead miscellaneous") and `:195` ("Engine control and yaw damper", "Switch guards").
- **Frozen files changed by the owner's exception (so plan 16 and the other plans know):** `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (plan 01: the engine-type filter, Task 8); `tests/MSFSBlindAssist.Tests/Bbs757DefinitionTests.cs` (plan 01: only the Engines test's expected order, Task 10); `tests/MSFSBlindAssist.Tests/Bbs757LandingAltitudeTests.cs` (plan 03, done: its panel lists follow the new buttons, Tasks 3 and 5).
- **Requests for frozen files (plan 16):** `MainForm.cs` and `MainForm.AircraftSwitch.cs`: hand the 757 profile the SimConnectManager at startup and on a switch, as the MD-11's `Attach(simConnectManager)`, so a PW's first section list after connecting never names the RR-only "Electronic engine control (RR)" panel (`BBS757Definition.cs` would take it in a small `Attach`).
- **For other plans:** plan 10 (First Officer): its title parser (`Bbs757FoStateEvaluator.TitleVariant`, case-sensitive with an `L:B757_ENG_VARIANT` fallback) and plan 12's `Bbs757Variant.FromTitle` (case-insensitive) agree on every shipped title: unify them. Plan 13: `Bbs757CockpitAnnouncements.cs` keys its "Switches: <panel>" Ctrl+M rows by panel name; keying by node would survive a panel rename; it now also watches Task 11's 30 controls (see the known minors). Plan 11: at a gate the tablet's GROUND A/C (411, 373: `BBS757_vis_aircon`) and LEFT and RIGHT AIR START (619, 436 and 1009, 436: both toggle `BBS757_vis_airstart` with `BBS757_air_start_door`) tiles work, and IRS rapid align (1209, 519) aligned all three IRS at once (`flight-12.md`). Plan 10 and 12's title parsers: keep plan 10's `L:B757_ENG_VARIANT` fallback when they are unified, so a livery whose title omits PW/RR still gets the filter (final review).
- **For the owner to decide:** the button labels say "1 s", which NVDA reads as "one s" ("1 second" reads cleanly); the order's judgment calls (`panel-order-12.md`: landing lights not hoisted, the alternate stab trim tier 2); a spring press made just before the app closes has no release (shared with the existing held buttons: no shutdown seam).
- **Known minors, left for a follow-up (final review):** while a switch that did not return is being released in the background (up to about a minute, longer if it cannot be read), both of its buttons are ignored without a word: a short "{name} switch still being released" would help; the retry's 60 s counts only its waits; switching away and back to the 757 within that minute lets the old retry cut a new hold of the same switch short (never unsafe). The aileron trim row reads the control wheel, so it shows trim only with the wheel otherwise centred (hands off, autopilot off). The flight deck door lock switch's Unlocked and Deny are momentary in BlueBird (its update puts it back on Auto within a second; the sweep read Auto): a pick there acts for under a second, where a held button, as the P61 tests are held, would act like the cockpit's. The 3 s held map tests (fire, oxygen and the others) send their release once and never read it back (final review M6). The P61 tests click about 33 times a second while held (BlueBird's switch template returns the switch to neutral within 0.01 s and the app writes it again every 30 ms; the sim's input events cannot hold them: the 757 exposes 15, none on the P61 panel, `flight-12.md`). New background speech from Task 11: BlueBird closing the side windows above 35 kt is spoken, and a mouse drag of a visor, headrest or stowage door to an unlisted value is spoken as the number, about twice a second while dragged (`Bbs757CockpitAnnouncements.cs`, plan 13's file: speak an unlisted value only once it has held, or round it). The left sun visor's positions come in the order Inboard, Outboard, Half (its animation's frame 0 repeats the inboard end). Task 11's position names come from the interior model, not the sim, except the windows, the visors' visibility and the seats; the chart holders' are a guess (`flight-12.md`).
- **The sweep's report:** run 2026-10-28 (in-sim check 6, `flight-12.md`): 168 controls, 267 of 276 positions landed, every control put back, no SimConnect exception. The nine: the flight deck door lock Unlocked and Deny (momentary in BlueBird: a known minor above), both Chrono ET RESET (momentary, back to HOLD as on the real clock), and the Autobrake 1, 2, 3, 4 and MAX AUTO (BlueBird puts the knob on DISARM unless the IRS are aligned, there is hydraulic pressure and the brakes are off, as the real 757; measured landing on 1 with those, so not a fault). Its first start had stopped at connect before any write (fixed, `1a9b39c8f`). The map has grown since (Task 11: 439 controls, dry run 198 to sweep).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** landed (Tasks 0-12 on `B757`, 2026-10-28; the owner's in-sim checks passed; Task 11's panels not yet seen in the app)
- **Claimed by:** kn4iee (the owner), 2026-10-28
- **Worked by:** kn4iee (the owner) with Claude, with subagents (superpowers:subagent-driven-development: an implementer and a reviewer per task, two whole-branch reviews, each with a fix wave)
- **Started:** 2026-10-28; branched from `B757` at f12d85afa (rebased onto 73a0ab42f, plan 10's First Officer, the same day)
- **Landed:** 2026-10-28 (Tasks 0-5 first, pushed at the owner's word before the ground tests; the rest the same evening); the last code commit bfce1cb83, the record in the commit that carries this line
- **Tests at landing:** full suite 19,027 passed, 0 failed; generator 281 OK (2026-10-28, after Task 12; at the first push 18,600 and 267)
- **Tasks:** 0-12 done (Tasks 4-12 added by the owner's instructions or the final review). Tasks 1-4 had a whole-branch review and one fix wave; Tasks 5-10 had theirs after the ground tests ("With fixes", no Critical), fixed in Task 12; every task had its task review (Task 11 and 12 approved; their minors fixed or in the handoff).
- **Waiting on the owner:** a short look at Task 11's two panels in the app (one side window, one visor, one seat, one stowage door, Jumpseat 2 with stowage door 1 open): built after the owner's in-sim checks; 24 of their 30 position names come from the model (`flight-12.md`).
- **In-sim results:** Task 3 Step 1 measured 2026-10-28 on the ground through the simconnect MCP (`flight-12.md`), with the brightness, zone temperature, auto rate and DDRMI reads; the cockpit tests measured in flight the same day (fire, cargo and wheel well fire tests, the master warning and caution buttons, the P61 tests held by re-writing, the CVR, squib and lamp tests); on the ground that evening, with BBS Nest and the APU, the tests that showed nothing in flight, the takeoff config check and the master button, the cockpit door, the windows and furniture, the tablet's air start and ground air tiles, the sweep's live run and the autobrake with the IRS aligned (`flight-12.md`); the owner's in-app checks 1-6: passed (the owner, 2026-10-28: "All in sim tests were good"), on the build before Task 11.
- **Changed from the plan:**
  - Task 1: Auto Rate INDEX is 3.5, the load value (the code uses its whole number, 3); the PFD brightness rows are the ADI knobs (`B757_ADI_1/2_LCD_KNOB` → `B757_PFD_1/2_SCREEN_KNOB`), not the PFD-named nodes, whose raster vars nothing reads; the brightness positions are Minimum/Half/Full at 0/20/40 (0 is a minimum, not off; the code takes 0-40); the zone temperatures are in (14 positions, read from BlueBird's code and measured); the weather radar knobs are out (nothing reads them); DDRMI unchanged (already in the map, measured working); the curation entry uses the `levers` form `[{value, label}]`; added a fix: the Status "Cabin Altitude" row read a var BlueBird never writes (now `B757_TEST_CABIN_ALT`, shown in feet).
  - Task 2: the denylist also holds the alternate flaps and their arm switches, the gear lock override, the switches under covers the map does not link, and the electrical sources (bus ties, STBY Power, Gen Conts, APU Gen); a stale denylist entry stops a live run; the tool reads only its shipped denylist.
  - Task 3: cabin climb writes 7 (the cockpit's click value; only the side of 4 counts) and the hold is re-written every 500 ms (the XML returns an outside write to 4 within a second); the release is read back 250 ms after each write and resent (3 writes), then retried in the background every 2 s for a minute; the readouts are the rudder trim indicator (units), the yoke (aileron trim, direction only: no units exist), the stab trim's existing call-out (the press says only "Stab trim did not move", or the trim when the call-out is muted or off) and the outflow valve (percent); the labels put the side first (VAR-1); 15 variables, not 8 (the press reads 7 vars fresh), and status rows for the rudder trim, aileron trim and outflow valve (added by the final review: a blind pilot could read them only by moving them); `Bbs757LandingAltitudeTests.cs` (plan 03's, done): its panel-control asserts loosened to their intent (the field first, Mode Select second), its display-list asserts kept exact with the Outflow Valve row added.
  - Task 4 (added): the owner asked for every-day items first in Tab order; `docs/b757/evidence/panel-order-12.md` (new) holds the research and its citations.
  - Task 5 (added): the owner asked for the cockpit tests to be tried in the sim and the panels revised; 20 test buttons are held 3 s (curation `hold_ms`, a new key; the plan's count of 19 was a miscount), the eight P61 test switches left the map for 16 buttons that re-write the test position every 30 ms for 3 s of real time (the P61 switches return to neutral at once, measured), and the lamp test says whether it is on; the master warning and caution buttons were measured working and left as they are. Known gap for the Task 5 review: if speaking the "did not return" warning throws, the background release retry does not start (`BBS757Definition.Overhead.cs`, from Task 3).
  - Task 6 (added): the cockpit tests' labels from BlueBird's procedure names (Engine Fire Test, Engine, APU and Cargo Fire Test, Wheel Well Fire Test, Squib Test, Lamp Test, TCAS Test, Weather Radar Test, Radio Test, Autoland Status Test 1/2 and Reset, First/Second Observer Oxygen Test), the T/O CHK button a plain press again (Takeoff Config Check: BlueBird "press and release"), and the Cockpit Door's positions Closed/Open (BlueBird's XML).
  - Task 7 (added): the panels follow the cockpit (`docs/b757/evidence/panel-audit-12.md`: every node placed by its 3D position; 55 panels became 63; the made-up panels Main panel switches, Overhead miscellaneous, Switch guards, Cockpit miscellaneous and Display brightness are gone, each guard with its switch, the brightness knobs on their real panels, the trims' buttons and readouts in a new Rudder and aileron trim panel); the fire tests stay with the fire handles (the owner).
  - Task 8 (added): only the loaded engine type's controls show (the title's PW/RR; the owner approved changing plan 01's frozen `BBS757Definition.cs`); right after a fresh connection a PW's first section list can still name the RR-only panel until a panel is built (handoff: a MainForm call at connect, as the MD-11's `Attach`).
  - Task 9 (added): each section's panel list follows the Simplified Procedures' first CHANGE of one of its controls (the owner: "Actions only"; checks, confirmations, optional and resting-position steps do not count, radio tuning does), the Forward panel pinned to open with Brakes (the owner) and the Pedestal's start pinned by plan 01's frozen test; the derivation table in `panel-order-12.md`.
  - Task 10 (added): the Engines panel engine by engine (the owner); plan 01's frozen Engines test's expected array updated to it.
  - Task 11 (added): the side windows, sun visors, shades, seats and stowage (the owner: "Yes add"), 30 controls on two new Other panels (439 controls, 65 panels); the seats need a generator path for a click that writes only `O:` vars; the model overturned three of the brief's default names (armrests 0 Up, headrests 0 Up, visors along a track).
  - Task 12 (added): the final review's fixes: the duct leak / fuel quantity test buttons removed (nothing reads that switch), Fire protection's handles before its tests, two comments, the background release started before the warning is spoken; then the Task 12 review's minors (the tests' own preflight order).
  - The sweep tool: its connect loaded SimConnect before copying the native DLL (fixed on its first live run).
- **Rulings made while working (Claude, under the owner's go-ahead to run the plan; each the owner's to reverse):**
  - Measuring Task 3 Step 1 with the MCP was the owner's go-ahead ("use the simconnect mcp server … coherent debugger"), the controller ran it; everything touched was put back (residual trims under 0.2 units).
  - The tasks ran in the order 2, 1, 4, 3 (Task 1 waited on the package research).
  - The order: tier 1 every flight in flow order, tier 2 set once, tier 3 non-normal with a guard before its switch; the research's judgment calls accepted as proposed; only the controls inside a panel move (Display brightness last in Forward panel).
  - The positions above (brightness 0/20/40 although the mouse stops at 30 or 20; zone temperatures one per whole degree; Auto Rate INDEX 3.5).
  - Spring buttons: one hold per control at a time; an unread Mode Select refuses; VAR-1 labels; the stab trim's speech; the Shift+T case; the 250 ms read-back wait; the background retry.
  - Deferred: a small stab trim move at high speed may be spoken by no one (in-sim check 4); "1 s" wording; a press right before the app closes.
  - Task 12 M1 reverses the Task 7 ruling (the tests before the handles): the option the owner picked said "after the handles", and a key on the panel's first control in a fire is then no test bell.
  - The ground tests and the sweep's live run were run by Claude with the owner at the sim ("Do what needs to be done", "Did you still need to do a sweep"); everything touched was put back, the parking brake by full braking.
