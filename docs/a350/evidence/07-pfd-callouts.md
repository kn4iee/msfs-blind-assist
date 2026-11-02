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

## Task 2: what can raise each message (2026-11-02)

- The rows are in (`df41bcfd9`): one per message, the rest on "Other PFD flags and warnings", the old row's mute carried over.
- Windshear and stall cannot come from the A350's own OIS on 1.2.8: the Situation Manager's SITUATIONS are DEPRESSURISATION, BIRD
  STRIKE, DUAL ENGINE FAILURE, MEDICAL EMERGENCY, UNRULY PASSENGER and SMOKE IN THE CABIN ("Once enabled a situation cannot be reset and
  the flight must be restarted"), and its FAILURES hold neither (`situations_128`, `failures_128`). The plan allows them only that way,
  so their live words wait for the owner's ruling (MSFS's own weather, or left unheard); their rows match WINDSHEAR, W/S AHEAD and STALL,
  and any other spelling falls to the old row.
- RWY TOO SHORT and MAX REVERSE need BTV armed for a runway shorter than the landing distance: not VCBI's 3,341 m.

## Task 4: scripted, not yet flown

An AI intruder can be spawned: `msfs_create_ai_object` with an AIG title (`AIGAIM_Delta Air Lines Boeing 717-200` confirmed, object id
returned; the stock `Cessna 172 Skyhawk Asobo` is not installed as a title on 1.2.8's MSFS 2024). The flight: `tcas_log.sh` samples every
`INI_TCAS_*` aural, `INI_TCAS_VS_TGT` and the RA bands every 100 ms while the intruder is placed ahead at the same altitude on the
reciprocal heading; the log is then replayed through `A350TcasCallouts` for what MSFSBA says.

## A take-off from an already-flown FMS (2026-11-02, 17:37)

On the runway after the circuit (placed there, not landed), the FMS armed no CLB and set no speeds. AP 1 engaged at 42 ft and held about
500 ft, the levers stayed at FLX (the script moved them to CL only above 1,100 ft), and the aircraft flew at 270 kt into rising ground 13 NM
north-east. The take-off script now pulls ALT for OP CLB when not climbing 20 s after lift-off and sets CL by speed too, and the runner
refuses to roll unless the FMA shows CLB armed: each test flight starts fresh from the menu.

## The second flight (2026-11-02, 18:25-19:32): Tasks 1, 4 and 5 Step 2

A fresh flight from the menu, flown end to end by the scripts (`prep_flight.sh`, `go_route.sh`, the landing script): the flows from
cold and dark, a route built on the MFD (VCBI 04, 1030, PBD01 = KAT/260/80 picked off DUPLICATE NAMES, the discontinuity cleared,
GOPRU, the ILS Z 04), SRS and a managed climb to FL200 in NAV, the cruise with the OIS's time compression AUTO up to 2X and SIM RATE
REMINDER on, the top-of-descent pause, a managed descent to FL100, the tests below, DIR TO GOPRU, the approach phase, LOC and G/S
captured, LAND, FLARE, ROLL OUT, then After Landing, Parking and Securing on stand A6.

- **Task 1, the clean speed tape** (captures with the sim's asks answered, `p07_efis_clean_gd40/49`, `p07_efis_cruise_sim_rate`):
  green dot (172 at FL100, 175 at FL200) is drawn at y 570 and read ("Green dot: 172, green") 40 kt below the speed, and drawn past the
  tape's foot (y 610-615) and not read 49 kt and 99 kt below. The green "=" (atlas 1001,723) is drawn at the tape's top (102, 135) at
  every speed from 212 to 275 kt: it marks no speed, and nothing reads it. VFE next reads where it is drawn ("VFE next: 255, amber").
- **Task 1, SIM RATE and SIM PAUSED:** "SIM RATE: 2X, red" in the cruise and "SIM PAUSED, red" in the pause, both read (`p07_efis_sim_paused`).
- **Task 5 Step 2, the pause at the top of descent:** with PAUSE AT T.O.D YES (30 NM) the aircraft paused itself at the top of descent
  (`INI_IS_PAUSED` 1); the OIS said "TOP OF DESCENT PAUSE: Aircraft has reached top of descent. The simulation has been paused. Click
  below to resume" over RESUME FLIGHT, which resumed it. MSFSBA's `A350PauseAlerts` keys on `FMGS_PAUSE_TYPE` from the memory copy,
  which was not logged: what it said is not proven by this flight.
- **Task 4, TCAS:** two AI aircraft spawned through SimConnect (`AIGAIM_Delta Air Lines Boeing 717-200` 7 NM ahead on the reciprocal at
  FL100, 250 kt; `AIGAIM_Qantas Airbus A330-200` 3 NM ahead, 300 ft below) were confirmed created, yet every `INI_TCAS_*` aural stayed 0
  for 2 minutes and the ND showed no traffic: the A350's TCAS does not see objects created that way (or they do not stay airborne). The
  advisories need traffic the sim itself flies: AIG traffic with flight plans, or an online network.

## The third flight (2026-11-02, about 20:20-20:50): Task 4 in SayIntentions traffic

The owner started SayIntentions, which flies its own AI traffic around VCBI. A script (`tcas_hunt.py`) listed the airborne aircraft
within 30 NM (SimConnect's `RequestDataOnSimObjectType`, the user's own object left out) and steered the A350 through the FCU at the
nearest one between 2,500 and 9,000 ft, hands off once an advisory came. `tcas_log.sh` sampled every `INI_TCAS_*` aural and
`INI_TCAS_VS_TGT` every 100 ms.

- **20:39:58, about 3,000 ft near VCBI:** `INI_TCAS_DESCENT` 1 with `INI_TCAS_VS_TGT` -500; 1.99 s later it turned to
  `INI_TCAS_DESCENT_CROSS` (-200); 113 s after the first, `INI_TCAS_CLEAR`. No `INI_TCAS_TRAFFIC` came before the RA, so no TA was heard.
- **What MSFSBA says** (it was not running; the samples replayed through `A350TcasCallouts`, flushed once a second as the monitor does,
  `A350TcasCalloutsTests.The_live_descend_crossing_descend_and_clear_are_said_in_order`, `9cd5251cf`): "TCAS: Descend. Fly vertical speed
  minus 500 feet per minute.", "TCAS: Descend, crossing descend. Fly vertical speed minus 200 feet per minute.", "TCAS: Clear of conflict."
- **Against `TcasRaGuidance`** (the A32NX's and A380's): the same "TCAS: <advisory>. Fly vertical speed <plus|minus> N feet per minute."
  form and the same signed words. The A350 gives one target (`INI_TCAS_VS_TGT`), where the FBW aircraft give a green band ("minus 500 to
  minus 1500"): each reads what its aircraft shows. No difference to fix.
- The other aurals (CLIMB and its NOW, CROSS and MORE, MAINTAIN, LEVEL OFF, ADJUST, MONITOR, TRAFFIC) did not come in this traffic; each is
  bound to its variable from `sound.xml` and pinned in `A350TcasCalloutsTests`.
