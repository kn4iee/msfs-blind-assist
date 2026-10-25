# BlueBird 757-200 prototype: in-sim test plan (plan 01)

For the owner. One check per line: do the step, then listen for what is expected. No check needs sight. Where a
check says "log", the line is in `%APPDATA%\MSFSBlindAssist\logs\debug.log` (it rotates in about ten minutes, so
read it soon after the check). Record each result; the fixes go in one wave afterwards.

Setup: MSFS 2024 with the BlueBird 757-200 at a gate, BBS Nest running, MobiFlight WASM installed, MSFSBA built
from branch B757 (Release). Input mode is `[`, output mode is `]`.

## A. Profile and BBS Nest

1. Start the flight with BBS Nest NOT running, select Aircraft > BlueBird 757-200: within about two seconds
   "BBS Nest is not running. The 757's systems will not respond until it runs and the flight is reloaded." Once only.
2. Start BBS Nest, reload the flight: no Nest warning this time.
3. Ctrl+M: the 757 Monitor Manager opens and lists APU state, master warning, master caution, ground state,
   glideslope and trim. Escape closes it.

## B. Panels (cold and dark, then powered)

4. Overhead > Electrical > Battery to On: no extra speech beyond NVDA's own; about 1.5 s later the combo still says
   On (it re-reads the aircraft). Status section: APU still Off.
