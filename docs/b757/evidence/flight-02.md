# Plan 02 in-sim checks: the control map on BlueBird's 757 (2026-10-26 to 27)

On the ground at the gate, BlueBird 757-200 PW, BBS Nest running, MSFSBA closed. The owner (Robin) handed the aircraft
over for testing and confirmed the first check himself. The rest were run by Claude: every write is the exact
calculator call the app sends for that pick (from `Bbs757Write.ToPosition`, `Bbs757Guarded.Plan`, `PressFor` and the
fire handle relock), with the app's inert `N 0 *` prefix (`ExecuteCalculatorCodeUnique`), sent through MobiFlight by
the simconnect MCP. Reads: all 2,173 BlueBird L:vars in one Coherent call (`docs/b757/tools/lvar-snapshot/snap.ps1`,
`live/snap.js`) 1.5 s after each pick (the app's read-back delay), diffed with `diff_snaps.py` and the session 8 noise
list plus the bogie friction, cabin altitude difference, F/O VSI needle and M730 battery percent; a 50 ms page-side
recorder in the `BBSTablet` view where a value moves for under a second; stock SimVars for downstream effects. Power:
the tablet's Before start panel state (APU, battery, electric hydraulic pumps); the aircraft was put back in Cold and
dark afterwards.

What this does not check: what the app shows and says (combos, read-backs, Shift+G, "Flaps 5"), since the app was not
running. The calls are the app's own, so BlueBird's response to them is what was measured.

## Results

| # | Check | Result |
|---|---|---|
| 1 | Gear Lever UP on the ground | Passed (owner, before this run): the gear stays down. |
| 2 | Emergency Lights OFF, ARMED, ON, ARMED | Passed. Each pick opens or closes the guard as planned; OFF and ON light UNARMED (`B757_OVHD_PASS_OXY_UNARMED_DECAL`), ON lights the cabin (`BBS757_CABIN_EMER_LIGHTING` 1), ARMED clears both. |
| 2 | Stab Trim C Cutout CUTOUT, NORMAL | Passed. The guard (`B757_AISLE_STAND_STAB_1_GAURD_SWITCH`, not in `snap.js`'s names, read directly) opens for CUTOUT and closes for NORMAL; nothing else moves on the ground. |
| 3 | Mode Select AUTO 1, AUTO 2, MAN, AUTO 1 | Passed. MAN lights `B757_OVHD_CAB_ALT_CTRL_AUTO_INOP_DECAL`; AUTO 1 clears it. |
| 3 | TCAS Mode STBY, ALT RPTG OFF, XPNDR, TA ONLY, TA/RA | Passed. Stock `TRANSPONDER STATE:1`: 1 (standby) in STBY, 5 (ground) in ALT RPTG OFF and XPNDR on the ground, 4 (altitude) in TA/RA. |
| 3 | C1, C2, L and R ELEC Pump off and on | Passed: each switches its own pump (`B757_HYD_PUMP_ACMP_*_RPM`, its ELEC and SYS PRESS lights). A pump takes a few seconds to restart; C2 runs on demand while C1 holds pressure. |
| 4 | Chrono ET RUN, HOLD, RESET, captain and F/O | Passed. RESET reaches 3 and BlueBird springs it back to HOLD after 0.4 s, as the real clock's RESET does. The ET count itself (hours and minutes on the clock's display) lives inside BlueBird's module with no variable, so nothing outside can read it; `BBS_CHRONO_ELAPSED_TIME` is the chronograph, not the ET (below). |
| 5 | Flight deck door Deny, Unlocked | Passed. Each lands (read straight after the write: 2; the recorder: 0 from 0.67 s) and BlueBird springs it back to Auto about 0.9 s later (a click would stamp `O:UpdateTime`, which the calculator cannot, so the app's hold is probably shorter). That does not matter: BlueBird's systems module (`BBS752.wasm`) never reads the switch, whose only references are its own cockpit component, and nothing simulates an entry request, so the switch is cosmetic. |
| 6 | Fuel pump buttons (the vendor slip) | Passed. The sim runs the code up to the slipped write: L Pumps FWD goes off and on; C Pumps L on lights the center pump's PRESS light (center tank empty), off clears it. The `_ON` write is lost (`_ON` stays 0; BlueBird's own panel state leaves it 0 too). Stock `FUELSYSTEM PUMP ACTIVE:1-4` did not follow within 4 s. |
| 6 | APU fire handle pull and stow (the dangling `and`) | Passed. Unlock, then the pull inside the unlock window: Pulled, APU FAULT, the APU shuts down. After the app's 1.5 s relock the same press stows it. |
| 7 | Gear Lever OFF and DOWN (on the ground) | Passed. 0.5 holds with all three legs down; DOWN returns. Stock `GEAR HANDLE POSITION` stays 1 (it has no OFF). UP and OFF airborne and Shift+G: not run. |
| 7 | Flap Lever 5 and UP | Passed. Lever 41, stock `FLAPS HANDLE INDEX` 2; with hydraulic pressure the slats and flaps extend (`BBS757_FC_*`, stock leading edge 36% at 10 s); UP retracts. Without pressure (pumps stopped) the handle follows but the surfaces stay put. |
| 8 | Task 8, Altitude Intervene | Not run: needs a VNAV climb or descent with a constraint. |

## The chronograph (2026-10-27)

- `L:BBS_CHRONO_ELAPSED_TIME` (seconds; `_FO` for the first officer) drives the clock's sweep hand (cockpit XML 26765:
  `60 % 5 /`). It is the chronograph BlueBird's Complete Flight Guide v1.01 times things with: "Start the timer with
  the glareshield CLOCK button as the starter engages" (engine start), and a timer after landing.
