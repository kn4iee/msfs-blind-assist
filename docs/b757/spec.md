# BlueBird Simulations 757-200 (0.1.0): what MSFS Blind Assist can read and drive

Investigation of 2026-10-23 to 2026-10-25, on the `B757` branch (kn4iee's fork) cut from `main` at 5a48f17c6. The
aircraft: package `bluebirdsim-aircraft-757-200` version 0.1.0 (release day), MSFS 2024 1.9.21.0, Microsoft Store.
This spec argues from the evidence in `docs/b757/evidence/`; the plans in `docs/b757/plans/` argue from this spec. The
integration plan deletes `docs/b757/` before the branch merges, as on `FenixV3`.

How it was checked: the installed package (read only: cockpit XML, `systems.cfg`, `cameras.cfg`, the WASM module's
strings, the two small JS files), the sim's work folder (settings JSON, keys only for credentials), the live sim
through the simconnect MCP (MobiFlight calculator path and native SimConnect), the Coherent debugger (:19999), window
captures for anything visual, public web research, and three agents (repo touchpoints, WASM strings, control map).
"Measured" means read or written in the running sim; everything else says where it comes from.

## 1. Before anything works

| # | Finding | Evidence | Consequence for the app |
|---|---|---|---|
| 1.1 | **BlueBird's systems do not run without BBS Nest** (`C:\BBS Nest\bbs_nest.exe`, BlueBird's companion app, its own SimConnect client). For a whole session with Nest not running, the cockpit model took every switch write but no system reacted (battery bus dead, APU never turned); after Nest started and the aircraft reloaded, the same writes worked. The aircraft's own warning is visual only | `live-findings.md` session 1 and the Nest start time; `wasm-findings.md` §1 | Check for the `bbs_nest` process on aircraft load and say so if it is missing ("BBS Nest is not running: the 757's systems will not work"), and offer the check on demand |
| 1.2 | **The aircraft always loads in BlueBird's own panel state** (cold and dark here), at the gate and on a runway alike; the sim's start position does not set it up | `live-findings.md` session 2 | Nothing in the app may assume engines or power at load. Getting ready is either the pilot's flow (First Officer) or BlueBird's EFB presets, which a blind pilot cannot use (§6) |
| 1.3 | **Systems react after a delay**: the battery bus followed the switch only after several seconds | `live-findings.md` session 2 | Read-backs wait for the delivered value (as [MD11-9] does), never a fixed short sleep |

## 2. Identity and variants

- One base SimObject (`bbs-757-200-base`) and ten variant SimObjects: PW or RR engines; winglets, no winglets (`N/W`),
  scimitar winglets (RR only); `AWE` (four-door) layouts; `NO CABIN` versions. TITLE examples: "BlueBird 757-200 PW N/W",
  "BlueBird 757-200 RR Scimitar/W". ATC type `BBS752PW` or `BBS752RR` (and `BBS752PWNW` on one), ATC model
  `TT:ATCCOM.AC_MODEL B752.0.text` (`evidence/package/variants.tsv`). The CDU IDENT page names the engine ("PW2037");
  the WASM has `L:B757_ENG_VARIANT` (PW2037 or RB211-535E4).
- Coming later (vendor): the 757-300 (native MSFS 2024 version only) and a freighter. Variant detection keys on TITLE
  prefix "BlueBird 757-200" plus the engine variable, and must not guess the later names.
- The app already knows B752/B753 for ground traffic and size class; the profile is chosen by the pilot as for every
  aircraft. Proposed profile code `BBS_757` (no `PMDG_`, `FENIX_` or `HS_` prefix: prefix checks exist,
  `evidence/repo-touchpoints.md` §1).

## 3. How the app talks to it

### 3.1 Cockpit controls: L:vars and plain RPN (measured)

