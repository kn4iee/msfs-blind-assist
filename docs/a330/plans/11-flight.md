# A330 Flight Plan (plan 11, audit)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. This plan is live work with the owner in the simulator; it changes no code. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own.

**Goal:** One flight, gate to gate, with the First Officer, proves what only a flight can and collects the measurements the other plans wait for (spec section 6: M7, M8, M10, M11, and the visual-guidance and taxi numbers plan 10 hands over). Every gap found goes to the plan owning the file.

**Architecture:** Four scenarios in the order of a flight, each a short numbered list of what the owner does, the keys, and the words to expect. Claude follows through the logs and the reader program's commands, never the desktop. What is heard and measured goes into one evidence file; nothing is fixed here.

**Tech Stack:** MSFS 2024, the A330, a SimBrief plan of about an hour between two airports with an ILS; the reader program's commands; the app's logs.

**Spec:** `docs/a330/spec.md` sections 3 (R19), 4, 6.

## Global Constraints

- Spec section 4's simulator rules apply to every step. The owner flies; Claude reads.
- No code changes. A gap is written in the evidence file with the plan that owns its file, and told to that plan's claimant; a plan already landed gets it as a handoff to plan 12 or is reopened by the owner.
- A scenario the flight does not give (no uplink, no go-around) is skipped and recorded as skipped: never staged at the cost of the flight.
- Before the flight: a Debug build of the branch with plans 03 to 10 landed; `fma.log`, the reader's log and `debug.log` cleared or their sizes noted; the route in SimBrief; MSFSBA running from the build; the EFB's chocks and covers press still present (plan 12 removes it afterwards).

## Review Focus

- A measurement written from memory after the flight: each is copied from a log line or a reader command's line with its time.
- A flight mode spoken as a number at the end of the flight: every such number is listed with the PFD's text at that time, for plan 09.
- A First Officer step that said done while the cockpit disagreed: listed with the step and the control's read, for plan 06.

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 11` (`Plan: 11`). Start once plans 03 to 10 say landed (a plan waiting only on this flight counts as landed).

### Task 1 [owner] [sim]: At the gate

- [ ] **Step 1:** Cold and dark at a stand. Shift+C: the First Officer's Electrical Power Up and Preflight flows. Expect each step spoken once and true in the cockpit; the ADIRS reminder if it stays the Captain's.
- [ ] **Step 2:** The EFB (from BA), in the order measured on 2026-11-20: Options, check the weight unit matches the OFP's (spec D11); Dashboard, "Import from SimBrief"; Payload, import and load; Ground, remove the covers; the chocks when ready.
- [ ] **Step 3:** Shift+M, with FROM/TO still empty: the INIT page's request (spec D12). Expect INIT A filled and the whole flight plan in. Then INIT B: 1R twice for the zero fuel weight, 3R twice for fuel planning. Check the departure, the ocean or oceanic-style coordinates if any, the STAR and the approach on the F-PLN, slewing with Page Up (later rows) and Page Down (spec D10). Then the EFB's Take Off page: sync, calculate, "Send to FMS", and on PERF TAKE OFF confirm the uplinked data (6R). Expect every typed character confirmed, every page read complete, no camera movement.
- [ ] **Step 4:** Output Alt+E and Alt+S: the memos and the DOOR and FUEL pages as the First Officer works. Before Start and Engine Start flows; After Start.
- [ ] **Record:** times to a complete MCDU page (the reader's log), every FMS message spoken, any step the First Officer reported wrongly (M8).

### Task 2 [owner] [in flight]: Take-off and climb

- [ ] **Step 1:** Taxi with taxi guidance: note where a turn came early or late (the taxi turn lead, for plan 10).
- [ ] **Step 2:** Take-off: V1, Rotate, V2 spoken; each flight mode change spoken in the PFD's words. After lift-off, from the autopilot window (Ctrl+P): AP 1, then A/THR if it is not active (M7).
- [ ] **Step 3:** Passing the transition altitude: Standard from Ctrl+B on the captain's side, then the first officer's (M7). A heading and an altitude change on the FCU through Ctrl+H and Ctrl+A, and one on hardware or a sim key: the knob call-out.
- [ ] **Record:** the PFD band's words against the flight mode call-outs heard, and `fma.log` (M10); what each press read back; the First Officer's After Takeoff flow.

### Task 3 [owner] [in flight]: Cruise and descent

- [ ] **Step 1:** MCDU in flight: PROG, a DIR TO, a LAT REV with an airway, the OVERFLY key on one waypoint (M4's last item), FUEL PRED, the arrival (runway, STAR, transition).
- [ ] **Step 2:** Output Alt+N: the next waypoint and the map items against the F-PLN page; two modes, two ranges (M11). Output Alt+S: CRUISE and one system page. Each read-out key once.
- [ ] **Step 3:** If the owner wants one: a datalink message to the DCDU and its reply (M11). Otherwise skipped.
- [ ] **Step 4:** Descent: the First Officer's Descent and Approach flows; QNH from Ctrl+B passing the transition level.
- [ ] **Record:** each page's reads; the reader program's `bench` figures taken in cruise for the MCDU, E/WD, ND and PFD (M1 in flight, M9); the flight-phase variable's values through the flight (plan 10).

### Task 4 [owner] [in flight]: Approach, landing and the stand

- [ ] **Step 1:** An ILS approach: LOC, then APPR, from the autopilot window (M7); each capture spoken from the flight modes; the approach capability line.
- [ ] **Step 2:** If the weather allows a hand-flown final with visual guidance: the tones against the PAPI or the glideslope at 500, 300 and 100 feet, and the flare cue's height (the two biases, for plan 10, measured apart).
- [ ] **Step 3:** Landing: the gear confirmation, the ground spoilers armed, the rollout and exit guidance, After Landing, taxi in, Parking, Shutdown and Securing with the First Officer.
- [ ] **Record:** every flight mode still spoken as a number, with the PFD's text; the First Officer's reports; the biases and the turn lead.

### Task 5: The evidence and the handoffs

**Files:** Create `docs/a330/evidence/flight-<date>.md`.

- [ ] **Step 1:** The evidence file: the simulator's version, the route, the times, and one section a measurement (M7, M8, M10, M11, M1 and M9 in flight, the visual-guidance and taxi numbers), each with its log lines or the reader command's lines.
- [ ] **Step 2:** A table of gaps: what was heard or measured, the plan owning the file, and whether its claimant was told. Each claimant fixes test-first in their own plan (reopened if landed) and records it there.
- [ ] **Step 3:** A second flight only for what the first could not give and a claimant needs to close a plan; say which.

## Handoff to plan 12

- **Pilot-facing:** that the aircraft has been flown gate to gate; the guide's "not yet flown" note can go.
- **For the pull request:** the flight's date and route, and the last commit it was flown on.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** not started
- **Claimed by:** none
- **Worked by:**
- **Started:**
- **Landed:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
