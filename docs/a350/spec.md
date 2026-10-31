# iniBuilds A350: the work after the first full flight (spec for the plans in `plans/`)

**Read this first, then `plans/00-index.md`.** Background: `docs/inibuilds-a350.md` (how the A350 works in MSFSBA),
`.claude/rules/inibuilds-a350.md` (A350-1 to A350-12), `tools/A350Probe/README.md` (the live harness).

## 1. Where this comes from

- **Francesco's handoff** (francescotissera1211, 2026-10-31, written at 7e6d5fbd1 "for the Claude picking this up on
  Gus's machine"). Its goal stands for every plan: the iniBuilds A350 at study level for a blind pilot, everything a sighted
  pilot can see or do readable and doable from MSFSBA. Its remaining work is section 3 below; its rules are section 4.
  The file itself is not in the repository.
- **The first full flight on the branch** (Robin Kipp, 2026-10-31, KATL to KDTW, A350 1.2.8, the Release build of
  7e6d5fbd1). The A350 findings are section 2; the log excerpts are `evidence/flight-2026-10-31.md`.
- **The installed version.** Pilots now have A350 **1.2.8** (released 2026-10-30); the branch was measured on 1.2.7.
  The flight read correctly on 1.2.8, but the control map, the address table in `docs/inibuilds-a350.md` and the OIS
  fixtures still name 1.2.7, and the handoff's "Do not chase" list must be re-checked on any new version.

A plan's "Spec:" line cites the sections here it argues from. The spec is frozen: a ruling made while working goes in
the plan's execution record; only the owner writes in section 5's Answer column.

## 2. The flight's A350 findings

### 2.1 The Altimeter window half-applies a typed QNH (plan 02)

At FL200, both sides on STD in hectopascals, the pilot typed 30.5 in the Altimeter window (Ctrl+B). Both sides refused
it in their unit (the same sentence twice, no side named), the ISIS (which takes either unit by size) went ahead, and its
short write and knob click took both sides off STD to QNH 1013. The ISIS itself did not take the value ("did not take
30.5"). After a unit change, the second entry set 30.50 as QNH on all three while still above the transition level,
where the procedure sets a preset on STD. Every knob click in both entries landed on 1013.25, whichever knob, and only
the direct corrections set the value: the OIS option LINKED INSTR (`INI_LINKED_INSTRUMENTS`, its click runs `LinkBaros`
and copies one baro to the others) was most likely on. The window plans three targets independently
(`A350BaroWindow` calls `PressRow` for each), which this option breaks. Also asked of the FCU windows' owner: "FCU
changes from external hardware announced" (handoff backlog).

### 2.2 Draw-engine traps and a transform the reader skips (plan 03)

Two traps in 3.6 hours, both memory faults at an address past the copy's linear memory, both in flight-plan drawing
while the plan changed (the ND's `ND_Logic::drawModernConnections` during an F-PLN edit, the MFD's
`FlightPlanButtonNew::draw` at touchdown). Each was retried on a fresh copy and drew, as designed ([A350-2], [A350-9]);
nothing said was lost or repeated. The log names neither the cause nor the cost, and the minute line does not say the
retry drew. Separately the reader warned once that the draw code called `nvgTransform`, which it does not apply, after
an MFD click in the menu bar: positions after it may be wrong. The address table ("Addresses for 1.2.7") and the probe's
`sdmap`/`sdsweep` layout check name 1.2.7.

### 2.3 E/WD memos change color and come back (plan 04)

COMPANY MSG (green) and WXR/PWS OFF (amber) swapped their colors for about a second, about 15 times between take-off and
landing, each swap said twice (there and back). The landing memo was said in full again a second after an AP OFF warning
was cleared, every line of it heard minutes before. The handoff's open check on `INI_CABIN_IS_READY` (CABIN READY and
COMPANY MSG repeating about every 100 s in an earlier flight) is likely the same family.