- Every cockpit control is defined in readable model-behavior XML: a component with its state L:var (`ANIM_CODE`),
  its click as RPN (`LEFT_SINGLE_CODE`, knobs `CLOCKWISE_CODE`/`ANTICLOCKWISE_CODE`, momentary release
  `LEFT_LEAVE_CODE`, levers `DRAG_CODE`), and often its position names (`ANIMTIP_0..3`, e.g. "BATTERY OFF" /
  "BATTERY ON"). The control map (`evidence/controls/`, made by `tools/b757-gen/extract_controls.py`, which expands the
  aircraft's own templates) counts **1,231 controls and 518 lights**: 511 circuit breakers, 178 toggles, 163
  momentary buttons, 138 CDU keys, 78 finite and 49 infinite rotary knobs, 35 multi-position switches, 30 levers, 24
  guards, 14 select buttons, 7 held buttons, 4 others (yokes, pedals, tiller). **582 are regular enough to generate
  mechanically** (an L:var state plus a replayable click); 943 have no label of their own (511 breakers, 138 CDU keys,
  about 294 others) and some vendor labels are copy-paste slips (the ALT knob says "BARO ADJUST"), so names need
  curation, as the MD-11's generated map did.
- Irregular controls (control map `panels.md`): **circuit breakers are bitmasks** (all 511 in `L:BBS_CB_BITS_0..15`,
  a click XORs one bit); **77 controls keep no L:var state** (animation-progress buttons such as EXT PWR, GEN DRIVE,
  PACK, RAT, PASS OXY use `O:` variables; the MCP ALT and standby baro knobs only fire K: events); **radio knobs bump
  request L:vars** the WASM consumes (`L:B757_VHF_L_WHOLE_INC`, `L:B757_ADF_L_INNER_INC`, `L:B757_OVHD_LDG_ELEV_CHG`);
  **toggles often write two vars** (state plus `_ON` legend), so the app replays the whole click; PW and RR fuel
  control switches differ (`L:BBS_fuelcut_PW1/2` against `L:BBS_fuel_RR`); 7 held buttons need a hold time (yoke A/P
  disconnect 1.5 s, PACK, cargo fire bottles, SYS FAIL).
- Lights are `L:<NAME>_DECAL` (nonzero = lit), set by the WASM, plus `_BULB_FAILED` (failed bulb) and
  `L:OVHD_TEST_LTS` (lights test). Measured: APU RUN lit while running, APU FAULT, L BUS OFF and BAT DISCH dark.
  **A light's own visibility also needs its panel-lighting circuit** (`A:CIRCUIT ON:288` for the overhead, fed from
  the left AC bus): on battery alone no annunciator shows, so the app reads the `_DECAL` var, not the visibility.
- **Writes work through both routes** once Nest runs (1.1): the MobiFlight calculator path (unique `<n> 0 *` prefix,
  [SIM-17]) and the native SimConnect L:var write. Measured: battery ON/OFF/ON, APU selector START then ON (APU
  started), standby power selector. Replaying a control's own click code is the safest write (it updates every var the
  cockpit updates, e.g. the battery click sets both `B757_OVHD_BATTERY_BUTTON` and `_ON`).
- Momentary buttons are held L:vars: MCP buttons pulse `L:<button>_PRESS` 1 then 0 (light `L:<button>`). The
  `H:BBS_AP_*` events in the XML are dead code (commented out); the only live H: events are a debug one on the EICAS
  EVENT button and a leftover on the first officer's standby altimeter. No B: input events are defined.
- No stock autopilot or electrical event drives BlueBird's systems directly, except where the cockpit code itself
  fires one: 19 controls do (MCP knobs `K:HEADING_BUG_INC/DEC`, `K:AP_ALT_VAR_INC/DEC`, `K:AP_SPD_VAR_INC/DEC`,
  `K:AP_VS_VAR_INC/DEC`; baro knobs `K:KOHLSMAN_INC/DEC` with index 1-3; TCAS/transponder `K:XPNDR_*`;
  `K:AUTOPILOT_OFF`, `K:AUTOPILOT_DISENGAGE_SET`, `K:AUTO_THROTTLE_TO_GA`, decision height, `K:LOGO_LIGHTS_SET`). The
  WASM also intercepts the stock flight-control, flap, spoiler, trim, throttle, gear, steering and brake events.

### 3.2 MCP values (measured)

