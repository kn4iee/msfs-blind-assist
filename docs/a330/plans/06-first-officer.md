# A330 First Officer Implementation Plan (plan 06, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The A330's First Officer has what the other Airbus profiles have, passes every test that sweeps all profiles, runs its flows and checklists in the simulator's own procedure order, naming each control as the panels do, and never reports a step done that it did not see take effect. Every write it makes is measured, and the engine start and shutdown flows have run end to end.

**Architecture:** First a written sweep of every flow step against what it writes and what it reads back, which finds false "done" and false "failed" reports before anything is added. Then the profile joins the cross-profile tests, and each failure is a real gap to close. The missing pieces (gear confirmation, ground-spoiler re-arm, take-off configuration verdict, the exit-light sequence, a working automation manager) reuse the shared First Officer classes the Fenix and FlyByWire profiles use. Last, the flows and checklists are laid against the simulator's own procedures.

**Tech Stack:** C# 13 / .NET 10, xUnit; the shared First Officer framework under `FirstOfficer/`.

**Spec:** `docs/a330/spec.md` sections 2.4, 3 (R13), 5 (D6, Q2), 6 (M6, M8); `docs/first-officer.md`; rules `.claude/rules/first-officer*.md` (FO-1 to FO-26, FOA-1 to FOA-14), [INI-1].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. The First Officer writes only through `IniA330Definition.HandleUIVariableSet` and the routes in `IniA330Definition.FirstOfficer.cs`, never a write of its own.
- Owned files: `FirstOfficer/IniAirbus/**`, `Aircraft/IniA330Definition.FirstOfficer.cs`, tests `tests/MSFSBlindAssist.Tests/FirstOfficer/IniAirbus*.cs`; by the index's named exception, the A330's rows in the cross-profile First Officer tests; `docs/ini-a330.md` "First Officer"; `docs/a330/evidence/06-first-officer.md` (new).
- The yardstick is the Fenix A320 and FlyByWire A32NX profiles (the app's other Airbus First Officers). A difference from them is closed, or kept with its reason written in the record.
- A flow's items and their order come from the simulator's own procedures (spec section 4, "Sources"); a step the simulator's procedure does not have is kept only if the other Airbus profiles have it and the aircraft has the control.
- The ADIRS, the flaps, the spoilers, the parking brake and the altimeters stay the Captain's unless a measurement in Task 4 shows the write is safe and read back; what stays the Captain's is a reminder, said once.
- Automatic flaps if they can be done (spec D6, the owner's answer of 2026-11-20): Task 3's automation manager builds the schedule the FlyByWire profiles have (`FbwA320FOAutoManager`, `FbwA380FOAutoManager`), behind the same "Auto-manage flaps" setting, once Task 4 has measured the flap lever's write and read on this aircraft. The speeds it turns on (F, S, green dot) are the aircraft's own (`INI_F_SPEED`, `INI_S_SPEED`, `INI_GREENDOT_SPEED`), which read 0 on the ground: plan 11's flight says whether they are live in flight. Until both are measured the schedule is written and tested but switched off, and the setting stays hidden for this aircraft; if either cannot be had, the record says which.
- Control keys are plan 04's and never change under this plan.

## Measured on 2026-11-20

- **Task 4 (M8), measured:** the parking brake's input event is 0 = set, 1 = released
  (`B:AIRLINER_PARKINGBRAKE`; `BRAKE PARKING POSITION` followed both ways, chocks in); the ADIRS mode
  selectors take 1 = NAV by value and all three aligned (the E/WD counted down from "IR IN ALIGN 6 MN";
  the MCDU's IRS INIT page then read ALIGNED ON GPS for each). Flaps and spoilers are still to measure.
- **Task 5 (M6) has its source:** the simulator's 38 procedures, 669 items, in order (plan 04's block).
  Their names, for matching flows: PREFLIGHT WALK-AROUND, PRELIMINARY COCKPIT PREP, COCKPIT PREPARATION,
  BEFORE PUSHBACK OR START, BEFORE ENGINES START (three parts), ENGINES START, AFTER ENGINES START, AFTER
  START CHECKLIST, TAXI OUT, BEFORE TAKEOFF (two parts) with RUNWAY LINEUP between, TAKEOFF, AFTER
  TAKEOFF, CLIMB (three parts), FL100 CLIMB, TOP-OF-CLIMB/CRUISE, DESCENT PREPARATION, DESCENT, FL100
  DESCENT, APPROACH, INITIAL APPROACH, INTERMEDIATE/FINAL APPROACH, ILS, RNAV and NON-PRECISION APPROACH,
  LANDING, GO-AROUND, AFTER LANDING (procedure and checklist), PARKING (procedure and checklist),
  SECURING AIRCRAFT (procedure and checklist).
- **The Preflight flow's switches, written once each and read back** (through the calculator's `_Set`,
  not the app's own write, so each is a lead, not a verdict): nav and logo lights, annunciator lights,
  standby compass light, crew oxygen, crossbleed, pack flow, strobes, wing lights, no smoking, emergency
  exit lights, TCAS traffic and both flight directors read as wanted. **Pack 1, pack 2, hot air 1 and
  hot air 2 toggled**: the value meant to switch them on switched them off, and the same again switched
  them on. The cabin pressure MODE SEL's read (`INI_CABIN_PRESS_MODE`) stayed 0 whatever was sent. Task
  1's sweep measures these five first through `HandleUIVariableSet`: a step that sets one of them must
  read first and write only when the read differs, as the engine masters do.
- The tablet's "Send to FMS" on its Take Off page uplinks the take-off data; the MCDU then needs
  `CONFIRM TO DATA*` (6R on PERF TAKE OFF) before the speeds are in. A Captain reminder, never a First
  Officer step ([FO-2]).
- The aircraft publishes its flight phase (`INI_flight_phase`, 1 at the gate): a flow gate can read it
  once plan 11 has its values.
- On batteries only the E/WD shows four cautions (VENT EXTRACT FAULT, FUEL FCMC 2 FAULT, DATALINK ATSU
  FAULT, DOOR POS DET 1 + 2 FAULT) that go when external power comes on: Electrical Power Up must not
  report them as faults of its own steps.
- (2026-11-22) "Read as wanted" above means the write landed, not that the position's name is right: a
  read of the control's own variable passes a swapped name (plan 04's proof rule). Each of those
  switches is still to be proven by plan 04's sweep.

## Measured on 2026-11-22 (in flight)

- **The First Officer switches predictive windshear OFF for take-off and ON after landing.** The Taxi
  flow's `TX_PWS` writes `A330_WX_PWS` = 1 and the Taxi checklist's "Predictive windshear: AUTO" reads 1
  as done; After Landing's `AL_PWS_OFF` writes 0 and reads 0 as done (`IniAirbusFlowDefinitions.cs`
  `TX_PWS`, `AL_PWS_OFF`; `IniAirbusChecklistDefinitions.cs` the same ids, at 7113f8803). Measured in
  flight: 1 is OFF and 0 is AUTO (plan 04's 2026-11-22 block: the E/WD's PRED W/S OFF memo showed at 1
  and went at 0). Both the write and the tick are wrong, and the read-back agrees with them.
