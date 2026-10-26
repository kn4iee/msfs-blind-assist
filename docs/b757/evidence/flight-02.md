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
| 4 | Chrono ET RUN, HOLD, RESET, captain and F/O | Passed. RESET reaches 3 and BlueBird springs it back to HOLD after 0.4 s, as the real clock's RESET does. `BBS_CHRONO_ELAPSED_TIME` did not count in 3.7 min at RUN, and BlueBird's own click code gave the same: not the direct write; what starts the count is open. |
| 5 | Flight deck door Deny, Unlocked | Passed. Each lands (read straight after the write: 2; the recorder: 0 from 0.67 s) and BlueBird springs it back to Auto about 0.9 s later. A click would stamp `O:UpdateTime`, which the calculator cannot, so the app's hold is probably shorter; whether the door logic registers a Deny that short needs an entry request to show. |
| 6 | Fuel pump buttons (the vendor slip) | Passed. The sim runs the code up to the slipped write: L Pumps FWD goes off and on; C Pumps L on lights the center pump's PRESS light (center tank empty), off clears it. The `_ON` write is lost (`_ON` stays 0; BlueBird's own panel state leaves it 0 too). Stock `FUELSYSTEM PUMP ACTIVE:1-4` did not follow within 4 s. |
| 6 | APU fire handle pull and stow (the dangling `and`) | Passed. Unlock, then the pull inside the unlock window: Pulled, APU FAULT, the APU shuts down. After the app's 1.5 s relock the same press stows it. |
| 7 | Gear Lever OFF and DOWN (on the ground) | Passed. 0.5 holds with all three legs down; DOWN returns. Stock `GEAR HANDLE POSITION` stays 1 (it has no OFF). UP and OFF airborne and Shift+G: not run. |
| 7 | Flap Lever 5 and UP | Passed. Lever 41, stock `FLAPS HANDLE INDEX` 2; with hydraulic pressure the slats and flaps extend (`BBS757_FC_*`, stock leading edge 36% at 10 s); UP retracts. Without pressure (pumps stopped) the handle follows but the surfaces stay put. |
| 8 | Task 8, Altitude Intervene | Not run: needs a VNAV climb or descent with a constraint. |

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
- The simconnect MCP refused a calculator call of about 290 characters ("negative count"); the app's limit is 900. A
  test through the MCP sends a long burst as separate calls.
