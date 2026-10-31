# A350 PFD and Call-outs Implementation Plan (plan 07, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The PFD's open checks are closed, each PFD message is a call-out on its own Ctrl+M row and heard live, LVR CLB is said as the aircraft shows it, the TCAS advisories are heard with real traffic, the PFD's metric altitude and pause lines read on 1.2.8, green dot, S and F are said on the ground as well as in flight (Shift+1 to 3), and every weight read-out says the aircraft's unit.

**Architecture:** Each check is measured on captures of the captain's EFIS (`A350Probe draw live EFIS#0` with `A350_DRAW_JSON`, `pfdlisten`) in the state it names, then read by `A350Pfd` and spoken by `A350PfdMonitor`'s announcers; a misread is fixed test-first on its capture. The PFD flags' one Ctrl+M row becomes one row per message (owner question 6) in plan 01's `A350CalloutRows.Pfd`, each muted by its key through the monitor's keyed lookup.

**Tech Stack:** C# 13 / .NET 10, xUnit with live-captured EFIS fixtures; `tools/A350Probe` (`draw`, `pfdlisten`, `listen`, `sample`).

**Spec:** `docs/a350/spec.md` sections 2.6, 2.7 (the test flights' speeds finding), 3 (handoff 4: TCAS TA/RA, LVR CLB; open checks: PFD, PFD messages, SIM RATE/PAUSED, T10's PFD boxes, KG units), 4, 5 (questions 6 and 15); `docs/inibuilds-a350.md` "The PFD and FMA".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/Display/A350Pfd.cs`, `A350PfdMonitor.cs`, `A350PfdFlagAnnouncer.cs`, `A350FmaAnnouncer.cs`, `A350FlashingLines.cs`, `A350TakeoffSpeeds.cs`, `A350Minimums.cs`, `A350SimPause.cs`, `Aircraft/A350/A350TcasCallouts.cs`, `A350TakeoffCallouts.cs`, `A350MinimumsCallout.cs`, `A350PauseAlerts.cs`, `A350CalloutRows.Pfd.cs`, `A350Hotkeys.cs`, `Aircraft/IniBuildsA350Definition.Hotkeys.cs` (the read-out hotkeys); the PFD flags line in `Forms/A350/A350MonitorManagerForm.BuildRows` (removed when Task 2 splits it, its only change there); tests `A350Pfd*Tests`, `A350FmaTests`, `A350FlashingLinesTests`, `A350TakeoffCalloutsTests`, `A350TcasCalloutsTests`, `A350MinimumsTests`, `A350PauseAlertsTests`, `A350MetricAltitudeTests`, `A350HudTests`, `A350HotkeysTests`, new `A350PfdMessageRowsTests.cs`, `A350GroundSpeedsTests.cs`; fixtures `p07_*.json`; `docs/inibuilds-a350.md` "The PFD and FMA", "Takeoff, flight phases and the T/D pause"; `docs/a350/evidence/07-pfd-callouts.md` (new).
- Never provoke a message in a way that risks the aircraft: a stall or windshear only as the OIS's own SITUATIONS or failures produce it, with the owner present, AP and A/THR ready; PULL UP and DH are already proven.
- Splitting the PFD flags row: a pilot who unticked the old row keeps those messages muted (each new key starts unticked when the old key is in the disabled list).

## Review Focus

- A message that flashes (two reads never agree on it): said once as flashing, never once per flash (Task 2 test on a flashing capture pair).
- The old "PFD flags" row unticked before the split: every message stays quiet after it (Task 2 test).
- LVR CLB flashing during a fast thrust reduction: said once, with "flashing", and its clear said only if the FMA column clears (Task 3 test).
- A TCAS RA reversal (CLIMB to DESCEND): the new advisory interrupts, never queues behind the old (Task 4 test).
- A metric altitude box read with `METRIC ALT` off: never said (Task 5 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/07-pfd-callouts.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 07`, with the `Plan: 07` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [sim, in flight]: The PFD's open checks

**Files:** create `docs/a350/evidence/07-pfd-callouts.md`; fixtures `p07_efis_<state>.json`; fixes in `A350Pfd.cs` (tests `A350PfdTests.cs`).

- [ ] **Step 1:** Capture the captain's EFIS in each state and read it with `A350Pfd`: clean configuration (the green "=" pinned at the top of the speed tape, atlas 1001,723: what it is, and read it if it is a speed); the green dot 45 and 55 kt from the speed (hidden as the tape hides it?); CONF FULL (no amber "=" at the foot?); the flap configuration digit while the flaps move; "FCU ALT ABOVE A/C" (where it is placed); the SIM RATE and SIM PAUSED lines with the reminder on.
- [ ] **Step 2:** Each misread fixed test-first on its capture (Steps 1-5 per item, test named for it), commit `fix(a350): the PFD reads <item>`; record each result.

### Task 2 [in flight]: The PFD messages, each a call-out with its own row

**Files:** `A350PfdFlagAnnouncer.cs`, `A350PfdMonitor.cs`, `Aircraft/A350/A350CalloutRows.Pfd.cs`, the PFD flags line in `A350MonitorManagerForm.BuildRows`; test `A350PfdMessageRowsTests.cs`; fixtures `p07_efis_msg_<message>.json`.

**Interfaces:**
- Each message `A350PfdFlagAnnouncer` knows (RWY TOO SHORT, MAX REVERSE, the rest of its list, windshear and stall as the PFD draws them) gets key `A350_PFD_MSG_<WORDS>` and the label "<message> call-out" in `A350CalloutRows.Pfd`; `A350PfdMonitor` mutes each by its key.
- [ ] **Step 1 [in flight]:** Hear each live and capture it: BTV armed for a short runway (RWY TOO SHORT, MAX REVERSE), the OIS's own SITUATIONS or failures for windshear and stall (owner present, AP and A/THR ready); record what was said and when.
- [ ] **Steps 2-6**, the usual five (tests `Each_pfd_message_has_its_own_row`, `A_muted_message_says_nothing`, `The_old_rows_mute_carries_to_every_message`, `A_flashing_message_is_said_once_as_flashing`); commit `feat(a350): each PFD message is its own call-out`.

