# Plan 10 evidence: the First Officer flown on the PW, measurements for its waits

Measured on Declan's PC, BlueBird 757-200 PW, BBS Nest running. Session 1 on 2026-10-27; session 2 on
2026-10-28 in MSFS 2020, cold and dark at a gate. Tools: the app's own First Officer window driven over UI
Automation (the flows started from its Flows tab and their step lists read back), `debug.log`'s `[FO]` and
`[BBS757]` lines, and the simconnect MCP for spot reads.

## 1. Engine start (PW), measured 2026-10-27

- N2 rises while cranking: 25 % about 25 s after the selector reaches GND.
- The starter cuts out at about 52 % N2. Idle: 59.5 % N2, 20 % N1, 240 °C EGT.
- After fuel CUTOFF: N2 below 10 % in 43 s and at zero in about 70 s.
- `B757_ENG_VARIANT` reads 1 on the PW.
- Isolation valve: 1 = open.
- The duct-pressure L:var reads only about 4-6 during a start, so it is not in psi. No step reads it.

## 2. APU, measured 2026-10-27 and 2026-10-28

- The APU runs about 60-80 s after START (2026-10-28: 58 s from the START write to `B757_APU_STATE` = 2).
- **The selector springs START back to ON before the read-back.** 2026-10-28: the FO wrote START (2). After
  1.5 s the read-back gave 1, so `FoSetAsync` reported "target 2 not reached" and the step was marked skipped.
  The APU started anyway, the same way at Before Start and After Landing. `StartApuAsync` now accepts a
  selector reading ON after the START write; only a selector below ON fails.
- APU OFF: 2026-10-27, the APU took about 110 s to come down. 2026-10-28, `B757_APU_STATE` read 0 after 18 s
  of Final Securing's wait. The battery then went OFF at once, before the stock APU RPM had been read. Since
  the manual says to keep battery power until the APU has stopped (E199), the battery is now refused while the
  APU state is not 0.

## 3. The First Officer's flows on the PW, 2026-10-28

Cold and dark at a gate, no GPU at first. The pilot connected the GPU on the EFB during Preflight.

| Flow | Result | Notes |
|---|---|---|
| Power Up | complete | Battery, standby power, electrical panel, IRS NAV (two clicks in one write; all three read 2), position lights. With no GPU: "No ground power available" to the Captain. Re-run with the GPU on: external power ticked ON. |
| Preflight | complete, 26/26 | 68 s. Re-run with the GPU connected: every switch "already set", none reverted on power-up. |
| Before Start | complete, 2 skipped | **IRS: ALIGNED** failed: a 5 s check about 3 min after NAV, while a normal alignment takes about 10 min. The check now comes last and waits up to 10 min. **APU: start** falsely failed (section 2). Hydraulic pumps R-C-L, fuel pumps, seat belts, APU bleed, TA/RA and beacon all set. |
| Engine Start | complete, 15/15 | 135 s for both engines, right first: GND, fuel point at 23 s, RUN, cutout at 27 s, running at 30 s. Left the same within 2 s. |
| After Start | complete | Packs AUTO, isolation closed, APU bleed off, APU off, speedbrake down, autobrake RTO. The flaps line ticked once the Captain set them. |
| Before Takeoff | complete | Landing, strobe and turnoff lights on. Wing lights were missing; added after the manual review. |
| After Landing | complete, 1 skipped | Run on the ground at the gate. The same false APU-start failure. |
| Shutdown | complete, 15/15 | Fuel CUTOFF to engines stopped: 45 s. |
| Final Securing | complete | IRS off, emergency lights off, APU off (state 0 after 18 s), external power off, battery off. |

Not run: After Takeoff, Descent and Before Landing (they need a flight).

## 4. The First Officer's flows on the RR, 2026-10-28 (after the fixes)

The same session, BlueBird 757-200 RR ("BlueBird 757-200 RR": the title gives the variant, and `B757_ENG_VARIANT`
reads 0). Cold and dark, with a GPU connected. Run on the build with the manual-review fixes.