### 2.4 The PARKING checklist's refusal (not A350-specific: section 7)

14 s after the engine masters went off, while the engines spun down, the First Officer said "Fuel pumps left on: not
read on the ground, or an engine is not read stopped." That was a checklist tick: the Parking flow itself waits up to
120 s for both engines to read stopped before the pumps (`A350FlowDefinitions`, `PK_ENGINES_STOPPED`), and a tick
refuses at once (FO-25). The sentence, which names neither condition, is the same on the Fenix, A32NX, A330, A380 and
iFly 737, so the A350 is not the odd one out (CLAUDE.md, "Before changing behaviour"): a change is an app-wide proposal
(question 5).

### 2.5 The OIS's sign-in code is kept in the speech log (plan 06)

The OIS's Navigraph pop-up and its code were spoken (they must be) and written to `a350_speech.log` as plain text. The
handoff's rule is that such values are never echoed, and [A350-12] keeps login codes out of fixtures; the speech log
should keep them out too.

### 2.6 What the flight proved, for the plans that check it

- Memory healthy (MSFSBA 1.3 to 1.5 GB, the A350 heap 44 to 66 MB, free commit 46 GB or more); no crash.
- The take-off speeds and flex read from memory matched the OIS on 1.2.8.
- LVR CLB heard once on the new FMA code: "FMA autothrust: MAN, white; FLX, white; +67, cyan; LVR CLB, white" (16:36:52):
  whether the aircraft flashed it, and whether MSFSBA should say so, is plan 07's check.
- The First Officer moved no flap and no gear: its flap margin is still not flown (plan 05).
- Taxi guidance ran out and in; the turn lead (`TaxiTurnLeadSeconds` 1.7) is still to be judged (plan 08).

## 3. The handoff's remaining work, by plan