5. Overhead > Electrical > STBY Power: note which positions move the switch. The map says 0 Off, 1 Auto, 2 BAT; the
   ends are unproven (1 = Auto is proven by BlueBird's panel states).
6. Overhead > APU > APU Selector to Start, then On: Status shows APU Starting, then Running after about 90 s
   ("APU Running" is announced).
7. Overhead > Fuel > L Pumps FWD on and off: the combo follows. These eight pump buttons replay BlueBird's own click
   code verbatim, which may carry a vendor slip (plan 01, Task 1): if a pump does not move, say which.
8. Overhead > Engine start > L ENG Start: GND and AUTO move the selector; note what Position 4, 5 and 6 are (the map
   leaves them unnamed until measured).
9. Glareshield > MCP > LNAV (a button): "LNAV pressed" once. (Since task 10: about two seconds later "LNAV on" or
   "LNAV off" instead, once; see check 54.)
10. Arrow quickly through a combo's rows and stop on one: only the row you stop on is written (300 ms settle).
11. Stop MobiFlight, pick any combo row: "{label} unavailable", nothing moves.
12. Forward panel > Yokes > Captain Yoke A/P Disconnect: "pressed" once (held 1.5 s by the vendor template).

## C. EFB window (Shift+T)

13. Shift+T opens the 757 EFB window. Tab to the Status box: it says whether the tablet can be reached.
14. Load preset, Before start: "Loading Before start"; within about 12 s battery, APU and AC power are up (Status
    section: APU Running, both AC bus sources APU).
15. For each preset marked "est." in the code (Cold and dark, Turnaround, Ready for taxi, Ready for takeoff, After
    takeoff, Cruise, Approach, Final approach): load it and check a state that differs (battery, APU, engines
    running, gear, flaps, by the readouts). The Before start position is measured; the rest are estimates.
16. Presets gate: Cruise on the ground says "That preset is for flight."; Cold and dark in the air says "That preset
    is for the ground."; nothing is clicked.
17. APU quick start, IRS rapid align: the APU state or the IRS alignment changes (Status section, or the IRS panel).
18. Sign in to Navigraph: "Already signed in to Navigraph" (signed in on 2026-10-25). Signed out (only if you want to
    test it): the whole code is spoken before the browser opens and takes focus.
19. Update nav data with cycle 2610 installed and current: "Nav data is current, cycle 2610" and nothing pressed.
    BlueBird's REMOVE button sits where UPDATE was, so this refusal is the important check.
20. Right after a flight load, Update nav data: "BlueBird is still checking for nav data. Try again in a minute."
21. On 2026-10-29 (cycle 2611's first day): "Nav data cycle 2611 is new today. Reload the flight after midnight UTC,
    then update."
22. From 2026-10-30 00:00 UTC, reload the flight, wait a minute, then Update nav data: "Nav data cycle 2611
    installed. Reload the flight to use it." Then reload. On a flight loaded earlier, or signed in to Navigraph after
    the load, the window refuses ("BlueBird checks for new nav data when the flight loads…"). Known residual risk:
    with a lapsed Navigraph subscription or a late cycle BlueBird may show REMOVE at that spot; the app then says the
    nav data was removed, and an update with an active subscription restores it.
23. SimBrief ID box: type your own ID, Enter: "SimBrief ID saved" (the app clears the keypad field first, so an ID is
    never appended to the stored one). Set it to a wrong ID, then your own again: "SimBrief ID saved" both times.
    "BlueBird stored a different SimBrief ID" means the keypad did something unexpected.
24. Fetch SimBrief flight with an OFP in the other weight unit than the aircraft's: "SimBrief flight requested…",
    including "The aircraft uses pounds" (or kilograms). Accept units: "Weight units now kilograms" (or pounds). This
    confirms the map 0 = kilograms, 1 = pounds, which rests on one live read.
25. On a later fetch that asks again, Keep units (est. position): "Weight units unchanged, …".
26. With no units dialog up, Accept units right after a fetch whose units already matched: note anything that changes
    (the spot under the dialog is unmeasured); the window says the units are unchanged.
27. Reload the flight with the EFB window closed, then open it and press an action at once: the first action works
    (a stale link is dropped when the window is shown).
28. Fuel and payload from SimBrief, on the ground, with your SimBrief ID set (check 23) and a plan on SimBrief: press
    Load fuel and payload from SimBrief. About three seconds later "Loaded" with the plan's flight and route, then
    "Fuel … pounds: left …, right …, center …. Passengers … pounds. Cargo … pounds: forward …, aft …." (kilograms if
    the aircraft is set to them), then, with more than 176 passengers in the plan, "The plan's … passengers are capped at
    the 757's 176 seats; the rest of the payload goes in the holds.", and last "For the ACARS weight and balance,
    request WGTBAL on the CDU." The fuel should be within about 1% of the plan's block fuel (BlueBird converts at
    6.72 lb/gal, the sim reads 6.70). Then request WGTBAL on the CDU's ACARS pages (note the key path) and compare the
    zero fuel weight and fuel it reads with the plan's. Repeat with a SimBrief plan in kilograms with an odd block
    fuel (for example 11589 kg): again "Loaded …", not "did not take all of the load" (the write is whole pounds, the
    odd pound goes to the left wing, and the center stays empty: "center 0"). With BBS Nest stopped and a SimBrief plan whose fuel differs from what the
    tanks hold now (an aircraft already holding the plan counts as loaded), press it again: about six seconds later
    "BlueBird did not take the load for …. Is BBS Nest running?" (log: a "SimBrief load NotTaken" line). With a plan for
    another type (an A320, say): "Your latest SimBrief plan is for A320, not a 757-200. Create a B752 plan, then try
    again." Airborne, the button says "Fuel and payload load only on the ground."

## D. CDU window (Shift+M)

29. Shift+M on a cold and dark aircraft: the Status box says "Waiting for the 757's CDU. Is the aircraft powered?";
    power up: the first page appears without reopening.
30. Powered: the page title is spoken (for example "IDENT"). Arrow down: title, labels, "1: …" to "6: …", scratchpad.
31. Alt+R, Alt+G, Alt+I: each new page's title is spoken. PageDown on RTE: "2/3" in the title; PageUp back.
32. Ctrl+Tab: "First officer CDU"; Alt+R: its RTE title. Ctrl+Tab: "Captain CDU".
33. Alt+S, type KSEA, Enter: about 3 s later "KSEA" read once, not letter by letter.
34. Backspace once: "KSE". Hold Backspace two seconds: only one more character goes, never the whole scratchpad
    (a long CLR would wipe it). Backspace until "Cleared".
35. Typing "-" then "+" in the scratchpad: note what the CDU shows (Boeing behavior assumed: one +/- press is "-",
    two are "+"; unmeasured on BlueBird).
36. Reload the flight with the window open, power up: both CDUs come back by themselves (log: no repeated "Tablet
    agent deaf" lines once powered).
37. Type a long entry, Enter, and at once reload the flight: "CDU connection lost, keys not sent" once; nothing
    typed into the new flight.

## E. SimBrief to an active route (EFB + CDU)

38. EFB: Fetch SimBrief flight. CDU: MENU (Alt+M), ACARS (Ctrl+2), OPERATIONS (Alt+2), PREFLIGHT (Ctrl+1), INIT FLT
    (Ctrl+1), INIT (Alt+6): "VHF IN PROG", then the flight's origin and destination on the page.
39. CDU: RTE (Alt+R), REQUEST SEND (Ctrl+3): within about 15 s "ROUTE 1 UPLINK READY"; LOAD (Ctrl+4), ACTIVATE
    (Alt+6), EXEC (Ctrl+Enter): "ACT RTE 1" in the title.

## F. MCP (input mode `[`)

40. Ctrl+H 90, Enter: about 1 s later "Heading 090". 360: "Heading 000". 361: range error, window stays open.
41. Ctrl+A 12049: "Altitude 12000". 99: error.
42. Ctrl+S 250 from 200: within about 1-3 s "Speed 250". Also 100, 399. 0.82: "Mach entry is not supported yet…".
    Log: the `AP_SPD_VAR` calls and whether a correction round ran (measures whether 25 events per call all count).
    (Since task 11 the speed goes in BlueBird's measured knob tiers; see check 77.)
43. Press the MCP IAS/MACH button, then Ctrl+S 250: "MCP is in Mach mode. Set Mach from the Glareshield panel." If
    instead the speed is set, the Mach flag `L:BBS_AIRSPEED_IN_MACH` is the wrong variable (it read 0 after a C/O
    pulse on 2026-10-25): note it.
44. Ctrl+V -1500: "Vertical speed minus 1500"; 0; 1500; -1550 (rounds to minus 1600). A direct write is adopted
    (measured 2026-10-25), so the log should show no stepping.
45. Output mode: Shift+H, Shift+S, Shift+A, Shift+V each match what was set.
46. Ctrl+H twice: one window only. Switch profile: the MCP windows close.

## G. Take-off and AI display reads

47. Takeoff Assist on the 757 (it starts at lineup, or Output Ctrl+T), take-off roll: no app "80 knots" or "100 knots"; the aircraft's own 80 knots,
    V1 and rotate are heard; the centerline tone and pitch calls still work. On any other aircraft the app's 80 and
    100 knots are unchanged.
48. Output mode, cockpit view, AI provider set: Alt+P reads the ADI, flight mode annunciations first; it must not read
    the separate airspeed indicator or altimeter. Note whether speed, altitude or heading are on the ADI at all.
49. Alt+N: the EHSI (map). Alt+E: the upper EICAS alerts in red or amber. Alt+S: the lower EICAS page.
50. Your view is put back after each read. From an outside view: "Switch to a cockpit view first".

## H. Recovery and exit

51. Quit MSFSBA with both windows open: it closes cleanly. Restart, Shift+M and Shift+T: both connect again.
52. Switch to another aircraft profile and back: no stale window, no doubled speech.

## I. Full-flight readiness (plan 01, task 10)

Names from the control map that rest on panel states and the manuals, not on a live read (stage 10a):

53. Pedestal > Control stand: Parking Brake, Speedbrake Down / Armed / Up, TO/GA, A/T Disconnect are there. Forward
    panel > Landing gear > Autobrake: from OFF one step should read DISARM with the amber AUTO BRAKES on the EICAS
    (Alt+E); then 1, 2, 3, 4, MAX AUTO; RTO before takeoff. Note it if the approach preset leaves it on DISARM.
54. Glareshield > MCP: the three CMD buttons are CMD L, CMD C and CMD R (from the nodes 1, 2, 3: unmeasured which is
    which side). HDG HOLD and ALT HOLD (were "Hold 1" and "Hold 2"): pressing HDG HOLD in flight engages HDG HOLD on
    the ADI (Alt+P), and so on. Each MCP mode or CMD press says its light about two seconds later ("ALT HOLD on"),
    once, never "pressed".
55. IRS selectors OFF, ALIGN, NAV, ATT: ALIGN and ATT are unmeasured. ND range (0 = 10 NM) and ND mode (0 VOR, 1 APP,
    2 MAP, 3 PLAN): turn one click and check the EHSI (Alt+N) shows the named range or mode. TCAS mode: STBY and TA/RA
    named, 1 to 3 numbered; transponder side 0 = L. Alternate flaps 1 to 7 numbered.
56. PW fuel control switches CUTOFF and RUN by the panel: the engine starts and stops with them.
57. TO/GA and A/T Disconnect in flight: "TO/GA pressed" / "A/T Disconnect pressed"; the ADI's autothrottle mode
    changes (GA, or the A/T drops).

Readouts and call-outs:

58. Status panel: rows Flaps, Flap Angle, Gear, Speedbrake, Parking Brake. Ctrl+M lists Flaps, Gear, Speedbrake,
    Parking Brake and the fourteen MCP lights (THR to CMD R), not the main gear legs, the flap angle or the engines.
59. Flaps: Output L clean says "Flaps UP". The simulator's flaps-down key once: "Flaps 1" spoken; L at once says
    "Flaps 1, moving, … degrees", or with no hydraulic pressure "Flaps 1, surfaces at 0 degrees". Settled at 15 and at
    30, L must say just "Flaps 15" and "Flaps 30": that confirms the flight model's angles (15 is 11 degrees, 30 is 34)
    are what the simulator reports. If it adds "surfaces at …" while settled, note the angle.
60. Gear (in flight): gear up by the simulator's key: "Gear in transit", then "Gear up"; Shift+G during the transit:
    "Gear in transit, lever up". Down: "Gear in transit", "Gear down". On the ground Shift+G: "Gear down". The stock leg
    positions following BlueBird's gear is unmeasured.
61. Speedbrake: Control stand > Speedbrake Armed: about two seconds later "Speedbrake armed", once. Up: "Speedbrake up"
    (the UP click's 100 is unmeasured). Down: "Speedbrake down". The simulator's arm key: "Speedbrake armed" from the
    change announcement. In flight, the lever moved part way by the simulator's speedbrake keys: "Speedbrake up 50
    percent" or similar.
62. Parking brake: Control stand > Parking Brake to Set: NVDA reads the combo, and no "Parking brake set" follows.
    Releasing it with the combo is unmeasured with PARKING BRAKE MODE on REALISTIC: note whether it stays Released or
    snaps back to Set. Holding the toe brakes (the simulator's brake key) releases it: "Parking brake released".
63. Fuel: F "Fuel … pounds: left …, right …, center …" (rounded to 10) matches the EICAS fuel; Shift+F the same in
    kilograms. W and Shift+W: the gross weight; compare with the CDU's PROGRESS or WGTBAL.
64. MCP lights on change: a mode that engages by itself (ALT HOLD at the altitude capture, LOC at capture) is spoken
    ("ALT HOLD on"). Untick VNAV in Ctrl+M: no VNAV call-outs. Untick Gear: no gear call-outs from any leg.
65. Reload the flight: no flaps, gear, speedbrake, brake or light call-outs at the load; the first real change speaks.

Windows:

66. Ctrl+P: the 757 Autopilot window, focus on "CMD L: …". Tab through sixteen buttons, each "Label: On/Off". A/T Arm,
    Captain F/D and F/O F/D flip within about a second of a press (NVDA reads the new caption). CMD L in flight with a
    mode engaged: "CMD L: On". Escape closes; Ctrl+P again brings the same window back. A press the aircraft does not
    take (CMD L on the ground, cold and dark): about two seconds later "CMD L did not engage", once.
67. Ctrl+B: the Altimeter and minimums window opens on the altimeter box; Tab to Status: the current values. Type
    30.12, Enter: within about six seconds "Altimeter: 1020, 30.12"; Alt+P's baro and both other altimeters agree (all
    three are set together, measured on the ground). 1013, Enter: "Altimeter standard" (typing standard is the
    Standard button); "29,92" with a comma reads as 29.92, the same; 1012: "Altimeter: 1012, 29.88"; 500 is refused
    aloud. Standard: "Altimeter standard". If they ever differ, note which "Altimeter 1, 2, 3" is the captain's, the
    first officer's and the standby (unmeasured).
68. Same window, Radio minimums 250, Enter: "DH 250" within about two seconds; then Output Alt+P (the AI read of the
    ADI) reads the minimums as DH250. Then 200, then 0 and 2500 (the ends: note any other value the read-back gives).
    Log: the INCREASE/DECREASE_DECISION_HEIGHT calls and whether a correction round ran.
69. Output B: "Altimeter: …. DH …" as set.
70. TCAS and transponder > Squawk Code: 4743, Enter: "Squawk 4743" (one number); 0421: "Squawk 0421"; 7778: "Squawk
    must be four digits, each 0 to 7."; 12a4: the same digits message; an empty box: "Type a four-digit squawk
    first." Output X ("Squawk 4743") and the panel's status display (Ctrl+3, "Squawk: 4743") read the code back
    from the transponder BlueBird uses (the stock one, measured with XPNDR_SET); nothing needs the radio panel's
    own display.
71. Engines panel at the first engine start: compare Left/Right N1, N2, EGT, Fuel Flow and EPR with an Alt+E read of
    the upper EICAS. All unmeasured on BlueBird (the stock engine variables; EPR's scale): note any that stay at 0 or
    read wrong.
72. Alt+P on an approach with APP armed: the reading includes the AUTOLAND STATUS annunciator (LAND 3, LAND 2, NO LAND
    3 or NO AUTOLAND) when it shows one, after the flight modes; nothing about it when it is blank.
73. EFB window, Set steering to rudder, with the setting already RUDDER (the owner's, since 2026-10-25): "Steering is
    already rudder", nothing pressed; taxi still steers with the rudder. The click itself (from REALISTIC) can be
    checked only on an install whose setting is still REALISTIC (a new install or a reset of BlueBird's settings):
    then "Steering set to rudder" and rudder steering on the next taxi.
74. Switch profile with the Ctrl+P and Ctrl+B windows open: both close.

Task 10 fix round 1 (rulings R31, R32):

75. Chocks: after a flight load (INITIAL CHOCKS IN PLACE on), the Status panel reads "Chocks: In place". EFB window,
    Remove chocks: "Chocks removed" about two seconds after the clicks, and the Status row follows ("Chocks:
    Removed"); press Remove chocks again: "Chocks are already removed", nothing pressed. Set chocks: "Chocks in place";
    again: "Chocks are already in place". Then remove them and taxi: the aircraft moves.
76. Named flow switches (R32), after loading the EFB's Before start preset: Overhead > Passenger signs > Seatbelts
    reads "ON", Air conditioning and bleed > L and R Pack Selector "AUTO", Pressurization > Mode Select "AUTO 1",
    Engine start > Ignition "1", Glareshield > MCP > Bank Limit "AUTO", Pedestal > EICAS control > EICAS Lwr Thrust
    Ref "BOTH", Wipers > Wiper "OFF". These read back the values the names were taken from, so they confirm the
    wiring, not the names: where a combo can be moved, note what the aircraft does (for example Seatbelts to OFF:
    the seat belt chime and sign, read by Alt+E or Alt+S if the EICAS shows it).

## J. First-flight fixes (plan 01, task 11)

77. MCP speed in BlueBird's knob tiers (R33): with the MCP speed at 200, Ctrl+S 153, Enter: within about two seconds
    "Speed 153", and Output Shift+S also says "Speed 153". Then Ctrl+S 100, Enter, and 280, Enter: "Speed 280". Both
    land exactly with no correction round: the log's "Sent command" lines for the 153 set are two bursts, "26 sp0 …
    AP_SPD_VAR_DEC …" then "2 sp0 …", and for the 280 set one, "53 sp0 … AP_SPD_VAR_INC …"; a further `sp0` line
    a second or more later is a correction. If a speed lands off, note the spoken value and those lines.
78. MCP window buttons (R34): Ctrl+H, then Tab from the value box: "HDG SEL", "HDG HOLD: Off", "LNAV: Off", each read
    with its Alt key (Alt+S, Alt+H, Alt+L). With an executed route, press LNAV: about two seconds later "LNAV on",
    once; nothing more from the window. If you hear the state twice (NVDA reading the caption change as well as "LNAV
    on"), note it. Alt+S from the value box: "HDG SEL pressed", once. Ctrl+S: SPD, THR, FLCH, IAS/MACH; Ctrl+A: ALT
    HOLD, VNAV, FLCH; Ctrl+V: V/S; one press each says its light once, IAS/MACH "IAS/MACH pressed" (press it twice:
    it switches the window between IAS and Mach). With MobiFlight stopped, a press says "LNAV unavailable" and nothing
    moves.
79. Ctrl+P, A/T Arm (R40): after a flight load "A/T Arm: On" (BlueBird loads every panel state armed). Press it: within
    a second or two "A/T Arm: Off"; again: "On". If the caption ever disagrees with the autothrottle mode the ADI shows
    (Alt+P), for example after the A/T Disconnect button, note both.
80. Trim in units (R35): with the hydraulics powered (the electric pumps on, or the engines running), trim with the
    simulator's trim keys: about once a second while it moves BA says the setting ("Trim 3.0", "Trim 3.1", …), and
    nothing once it stops; the trim as found when BA connects is not spoken (after a flight reload the new flight's
    trim may be said once, as on every aircraft). On the CDU's TAKEOFF REF page (Shift+M, INIT REF) note the
    TRIM figure and trim until BA says it; Claude compares the stabilizer trim indicator on a window capture with the
    spoken value there and near each end of the travel (the strip formula is weakest at the ends). Output Shift+T:
    "Trim announcements off" and trimming is silent; Shift+T again: "Trim announcements on".
81. EXEC light (R36): route by ACARS as in checks 38-39. After ACTIVATE (Alt+6 on RTE): "EXEC light on", once; after
    EXEC (Ctrl+Enter): "EXEC light off", once. A later modification (a direct-to, say) says "on" again and its EXEC
    "off". Ctrl+M lists "EXEC light"; untick it: no EXEC call-outs. Reload the flight: nothing is said at the load.