The selected values live in WASM L:vars `L:BBS_AP_TARGET_HEADING`, `L:BBS_AP_TARGET_ALTITUDE`, `L:BBS_AP_TARGET_VS`
(IAS/Mach target not found yet). **Writing the L:var sets the MCP exactly**: heading 90 written, the window showed
091 after one step (window capture). The WASM counts `K:HEADING_BUG_INC/DEC` steps but **ignores stock
`HEADING_BUG_SET`** (the stock bug went to 90, BlueBird's target did not), and the stock autopilot variables disagree
with the MCP (stock speed 100 against 200 shown). So the app sets MCP values by L:var and never reads them from stock
autopilot variables. Measured since (`evidence/live-findings.md`, MCP): altitude adopts a direct write (100 ft per
`K:AP_ALT_VAR_INC`); V/S adopts a direct write, negative values too (500 and -1500 read back; one `K:AP_VS_VAR_INC` is
+100 fpm); the IAS target is `L:BBS757_ORANGE_AIRSPEED_BUGS`, read-only, set by `K:AP_SPD_VAR_INC/DEC` steps of 1 kt,
which BlueBird speeds up within a burst (events 11-25 move 2 kt, later ones 5 kt; measured after the first flight, rule
B757-3).
The Mach-mode flag is unproven: `L:BBS_AIRSPEED_IN_MACH` stayed 0 after a pulse of the IAS/MACH changeover button.

### 3.3 CDU: full text both sides, keys by held L:vars (measured)

Both CDUs' screens arrive on the sim's Comm Bus (`BBS_CDU_DISPLAY_1`, `_2`) as MobiFlight WinCtrl JSON: 14 rows of 24
cells, each with character, color, size (large/small) and inverse. A read-only listener injected through the Coherent
debugger into the hidden helper view ("VCockpit28 - BBSTablet") receives both, with no port and no MobiFlight
Connector; the captain's current frame is also cached on the helper element (`lastMobiFlightCduPayload`). Keys are
held L:vars `L:B757_CDU_<1|2>_<KEY>` (69 per CDU): a 0.25 s hold typed reliably; a 1 s CLR wiped the scratchpad, as on
the aircraft. The CDU's BLUEBIRDSIM MENU (RELOCATE, STATS) is reachable the same way. Details: `evidence/cdu.md`.
This is the same shape as the HorizonSim 787 CDU client (`SimConnect/CoherentHS787CduClient.cs`): Coherent socket,
view by title, injected agent; one socket per view ([DBG-9]).

### 3.4 Displays: WASM-drawn, no DOM; AI reads through instrument views

Every display (PFD, ND, EICAS upper and lower, CDU, EFB, IRS display, radio, transponder and clock LCDs) is a WASM
gauge drawn natively: no DOM, no canvas readable through the debugger. The aircraft defines instrument camera views:
ADI/EHSI, EICAS, Overhead, IRS, Engines, FMC, Circuit Breakers. **Measured: index 1 is the EICAS view** (it frames
the whole MCP, both EICAS CRTs with legible messages, the standby instruments, gear lever and flap gauge), **index 2
the overhead**. These make AI display reads practical (`evidence/live-findings.md`).

### 3.5 What the WASM publishes (read, partly measured)

About 2,055 L:var names in the module; 835 not named in the cockpit XML. Measured so far: `B757_APU_STATE` (1
starting, 2 running), `B757_APU_RPM_PRC` (the real APU speed; **stock `APU PCT RPM` read 100 % at once and is
unusable**), `B757_L_BUS_PWR_SRC`/`_R_` (2 on the APU), `B757_LEFT_PACK_STATE`/`_RIGHT_` (matched the EICAS),
`B757_M730_BATT_PERCENT`, `BBS_ECAM_Mode`. Candidates worth announcing (`evidence/wasm-findings.md` §3): cabin
altitude, rate and differential (`B757_DEBUG_PRESSURE_*`), brake temperatures, hydraulic pump speeds, engine starter
valves and combustion, GPU cart state, door switches, master caution and warning, the 72 `BBS_PLAY_*` aural triggers,
VNAV path deviation and distance to top of descent, pitch limit, stick shaker.

### 3.6 What is not published

- **Active EICAS messages**: all 440 texts are in the module (15 warnings, 42 cautions, 214 advisories, 7
  communication, 162 status) but the active list is internal. Readable only by AI display read, or proxies (master
  caution/warning, aural triggers, the system state vars).
- **FMA modes** (autothrottle, pitch, roll, armed, CMD/FD): internal only. Proxies: the MCP button light vars (to test
  whether they follow the modes) and AI reads of the PFD.
- **The EFB** (section 6): drawn and clicked inside the WASM; no variable or event loads a panel state.
- **Client data**: the module talks to BBS Nest over SimConnect client data (terrain image, traffic, a handshake);
  nothing the app can use.

### 3.7 Stock SimVars that cannot be trusted on this aircraft

`APU PCT RPM` (3.5), the autopilot selected values (3.2), `ELECTRICAL MASTER BATTERY` (read 1 with the battery bus
dead). Engines: N1, N2, EGT, fuel flow and oil come from stock engine variables per the module's strings; agreement
with the displays is unmeasured (engines not started yet).

## 4. Pilot-facing settings that change what the app must do

From `work\efb_settings.json` and `work\liveries\<registration>-opt.json` (`evidence/wasm-findings.md` §7):

- **Built-in callouts**: pilot-monitoring 80 knots, V1, rotate, positive rate, and EGPWS altitude callouts, each
  configurable per registration. The app must not speak a callout the aircraft already plays.
- `tiller_realistic` (rudder pedals steer only 5 degrees; the tiller does the rest): matters to taxi guidance and to a
  pilot steering with rudder axes.
- `parking_brake_realistic` (needs full braking to set), `displays_bootup_realistic`, `irs_align_time`,
  `units`, `tod_pause_distance` (the aircraft pauses the sim itself before top of descent), `dirty_panel_states_enabled`
  (random discrepancies at cold and dark: keep off for tests), `electrical_relay_lvar_export_enabled` (exports 32
  relay states as L:vars).
- The files are plain JSON the app could read; whether BlueBird re-reads them while loaded is unknown (to measure),
  and they hold credentials (Hoppie, SayIntentions) the app must never display or log.

## 5. What the app can offer, by area

| Area | Read | Drive | Source | Confidence |
|---|---|---|---|---|
| Overhead, glareshield, pedestal switches and knobs | yes | yes | L:vars, click RPN | measured on a sample; sweep the rest (§8) |
| Annunciators | yes | test only | `_DECAL` L:vars | measured on a sample |
| MCP selected values | yes | exact set | `BBS_AP_TARGET_*` | heading measured; others to measure |
| MCP modes and A/P engage | button lights (proxy) | yes | `_PRESS` pulses | to measure |
| FMA | AI read, proxies | n/a | PFD view | not published |
| CDU (both) | full text with color and size | every key | Comm Bus + held L:vars | measured |
| EICAS messages | AI read, proxies | cancel/recall buttons | EICAS view | not published |
| Engines | stock A: vars (to verify), WASM extras | start switches, fuel control | | to measure |
| APU, electrics, packs, pressurization | yes | yes | WASM L:vars | partly measured |
| Radios, transponder, TCAS, audio | to sweep | to sweep | L:vars, `K:XPNDR_*` | XML only |
| SimBrief, route, winds, performance, W&B, weather, clearances | CDU text | CDU keys | ACARS and FMC COMM pages | CDU measured; the ACARS sequence itself to run (§8) |
| Fuel by tank | stock tank quantities | `BBS_EFBX_SET_FUEL_<TANK>_LBS` | WASM setter | left tank measured |
| EFB: presets, payload, doors, ground services | door and GPU vars | payload and door setters untested; presets none | see §6 | |
| Failures | no | fire scenario and detector injections exist | `B757_SCEN_*`, `B757_INJ_*` | unmeasured, owner decision |
| Reposition | CDU text | CDU keys | BLUEBIRDSIM MENU → RELOCATE | readable, untested |

## 6. The EFB with a screen reader

BlueBird's tablet ("BBOS") is drawn by the WASM and driven by mouse only: no DOM, no Coherent page, no keyboard
route. It holds cockpit presets, quick start (APU, engines, IRS), ground services (stairs, jetway, catering, GPU,
chocks, ground air, air start, cargo, fuel truck, pushback), doors, fuel and payload with SimBrief import,
maintenance, failures, charts, checklists, settings and notifications. A blind pilot can use none of it directly.

**But the app can operate it (measured end to end, `evidence/efb-and-manual.md`)**: the tablet's mouse handler is
`Coherent.call("WASM_CLICK" …)` with page coordinates, callable from the cockpit page through the Coherent debugger,
so the app can press any EFB button at a known position; and the tablet's picture, mirrored onto the left ND's screen
and captured, can be read (AI vision) when the app needs to see it. Proven this way: Navigraph AIRAC update (one click,
then a flight reload), the SimBrief ID entry (on-screen keypad), FETCH SIMBRIEF OFP, the units dialog, and loading
the BEFORE START cockpit preset (battery, APU and AC power in about 12 seconds). After an AIRAC update the aircraft
misbehaves until the flight is reloaded (APU died and would not restart): the app must say "reload the flight".
Positions are the vendor's layout of release 0.1.0 and can move with an update; each scripted step therefore checks
its result through state the app can read (the settings file, the CDU text, bus power, a capture), never blindly.

**It is not critical for a full flight once set up** (the manual, `evidence/efb-and-manual.md`): every flight-data
step runs on the CDU's ACARS and FMC COMM pages: SimBrief flight (ACARS INIT FLT), route (ROUTE `<REQUEST`, LOAD,
ACTIVATE, EXEC), winds, take-off and landing performance (PERF INIT and TAKEOFF REF uplinks), weight and balance
(PRELIM, FINAL), weather, clearances and reports. The app reads and drives all of it through §3.3.

