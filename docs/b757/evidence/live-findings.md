# BBS 757 live probe notes (2026-10-25)

MSFS 2024 1.9.21.0 (Microsoft Store), package `bluebirdsim-aircraft-757-200` version 0.1.0 (`manifest.json`, built
2026-10-21), loaded as "BlueBird 757-200 PW N/W" (ATC type `BBS752PW`, registration G-BBS75). MobiFlight event module
1.0.1; MobiFlight Connector running (it does not serve port 8320). Probed through the simconnect MCP (MobiFlight
calculator path and native SimConnect), the Coherent debugger (:19999) and window captures. Every value changed was
put back unless noted at the end.

## Session 1: KSEA gate, cold and dark (about 00:25 to 00:55 local)

- The cockpit model took every L:var write (the overhead capture showed the APU selector where it was written), but
  BlueBird's systems did not react to any of them for the whole session: battery ON (calculator path, twice, with a
  fresh 0 → 1 transition), standby power to BAT, APU START, and the WASM's own `L:BBS_APU_INSTANT_START`. The battery
  bus (`ELECTRICAL MAIN BUS VOLTAGE:3`) stayed at 0 V. The WASM was running (it publishes
  `L:B757_M730_BATT_PERCENT` 100) and sim time ran at real-time rate.
- At the start of the session the Coherent debugger's port was held but did not answer; the owner killed some
  processes, after which it answered.
- **Cause, found afterwards: BBS Nest was not running.** BlueBird's companion app `C:\BBS Nest\bbs_nest.exe` (its
  own SimConnect client, `SubSystem.dll`; it renders the ND terrain picture and feeds traffic over SimConnect client
  data) started at 00:55:33, after this session ended and just before the reload; the sim had run since 00:25:45. The
  module carries a message that the aircraft systems do not run without BBS Nest (`wasm-findings.md` §1). A blind
  pilot gets that message only on a display, so the app must check for the process itself and say so.
- Annunciator read-back trap: the battery ON light's visibility also needs `A:CIRCUIT ON:288`, which is the overhead
  panel lighting circuit, fed only from the left AC bus (`systems.cfg` circuit.288 → bus.9). With battery power alone
  no annunciator can show, so a dark light proves nothing until AC is up.

## Session 2: reloaded on a runway (about 00:58 local)

- **The aircraft loads cold and dark on a runway too**: battery off, all buses but the hot battery buses (1, 2) at
  0 V, engines and APU stopped. BlueBird applies its own panel state at load, whatever the sim's start position.
- **Switch writes work after the reload, through both routes.** Battery ON by native SimConnect L:var write: after
  8 s buses 3 (battery), 5 (DC standby) and 8 (AC standby) read 30.6 V. Battery OFF through the MobiFlight calculator
  path: 0 V after 8 s; ON through the calculator path: 30.6 V after 8 s. The reaction is not immediate (a relay or a
  slow WASM tick): a read-back must wait several seconds.
- **APU start through the calculator path works**: selector `L:B757_OVHD_APU_SWITCH` 2 (START) for 3 s, then 1 (ON).
  `L:B757_APU_STATE` went to 1 and `L:B757_APU_RPM_PRC` climbed (14.8 % → 19.8 % → 31.8 % over about a minute),
  realistic spool-up. **The stock `APU PCT RPM` read 100 % at once while BlueBird's was at 19.8 %**: stock APU values
  are not usable on this aircraft; use `L:B757_APU_RPM_PRC`. It reached 99.6 % after about 90 s; with the APU
  generator switch already ON (its default), the left and right AC buses (9, 11) read 32 V.
- **Both CDUs then came up and their screens arrived as text** over the Comm Bus; key presses work as held L:var
  pulses. Details in `cdu.md`.

## MCP (session 2)

- The selected values live in WASM L:vars: `L:BBS_AP_TARGET_ALTITUDE` (10000, matches the window),
  `L:BBS_AP_TARGET_HEADING` (0), `L:BBS_AP_TARGET_VS`. No IAS target var found yet (the window showed 200; stock
  `AUTOPILOT AIRSPEED HOLD VAR` read 100; `L:BBS_TARGET_MACH` exists): find it in the panel sweep.