- `IniAirbusKeys`' summary calls its values "the generated map's measured positions". The 2026-10-16
  probe measured which variable moved, not which position a value is, so these values are only as good
  as the map's names: plan 04 counts 79 selectors and 23 push-buttons named by convention.

## Found by a code review on 2026-11-22 (read in code, not run in the sim)

Each is a step below; each claim is checked against the code before its fix.

- **Electrical Power Up can switch the engine masters off in flight.** It opens with
  `EnginesStoppedGuard("EPU_ENGINES_STOPPED")` and then writes both masters OFF, with no on-ground guard
  (`IniAirbusFlowDefinitions.cs`, `BuildElectricalPowerUp`): with both engines flamed out in the air it
  cuts the masters a relight needs. The Headwind A330's has `EPU_ON_GROUND`; the flow's own checklist
  tick already refuses this case. Task 2 Step 0.
- **The weather radar system selector rests on the same kind of name as windshear:**
  `direct("1", "Off", "2")` on `INI_WX_SYS_SWITCH`, unproven. Electrical Power Up's "Weather radar: OFF"
  writes 1, Taxi's "SYSTEM 1" writes 0, After Landing's "OFF" writes 1; if 0 and 1 are swapped, each
  does the opposite and both radar checks read backwards. Task 4 Step 1.