What remains EFB-only, and the outside route for each:

| Need | Outside route | Status |
|---|---|---|
| SimBrief Pilot ID (once; **required**: without it ACARS INIT does nothing, the shared Navigraph login does not replace it) | EFB keypad by click (proven; BlueBird writes `simbrief_id` to its settings file at once), or the settings file between loads (untested) | **proven** |
| SimBrief OFP (each flight) | FETCH SIMBRIEF OFP on the EFB's ACARS Dashboard by click, then ACARS INIT FLT, RTE REQUEST SEND, LOAD, ACTIVATE, EXEC on the CDU | **proven end to end** (route KSEA → KDEN active) |
| Navigraph sign-in (once; needed for performance requests, charts, AIRAC) | the sign-in runs in the hidden helper page: its pending device request (8-character code, navigraph.com address with the code built in) is readable there; the app speaks the code and opens the page in the pilot's browser; the helper stores the tokens | **proven end to end**: the owner signed in by screen reader from the read-out code; the helper stored the login and stopped polling. Survives a reload: to check |
| AIRAC update (the bundled cycle 2503 flies, but SimBrief routes may name newer points) | EFB Settings → AIRAC data → UPDATE by click (the check for a newer cycle runs at aircraft load), then a flight reload | **proven end to end**: cycle 2610 installed and active on the CDU after the reload |
| Hoppie / SayIntentions codes (once) | the settings file (credentials: never displayed or logged) | as SimBrief ID |
| Fuel (each flight) | **`L:BBS_EFBX_SET_FUEL_LEFT_LBS` works** (10,000 written: the left tank went to it, the setter reads -1 after); the total setter changed nothing; stock tank variables refuse writes | center and right to confirm |
| Passengers and cargo, doors (each flight) | `BBS_EFBX_SET_PAX_*`, `_CARGO_*`, `_DOOR`; BlueBird's automatic boarding setting | to measure |
| Ground power | the APU (measured) | covered |
| Pushback | MSFS's own or GSX (the manual names both) | covered |
| Cockpit presets | EFB Aircraft → COCKPIT PRESETS by click | **BEFORE START proven** (power and APU in about 12 s); the others unmeasured |

