# Plan 11: the panels

## Task 1: the control map from 1.2.8 (2026-11-03)

`generate_a350_map.py` on the installed 1.2.8 package against the committed 1.2.7 map (`diff_maps.py`): 700 controls (696), the
lamps unchanged, and one change, the four oxygen masks (`OXYGEN_TEST`, behaviour components `OXY_<seat>_*`):

| 1.2.7 input event | 1.2.8 input event | What it is (1.2.8 tooltip) | Key |
| --- | --- | --- | --- |
| `OXYTEST_<seat>_HATCH` | `OXY_<seat>_HATCH` | OXYGEN MASK, open/close (`INI_OXY_HATCH_<seat>`) | kept |
| `OXYTEST_<seat>_N` | `OXY_<seat>_N` | OXYGEN PERCENTAGE, NORMAL/100% (`INI_OXY_<seat>_N`) | kept |
| `OXYTEST_<seat>_PANEL` (hold) | `OXY_<seat>_MAIN` (hold) | OXYGEN MASK TEST, PUSH TO TEST: holds `INI_OXY_BLINKER_<seat>` | kept |
| `OXYTEST_<seat>_MASK` (hold, `INI_OXYTEST_EMER_<seat>`) | `OXY_<seat>_KNOB` (toggle) | EMER PRESSURE SEL, NORMAL/EMERGENCY: flips `INI_OXY_EMER_<seat>` | kept |
| none | `OXY_<seat>_KNOB_PUSH` (hold) | OXYGEN MASK TEST on the knob, PUSH TO TEST: holds `INI_OXY_BLINKER_<seat>` | new: `A350_OXY_<seat>_KNOB_PUSH` |

The seats are CPT, FO and the observers', TO and 4TH on 1.2.7, 1 and 2 on 1.2.8 (third and fourth occupant). The generator keeps
each same control's 1.2.7 key (`KEPT_KEYS`, 16 keys), so Ctrl+M settings and tests pinning them still find them; the four knob
pushes are the only new keys, and no key was lost. The flow blinkers (MSFSBA's own indicators, no lamp node) read
`INI_OXY_BLINKER_<seat>` now and keep their 1.2.7 light keys; the latched holds (`A350WritePlan.Latched`) are MAIN and KNOB_PUSH,
the emergency selector no longer a hold.

Live on 1.2.8 through MSFSBA's rows, each seat (scratchpad `oxy_walk.sh`): stowage Closed to Open and back, N/100% Normal to 100%
and back, emergency pressure Normal to Emergency and back, each read back from its `INI_OXY_*` variable and said ("Third occupant
oxygen emergency pressure: Emergency"); the mask test and the knob's push each held the flow blinker at 1 and let it go to 0,
"First officer oxygen flow blinker on", "... off".