- **The transponder and TCAS selectors may be crossed** (plan 04's block): Taxi's "Transponder: AUTO"
  writes 1 and Line-up's "TCAS: TA/RA" writes 2; crossed, the aircraft would take off with TCAS in TA
  ONLY and the transponder ON, while the line reads done. Task 4 Step 1.
- **Push-buttons nobody checked for toggling, written as groups:** both external power buttons, the APU
  master and bleed, the three batteries, the eight fuel pumps, the anti-ice buttons, both LS buttons. A
  multi-switch step (`Multi`) and the checklist's `SetAll`/`SetPumps` write every key of the group when
  any one differs, and a `set` control never reads first, so a toggling button that was already right
  flips (Before Start's and Securing's "External power: OFF" also write EXT B, which the First Officer
  never switched on). Task 1 Step 3.
- **Only three writes are ever read back** (`ES_MODE`, `ES_ENGn`, `ES_MODE_NORM`); every other switch step
  is done when sent. Reads that echo their own input event read done even when the aircraft ignored the
  write: both centre tank pumps (inside "Fuel pumps: ALL ON / OFF" and the Parking line), both wipers,
  altitude reporting. The ECAM page presses never re-read `INI_ECAM_ACTIVE_PAGE`. The start flow's
  verifies give up after 2.6 s, against a measured lag of a few seconds. Task 1 Step 3.
- **Three Captain lines are ticked with nothing read:** After Takeoff's "Ground spoilers: DISARM", After
  Landing's "Ground spoilers: DISARM" and "Flaps: UP" are plain reminders, ticked and latched when the
  flow ends ([FO-7]), though the spoilers and the flaps can each be read; After Start does it right (a reminder, then a
  read-only check). Task 3 Step 7.
- **What the Headwind A330's flows have and this profile lacks:** recorder ground control ON (the map has
  `A330_CVR_GND_CTL`), the Before Start FCU managed pushes (the developer doc names the FCU's managed
  pulses); the CVR test, the cockpit door, the rudder trim reset and the cockpit lighting scene have no
  control on this aircraft. The Taxi and Approach "Engine mode selector: SET" lines give no live value,
  though the First Officer reads the selector. Task 3 Step 7.
- **The Landing reminder says ARM over an extended speed brake** ([FOA-11]): it never reads `A330_SPDBRK`,
  which the definition registers. Task 3, the spoiler step.
- **The engine start checklist lets both masters open together:** master 2, master 1, then running 2,
  running 1; a master's tick checks only the selector, the ground and its own engine. The Headwind A330
  interleaves master and running lines. Task 6 Step 1.
- **What Task 2 will see** (inferred, not run): `FoCutGuardSweepTests` fails on Electrical Power Up;
  `A320FamilyLandingFlowTests` fails (no `LD_SPOILERS_ARM` write), exempt with its reason until Task 4
  measures the arm; `A320FamilyReadbackLinkTests`' latched lines differ by the three Captain lines above.

**The First Officer's writes that rest on a convention name** (beside packs, hot air and windshear,
already listed; the masters, the start selector and APU START are measured):

