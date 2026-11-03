# Plan 24 ledger: every display decoded

Measured with `tools/a350-probe/coverage.py` and `sprites.py` over the 2026-11-03 corpus (two flights, 100 rounds plus 14 gear-travel
captures, read again offline after each change with `A350Probe reread`). A gap is read, turned into words, or listed here as junk with
its reason.

## Read (commits on feat/inibuilds-a350)

| Display | What | Commit |
| --- | --- | --- |
| SD WHEEL | gear doors by their hinges, gear extending "in transit" | 9c93f75a7 |
| FCU (callouts) | the emergency descent's heading turn said once, settled (EMER DES = INI_PITCH_MODE 33) | 3f8447171, 122064e19 |
| HUD | speed, speed target, V1, Mach, altitude, V/S, selected altitude on the tape, LS block, heading, bank, pitch, the lone N | 3a964e5eb |
| SFD, SND | LS block, heading source (I, R, 3), SND heading off the rose | 806c80619 |
| FCU display | degree sign as "degrees", KT as "knots", dashes as "Dashes" | 4b20f1d3b |
| ND | path symbols: level-off (climb, descent), top of descent, intercept point, decelerate point, speed change; placed in the ND window | 9ddd83254 |
| PFD | VLS, VMAX, V alpha prot, V alpha max (caution strips) | 402473bda |
| PFD | speed trend, short-term managed speed, LS course pin, gear, take-off trim pointer, sidestick order, yaw bar, tail strike limit | e45a43ba8 |
| PFD | flight director command, slip index | 7455a4366 |
| PFD | linear deviation (vertical profile) | d3355527c |

## Not a gap (consumed into a value, or a false hit of the word measure)

- Tape, drum, ladder and compass labels on the PFD, HUD, SFD and SND: read as the speed, altitude, pitch, heading they give.
- Label words said in other words: GS and TAS (Ground speed, True airspeed), STBY (the RMP's standby), IKONAF (IKONA with the overfly
  glyph), the engine gauges' "75." and "9" (75.9 %), the LS frequency "109." and "90".
- ND map labels outside the ND (left of x 836 or below the screen): scissored out of view.
- The ETP's green circle (its "(ETP)" label is read), the airport's cyan crossed circle (its name is read), the white selected-altitude
  bracket on the approach (its number is read).

## Junk (said nowhere, with the reason)

- The FCU's EFIS key legends (CSTR, WPT, VORD, NDB, ARPT, WX, TERR, ID, and the symbol-font "abca", "adee"): the same white words in
  all 100 rounds, the keys' printed names; the EFIS panels name and read those keys.
- RECALL, PRINT, FLT on the E/WD: fixed labels.
- The PFD's normal-law pitch limit marks (four green "="): always there in normal law; their loss (amber crosses) is read.
- Background art: the attitude sphere's arcs, the bank scale, the flap indicator's frame and dots, the trim scale's picture, the boxes
  behind the heading and altitude windows.

## Still to read

- HUD sprites: flight path vector and guidance cue (against the boresight), speed target triangle, green dot, the speed tape's caution
  strips and VFE/F/S marks, slip index, LS scales and diamonds.
- ND: the VOR bearing pointers (circled 1 and 2), the selected heading bug and lubber line on the compass, the range marks.
- E/WD and SD: the 35 CTR sprites against `A350SdSymbols`' catalogue.
- SFD and SND vector shapes (deviation, needles).
- MFD leftovers: the F-PLN header's DIST and FPA, MODE, MACH; the green "=" parked above the PFD's speed tape (never on its scale here).
