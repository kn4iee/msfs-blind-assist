# Plan 13 flight evidence

## Flight 1: 2026-10-27, KCVG to CYHM (Daniel), ILS autoland

The app stayed running (plan 13's build). The L:var snapshot (`docs/b757/tools/live/snap.js`, every BlueBird name,
about 0.8-0.95 s a read) ran through the sim's own **VLogic** view with `tools/coherent-eval.ps1 -Title VLogic`, not
the `BBSTablet` view the app holds ([DBG-9]); VCockpit08 refused the socket (the app's EFB client holds it). VLogic
reads L:vars, the `Shared Variables` view has no `SimVar`. So Task 1 does not need MSFSBA closed.

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