### Task 3 [in flight]: LVR CLB as the aircraft shows it

**Files:** `A350FmaAnnouncer.cs`, `A350FlashingLines.cs`; tests `A350FmaTests.cs`, `A350FlashingLinesTests.cs`; fixtures `p07_efis_lvr_clb_on.json`, `_off.json`.

- [ ] **Step 1 [in flight]:** At the thrust reduction altitude, capture the EFIS several times over 2 s (`A350Probe draw live EFIS#0` in a loop, or `pfdlisten`): does the aircraft flash LVR CLB? The flight of 2026-10-31 heard "FMA autothrust: MAN, white; FLX, white; +67, cyan; LVR CLB, white" once, without "flashing".
- [ ] **Step 2:** If it flashes and the announcer did not say so, Steps 1-5 on the two captures (test `Lvr_clb_flashing_is_said_flashing_once`), commit `fix(a350): LVR CLB is said flashing`; else record that it does not flash.

### Task 4 [in flight] [owner]: TCAS advisories with real traffic

**Files:** `Aircraft/A350/A350TcasCallouts.cs`; test `A350TcasCalloutsTests.cs`.

- [ ] **Step 1:** In traffic that provokes advisories (an online network or a dense AI traffic area, the owner flying), log `INI_TCAS_CLIMB`, `_DESCENT` with `_NOW`, `_CROSS`, `_MORE`, `INI_TCAS_MAINTAIN_VS`, `_LEVEL_OFF`, `_CLEAR`, `INI_TCAS_RA_UP_VVI`, `_DOWN_VVI` and `INI_TCAS_VS_TGT` (`A350Probe sample`), and what MSFSBA said; compare the wording with `TcasRaGuidance`'s.
- [ ] **Step 2:** Each difference fixed test-first (test named for the advisory), commit `fix(a350): the TCAS <advisory> call-out`.

### Task 5 [in flight]: Metric altitude, the pauses and the take-off reads on 1.2.8

**Files:** `docs/a350/evidence/07-pfd-callouts.md`; fixes in `A350Pfd.cs`, `A350SimPause.cs`, `A350PauseAlerts.cs`, `A350TakeoffSpeeds.cs` as found.

- [ ] **Step 1:** With METRIC ALT on: "Selected altitude in metres" and "Altitude in metres" (the app's words) read from the PFD, A and Q say meters, the FCU stays in feet; off: no metric line.
- [ ] **Step 2:** The aircraft's own pause at the top of descent (OPTIONS > SIMULATION > PAUSE AT T.O.D on, the owner's choice) heard with the OIS window closed; RESUME heard.
- [ ] **Step 3:** The take-off speeds and minimums said as entered on 1.2.8 (the flight of 2026-10-31 read V1 140, VR 141, V2 146, FLEX 67 right); record.

### Task 6 [sim]: Green dot, S and F on the ground (Shift+1 to 3)

**Files:** `Aircraft/IniBuildsA350Definition.Hotkeys.cs` (`AnnounceTapeSpeed`), `Aircraft/A350/A350Hotkeys.cs`; test `A350GroundSpeedsTests.cs`.

Found in the test flights after the first: on the ground, with fuel and payload loaded and the V-speeds set, Shift+1 said the speed was not shown: the hotkeys read green dot, S and F only as symbols on the captain's speed tape (`A350Pfd.TapeSymbol`), which draws none on the ground. The First Officer reads the same speeds from the A350's own variables (`A350StateEvaluator.GreenDot` = `FMGEC_GD_SPD`, `SSpeed` = `FCPC_S_SPEED`, `FSpeed` = `FCPC_F_SPEED`), and the MFD's PERF T.O page shows green dot.
- [ ] **Step 1 [sim]:** On the ground after fuel, payload and the PERF T.O entries, then in flight: read the three variables beside what the MFD's PERF T.O page and the speed tape show; record when each holds a value and whether they agree.
- [ ] **Steps 2-6**, owner question 15's answer (recommended: the tape's value while it shows the symbol, else the variable's while it holds one, the sentence unchanged, "Green dot speed 213 knots"; "not shown" only when neither has one), the usual five (tests `On_the_ground_green_dot_comes_from_its_variable`, `In_flight_the_tape_still_wins`, `Neither_gives_not_shown`, `The_speed_reads_the_same_under_sv_SE`); commit `fix(a350): green dot, S and F are said on the ground`.

### Task 7 [sim]: Every weight in the aircraft's unit

**Files:** `A350Hotkeys.cs`, `Aircraft/IniBuildsA350Definition.Hotkeys.cs` as found.

- [ ] **Step 1:** With the aircraft set to kilograms, then pounds: every weight MSFSBA says (the fuel, weight and readout hotkeys) in that unit; the MFD's FUEL & LOAD page and the OIS LOADSHEET read as drawn (plans 09 and 06 if not). Each wrong unit fixed test-first with a de-DE test, commit `fix(a350): <read-out> says the aircraft's weight unit`.

## Handoff to plan 21

- **Pilot-facing:** "A350: each PFD message (RWY TOO SHORT, MAX REVERSE, …) has its own Ctrl+M row; LVR CLB said flashing; green dot, S and F said on the ground" (as found). Guide: the A350's PFD call-outs, its read-out keys and the Ctrl+M list; changelog `improvement`/`fix`.
- **Rule proposal:** none expected.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

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