| Flow | Result | Notes |
|---|---|---|
| Power Up | complete | No external power on line, so Power Up started the APU itself (running about 55 s later) and reported no false failure. |
| Preflight | complete, 27/27 | Engine limiters and EEC already ON. A/T ARM read BlueBird's armed flag (`L:BBS_AUTO_THROTTLE` = 1). |
| Before Start | complete | 1st run: **Parking brake: SET failed** (section 5). After the fix the brake set. External power went OFF once the APU was on line, then the Captain's GPU reminder. IRS aligned within its wait. |
| Engine Start | complete, 18/18 | 113 s for both engines. Fuel point at about 22 s, light-off at once, cutout at about 2 s. The fuel switch went to RUN (`L:BBS_fuel_RR`/`RR2` = 0; RICH only below 0 °C EGT). **RR idle: 50.5-50.8 % N2, 20.4 % N1**, EGT 356 °C settling, so `RrRunningN2` 48 has margin. |
| After Start | complete, flaps line timed out | "Flaps: set for takeoff" waited 120 s for the Captain, who had not set them yet. That is correct behaviour. |
| Before Takeoff | complete | Landing, strobe, wing and turnoff lights on. |
| After Landing | complete, 14/14 | Wing anti-ice off and wing lights off; APU start with no false failure. |
| Shutdown | complete, 21/21 | Engines stopped 33 s after CUTOFF. Stabilizer reminder, APU bleed ON, exterior lights position-only, transponder STBY. |
| Final Securing | complete | The APU took 80 s to read stopped; the battery went OFF only after that. |

## 5. The parking brake needs full braking in the same frame (REALISTIC mode)

BlueBird's PARKING BRAKE MODE was REALISTIC ("needs full braking", wasm-findings.md). With the brake released:

- The lever's own write (`(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)`) left it at 0.
- Full brake pedals (`16383 (>K:AXIS_LEFT_BRAKE_SET)` and right) in one write, then the lever in a second: still 0.
- Full brake pedals, `(>K:BRAKES)` and `1 (>L:B757_FC_PARK_BRAKE,bool)` in ONE calculator string: 1. It stayed 1
  after the pedals were let go (`-16383` on both axes). Stock `BRAKE LEFT POSITION` then read 100 % (BlueBird
  holding the brakes with the parking brake set).

The First Officer now sets the brake that way (`FoSetParkingBrakeAsync`) and always lets the pedals go afterwards;
it never releases the brake (pilot's job). The panel's own Parking Brake control (plan 03) writes the lever alone,
so it is likely to fail the same way in REALISTIC mode: for its owner to check.

## 6. The ILS frequency in MSFS 2020

Setting the ILS frequency in the Nav Radios window (Input Ctrl+N) always ended "the ILS display could not be
read" in MSFS 2020. The course worked (a direct write). Cause: the reader lays its copy of the ILS display over the
sim's `mainUi` page, which MSFS 2024 has and MSFS 2020 does not. In 2020 only the toolbar page
(`coui://html_UI/Pages/ToolBar/ToolBar.html`, untitled) drew a test box over the whole window; InGameMarkers and
Pause did not. With the reader on that page in 2020 only (`CoherentBbs757IlsDisplay(fs2020: true)`): 110.50 set
and read back ("ILS 110.50 set"); every capture decoded. 2024 reads are unchanged (mainUi only, pinned by a test).

## 7. Pilot's notes from the run

- "The APU kept starting properly but said something skipped": the false failure above.
- "Should it arm A/T?": the Preflight A/T line read the MCP switch only, so it said "already set" without
  BlueBird's armed flag. It now reads `BBS757_AT_ARMED` (as the review found too; R40).
- LNAV/VNAV stay with the pilot. The 737 First Officers' optional after-takeoff LNAV/VNAV push is not in this
  plan.

## 8. Still to measure

- A cold RR start (EGT at or below 0 °C) to see the RICH position used.
- A failed start, to see the light-off watch and the abort (not provoked).
- The air flows (After Takeoff, Descent, Before Landing) in flight.
- The GPU: `BBS_GPU_CART_CMD=1` started the cart's engine but did not connect it; writing 0 back left the cart
  running (2026-10-27). The First Officer leaves the GPU to the pilot on the EFB.
