# Plan 03: the reader on 1.2.8

Measured 2026-11-02 on the iniBuilds A350 1.2.8 at the VCBI stand, engines running, through `A350Probe` built from plan 03's commits
(patch version 6), with `A350_LOG=1` so the reader wrote its logs. A memory copy for `sdmap` and `sdsweep` was saved in the session's
scratch folder and stays on that machine ([A350-1]; never committed).

## Task 4 Step 6: what draws with `nvgTransform`

Not the MFD, as the 2026-10-31 log suggested: on 1.2.8 both EFIS draws call it (`EFIS#0: the draw code called nvgTransform (applied)`,
`EFIS#1` the same, the moment the reader started). An MFD menu-bar click (POSITION at 308,77) opened its menu and logged nothing new.

On the ground the ND shows the airport map, and every one of its labels is drawn at its own origin (local 0, 0) and placed by
`nvgTransform` alone: "TWR", the stands ("P", "C", "D", "E1"), "TERMINAL", "PIER 1", "CARGO VILLAGE TERMINAL", each now at its own place on
the ND (x 1,156 to 1,504). Before patch 6 the transform was dropped, so every such label sat at one point. The capture is
`tests/MSFSBlindAssist.Tests/A350/Fixtures/p03_efis_transform.json` (`A350TransformTests.The_airport_maps_labels_are_placed_by_nvgTransform_across_the_nd`).
What the ND window and the ANF make of the labels now in place is plan 08's.

## Task 5 Step 6: the facts check at the gate

Both parts decided within 7 s of the displays being ready, and agreed; nothing was withheld or said.

- Table: agrees on 141 pairs, the variable table's slots the definition streams: the switches and lights (`INI_BUS_TIE`, `INI_COMMERCIAL1_ON`,
  `INI_GEN_1A_SWITCH` … `INI_GEN_APU_GEN_SWITCH`, `INI_OUTER_TANK_LEFT`, …), each 1 in the copy and 1 in the sim.
- Variables: agrees on 2 pairs, `INI_RMP1_TRANSMIT_CHANNEL` and `INI_RMP1_1_RECEIVE_SEL`, 1 and 1.
- The minute line: `… no failures, 0 retried, 0 torn texts; … facts: table agrees, variables agrees`.

At the gate the speeds the table holds (VLS, VFE, VMAX) read 0, so no pair counted them. In the climb of the flight that afternoon (the reader
started again at 6,000 ft): table agrees on 141 pairs, `INI_VFE_SPEED` 255 and `INI_VMAX_SPEED` 340 among them, each the same in the copy and the sim;
`INI_VLS_SPEED` (143.2, moving as fuel burned) did not count, as designed; variables agree on 5 pairs (`INI_AUTOTHRUST_TLA:1` and `:2` 25, `INI_SPOILERS_ARMED`
1, and the two RMP ones).

## Task 6: the addresses on 1.2.8

| What | 1.2.8 | 1.2.7 |
| --- | --- | --- |
| `Variables` f64 array | 1343312 | 1343616 |
| `VariablesEnum` name table | 920624, 1939 names, the same indexes (`INI_FMC_PAGE1` 303, `INI_flight_phase` 337, `INI_V1` 358, `INI_IS_PAUSED` 623, `FMGS_PAUSE_TYPE` 1670) | 920480 |
| `VariablesEnum` → L:var id | 1335552 | 1335856 |
| `FlightPlan` | 1417664; V1/VR/V2 +964/+968/+972, FLEX +936, minimums +748/+752 | 1418656, the same offsets |
| `BTVUpdater` | 1291824, the same offsets; autobrake level 1343560 (enum 31) | 1292128 |
| SD page shown | 1449824 | 1451120 |
| pointer request slots | x 1080776, y 1080780, flags 1411689 / 1411690 | 1082152, 1082156, 1413577 / 1413578 |
| FMGS page vector, page flag and elements, MFD pointer | unchanged (FMGS+52, page+192, +196/+200, FMGS+200/+204): the MFD window reads its pages and its clicks land | |

The variable table and the L:var ids moved 304 bytes, the name table 144; the SD page 1,296. `sdmap` and `sdsweep` now take the table and
the page from the module: `sdsweep copy 1 V:INI_flight_phase=1,4` ran (page 1 the same at both), and `sdmap copy 1` named 29 cells, among them
`INI_IS_METRIC` turning page 1's "KG/H" into "LBS/H", the right variable for that cell.

## Task 7

The flight of 2026-11-02 (VCBI to VCBI, about 55 minutes of the reader's minute lines from the gate to the stop after the autoland, copies of 42 to
44 MB): no failures, no traps, 0 retried; the only fresh instances were the reader's two starts (9 and 8 ms). Traps per hour: 0 at this memory size;
the 2026-10-31 traps came at much larger copies late in a long flight.
