# Plan 15: the manual's ANF to HUD pages (PDF 64-80)

2026-11-03, 1.2.8. The ledger is `docs/a350/ledger/15-anf-to-hud.md`. The noise function's three scenarios (PDF 70-72) are image
pages, rendered and read: each a climb profile with its FMA boxes (MAN TOGA / SRS, then THR DCLB / CLB or NOISE N1 88% / SRS and
CLB, the ramp THR DCLB*, then THR DCLB / CLB), its speeds (V2+10, 215 kt, 250 kt) and the PERF T.O fields set for it (THR RED
1650, ACCEL 1650 or 3000, NOISE END 4000, N1 88.0 in scenario 3).

Live on the stand:

- MFD PERF T.O: THR RED, ACCEL, the NOISE key; NOISE opens NOISE END, N1 and SPD (grey until NOISE END is set) and CANCEL * NOISE,
  which put the page back.
- MFD SURV: XPDR MODE STBY asked "SELECT XPDR IN STBY?", CONFIRM * put `TRANSPONDER STATE:1` to 1 and greyed TA/RA and TA ONLY;
  DEFAULT SETTINGS asked "SELECT SURV DEFAULT SETTINGS?", CONFIRM * put it back to 5 (AUTO) with TA/RA, and the E/WD said "ADS-B
  TRAFFIC ON".

To fly: a noise take-off (the three scenarios' FMA, plan 07); a BTV rollout to its end (DECEL, off at 10 kt, AUTO BRK OFF and its
aural, the disconnect press; plan 08's fresh-session autoland). Gap for plan 07: the HUD's and PFD's symbols (FPV, energy
chevrons, flight path director) no line reads.
