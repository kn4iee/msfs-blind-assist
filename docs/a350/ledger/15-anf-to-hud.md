# Plan 15 ledger: "Airbus A350 Manual 1.5", PDF pages 64-80 (ANF, BTV, noise, thrust levers, radios, transponder, HUD)

Classes as plan 12's ledger (`12-front.md`). Plan 08's evidence is `docs/a350/evidence/08-nd-anf-taxi.md` (1.2.8, two flights),
plan 10's `10-rmp.md` (1.2.8).

| PDF page | Item (the manual's words) | Where in MSFSBA | Class | Evidence | Owning plan |
| --- | --- | --- | --- | --- | --- |
| 64 | ANF in NAV, ARC or PLAN below the ZOOM range, 5 NM to 0.2 NM | EFIS range rows; the ND read's airport map; the ND window | proven live | plan 08 (PLAN at the 2 NM step draws the ANF from 45 NM out) | |
| 64 | Navigraph subscription needed; the airport must be in the AMDB | the owner's Navigraph sign-in on the OIS | proven live (VCBI, OMDB) | plan 08; a second AMDB airport is plan 08's owner item | 08 |
| 65 | Clicking the ANF opens its menu, by mode | the ND window's map menu (SET LDG RWY, SET BTV EXIT ...) | proven live | plan 08 | |
| 65 | Scrolling the PLAN-mode map by click and drag | the ND window's map; exits listed without dragging (`A350BtvArming.ExitsOf`) | proven live | plan 08: "BTV in one step" | |
| 66 | BTV needs no failure, the arrival runway in the plan, an exit in the ANF | the ND window's BTV group (Landing runway, BTV exit, Arm exit) | proven live | plan 08 Task 2 | |
| 66 | Arming 1-2: pull the RWY COND knob, pick the condition on the SD, push it | Autobrake panel: runway condition; the SD's menu read | proven live | Panels table "Autobrake": every runway condition picked | |
| 67 | EFIS PLAN, ZOOM range, the arrival airport; top left RWY, LDA, condition | the ND window's Arm exit sets them; the ND read's BTV block | proven live | plan 08 | |
| 67 | ND cursor key, click and hold to reach the exit, the exit's menu, SET BTV EXIT | the ND window (BTV EXIT AT through the LANDING tab, as SET BTV EXIT was grey on two exits) | proven live | plan 08 Task 2 (ruling recorded there) | |
| 67 | EXIT, ROT, TURNAROUND (MAX and IDLE reverse) at the top left | the ND read's BTV block ("EXIT ...", "ROT ...", "TURNAROUND ...") | proven live | `docs/inibuilds-a350.md` (ND::DrawBTVInfo, live 2026-10-30) | |
| 67 | A/BRK; BTV armed on the PFD | Autobrake: A/BRK; the FMA read | proven live | plan 08 ("BRK BTV" read back) | |
| 68 | After touchdown BTV engages (FMA), DECEL at the speed tape's foot, off at 10 kt | the FMA call-outs; the PFD read | proven live (DECEL, the exit distances); the 10 kt release not seen (BTV gave way to BRK MED, exit too short) | "190 meters to exit", "140 meters to exit", PFD "DECEL" (flight-2026-11-03) | 08 |
| 68 | Deactivating BTV: toe brakes or the A/THR disconnect | the brake keys; Thrust Levers: A/THR disconnect | proven live | the pilot's brakes after the stop: "AUTO BRK OFF" (flight-2026-11-03) |  |
| 68 | AUTO BRK OFF on the PFD and the ECAM memo, the aural; a second disconnect press removes them | the PFD and E/WD reads | proven live | PFD "AUTO BRK OFF, amber", ECAM "AUTO BRK OFF", "Master caution on" (flight-2026-11-03) |  |
| 69 | NOISE on PERF T.O; THR RED and ACCEL from OIS OPTIONS AIRCRAFT; TOGA and SRS to 1650 ft by default | MFD window, PERF T.O: THR RED, ACCEL, NOISE | proven live (read) | 2026-11-03, 1.2.8: "THR RED: 1060 FT", "ACCEL: 1060 FT", "NOISE button" | |
| 69 | NOISE END first; then N1 and SPD (N1 blank by default, a preset speed); CANCEL NOISE | NOISE END, N1, SPD (grey until NOISE END), CANCEL * NOISE | proven live (read) | 2026-11-03, 1.2.8: N1 and SPD read "grey" before NOISE END; CANCEL * NOISE pressed back | |
| 70-72 | Scenario 1: TOGA/SRS to THR RED 1650, THR DCLB/CLB at 215 kt to NOISE END 4000, then 250 kt (image page, rendered) | the FMA call-outs on a noise take-off | proven live | NOISE END 4000: MAN FLX, SRS, RWY; at THR RED CLB and THR DCLB at the noise speed 165 kt to NOISE END, then 250 kt (flight-2026-11-03) |  |
| 70-72 | Scenario 2: ACCEL above THR RED (3000 ft): DCLB at V2+10 to 3000 | the same | built but not proven | | 07 |
| 70-72 | Scenario 3: a noise N1 (88%): NOISE N1 88% / SRS, then CLB, the ramp THR DCLB*, then THR DCLB | the same; the ramp's star read | built but not proven | | 07 |
| 73 | Thrust levers: A/THR armed at FLX or TOGA, go-around at TOGA, max thrust with A/THR, A/THR off at IDLE, autobrake off after touchdown, manual thrust, armed between IDLE and CL | Thrust Levers rows; the lever call-outs; the FMA | proven live (take-off, A/THR, TOGA go-around, IDLE) | flown 2026-10-30 to 11-03 (FLX take-offs, a go-around, autolands) | |
| 74 | RMP: VHF, HF, transponder code and IDENT | the RMP window, three RMP panels | proven live | plan 10 | |
| 74 | MFD SURV page from the menu or the SURV key | the MFD window: SURV (Alt+V), "Go to MFD page" | proven live | plan 09 Task 1 | |
| 75 | RMP 1 access keys, 2 activation keys, 3 line select keys, 4 message clear, 5 message area, 6 reset key, 7 microphone keys, 8 reception knobs, 9 keypad, 10 brightness, 11 INT/RAD | the RMP window's keys and the RMP panels' rows | proven live | plan 10; `docs/inibuilds-a350.md` "The RMPs" (every key, live 2026-10-28) | |
| 76 | Tuning: pick the page, the radio, type, activate; the leading 1, the second 1 and the point optional | the RMP window: Ctrl+1/2/3, digits, Enter, Alt+1/2/3 | proven live | plan 10; "272 loaded as 127.200" (`A350RmpEntry`) | |
| 77 | Transponder code: SQWK, type it | the RMP window's Alt+T (the stock XPNDR_SET, kept in step) | proven live | `docs/inibuilds-a350.md` (8192 put SQUAWK : 2000) | |
| 77 | The RMP sets only the code and IDENT | Alt+I, the SQWK page's IDENT | proven live | plan 10 Task 1, 2026-11-03 | |
| 77 | The SURV page by the menu or the KCCU SURV key | as above | proven live | | |
| 78 | Activating the transponder and TCAS: DEFAULT SETTINGS, CONFIRM * | the MFD window: SURV, DEFAULT SETTINGS, CONFIRM * | proven live | 2026-11-03, 1.2.8: XPDR to AUTO (`TRANSPONDER STATE:1` 5), TA/RA checked, "ADS-B TRAFFIC ON" said | |
| 79 | Deactivating: XPDR MODE, CONFIRM * | the MFD window: SURV, MODE: STBY, CONFIRM * | proven live | 2026-11-03, 1.2.8: "SELECT XPDR IN STBY?", then state 1, TA/RA and TA ONLY unavailable | |
| 79 | HUD: 1 NORM/X WIND, 2 brightness, 3 declutter | HUD panel rows | proven live | Panels table "HUD" (fitted and deployed) | |
| 80 | HUD symbols: FPV (AP on, AP off), energy chevrons, flight path director | the HUD read | gap (symbols) | the HUD reader reads its texts; the FPV, chevrons and FPD are drawn symbols, as the PFD's FPV | 07 |