Asking BlueBird for an external interface (load a preset, run a ground service, publish EICAS and FMA as the CDU
already is) would still close the rest of the gap.

## 7. Owner decisions

1. Profile name and code: "BlueBird 757-200", `BBS_757`?
2. BBS Nest check: speak it on aircraft load when missing, and keep it in the status display?
3. CDU transport: the Coherent Comm Bus listener (both CDUs, no port) rather than serving MobiFlight's port 8320
   (captain only, and it would collide with a MobiFlight WinCtrl CDU user)?
4. EICAS and FMA: AI display reads (EICAS view, PFD view) plus the proxies now; and do we write to BlueBird asking for
   an export (EICAS, FMA, EFB actions), as the CDU already has?
5. EFB: build an accessible "757 EFB" window that operates BlueBird's tablet by scripted clicks and confirms each
   step through readable state: guided Navigraph sign-in (speak the code, open the page), AIRAC update (then "reload
   the flight"), SimBrief ID entry, FETCH SIMBRIEF OFP, cockpit presets, quick starts, fuel by tank, ground services
   and doors as they are measured; plus an optional "read the EFB" AI picture read through the ND mirror for
   anything else? Accept the risk that a BlueBird update moves buttons (each step verifies, and the positions live in
   one table)?
6. Settings: read BlueBird's settings and livery options to adapt (callouts, tiller, units); one-time setup goes
   through the EFB window of question 5, not by editing the file?
7. Built-in callouts: skip the app's own take-off and altitude callouts the aircraft already makes?
8. First Officer for the 757 in this branch, or later?
9. Failures (fire scenarios, detector injections): in scope, or leave to BlueBird's EFB?

### Prototype defaults (2026-10-25)

The owner asked for one plan and a flyable prototype overnight, so the prototype (`plans/01-prototype.md`) takes these
defaults; each can be overruled later: (1) "BlueBird 757-200", code `BBS_757`; (2) the BBS Nest check speaks once
per connection when the process is missing; (3) the Coherent Comm Bus listener for both CDUs; (4) AI display reads
only, no letter yet; (5) a "757 EFB" window operating the tablet by scripted, verified clicks (Navigraph sign-in,
AIRAC update, SimBrief ID, fetch OFP, units answer, cockpit presets, quick starts); (6) BlueBird's settings files are
read, never written by the app (the SimBrief ID goes in through the EFB); (7) no app take-off callouts on the 757
(the aircraft makes its own); (8) no First Officer yet; (9) no failures UI.

## 8. Still to measure in the sim (few, prioritized)

1. The panel sweep: every control's write and read-back by panel (owner present for anything audible), especially
   knobs with more than two positions, guarded switches, the fuel control and fire handles, and the radios.
2. MCP: the Mach-mode flag and Mach target (heading, altitude, V/S and IAS are measured, §3.2); whether a burst of 25
   speed steps in one calculator call all count; whether the button light vars follow the active modes (an FMA proxy).
3. Engines: a start (starter valve, combustion, N2 rise) and stock engine values against the EICAS.
4. EFB routes: the center and right fuel setters; payload, cargo and door setters; automatic boarding (START
   BOARDING); the GPU and other ground services by click; every cockpit preset and quick start; the ACARS PERFORMANCE
   requests (take-off and landing data, PERF INIT and TAKEOFF REF uplinks). Done 2026-10-25: Navigraph login survives
   a reload; AIRAC update; SimBrief ID; OFP fetch; ACARS INIT; route uplink, activate and execute.
5. Camera views: the remaining instrument indices (ADI/EHSI, IRS, Engines, FMC, Circuit Breakers) by capture.
6. The CDU: shortest reliable key hold and safe typing rate; other colors and inverse cells.

## Evidence files

In `docs/b757/evidence/`: `web-research.md` (public facts, release day), `repo-touchpoints.md` (where a new
aircraft plugs into the app), `package/` (the survey tables), `controls/` (the control map), `wasm-findings.md`,
`live-findings.md`, `cdu.md`, `efb-and-manual.md` (what a flight needs from the EFB, from BlueBird's manual and live
checks). Tools: `docs/b757/tools/survey_package.py`; `tools/b757-gen/extract_controls.py` and `generate_b757_map.py`.
