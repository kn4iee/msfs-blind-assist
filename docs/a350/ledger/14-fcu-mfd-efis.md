# Plan 14 ledger: "Airbus A350 Manual 1.5", PDF pages 52-63 (FCU, MFD and KCCU, EFIS)

Classes as plan 12's ledger (`12-front.md`). "Panels table" is `docs/inibuilds-a350.md`'s per-panel sweeps; the 1.2.8 map changed
none of these controls (plan 11 Task 1). The MFD lines' evidence is plan 09's (`docs/a350/evidence/09-mfd-window.md`, 1.2.8).

| PDF page | Item (the manual's words) | Where in MSFSBA | Class | Evidence | Owning plan |
| --- | --- | --- | --- | --- | --- |
| 52 | AFS: flight guidance (AP, FD, A/THR), FMS, flight envelope | the FCU panel and read-outs, the MFD window, the PFD | reference | | |
| 52 | Flight envelope: windshear detection | the PFD's WINDSHEAR flag and its call-out | built but not proven | the PFD flag call-outs (`A350PfdFlagAnnouncer`); a windshear is plan 07's owner item | 07 |
| 52 | Flight envelope: GW and CG computation | the E/WD's GW and GW CG, the MFD FUEL&LOAD | proven live | E/WD "Gross weight: -- KG", "Gross weight CG: -- %" on the stand (2026-11-03); flown with values | |
| 52 | Two FMS, each an FMC, MFD, ND, PFD, EFIS CP, KCCU | each side's displays and panels, the MFD window's side box | proven live | plans 08, 09 | |
| 52 | Managed and selected guidance | the FCU knobs' push (managed) and pull (selected); the FMA | proven live | flown 2026-10-30 to 11-03 | |
| 53 | Selected when pulled, managed when pushed | each FCU knob's push and pull rows | proven live | Panels table "FCU": every row | |
| 53 | 1 MACH/SPD push button | FCU: SPD/MACH | proven live | Panels table | |
| 53 | 2 SPD/MACH knob, pulled (IAS or Mach) and pushed (managed) | FCU: speed knob push and pull; the Speed typed field; Shift+S | proven live | Panels table: typed fields in knots and Mach | |
| 53 | 3 TRUE/MAG push button | FCU: TRUE/MAG | proven live | Panels table | |
| 53 | 4 HDG knob pulled (heading or track), pushed (NAV, or NAV within a margin) | FCU: heading knob push and pull; the Heading typed field; Shift+H | proven live | Panels table; flown | |
| 53 | 5 LOC push button | FCU: LOC | proven live | Panels table | |
| 53-54 | 6 HDG V/S - TRK FPA push button: track and FPA, the FPV on the PFD, the flight path director | FCU: HDG-V/S / TRK-FPA; the FCU windows say TRK and FPA ("FPA -3.0 degrees") | proven live (the button, the windows); gap (the FPV) | Panels table; `A350FcuCalloutsTests`; the FPV is a symbol on the PFD that no line reads | 07 |
| 54 | 7 FD push button | FCU: FD; "FD: On" said | proven live | 2026-11-03 ("[generic] FD: On", plan 11 IRS run) | |
| 54 | 8 AP1 and AP2; both for CAT 2 and autoland; disconnect on the sidestick | FCU: AP 1, AP 2; the sidestick takeover keys (AP off) | proven live | autoland with both APs flown 2026-11-03 (plan 08); `A350Levers`: the takeover fires `INI.AUTOPILOT_OFF`, live 2026-10-29 | |
| 54-55 | 9 A/THR push button; disconnect on the thrust levers | FCU: A/THR; Thrust Levers: left and right A/THR disconnect | proven live | Panels table: the A/THR disconnect put the A/THR light out | |
| 55 | 10 METER push button | FCU: metric altitude | proven live | "Selected altitude in metres: 1520", "Altitude in metres: 0", off: none (flight-2026-11-03; plan 07 Task 5) |  |
| 55 | 11 Altitude knob pulled (OP CLB / OP DES), pushed (CLB / DES, needs NAV), 100/1000 ft | FCU: altitude knob push and pull, altitude 100/1000, the Altitude typed field; Shift+A | proven live | Panels table; flown | |
| 55 | 12 ALT push button (level off) | FCU: ALT | proven live | Panels table | |
| 55 | 13 V/S or FPA knob pulled (V/S, FPA), pushed (level off) | FCU: V/S knob push and pull, the VS/FPA typed field; Shift+V | proven live | Panels table | |
| 55 | 14 APPR push button | FCU: APPR | proven live | ILS approaches and an autoland flown (plan 08) | |
| 56 | One MFD split in two sections; two KCCUs | the MFD window, its side box | proven live | plan 09 | |
| 56 | MFD: flight plan, performance, secondary plan, revisions | the MFD window's pages | proven live | plan 09 Task 1: 33 pages | |
| 56 | KCCU: keyboard, shortcut keys, trackball, scroll wheel | the window's typing, Alt+letter page keys and page buttons, the click on a line, Control+arrows | proven live | plan 09 Tasks 1, 5 | |
| 56 | The PC keyboard into a field after LEFT CTRL; BACKSPACE also the sim's | the MFD window types through the KCCU's keys, not the sim's keyboard | proven live | plan 09 Task 5 (BACKSPACE deletes one character) | |
| 57 | The General Menu Bar and every page it reaches (a composite image) | "Go to MFD page" (33 pages), the menus read | proven live | plan 09 Task 1, every menu walked | |
| 58 | The interactive flight plan: a waypoint opens its menu | Enter on an F-PLN row (the A380's grid, "button") | proven live | plan 09: the revision menu read whole (`mfd_fpln_revision_basur`) | |
| 59 | Time, speed or altitude of a waypoint opens VERT REV | a click on the row's columns | proven live (2026-10-28, 1.2.7) | VERT REV fixtures (its CMS tab, AT OR ABOVE) | |
| 60 | 1 Escape key | the window's ESC (the KCCU key) | proven live | plan 09 Task 5: ESC cancels the edit | |
| 60 | 2 Scroll arrows | Control+arrows (KCCU UP, DOWN) | proven live | plan 09 Task 4: DOWN moves the list's box, then the list | |
| 60 | 3 Shortcut keys | Alt+letter keys, the page buttons | proven live | plan 09 Task 1 | |
| 60 | 4 CLEAR INFO key | Alt+C; "CLEAR INFO button" | proven live | plan 09 Task 5 | |
| 60 | 5 Cursor navigation keys (ND, MFD, OIS) | the KCCU ND, MFD and OIS keys (each window moves the cursor first, [A350-4]) | proven live | plan 09 Task 5: OIS moves the cursor off | |
| 60 | 6 Scroll wheel: the flight plan and menus | the KCCU's SCROLL keys | proven live | SCROLL_DOWN and SCROLL_UP move the F-PLN a row each, on a nine-row plan (flight-2026-11-03) |  |
| 60 | 7 Notepad | Alt+N | proven live | `mfd_notepad_open` and its list (plan 09) | |
| 60 | 8 Enter key | Enter in the window | proven live | plan 09 | |
| 61 | The electronic checklist on either MFD: C/L MENU, boxes for pilot items, sensed items green, RESET, C/L COMPLETE, C/L MENU | the MFD window's C/L MENU (Alt+K) | proven live | plan 09 Task 6, 2026-11-03, 1.2.8 | |
| 62 | The PFD | the Displays section's Captain and First Officer PFD; the FMA and PFD call-outs | proven live | plan 07; flown | |
| 62 | The PFD's lower part: slat/flap, gear, speed brake, parking brake, pitch trim, messages | the PFD read: the messages ("T.O THS, white; OUT OF RANGE, red", 2026-11-03) | proven live (flaps, speed brake, parking brake, T.O THS, messages); built but not proven (gear) | flaps read moving and set; the speed brake and parking brake sprites read since 069b8c22f; the gear indication not yet found (flight-2026-11-03) | 12 |
| 62 | The ND | the ND read and the ND window | proven live | plan 08 | |
| 62 | The VD | the ND read's vertical display | proven live | `docs/inibuilds-a350.md` "The range map" (VD on its scales) | |
| 62 | The interactive ND: the ND cursor key, KCCU input to the ND | the ND window's clicks and keys (the cursor moved first) | proven live | plan 08 (SET LDG RWY, the functions panel) | |
| 63 | EFIS CP: modes, ranges, the extra information buttons, WX | EFIS Captain and First Officer panels | proven live | Panels table: every position and button | |
| 63 | Baro preset on STD, shown top right, memorised, shown when pulled | the baro knob's turn and pull rows; the baro said (plan 02) | proven live | plan 02: each side's preset, the pull from it, LINKED INSTR (1.2.8) | |
