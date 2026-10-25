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
9. Glareshield > MCP > LNAV (a button): "LNAV pressed" once.
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

## D. CDU window (Shift+M)

28. Shift+M on a cold and dark aircraft: the Status box says "Waiting for the 757's CDU. Is the aircraft powered?";
    power up: the first page appears without reopening.
29. Powered: the page title is spoken (for example "IDENT"). Arrow down: title, labels, "1: …" to "6: …", scratchpad.
30. Alt+R, Alt+G, Alt+I: each new page's title is spoken. PageDown on RTE: "2/3" in the title; PageUp back.
31. Ctrl+Tab: "First officer CDU"; Alt+R: its RTE title. Ctrl+Tab: "Captain CDU".
32. Alt+S, type KSEA, Enter: about 3 s later "KSEA" read once, not letter by letter.
33. Backspace once: "KSE". Hold Backspace two seconds: only one more character goes, never the whole scratchpad
    (a long CLR would wipe it). Backspace until "Cleared".
34. Typing "-" then "+" in the scratchpad: note what the CDU shows (Boeing behavior assumed: one +/- press is "-",
    two are "+"; unmeasured on BlueBird).
35. Reload the flight with the window open, power up: both CDUs come back by themselves (log: no repeated "Tablet
    agent deaf" lines once powered).
36. Type a long entry, Enter, and at once reload the flight: "CDU connection lost, keys not sent" once; nothing
    typed into the new flight.

## E. SimBrief to an active route (EFB + CDU)

37. EFB: Fetch SimBrief flight. CDU: MENU (Alt+M), ACARS (Ctrl+2), OPERATIONS (Alt+2), PREFLIGHT (Ctrl+1), INIT FLT
    (Ctrl+1), INIT (Alt+6): "VHF IN PROG", then the flight's origin and destination on the page.
38. CDU: RTE (Alt+R), REQUEST SEND (Ctrl+3): within about 15 s "ROUTE 1 UPLINK READY"; LOAD (Ctrl+4), ACTIVATE
    (Alt+6), EXEC (Ctrl+Enter): "ACT RTE 1" in the title.

## F. MCP (input mode `[`)

39. Ctrl+H 90, Enter: about 1 s later "Heading 090". 360: "Heading 000". 361: range error, window stays open.
40. Ctrl+A 12049: "Altitude 12000". 99: error.
41. Ctrl+S 250 from 200: within about 1-3 s "Speed 250". Also 100, 399. 0.82: "Mach entry is not supported yet…".
    Log: the `AP_SPD_VAR` calls and whether a correction round ran (measures whether 25 events per call all count).
42. Press the MCP IAS/MACH button, then Ctrl+S 250: "MCP is in Mach mode. Set Mach from the Glareshield panel." If
    instead the speed is set, the Mach flag `L:BBS_AIRSPEED_IN_MACH` is the wrong variable (it read 0 after a C/O
    pulse on 2026-10-25): note it.
43. Ctrl+V -1500: "Vertical speed minus 1500"; 0; 1500; -1550 (rounds to minus 1600). A direct write is adopted
    (measured 2026-10-25), so the log should show no stepping.
44. Output mode: Shift+H, Shift+S, Shift+A, Shift+V each match what was set.
45. Ctrl+H twice: one window only. Switch profile: the MCP windows close.

## G. Take-off and AI display reads

46. Takeoff Assist on the 757 (it starts at lineup, or Output Ctrl+T), take-off roll: no app "80 knots" or "100 knots"; the aircraft's own 80 knots,
    V1 and rotate are heard; the centerline tone and pitch calls still work. On any other aircraft the app's 80 and
    100 knots are unchanged.
47. Output mode, cockpit view, AI provider set: Alt+P reads the ADI, flight mode annunciations first; it must not read
    the separate airspeed indicator or altimeter. Note whether speed, altitude or heading are on the ADI at all.
48. Alt+N: the EHSI (map). Alt+E: the upper EICAS alerts in red or amber. Alt+S: the lower EICAS page.
49. Your view is put back after each read. From an outside view: "Switch to a cockpit view first".

## H. Recovery and exit

50. Quit MSFSBA with both windows open: it closes cleanly. Restart, Shift+M and Shift+T: both connect again.
51. Switch to another aircraft profile and back: no stale window, no doubled speech.
