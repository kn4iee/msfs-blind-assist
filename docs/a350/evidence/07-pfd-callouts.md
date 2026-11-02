# Plan 07: the PFD and its call-outs on 1.2.8

Measured 2026-11-02 on the iniBuilds A350 1.2.8 at VCBI on a fresh flight from the menu (stand A6, cold and dark, 155 t): the flows, a
scripted take-off from runway 04 with the levers held at FLX to 1,700 ft, a left-hand circuit at 5,000 ft, an approach to the ILS Z 04 with
a go-around from about 1,800 ft. Reads through `A350Probe repl`'s `disp Captain PFD` (MSFSBA's own reader, the sim's asks answered from
SimConnect), `A350Probe pfdlisten` (MSFSBA's PFD monitor and what it would say) and `A350Probe draw live EFIS#0` captures.

## Task 3: LVR CLB

- **It flashes.** From the thrust reduction height (THR RED 1,030 ft on the MFD's PERF T.O) the FMA's autothrust column read
  "MAN, white; FLX, white; +67, cyan; LVR CLB, white" and "MAN, white; FLX, white; +67, cyan" in turn, about a second each, until the levers
  went to CL at 1,700 ft (reads 0.3 to 3 s apart: on at 1,036, 1,181, 1,458 and 1,574 ft, off at 1,357 and 1,640 ft). The same after TOGA on
  the go-around: "MAN; TOGA; LVR CLB, white" and "MAN; TOGA, white" alternating every 1-2 s for 30 s, 22 reads.
- **MSFSBA said it once, without "flashing":** `pfdlisten` heard "FMA autothrust: MAN, white; FLX, white; +67, cyan; LVR CLB, white" once
  at the thrust reduction, then nothing until "THR DCLB". The fix of Task 3 Step 2 applies.
- **The captures:** the standalone `draw live EFIS#0` never drew LVR CLB in 25 tries (its sim asks answer with defaults, `A350_N2=95`
  included), while MSFSBA's reader showed it flashing in the same seconds. The fixture pair for the fix comes from the reader's own lines,
  not a draw capture (a ruling, recorded in the plan).

## Task 1: the PFD's open checks

| State | What MSFSBA's reader said | Captured |
| --- | --- | --- |
| CONF FULL on the glideslope (2,000 ft, 129 kt) | "Flaps: FULL, green", "Speed target: 123, magenta" | `p07_efis_conf_full.json` |
| The flap lever moving to 2 | "Flaps: 2, green" (the lever's position, said once) | `p07_efis_flaps2_moving.json` |
| FCU ALT ABOVE A/C (V/S -600 below the FCU's 5,000 ft) | "FMA lateral: HDG, green; FCU ALT ABOVE A/C, white": at the end of the lateral column | no (the standalone draw did not draw it) |
| Clean, green dot 45 and 55 kt below the speed (217 and 227 kt, green dot 172) | read live; captures taken but without the speed tape | waiting: a capture with the sim's asks answered |

The standalone draw draws no speed tape (its asks answer with defaults), so the clean configuration's green "=" and the green dot at 45 and
55 kt away still need a capture taken the way MSFSBA draws. `A350Probe displays` answers the asks that way but saves only the E/WD: a
request for plan 20, which owns the probe (save the EFIS too). SIM RATE and SIM PAUSED were not tried (no pause, per the owner).

## Task 5: METRIC ALT

- On (the FCU's METRIC ALT, row `A350_FCU_METER`, state `INI_FCU_METRIC_STATE`): "Selected altitude: 2500, cyan" then "Selected altitude in
  metres: 760, cyan", and "Altitude: 4909 feet, green" then "Altitude in metres: 1500, green". Off: no metric line. The FCU stays in feet.
  Pinned on the captures: `Metric_altitude_reads_on_1_2_8_with_metric_alt_on`, `A_metric_altitude_box_is_never_said_with_metric_alt_off`.
- The take-off speeds as entered: V1 141, VR 143, V2 148, FLEX +67 on the MFD's PERF T.O, "Speed target: 148, magenta" and "V1: 141, cyan"
  on the PFD at the line-up.

## Task 6: green dot, S and F on the ground

On the stand with fuel (15 t), payload (ZFW 140 t) and the PERF T.O entries in, before engine start: `FMGEC_GD_SPD` 173.6, `FCPC_S_SPEED`
153.7, `FCPC_F_SPEED` 137; the MFD's PERF T.O page "Green dot: 173 KT", "S: 153 KT", "F: 137 KT"; the speed tape "30 knots or less", no
symbol. The variables hold the page's values on the ground, truncated as the page shows them.

## Task 7: every weight in the aircraft's unit

Set on the OIS (OPTIONS > OIS SETTINGS > AIRCRAFT WEIGHT UNITS), live 2026-11-02 on the runway after the flight:

| Read | KGS | LBS |
| --- | --- | --- |
| SD permanent data (W, Shift+W read it) | "Gross weight: 142200 KG", "Fuel on board: 2200 KG" | "Gross weight: 313500 LBS", "Fuel on board: 4800 LBS" |
| The sim (F, Shift+F read the stock fuel) | — | FUEL TOTAL QUANTITY WEIGHT 4808 lb, TOTAL WEIGHT 313455 lb: they agree with the SD |
| MFD FUEL & LOAD, as drawn | "GW: 155.0 T", "BLOCK: 15.0 T" (tonnes) | "GW: 313.5 LBS", "FOB: 4.8 LBS", "TAXI: 1.3 LBS": the MFD draws thousands of pounds with the label "LBS" (`MFD#0` raw texts "4.8" and "LBS"), read as drawn |
| OIS LOADSHEET | "…KG" | "FIRST: 0 PAX, FIRST/PREM ECO 0 LBS", the holds in LBS: it follows the aircraft's unit |

W and Shift+W say the asked unit from the SD's figure in either unit, F and Shift+F from the stock fuel in either: no wrong unit, so no fix.
Pinned: `Gross_weight_from_the_aircraft_in_pounds_reads_under_de_DE`. The MFD's "313.5 LBS" for 313,500 pounds is iniBuilds' own
drawing (its kilogram mode draws T for tonnes); a pilot hears what the screen shows, as everywhere else.