| Handoff item | Plan |
| --- | --- |
| 3.3 Fresh review of `345bfd07f`..`7e6d5fbd1` | 01 |
| 3.2 Every OIS EQUIPMENT and AIRCRAFT option, on and off | 06 (LINKED INSTR's baro effect: 02) |
| 3.1 Cover-to-cover manual pass, a ledger line per item | 12 to 17, by page range (section 6) |
| 4 Built, never proven: TCAS TA/RA; LVR CLB | 07 |
| 4 Built, never proven: FO `ExtendWithinKts = 3` | 05 |
| 4 Built, never proven: Alt+M (MFD MAILBOX key) in flight; mailbox SEND and uplinks | 09 |
| 4 Built, never proven: taxi lead tone | 08 |
| 4 Open checks: PFD; PFD messages as call-outs; SIM RATE/SIM PAUSED lines; T10 METRIC ALT's PFD boxes | 07 |
| 4 Open checks: `INI_CABIN_IS_READY`; engine and reverser status; flight phase values; W3 SD pages | 04 |
| 4 Open checks: waypoint passing; ANF | 08 |
| 4 Open checks: X12 KCCU; KG units on every weight read | 09 |
| 4 Open checks: W4 RMP | 10 |
| 4 Open checks: panels and SND keys; gear position and UNLK lights; T8 IRS; the panel backlog (CVR, oxygen, ELT, chronos, trims, lights one at a time) | 11 |
| 4 Open checks: auto-gear (stock GEAR_UP/DOWN against the A350's own `INI.GEAR` events); T3 lowest AP engage height; all normal checklists end to end | 05 |
| 4 Open checks: FCU changes from external hardware | 02 |
| 4 Open checks: OIS powered/unpowered, OIS field titles, duplicated pop-up text, GROUND EQUIP colors as words | 06 |
| 4 Last: tools portable | 20 (the OIS harness: 06) |
| 4 Last: a full SimBrief A-to-B, cold and dark to securing | 17 (the walk-through), then 21's regression flight |
| 4 Do not chase (re-check on any new version) | each plan for its area on 1.2.8; 21 records the list |
| 5.1 Visual guidance from the displaced threshold | 18 (shared pull request off `main`, then the A350's biases) |
| 5.2 First Officer flaps on the other Airbus types | 19 (shared pull requests off `main`; after 05) |
| 6 Already proven | not redone unless a change touches it |

## 4. The team's rules (every plan's Global Constraints include these)

From the handoff, kept as written there; CLAUDE.md applies in full besides.

**Safety and the machine.** Never write into the sim's memory ([A350-1]). Never enter credentials (SimBrief ID,
SayIntentions key, Hoppie logon, Navigraph login): the human signs in; never echo those values, log them or put them in
fixtures; on OIS OPTIONS > 3RD PARTY, structure and field lengths only. Decline consent dialogs. Don't close the owner's
other apps; check what a process is before killing it. No builds under 10 GB free commit.

**The sim.** Never call `msfs_load_flight` on the A350 (it wrecks the aircraft; restore only through the OIS saves).
Never pause the sim unless the human says so (paused, the WASM ignores input). Never fly by tool calls: before a critical
phase AP and A/THR on, stable early, OIS SAVE NOW. Never hand control to nobody on a scripted take-off: engage the AP in
the same process that flew it; prefer an in-air spawn when the take-off is not what is tested. Watch memory: stop by
15 GB free commit, checkpoint first (OIS SAVE NOW and `msfs_save_flight`), then restart; time compression slows the
growth. The handoff's "Flying facts that cost me aircraft" (lever detents, SRS take-off, phases, go-around re-arming,
TOGA with flaps up, cruise under TC, taxi, placing the aircraft, OIS options that pause) apply to every flight a plan
asks for.

**Standards.** Complete, no hierarchy: cover and live-validate every item, never a "key" subset. No simplification:
report state and let the pilot decide; never block or auto-complete. No shortcuts: if the cockpit, OIS or MFD can reach
it, use that path (parity hotkeys are the exception). Match the released aircraft (the A380 for panels, typed entry,
announcements, displays and hotkeys). Every auto-announce gets its own Ctrl+M row; buttons show their status; each
display item's color is said; announce only what would interrupt a sighted pilot too. Research before each phase (the
real procedure, the add-on manual, the live sim; never one manual line). Multiple passes, on the ground and in the air;
ECAM from cold and dark.