| Control | Steps and values | Risk |
|---|---|---|
| Weather radar system | EPU_CHK_WXR 1, TX_WXR 0, AL_WXR_OFF 1 | High (same panel and naming as windshear) |
| Transponder and TCAS mode | TX_XPDRAUTO 1, LU_TCAS 2, the shutdown steps 0 | Medium-high (names may be crossed) |
| Centre tank pumps | BS_FUELPUMPS 0, SD_FUELPUMPS_OFF 1 | Medium (read is its own event) |
| External power A and B | EPU_EXTPWR 1, BS_EXTPWR_OFF and SC_EXTPWR_OFF 0 | Medium (toggle risk) |
| APU bleed | BS 1, SD 1, AS 0, SC 0 | Medium (the engine bleeds use the other convention) |
| Wing fuel pumps | BS 1, SD 0 | Medium |
| LS buttons | AP_LS 1, SD_LS_OFF 0 | Medium (the flight directors use the other convention) |
| Landing lights | LU 1, AL 0, the 10,000 ft lights | Medium (position words listed unverified) |
| Altitude reporting | PF_ALTRPTG 1 | Medium (read is its own event) |
| Engine and wing anti-ice | AL_ANTIICE_OFF 0 | Low-medium |
| Runway turn-off lights, dome light, wipers, seat belts, beacon, nose light, APU master, batteries | as their steps | Low |

## Review Focus

