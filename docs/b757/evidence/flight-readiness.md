# Full-flight readiness: BlueBird's manuals against the prototype (2026-10-25)

Sources: BlueBird's v1.01 manuals of 12 October 2026 (Complete Flight Guide, User Procedures, Normal Checklist, EFB
Manual; the owner's copies, not committed), an inventory of the app on branch B757 at 9ef035f36, and live
measurements in the sim the same day (`live-findings.md`, sessions 5 and 6).

## What the manuals do not cover

Neither volume has an autoland procedure: no number of autopilots in CMD, no CMD step, no disconnect step. They give
only fragments: verify FLARE and ROLLOUT armed by 1,400 ft radio altitude (FP-2), LAND 2 or LAND 3 below 1,500 ft RA
(p. 210F), the autoland status annunciator (ASA) test states and the APP lockout (p. 190). Nothing on BBS Nest,
keyboard or joystick bindings, or how to turn the radio, transponder, baro or MCP knobs.

## Covered today

- Every regular panel switch, guard and button in the overhead, glareshield, forward panel and pedestal (418
  controls), with read-back; BBS Nest warning.
- MCP heading, speed (IAS), altitude and V/S entry and readouts.
- Both CDUs as text with every key: the whole ACARS flow (INIT FLT, route, winds, performance, WGT/BAL) runs there.
- EFB: Navigraph sign-in, AIRAC update, SimBrief ID, OFP fetch, units, presets, quick starts, and the instant fuel
  and payload load from SimBrief.
- AI reads of the ADI (FMA), EHSI and both EICAS screens.
- Through the simulator's own keys (measured): flaps (`FLAPS_INCR`/`UP` move BlueBird's lever), speedbrake arm
  (`SPOILERS_ARM_TOGGLE`), COM 1 standby (`COM_STBY_RADIO_SET_HZ`), transponder (`XPNDR_SET`, BCD), altimeters
  (`KOHLSMAN_SET` sets all three), minimums (`INCREASE/DECREASE_DECISION_HEIGHT`, 10 ft per step; read-only stock
  `DECISION HEIGHT`, which the ADI shows). BlueBird's WASM intercepts the stock gear, brake, throttle and trim events
  too (unmeasured in flight).

## Gaps for a full flight

| # | Need (manual phase) | Today | Proposed |
|---|---|---|---|
| 1 | Parking brake set/release (before start, after landing, parking) | not in the app (lever dropped under the vendor banner "RIGHT WINDOW RAIN EFFECTS"); the stock `PARKING_BRAKES` does not release it with PARKING BRAKE MODE = REALISTIC | panel control on `L:B757_FC_PARK_BRAKE` with read-back, plus a readout |
| 2 | TO/GA (takeoff thrust, go-around) and A/T disconnect | dropped (thrust-quadrant buttons) | buttons: TO/GA (`L:GO_AROUND_PADDLE1/2` click code, stock `AUTO_THROTTLE_TO_GA`), A/T disconnect (`L:B757_AT_DISC_1/2`) |
| 3 | Altimeters set (before start, descent, transition) | generic B / Ctrl+B do nothing on the 757 | Output B reads them; Input Ctrl+B sets all three by `KOHLSMAN_SET` (inHg or hPa, STD), as on the other Boeings |
| 4 | Minimums (descent preparation, autoland) | DH knobs dropped (endless knobs) | a minimums entry (radio DH, stepped 10 ft) and a readout |
| 5 | Transponder code (before start) | code knobs dropped | a "Set Squawk Code" field in the transponder panel (`XPNDR_SET`, BCD), as on the iFly |
| 6 | Flap, gear, speedbrake state (every phase) | levers work by the sim's keys, but nothing reads them back | Output L (flaps: lever detent and surface angle), Shift+G (gear), speedbrake state; spoken on change |
| 7 | Autopilot state (climb engagement, autoland: CMD L, C, R, APP) | MCP buttons say "pressed" only | read the MCP button lights (`L:B757_MCP_<button>_BUTTON`): a press reads back "CMD R on"; an Input Ctrl+P autopilot window with live states, as on the iFly, HS787 and MD-11 |
| 8 | Autobrake RTO / landing setting | 8 positions unnamed | name them 0 RTO, 1 OFF, 2 DISARM, 3-6 "1" to "4", 7 MAX AUTO (vendor panel states: takeoff 0, cold and dark 1, approach 2) |
| 9 | Fuel control switches RUN / CUTOFF (engine start, shutdown) | unnamed | PW: 0 CUTOFF, 2 RUN (vendor panel states) |
| 10 | Fuel and weight readouts (before start, cruise) | F, W do nothing on the 757 | Output F / Shift+F (tanks and total), W / Shift+W (gross weight) |
| 11 | Engine start monitoring (N2, EGT, fuel flow) | only AI reads of the EICAS | readouts from the stock engine variables, if valid on this aircraft (to measure at the first start) |
| 12 | FMA and the autoland status annunciator (LAND 3, NO LAND 3) | AI read of the ADI only; no variable exists (`wasm-findings.md` §5) | the ADI read also reads the AUTOLAND STATUS annunciator beside it |
| 13 | Nosewheel steering by rudder | BlueBird's default is REALISTIC (tiller) | a "Steering: rudder" button in the 757 EFB window (Settings > Aircraft switch, measured); the owner's is switched |
| 14 | Other unnamed positions (ND range and mode, IRS mode selectors, TCAS mode, alternate flaps) | numbers | names from the vendor's tooltips and panel states |

Gap 14, follow-up (ruling R32, 2026-10-25, Task 10 fix round 1): seven more controls the flows need still read as
numbers after 10a, and are named where BlueBird's nine panel states and the manual's procedure text agree: Seatbelts
and No Smoking 0 OFF, 2 ON (seat belts 0 in cold and dark and turnaround, 2 in the other seven; No Smoking 2 in all
nine, so its OFF rests on the identical seat belt switch and the manual's shutdown); the pack selectors 0 OFF, 1 AUTO
(0 in cold and dark, 1 in every powered state; "PACK SELECTORS ... OFF with no supply"); pressurization Mode Select
0 AUTO 1 ("mode selector AUTO 1 or AUTO 2"); the overhead ignition selector, relabeled Ignition (its node says ENG
START; tooltip 'Engine Ignitors'), 0 "1" ("Ignition 1 is the illustrated example"); Bank Limit 0 AUTO ("BANK LIMIT
SELECTOR ... AUTO"); the EICAS thrust reference 1 BOTH ("thrust reference BOTH"); the wiper 0 OFF (0 in all nine).
Every other position stays "Position n". Limit: the in-sim preset check reads back the very values the names were
taken from, so a misnamed position would not show there.

Beyond the 14 gaps (ruling R31): BlueBird's INITIAL CHOCKS IN PLACE (the owner's is ON) puts wheel chocks on at every
flight load, and the aircraft will not move with them. The only control is the CHOCKS tile on the EFB's Ground
Services page, which toggles `L:BBS757_vis_chocks` (measured: `live-findings.md`, EFB Ground Services). The 757 EFB
window gains Remove chocks / Set chocks (read first, tapped only when they differ) and the Status display a Chocks
row.

Seen while measuring: BlueBird's left thrust lever read 100% and the right 0% with the engines off (a hardware throttle
axis or a leftover). Check the thrust levers before engine start.