**Git.** Branch `feat/inibuilds-a350` on `kn4iee/msfs-blind-assist`; push every commit once its task is green (the
index's workflow). No pull request until the owner says; it opens with a disclaimer crediting Gus Pacleb and Tyler Wood.
The A350 work is confidential: keep it out of anything public, including shared-code pull requests (plans 18 and 19
never name it). Commit messages end with a `Co-Authored-By` line for the model in use. Don't push the owner's
`local-build` branch or to Marshall's pull requests. Never commit scratch test files. Superpowers specs and SDD ledgers
stay untracked (`docs/superpowers/`, `.superpowers/sdd/`); this folder is tracked so a claim can be a pushed commit, and
plan 21 deletes it before the pull request merges.

## 5. Questions for the owner

Each plan builds the recommendation unless the Answer says otherwise; a plan that reaches its question first may ask
it then. Only the owner fills in "Answer" (with the date).

| # | Plan | Question | Recommendation | Answer |
| --- | --- | --- | --- | --- |
| 1 | 02 | The sides are in hectopascals and the pilot types inches (or the reverse): refuse the whole entry, or convert by size as the ISIS does? | Refuse it all in one sentence naming each side's unit (the A380's window checks each side in its own unit) | |
| 2 | 02 | With LINKED INSTR on, may the window write the captain's side only and let the aircraft copy it, as one knob turned in the cockpit does? | Yes, once plan 02's Task 1 shows the copy | |
| 3 | 04 | A memo line that comes back unchanged within 10 s (after an alert is cleared): say it again? | No | |
| 4 | 04 | If the aircraft itself flips a memo's color for about a second: say the flip? | No: a color change is said once it has held 3 s | |
| 5 | — | A checklist tick that leaves the fuel pumps, IRS or batteries on says "not read on the ground, or an engine is not read stopped" on every aircraft: name the condition that failed ("engine 2 still turning"), for all aircraft, in a shared pull request off `main`? | Yes, its own pull request, outside these plans | |
| 6 | 07 | The PFD messages (RWY TOO SHORT, MAX REVERSE, windshear, stall, ...): one Ctrl+M row each, or the one "PFD flags" row? | One row each (the handoff: every auto-announce its own row) | |
| 7 | 08 | When the ND's TO waypoint changes: what is said, and is the row on by default? | "Next waypoint {name}", its own row, on | |
| 8 | 09 | May plan 09 send a real CPDLC request through the mailbox (owner signed in, owner present)? | Yes: a logon, one departure clearance request, a logoff | |
| 9 | 10 | STBY RAD NAV has no cockpit writer (documented in iniBuilds' FAQ): offer a row writing its L:var? | Decide after plan 10's Task 2, from what it finds | |
| 10 | 18, 19 | Open the shared pull requests off `main` (they change released aircraft)? | Yes, each after its measurements, with its in-sim test plan | |
| 11 | 19 | The other Airbus flap fixes: one pull request per aircraft, or one for all? | One per aircraft (CLAUDE.md: each gets its own in-sim test and review) | |

## 6. The manual by page range (plans 12 to 17)

"Airbus A350 Manual 1.5" is `Resources\Airbus A350 Manual 1.5.pdf` in the installed package (128 pages). Page numbers
here are the PDF's; the manual's own numbers are one lower. Several pages are images the text extraction drops (the MFD
menu map): render them (Windows PowerShell 5.1 and `Windows.Data.Pdf`, or Git Bash's `pdftotext` for the text layer).

| Plan | PDF pages | The manual's chapters |
| --- | --- | --- |
| 12 | 1 to 30 | Preface, About, Aircraft Selection and Liveries, Cockpit Interaction, Checklists, Control Sensitivity, Limitations, Aircraft Configuration Summary, A350 Specifications, Important notes (FMA, lights out, fly by wire), Cockpit Layout, Other Controls (HUD, sun visors, the captain's side, oxygen mask) |
| 13 | 31 to 40 | OIS: Mission (FLT OPS PLAN, LOADSHEET, charts), Performance (T.O PERF, LDG PERF), Utilities (AOC, MAINTENANCE, FAILURES, AUTOSAVE, PANEL STATES) |
| (06) | 41 to 51 | OIS OPTIONS (AIRCRAFT, EQUIPMENT, PRINTER, GPS JAMMING, throttle, CALLOUTS, VOLUME, 3RD PARTY, SIMULATION): plan 06 keeps their ledger lines |
| 14 | 52 to 63 | Aircraft Systems: FCU, MFD and KCCU, Electronic Checklist, EFIS, EFIS Control Panel |
| 15 | 64 to 80 | Aircraft Systems: ANF, BTV, Noise Function, thrust levers, radio and transponder, RMP, TCAS, HUD symbols |
| 16 | 81 to 86, 124 to 128 | Failures and Abnormal Procedures (ECAM, sensed and not sensed, STATUS, deferred procedures, automatic emergency descent); the Checklists |
| 17 | 87 to 123 | Operations and Techniques: the walk-through, cold and dark to securing, flown as a whole SimBrief flight |

## 7. Not in these plans

The flight's shared findings (`evidence/flight-2026-10-31.md`, last section) go to their own sessions and pull requests
off `main`, never naming the A350: the taxi turn warning lost behind a short route segment, a positive-rate reminder
when the automation's gear-up is off, GSX's invoice read at 350 ft, the ground traffic "Stop" while docking, the status
box's forced reads logged every second, and (question 5) the checklist ticks' refusal naming the condition that failed. The GSX stop position for the A359 stays the known limit ("GSX cannot tell BA its
stop marks").