- A write that worked reported as failed, because the read-back key lags or reads another encoding ([INI-1]: a push-button's read is the opposite of its input): Task 1's table has the read-back and its settle time for every step.
- A step reported done because the write was sent, with nothing read back: each such step either gains a read-back or says "sent", never "set" (Task 1).
- Engine start with the simulator's engine covers on holds N2 at 0 for a minute (measured): the start flow checks the covers first and says so, never a silent wait (Task 6).
- A flow run twice, or resumed after an aircraft switch: no step fires twice (the cross-profile dependency tests, Task 2).
- The gear call with one gear not down (FO-13): the confirmation names it (Task 3).
- A value the First Officer writes whose position name is unproven in the map (plan 04's review focus): the profile's values are pinned to the map's labels (`IniAirbusFoTests`), so a swapped name in the map becomes a wrong First Officer action that reads back as done (predictive windshear, 2026-11-22). Task 1 lists each write's proof status; once plan 04's sweep has landed, a write to an unproven position fails a test (Task 1 Step 2).
- A cut in flight: every flow that writes the engine masters, fuel, IRS or batteries off has the guards [FO-25] names, and the profile is in `FoCutGuardSweepTests` (Task 2 Step 0).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 06` (`Plan: 06`). Branch from the base commit the index records for early plans.

### Task 1a: Predictive windshear, measured inverted (no sim)

Done in one commit with plan 04's Task 2a (the map's fix), by whichever plan is claimed first, who tells the other plan's claimant.

- [ ] **Step 1:** Steps 1-5: `IniAirbusKeys` gains named positions as the Fenix's flows have (`PwsAuto = 0`, `PwsOff = 1`, citing this plan's 2026-11-22 block); `TX_PWS` writes `PwsAuto` and its checklist line is done at `PwsAuto`; `AL_PWS_OFF` writes `PwsOff` and is done at `PwsOff`. Tests `Taxi_sets_predictive_windshear_to_auto_0`, `After_landing_sets_predictive_windshear_off_1`, and `IniAirbusFoTests` pinned against plan 04's corrected label. Commit with plan 04's Task 2a as `fix(a330): the Weather Radar predictive windshear`.

### Task 1: The written sweep: what every step writes and reads back

**Files:** the evidence file (the table); fixes in `FirstOfficer/IniAirbus/IniAirbusFlowDefinitions.cs`, `IniAirbusStateEvaluator.cs`, `IniAirbusActionExecutor.cs`, `IniAirbusKeys.cs`; test `tests/MSFSBlindAssist.Tests/FirstOfficer/IniAirbusReadBackTests.cs`.

- [ ] **Step 1:** One table in the evidence file, a row a flow step and a row a checklist item: the control key, the route (`FoWriteRoute`: calculator, input event, toggle, hold), the value sent for the wanted position (`FoPositionValue`), the map's name for that value and its proof status (proven, unproven, or read is its own variable: plan 04's Task 4), the key and value the evaluator reads to call it done, and whether a write that worked could read as failed or a write that did nothing as done. Read the code; no sim needed. A value whose name is unproven and whose step makes aviation sense only the other way round is flagged for plan 04's sweep first.
- [ ] **Steps 1-5 per defect found:** a test in `IniAirbusReadBackTests` that shows it on an in-memory map (`A_pushbutton_that_went_on_reads_done`, `A_step_with_no_read_back_says_sent_not_set`, one test a defect), the fix, commit `fix(a330-fo): <the step>`.
- [ ] **Step 2 (after plan 04's Task 4 has landed):** Steps 1-5 for `An_fo_write_to_an_unproven_position_fails` (every flow step's and checklist action's value is a position plan 04 marks `proven`) and, where a control's proof is a variable (a lamp, a simulator variable), `A_step_is_done_by_its_proof` (the evaluator reads the proof, not the control's own variable). Each step the test catches is fixed, proven by plan 04, or becomes a Captain reminder until it is; each is listed in the record. The settle times of plan 04's `lagged` verdicts set that step's read-back wait ([FO-10], [FO-11]). Commit.
- [ ] **Step 3, the review's read-back findings:** Steps 1-5 per item, one commit each. (a) Every push-button the First Officer writes is driven through `HandleUIVariableSet` twice from each state (plan 04's sweep gives the verdicts); one that toggles becomes a `toggle` control in plan 04's curation, never a workaround here, and a group step (`Multi`, `SetAll`, `SetPumps`) writes only the keys whose own read differs: `A_group_step_leaves_a_switch_that_is_already_right_alone`. (b) The external power OFF steps write only the buttons that read on. (c) Every switch step reads back: through its proof where plan 04 names one, else its read, else its line says "sent", never "set" (`A_step_with_no_read_back_says_sent_not_set`); the echo reads (centre tank pumps, wipers, altitude reporting) are named in Task 1's table and gain a downstream read where one exists (the FUEL page, the ND). (d) An ECAM page press verifies `INI_ECAM_ACTIVE_PAGE`. (e) The start flow's verify window comes from Task 4's measured settle time, not 2.6 s.

### Task 2: The profile in every cross-profile test

**Files:** the cross-profile tests (`FoCutGuardSweepTests`, `FoDepartureGroupsTests`, and every other test under `tests/MSFSBlindAssist.Tests/FirstOfficer/` that lists profiles: find them with `git grep -n -l "Fenix" tests/MSFSBlindAssist.Tests/FirstOfficer`); fixes in `FirstOfficer/IniAirbus/**`.

- [ ] **Step 0 ([FO-25]):** Steps 1-5: Electrical Power Up gains `OnGroundGuard("EPU_ON_GROUND")` after its engines guard, as the Headwind A330's has, and the profile joins `FoCutGuardSweepTests` (the A320 family list, and the cold-and-dark and securing guard lists) in the same commit: `fix(a330-fo): Electrical Power Up never cuts the masters in the air`.
- [ ] **Step 1:** Add the A330's profile to each list (a helper `IniAirbus()` like the other profiles' in each test). Run them. `A320FamilyLandingFlowTests` keeps the A330 out with its reason (no `LD_SPOILERS_ARM` write) until Task 4 measures the arm.
- [ ] **Step 2:** Each failure fixed at its cause in the profile, one commit a cause: `fix(a330-fo): <what the sweep found>`. A test that cannot apply to this aircraft (it has no such control) keeps the A330 out with the reason in a comment and in the record.

### Task 3: What the other Airbus profiles have

**Files:** Create `FirstOfficer/IniAirbus/IniAirbusGearConfirmation.cs`; modify `IniAirbusFOAutoManager.cs`, `IniAirbusFlowDefinitions.cs`, `IniAirbusChecklistDefinitions.cs`, `IniAirbusStateEvaluator.cs`, `IniAirbusFoProfile.cs`; tests `IniAirbusGearConfirmationTests.cs`, `IniAirbusAutoManagerTests.cs`, `IniAirbusLandingFlowTests.cs`, `IniAirbusTakeoffConfigTests.cs`.

- [ ] **Steps 1-5, gear confirmation ([FO-13]):** modelled on the Fenix's and the FlyByWire's `*GearConfirmation`; tests `Three_green_is_confirmed`, `A_gear_in_transit_is_named`, `An_unread_gear_is_not_confirmed`; the evaluator's own `GearFrom` goes; commit.
- [ ] **Steps 1-5, the automation manager:** `IniAirbusFOAutoManager` becomes a working manager as the Fenix's (what it automates there, less anything this aircraft lacks), plus the flap schedule of the FlyByWire managers (D6): the schedule is a pure step (`IniAirbusFlapSchedule.Decide(phase, speed, F, S, greenDot, flapHandle)` giving the handle to select or none), with one test a transition, named for it, and tests that it gives nothing when a speed reads 0 or the handle is unread; `AutoFlapsEnabled` acts only when `FlapWriteMeasured` is true (Task 4 sets it), otherwise the setting is hidden for this aircraft as the Fenix's profile does; [FO-15]'s line gains this aircraft in plan 12 once it is switched on; commit.
- [ ] **Steps 1-5, the Landing flow re-arms the ground spoilers ([FOA-11]):** through the shared `AirbusGroundSpoilers`; tests as the Fenix's; commit. Until the arm is the First Officer's, the Landing reminder reads `A330_SPDBRK` and speaks `AirbusGroundSpoilers.LeaveAloneText` while the speed brake reads extended (`The_arm_reminder_warns_over_an_extended_speed_brake`).
- [ ] **Steps 1-5, the take-off configuration verdict and the exit-light sequence:** each as the Fenix's or FlyByWire's profile has it, if the aircraft has the control (the T.O CONFIG key is in the ECAM panel; the EMER EXIT LT switch in Signs): tests named for each; commit. One the aircraft cannot support is recorded with the reason.
- [ ] **Step 6:** Center tank pumps: say in the record whether this aircraft needs what `CenterFuelPumpAutomation` does on the others (its fuel panel and the simulator's procedures decide), and add it or record why not.
- [ ] **Step 7, the review's parity findings:** Steps 1-5 per item. (a) After Takeoff's and After Landing's "Ground spoilers: DISARM" and After Landing's "Flaps: UP" become a Captain reminder followed by a read-only check, as After Start's lines are, until Task 4 makes the writes the First Officer's (`A_captain_line_is_ticked_only_when_its_state_reads_true`). (b) The Headwind A330's Preflight "Recorder ground control: ON" (`A330_CVR_GND_CTL`) and Before Start FCU managed pushes, through plan 04's and plan 05's controls. (c) The CVR test, the cockpit door, the rudder trim reset and the cockpit lighting scene have no control on this aircraft: each stays a reminder, recorded with "no control". (d) The Taxi and Approach "Engine mode selector: SET" lines take the selector as their live value. Commit a phase at a time.

### Task 4 [sim]: The writes nobody measured (M8)

**Files:** the evidence file; `FirstOfficer/IniAirbus/IniAirbusKeys.cs`, `IniAirbusFlowDefinitions.cs`, `IniAirbusFlightPhaseMonitor.cs`, `Aircraft/IniA330Definition.FirstOfficer.cs` as found.

- [ ] **Step 1:** On the ground, engines off, with the owner's go-ahead for each, through the First Officer's own routes, read back after 2 s and put back: the three IRS selectors to NAV and back (measured 2026-11-19 through the calculator: input 1 gives NAV on all three); flaps 1 and back; ground spoilers armed and disarmed; the parking brake set and released (its direction is listed unverified); the seat belt and no-smoking signs; each fire test; the APU master and start. Then, each judged by its proof and never by its own variable: the weather radar system selector's three positions (the ND's radar picture) with Task 1a's windshear, and the transponder and TCAS mode selectors' positions (the ND's TA ONLY, the E/WD's TCAS STBY memo, `TRANSPONDER STATE:1`); a crossed pair is fixed in plan 04's curation and this profile's values follow.
- [ ] **Step 2:** Each step whose comment says "never probed", "nothing measured" or "unverified" loses the comment and gains its measurement's date, or is fixed test-first. `AltimeterWritesVerified` follows plan 05's Task 6: true only for the sides it measured.

### Task 5: The flows and checklists against the simulator's own procedures (M6)

**Prerequisite:** plan 04's Task 6 Step 1 has landed (`tools/a330-gen/data/procedure_order.json`).

**Files:** `FirstOfficer/IniAirbus/IniAirbusFlowDefinitions.cs`, `IniAirbusChecklistDefinitions.cs`; fixture `tests/MSFSBlindAssist.Tests/A330/Fixtures/p06_procedure_order.json` (a copy of the order file, so no test reads the generator's folder); test `IniAirbusProcedureOrderTests.cs`; the evidence file.

- [ ] **Step 1 (with the private reference):** Someone with access lays each flow beside the simulator's procedure for the same phase (Electrical Power Up and Preflight against the preliminary and cockpit preparation; Before Start; Engine Start; After Start; Taxi; Line-up; After Takeoff; Descent; Approach; Landing; After Landing; Shutdown; Securing), and each checklist beside the simulator's checklist. The side-by-side with the simulator's words stays in the private reference. In this plan's evidence file, by control key and place only: a control the simulator's procedure acts on and the flow does not; one the flow acts on and the procedure does not; an order that differs.
- [ ] **Step 2:** Steps 1-5: `Each_flows_steps_follow_the_simulators_order` (a flow's steps whose control is in the order file appear in the file's order), `Each_checklists_items_follow_the_simulators_order`; reorder; add the missing steps the aircraft has a control for (plan 04's new controls included once it has landed them); an item's spoken name is its control's name in the panels (plan 04), so the two always agree; commit a phase at a time.
- [ ] **Step 3 [owner]:** The differences that remain are the owner's to rule on (a step kept because the other Airbus profiles have it).

### Task 6 [sim] [owner present]: Engine start and shutdown, end to end

- [ ] **Step 1:** On the ground with the APU running and its bleed on: the Before Start flow, then Engine Start (the masters and the start selector are measured: the master's input event takes the position by value, the selector's 2 is IGN START and 1 NORM), After Start, then Shutdown and Securing. Record each step's result and time. The start flow first reads the simulator's engine covers (`A:COVER ON:1`) and says "Engine covers are on" instead of starting. The engine start checklist's lines interleave as the flow runs (master 2, engine 2 running, master 1, engine 1 running), and the second master's tick is left alone until the first engine reads started (`The_second_master_waits_for_the_first_engine`).
- [ ] **Step 2:** Each miss fixed test-first; the "not yet flown" comments go.

## In-sim checks (owner)

1. A cold aircraft at the gate: Shift+C, run Electrical Power Up to After Start with the First Officer; every step it reports is true in the cockpit.
2. Landing: "gear down" is confirmed with three greens, and the ground spoilers are armed.
3. Plan 11 flies the rest (the whole flight with the First Officer).
4. After the Taxi flow the E/WD shows no PRED W/S OFF memo; after the After Landing flow it does (Task 1a).

## Handoff to plan 12

- **Pilot-facing:** the First Officer's new pieces; flows in the simulator's own order. Guide: `guide/first-officer.md` and the A330's chapter; `docs/first-officer.md`'s profile list. Changelog `improvement`.
- **Rule proposals:** as found; a measured trap (the covers before a start) proposes its line.
- **Frozen files:** the cross-profile tests' rows (done here by exception).
- **For plan 11:** M8's list: the flows from After Takeoff to After Landing in flight.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** not started
- **Claimed by:** none
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