- The heading knob's cockpit code steps a knob counter and fires `K:HEADING_BUG_INC`/`DEC`; the WASM counts those
  events: one stock `HEADING_BUG_INC` moved BlueBird's target 0 → 1 and the stock bug 0 → 1.
- **Stock `HEADING_BUG_SET` does not reach BlueBird**: it set the stock bug to 90, BlueBird's target stayed at 1.
- **A direct write of `L:BBS_AP_TARGET_HEADING` sets the MCP heading exactly**: 90 written, the next INC gave 91, and
  the MCP heading window showed `091` (window capture, cropped). So exact MCP values can be set by L:var write, if the
  same holds for altitude, V/S and speed (to measure); the stock bug then disagrees and must not be read as the MCP
  value.
- Restored afterwards: target heading 0, stock bug 0.
- **Speed (session 3)**: the MCP IAS target is `L:BBS757_ORANGE_AIRSPEED_BUGS` (read 204 while the MCP window
  showed 204 in a capture; found by diffing all WASM names before and after five `K:AP_SPD_VAR_INC`). It is an
  output: a direct write of 210 was overwritten (the next INC gave 205). Each `K:AP_SPD_VAR_INC/DEC` is 1 kt, and a
  burst of 10 or 11 events in one calculator call all counted. So the app sets speed by stepping the difference.
  The stock `AUTOPILOT AIRSPEED HOLD VAR` moves with the steps but sits at an unrelated offset (100 against 200):
  never read it as the MCP value. The window's digit drums (`B757_MCP_Gau_SPD_AUTO_1/10/100`) are cumulative
  animation positions, not digits.
- **Altitude (session 3)**: `L:BBS_AP_TARGET_ALTITUDE` adopts a direct write (12000 written, one `K:AP_ALT_VAR_INC`
  gave 12100: a step is 100 ft). Restored to 10000.
- **V/S (session 4, 2026-10-25 overnight, aircraft powered at the gate)**: `L:BBS_AP_TARGET_VS` adopts a direct write,
  negative values too (500 and -1500 read back), and one `K:AP_VS_VAR_INC` added 100 fpm (500 → 600). Restored to 0.
  Reads through the simconnect MCP lag one call behind its writes (its own registration), so read twice.
- **Mach flag (session 4)**: pulsing the IAS/MACH changeover (`L:B757_MCP_AP_SPD_MACH_SW` 1 then 0, separate calls)
  left `L:BBS_AIRSPEED_IN_MACH`, `L:BBS_TARGET_MACH`, stock `AUTOPILOT MANAGED SPEED IN MACH` and the speed target
  (200) unchanged: either the pulse did not register or the flag is another variable. Unproven.
