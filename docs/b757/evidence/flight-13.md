# Plan 13 flight evidence

## Flight 1: 2026-10-27, KCVG to CYHM (Daniel), ILS autoland

The app stayed running (plan 13's build). The L:var snapshot (`docs/b757/tools/live/snap.js`, every BlueBird name,
about 0.8-0.95 s a read) ran through the sim's own **VLogic** view with `tools/coherent-eval.ps1 -Title VLogic`, not
the `BBSTablet` view the app holds ([DBG-9]); VCockpit08 refused the socket (the app's EFB client holds it). VLogic
reads L:vars (and stock variables, flight 2), the `Shared Variables` view has no `SimVar`. So Task 1 does not need MSFSBA closed.

Snapshots, diffed with `docs/b757/tools/live/noise.txt` and the flight-control noise (ailerons, elevator, yoke, PLI,
airspeed needles, thrust levers, MCP drum gauges) set aside:

| Moment | What changed |
| --- | --- |
| APP pressed (against a baseline 25 nm out) | `B757_MCP_APP_BUTTON` 0 → 1; `B757_MCP_CMD_2_BUTTON` and `B757_MCP_CMD_3_BUTTON` 0 → 1 (all three autopilots) |
| LOC captured | `B757_MCP_LNAV_BUTTON` 1 → 0 (the LNAV light goes out as LOC takes over); `BBS_LOC_DEV_DEG` 2.71 → -0.09 |
| G/S captured, then LAND 3 (one interval) | `B757_MAIN_L_AUTOLAND_1_BUTTON_ON` and `B757_MAIN_R_AUTOLAND_1_BUTTON_ON` 1 → 0 (LAND 3; the `_2_` pair stayed 1); `BBS_LOC_DEV_DEG`, `BBS_VNAV_PATH_DEVIATION` moved. Nothing for the glideslope capture itself. |

Not captured: FLARE, ROLLOUT, the A/P and A/T disconnects (the owner was flying; next flight).

### What it means for Task 6

- **No FMA mode variable** changed at LOC capture or G/S capture, as `wasm-findings.md` section 5 predicted (the AFDS
  modes live in `bbs::afds`, getters only). LOC capture has a proxy: APP lit and the LNAV light going out. G/S capture
  has none in L:vars; the stock `NAV GSI:1` / `NAV HAS GLIDE SLOPE:1` were not read this flight.
- **`BBS_LOC_DEV_DEG`** is the localizer deviation in degrees, directly.
- **`BBS_VNAV_PATH_DEVIATION`** read 365 then 286 on the approach, while the owner had found himself several hundred
  feet high on the PROG page: very likely feet above (+) the VNAV path. A readout ("365 feet high") would answer the
  question the owner used PROG for; unconfirmed sign and units (compare with PROG next flight).
- No `DRAG REQUIRED` was heard with the CDU window open: the FMC shows it only in a VNAV PATH descent; the owner had
  likely left VNAV (the VNAV and flaps finding), so this is no evidence against the CDU window.

### Plan 13's own checks on this flight

The owner reported everything else passing: no double flaps or gear, lights summed up ("1 light on, 9 off" heard as
the engines came up). Which light the "1 on" was is unknown: the summary names none and nothing logs it (a debug log
line naming a summary's lights is proposed).

## Flight 2: 2026-10-28, CYHM to CYMX (Daniel), ILS 24 autoland

The app stayed running until after the landing. The same snapshot through VLogic, now in a loop with a read of the
stock `NAV GSI:1`, `NAV CDI:1`, `NAV HAS GLIDE SLOPE:1`, `PLANE ALT ABOVE GROUND`, `SIM ON GROUND` and `AUTOPILOT
MASTER` (VLogic's `SimVar.GetSimVarValue` answers stock variables too), from 15 nm out to the stop: 135 snapshots,
08:23:07 to 08:30:53, about one every 3.5 s (two calls per round, not the 2 s planned). Diffed round by round, with the
noise list and the drift set aside (flight controls, engines, hydraulics, pressurisation, bogie heights).

| Time | Height AGL | What changed | Reading |
| --- | --- | --- | --- |
| 08:23:10 | 2,815 | `B757_MCP_APP_BUTTON`, `B757_MCP_CMD_2_BUTTON`, `B757_MCP_CMD_3_BUTTON` 0 → 1 | APP armed, three autopilots (as flight 1) |
| 08:24:04 | 2,731 | `B757_MCP_LNAV_BUTTON` 1 → 0; `BBS_AP_TARGET_HEADING` 335 → 240 | LOC captured (as flight 1) |
| 08:24:21-45 | 2,770 | doors open, `BBS757_MAIN_GEAR_*` 0 → 200/260, `B757_MAIN_light_landingGear_L/R/nose` 1, doors closed | Gear down: one G press from OFF |
| 08:24:59 | 2,761 | `B757_MCP_HOLD_2_BUTTON` 1 → 0, the descent starts | G/S captured from ALT HOLD |
| 08:26:50 | 1,454 | `B757_MAIN_L/R_AUTOLAND_1_BUTTON_ON` 1 → 0 | LAND 3 (as flight 1) |
| 08:28:24-31 | 300 | `BBS_PLAY_CALLOUT_PLUS_HUNDRED`, `BBS_PLAY_CALLOUT_300` pulse | BlueBird's own height call-outs |
| 08:28:52 | 20 | `B757_MCP_APP_BUTTON` 1 → 0, `B757_MCP_LOC_BUTTON` 0 → 1, `B757_MCP_SPD_BUTTON` 1 → 0 | Probably FLARE, the A/T retarding (one round, unconfirmed) |
| 08:28:56 | 12 | `B757_MCP_LOC_BUTTON` 1 → 0 | Probably ROLLOUT at touchdown (unconfirmed) |
| 08:28:59 | 11 | stock `SIM ON GROUND` 0 → 1 | On the ground |
| 08:29:03 | - | `B757_MCP_CMD_1/2/3_BUTTON` 1 → 0; `B757_MAIN_L/R_AUTOLAND_1_BUTTON_ON` 0 → 1; `BBS757_FDAS_DISARM` 0 → 1; `B757_DECAL_LOCK_FAIL` 0 → 1 | The owner's A/P disconnect on the rollout |
| 08:29:06 | - | `B757_MAIN_ATDISC_1_DECAL` 0 → 1; `BBS_757_TR_1/2` 0 → 100 | A/T disconnect, by itself with the reversers (the owner did not press it, as he remembers) |
| 08:29:13 | - | `BBS_CA_MASTER_CAUTION`, `BBS_FO_MASTER_CAUTION` 0 → 1 | Master caution, cause not seen (the owner thinks the A/P disconnect) |
| 08:29:23 | - | brake pedals 100, `BBS_AUTOBRAKE_KNOB` 4 → 2, `B757_MAIN_AUTOBRAKES_1_DECAL` 1 | Autobrake disarmed by the pedals |

### What it means for Task 6

- **LOC capture:** the LNAV light going out with APP lit, on both flights.
- **G/S capture:** the ALT HOLD light going out, only when the glideslope is met in ALT HOLD (flight 1 came in on a
  VNAV path and showed nothing). No variable for a capture from VNAV PTH is known.
- **LAND 3, the A/P disconnect, the A/T disconnect:** each has its own variable (above).
- **FLARE and ROLLOUT:** nothing shows them armed. The MCP light changes at 20 ft and 12 ft are one round each, 3.5 s
  apart: too thin to build on. A third autoland with a faster loop (the snapshot alone, under 1 s a round) settles them.
- **The stock ILS variables read 0 for the whole ILS** (`NAV GSI:1`, `NAV CDI:1`, `NAV HAS GLIDE SLOPE:1`), and
  `AUTOPILOT MASTER` read 0 with three autopilots on: BlueBird feeds neither, so Task 6 cannot use them. Other NAV
  indexes were not tried.

### For other plans

- **Plan 02 Task 8 (Rob):** the ALT SEL push in a VNAV climb at about 9,000-20,000 ft (a baseline at 9,300 ft, the
  push later in the climb) changed no MCP, VNAV or altitude variable. Whether LEGS had a constraint ahead was not
  checked, so this is a second "nothing happened", not a verdict.
- **Plan 06, `BBS_VNAV_PATH_DEVIATION`:** +749 while PROG said about 800 high, in a VNAV descent: feet, positive above
  the path. Settles flight 1's question; a readout ("750 feet high") is a follow-up.
- **The gear lever and the sim's gear keys (for BlueBird, and plans 06 and 14):** after takeoff the gear stayed down
  with the lever at OFF. The baseline at 9,300 ft (about 07:37:30, 4 minutes after liftoff) read
  `L:B757_GEAR_HANDLE_POS` 0.5 (OFF), the gear lights lit and `BBS757_MAIN_GEAR_*` at 200/260 (down), while the stock
  `GEAR HANDLE POSITION` read 0 (up), so Shift+G said "Gear down, lever up". Cycling the lever with G fixed it. In
  cruise the owner pressed G again: the lever went UP to OFF (0.5, read back), and on the approach one press went OFF
  to DOWN. So BlueBird steps the lever one detent per sim gear key, DOWN, UP, OFF, DOWN, and the after-takeoff press
  most likely got two steps before the gear moved. Shift+G should read BlueBird's lever (fixed:
  `fix(b757): Shift+G says the gear lever's own position, OFF included`).