- It is started, stopped and reset by the clock's CHR button (`L:B757_CHRONO_BUTTON_PRESS`, the map's "Clock Button")
  or the glareshield CLOCK button (`L:BBS_757_CAPT_CLOCK_BUTTON`, the map's "757 Capt Clock"): each press cycles
  start, stop, reset. It needs power (nothing with the battery off).
- Hold, measured with timers in the `BBSTablet` page: CHR 100 ms ignored; 200, 300, 500, 800 and 1,200 ms each
  registered; glareshield 200 and 500 ms registered. The app holds a button 200 ms, at the edge: two quick presses
  sent as separate calls (their gap unmeasured) were missed.
- The ET knob's own count is not exposed (no variable in `BBS752.wasm` besides the knob), so neither the app nor a
  test can read it; the knob itself lands (row 4 above). The F/O side is symmetric in the code; not exercised.

## Findings for other plans

- The app's `N 0 *` prefix matters: two identical calculator calls in a row reach MobiFlight as one (a second Mode
  Select step sent without it moved nothing). The app already sends everything through `ExecuteCalculatorCodeUnique`.
- Spring-loaded positions read back as their rest position: chrono ET RESET reads HOLD, door Deny and Unlocked read
  Auto, 1.5 s after the pick. If the app announces a pick that does not read back as "did not move", these will say
  so wrongly (plan 12, with the door's presentation).
- Gear readouts: the stock gear handle cannot show OFF; Shift+G and any gear call-out that should say OFF must read
  `L:B757_GEAR_HANDLE_POS` (plans 06 and 14).
- The APU fire handle pulls only within 1.5 s of its unlock (the cockpit's own timer, which the app mirrors): for a
  screen reader user, pressing the unlock and then picking Pulled in that window is tight (plans 03 and 12).
- EXT PWR after an APU fire-handle shutdown: with the APU FAULT latched and ground power connected and available, the
  press code ran (`_ON` read 1 for about 70 ms) and BlueBird put it back to 0; a Cold and dark then Before start load
  cleared the fault. Plan 07's own check of EXT PWR from the app passed in a normal state (plans 06 and 07).
- The clock buttons (both CHR "Clock Button" rows and the glareshield "757 Capt Clock" and "757 FO Clock") want a
  longer hold than the app's 200 ms (for example kind `held` with `hold_ms` 500), labels that say what they are, and the
  F/O glareshield button out of the Captain chronometer panel (plan 12).
- The chronograph is readable (`BBS_CHRONO_ELAPSED_TIME[_FO]`), so a readout ("chronograph 1 minute 23") would make
  BlueBird's timer usable without sight; the ET count is not readable (plans 13 and 14).
- The simconnect MCP refused a calculator call of about 290 characters ("negative count"); the app's limit is 900. A
  test through the MCP sends a long burst as separate calls.