- **Fuel and payload setters (session 5, 2026-10-25 morning, on the ground, APU running)**: every
  `L:BBS_EFBX_SET_*` setter rests at -1, takes one write and is consumed. Fuel: `_FUEL_LEFT_LBS` 10000 and
  `_FUEL_RIGHT_LBS` 10000 gave 1,488.5 gal per wing tank, `_FUEL_CENTER_LBS` 20000 gave 2,973 gal (BlueBird converts at
  about 6.72 lb/gal; the sim reports 6.70); total 39,865 lb. Tanks (`flight_model.cfg`): center 6,924 gal, left and
  right 2,176 gal each. Payload (stations `PAYLOAD STATION WEIGHT:3` First Class, `:4` Economy, `:5` Baggage FWD, `:6`
  Baggage AFT; `:1`/`:2` are the pilots at 200 lb): `_PAX_BUSINESS_LBS` 1900, `_PAX_ECONOMY_LBS` 30000,
  `_CARGO_FWD_LBS` 4000, `_CARGO_AFT_LBS` 6000 each set their station exactly. `_PAX_WEIGHT_LBS` 32000 split itself
  2,909.09 / 29,090.9 (1 : 10, BlueBird's cabin ratio). `_CARGO_WEIGHT_LBS` fills the forward hold first: 8000 went
  all forward, 25000 went 10,300 forward and 14,700 aft (the hold limits, it seems). Left loaded: fuel 39,865 lb,
  passengers 32,000 lb, cargo 25,000 lb.
- **BlueBird's own SimBrief loading (session 5, EFB by scripted clicks, seen through the ND mirror)**: the plan was
  BVI 4RK KSEA-KDEN, units lbs, block fuel 25,550, 224 passengers at 175 lb (+55 lb bags), cargo 12,259, payload
  51,459, ZFW 184,000 (= max ZFW). EFB positions (EFB-local): Aircraft app menu FUEL (67,237), WEIGHT & BALANCE
  (124,307); Fuel page IMPORT FROM SIMBRIEF (508,962; 511,981 missed); START BOARDING in the top bar (1231,24); the
  "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?" dialog YES (400,648), the same spot
  as the units dialog's YES; "BOARDING IS COMPLETE!" OK (731,879). The Fuel page's import and START BOARDING both ask
  that question when current fuel is above the plan's.
  - Fuel (IMPORT FROM SIMBRIEF, then the defuel ran about 3.5 minutes): **both wings evenly, center empty** for a
    block under the wings' capacity: left 1,902.0 gal, right 1,902.7 gal (about 12,745 lb each), center 0; block
    25,550 on the EFB. It first filled both wings to full (2,176 gal, 14,579 lb) while draining the center.
  - Payload (START BOARDING, about 25 minutes, cargo-door notifications; it zeroes the stations first): passengers
    **30,800 lb = 176 seats × 175 lb** (the plan's 224 capped at the cabin's 16 + 160 seats), split by BlueBird
    2,800 First / 28,000 Economy (shown as 15/16 and 151/160 passengers, about 185 lb each); cargo **20,659 lb =
    payload − passenger weight**, split evenly between the holds but capped at the forward hold's 10,300 lb: forward
    10,300, aft 10,359. Result: ZFW 182,734, ZFW CG 28.9 %, gross 208,239, CG 31.2 %.
  - The EFB was left on Home, the mirror removed, the camera restored (cockpit view 1/1).
- **Calculator string length (session 4)**: the simconnect MCP refuses calculator strings longer than about 220
  characters ("negative count": it pads to a 256-byte message), so a 25-event speed burst could not be measured through
  it. MSFSBA's own MobiFlight channel is 1,024 bytes (`MobiFlightWasmModule.MAX_COMMAND_SIZE`).

## WASM-only variables, first reads (session 2, APU running)

From `wasm-findings.md`'s list; all read through the calculator path:

| Variable | Read | Meaning so far |
|---|---|---|
| `L:B757_APU_STATE` | 1 while spooling, 2 running | enum, EFB labels APU OFF / START / RUN / SHUTDOWN (0-3 presumably) |
| `L:B757_L_BUS_PWR_SRC`, `L:B757_R_BUS_PWR_SRC` | 2, 2 | power source per AC bus; 2 on the APU generator (other values to map) |
| `L:B757_LEFT_PACK_STATE`, `L:B757_RIGHT_PACK_STATE` | 1, 0 | matches the EICAS "R PACK OFF" at that moment |
| `L:BBS_ECAM_Mode` | 1 | lower EICAS page (to map) |
| `L:eK104_MAIN_BAT_RELAY` | 0 with the battery on | expected: relay export is off in the settings |
| `L:B757_DEBUG_PRESSURE_CABIN_ALT_FT` | 0 on the ground | needs a climb |
| `L:BBS_CA_MASTER_CAUTION`, `_WARNING` | 0, 0 | while EICAS showed cautions: probably the lamps after a reset; to measure |
| `L:BBS_EFBX_SET_TOTAL_FUEL_LBS` | wrote 20000, read -1 after 3 s | **the WASM consumes it** (-1 = idle), but fuel did not change in 15 s (19,946 lb, APU burning); probably sets the EFB's fuel target and still needs the EFB's load step or the fuel truck |

## EFB-replacement checks (session 2)

- Navigraph: not signed in (helper `<bbs-tablet>`: `ngAccessToken` null, `ngRefreshToken` empty), but a device
  sign-in request was pending and polling (`ngPoll` true): `nav_device_auth` holds `device_code` (secret, never read
  out), `user_code` (8 characters), `verification_uri` (navigraph.com), `verification_uri_complete`
  (identity.api.navigraph.com), `expires_in` 1800, `interval` 5. Only field names and lengths were read.
- **Navigraph sign-in done by the owner from the read-out code** (user code and the address with the code built in,
  read off `nav_device_auth`): afterwards `ngAccessToken` held 1,102 characters, `ngRefreshToken` 32, `ngPoll` false.
  The pending request had been replaced about every 5 minutes (`nav_auth_time_start` 1,761,558 → 2,082,744 →
  2,152,939, `performance.now()` milliseconds), so a code is good for minutes, not the 30 its `expires_in` says.
- Fuel: stock `FUEL TANK LEFT MAIN QUANTITY` refused a write (stayed 1,476.6 gal). `L:BBS_EFBX_SET_FUEL_LEFT_LBS`
  10000 → left tank 1,488.4 gal within 6 s, setter reads -1 after. `L:BBS_EFBX_SET_TOTAL_FUEL_LBS` 20000 → consumed,
  no change in 15 s.

## State left in the sim after session 2

Battery ON, APU running with its generator on the AC buses, CDUs on (captain on POS INIT 1/4, first officer on IDENT,
scratchpads empty), MCP heading 000, left main tank set to 10,000 lb (about 80 lb more than loaded). Everything else
as loaded.

## Camera and displays

- Instrument camera views (`cameras.cfg`, category Instrument): ADI/EHSI, EICAS, Overhead, IRS, Engines, FMC,
  Circuit Breakers. **Measured: instrument index 2 is Overhead** (`CAMERA VIEW TYPE AND INDEX:0` = 2, `:1` = 2,
  captured). **Instrument index 1 is the EICAS view**, and it frames far more than EICAS: the whole MCP, both EICAS
  CRTs (upper messages legible at 1600 px wide: "R FUEL SYS PRESS", "R ENG SHUTDOWN", "R PACK OFF", "PAGE 1", TAT,
  EPR), the standby instruments, autobrake, flap gauge, gear lever and the FO-side airspeed: a strong candidate frame
  for AI display reads. Cockpit pilot view: `:0` = 1, `:1` = 0. When switching type and index, write them in
  separate calls and re-read: one `:0` write was reported as not taking effect and had to be repeated. `CAMERA GAMEPLAY PITCH YAW` is not settable (no way found yet to
  look left at the EFB). `K:VIEW_LEFT` did nothing.
- The WASM displays (PFD, ND, EICAS, CDU, EFB, radio LCDs) render natively: their Coherent views (VCockpit29/30) hold
  no DOM canvas, so their pixels cannot be read through the debugger. Window captures are the only way to see them.

## Coherent views with the 757 loaded

`VCockpit28 - BBSTablet - WasmInstrument - WasmInstrument` (the hidden `bbs-tablet.html` helper and the systems
gauges), `VCockpit29` (eleven instruments including `BBSNavigraphMap`), `VCockpit30` (eighteen WASM instruments).
A read-only Comm Bus listener injected into VCockpit28 registers fine (`RegisterViewListener('JS_LISTENER_COMM_BUS')`
then `.on('BBS_CDU_DISPLAY_1' | '_2')`); with the aircraft unpowered it received nothing, as expected for a sender
that only sends on change.

## Local servers

No new listening port appeared with the 757 loaded (compared against the Fenix-loaded baseline): BlueBird runs no
local server. Nothing listens on 8320 and the sim made no connection to it during the checks.
