# BBS752.wasm: what the systems module exposes (offline string survey)

Prepared 2026-10-25 from printable strings only: no disassembly, no sim, no simconnect MCP, no Coherent debugger.
Sources, all read-only:

- `SimObjects/Airplanes/bbs-757-200-base/panel/BBS752.wasm` (52 MB). ASCII strings (4+ chars, the scratchpad
  `bbs752-all-strings.txt`), plus UTF-32 and UTF-16 strings extracted for this note (the module draws its displays
  with `wchar_t`, which in wasm is 32-bit: **the EICAS, FMA and CDU texts exist only as UTF-32**, invisible to a
  plain `strings`).
- The package's `sound/sound.xml`, cockpit XML, `Data/panel_states/*.snap`, and the two JS files under
  `html_ui/Pages/VCockpit/Instruments/BBS/bbs-757/`.
- `LocalState\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work\efb_settings.json` and `work\liveries\*.json`: key
  names and value types only. No value of any key is recorded here.

Working lists (scratchpad, not committed), folder
`C:\Users\robin\AppData\Local\Temp\claude\C--Claude-msfs-blind-assist--claude-worktrees-review-298-90ca21\8da6d0f8-2ca6-4f8a-8cf1-6883ac5e99c4\scratchpad\wasm757\`:
`presence-none.txt` (names in no package file), `presence-sound.txt` (only in sound.xml), `presence-xml.txt` (in
cockpit XML as template parameters), `eicas-warnings.txt`, `eicas-cautions.txt`, `eicas-advisories.txt`,
`eicas-status.txt`, `failures.txt`, `utf32.txt`, `utf16.txt`.

## Headline

1. **Client data**: the module imports `CreateClientData`, `MapClientDataNameToID`, `AddToClientDataDefinition`,
   `RequestClientData` and `SetClientData`, but **no client data area name is visible** in ASCII, UTF-16 or UTF-32
   strings (names are built at run time or hidden). Symbols show what the channel is for: a packet link to an
   external process, `bbs::sim_con::send_packet<bbs::egpws_image_request>(bbs::packet_type, ...)`,
   `sim_con::get_bbshs`, `sim_con::request_egpws_image`, `sim_con::get_egpws_stats`, `sim_con::request_nearest_traffic`,
   `bbs::traffic_data`. The peer is **BBS Nest** (`C:\BBS Nest\bbs_nest.exe`, running on this PC; its `SubSystem.dll`
   carries its own SimConnect client and a `bbs::renderer` taking `egpws_image_request`). The module has a message
   saying BBS Nest must be running for the aircraft systems to run. Nothing here is a vendor-hardware (WINGFLEX)
   style mapping area. **For our app: nothing to read on the client data side.**
2. **Comm Bus**: 15 event names in the WASM (below); the only one carrying cockpit content is the CDU screen pair.
3. **L:vars**: the module's L:var name table holds about 2,055 names; 1,220 are in `lvars.tsv`, **835 are not**.
   Of those 835, 272 do appear in the cockpit XML as template parameters (`lvars.tsv` misses `LVAR_DOWN`-style
   parameters: all 138 `B757_CDU_1_*`/`B757_CDU_2_*` key names are of this kind), 129 appear only in `sound.xml`,
   and **434 appear in no package file at all**.
4. **EICAS**: the full message table is present: **440 texts: 15 warnings, 42 cautions, 214 advisories, 7
   communication messages, 162 status messages**. **No L:var, Comm Bus event or data area exposes the active
   list**; it lives in `bbs::eicas_computer` (`get_messages`, `get_status_messages`, `get_com_messages`) and is drawn
   on the `eicas` gauge.
5. **FMA**: all mode annunciations are present as UTF-32 texts (list below). **No variable holds the active modes**;
   `bbs::afds` keeps them internally (`get_active_pitch_mode`, `get_armed_roll_mode`, ...).
6. **EFB**: a full in-WASM tablet ("BBOS") with 9 factory panel states, custom snapshots, ground services, doors,
   fuel and payload, maintenance, 100+ failures, checklists, charts. Its actions are mouse-driven inside the gauge;
   **no L:var or event loads a panel state**. A few L:vars look like external setters (`BBS_EFBX_SET_*`), and the
   CDU has its own BlueBird menu (`RELOCATE`, `STATS`) that the app can both read and drive.

## 1. Client data areas

- Imported: `SimConnect_CreateClientData`, `MapClientDataNameToID`, `AddToClientDataDefinition`,
  `RequestClientData`, `SetClientData`, also `RequestDataOnSimObjectType`, `RequestFacilityData`,
  `AddToFacilityDefinition`, `EnumerateControllers`, `MapInputEventToClientEvent_EX1`, `TransmitClientEvent`.
- No string near these or anywhere else looks like a client data name (no dotted names apart from Navigraph chart
  styles `ifr.lo.day` and similar; no `MobiFlight.*`, `WASM.*`, `BBS.*`).
- Packet types seen: `egpws_image_request`, `egpws_stats`, `traffic_data`, `bbshs` (likely a handshake). The terrain
  picture for the ND and probably traffic come from BBS Nest. The module also authenticates against
  `https://bluebirdsim.com/api/v1/authenticate`.
- Layout: not recoverable from strings.
- In-sim hazard for our tests: BBS Nest has to be running, or the systems do not run.

## 2. Comm Bus events

`fsCommBusRegister`, `fsCommBusCall`, `fsCommBusUnregister` are imported; `bbs::comm_bus_manager` keeps a map of
named callbacks taking rapidjson documents (`register_wasm_callback`). Names in the WASM:

| Event | Direction (inferred) | Carries |
|---|---|---|
| `BBS_CDU_DISPLAY_1`, `BBS_CDU_DISPLAY_2` | WASM to JS | MobiFlight CDU JSON, `{"Target":"Display","Data":[...]}`, one cell per character with color and size (`build_mobiflight_display_json`, `translate_mobiflight_cdu_char`); sent only when changed. The JS forwards the captain's to `ws://localhost:8320/winwing/cdu-captain`. |
| `BBSJ2WI` | JS to WASM (likely) | JSON; name suggests "JS to WASM interface". The JS also knows `BBSJ2WI_OFF`. |
| `bb_ng_handshake`, `bb_ng_logout`, `bb_ng_access_update`, `bb_ng_code` | both ways | Navigraph sign-in between the tablet JS and the WASM (device code, tokens, tile cookies). JS also uses `bb_ng_refresh_update`. |
| `ng_map_layer_update`, `ng_map_layer_hide`, `ng_map_layer_status`, `ng_map_control`, `ng_map_overlay_mask` | WASM and map JS | Navigraph map tiles drawn by `bbs-navigraph-map.js` over the EFB (zoom, night mode, provider, recenter, fit route). |
| `NAVIGRAPH_CallFunction`, `NAVIGRAPH_FunctionResult`, `NAVIGRAPH_Event` | WASM and Navigraph module | Navigraph navdata interface (`msfs_navigation_data_interface.wasm`): `DownloadNavigationData`; database at `/work/NavigationData/db.s3db`. |

JS-side helpers also use `COMM_BUS_WASM_CALLBACK`, `JS_LISTENER_COMM_BUS`, `JS_LISTENER_WASM_MODULE`,
`FOCUS_INPUT_FIELD`/`UNFOCUS_INPUT_FIELD` (Coherent keyboard focus, not Comm Bus). No Comm Bus event carries EICAS,
FMA, EFB or system state.

Key events: the module registers `register_key_event_handler_EX1` and intercepts the stock flight-control events
(axes, `ELEV_TRIM_UP/DN`, `FLAPS_INCR/DECR/SET`, `SPOILERS_*`, `SPOILERS_ARM_*`, `THROTTLE*_SET/DECR`, reversers,
`GEAR_UP/DOWN/TOGGLE`, `STEERING_*`, `BRAKES*`). It writes calculator code for pushback (`K:TUG_HEADING`,
`K:TUG_SPEED`, `K:TOGGLE_PUSHBACK`, `K:TUG_DISABLE`). No H: event names appear in the WASM; the XML's
`H:BBS_AP_*` events are XML-side.

## 3. L:vars the cockpit XML does not name

How the names were found: the module's L:vars are `bbs::lvar` objects (typed: `bool`, `double`, and enums such as
`apu_state`, `aircon_pack_mode`, `ecam_mode`, `engine_variant`, `gpu_cart_run_state`, `autbrake_kb`,
`irs_switch_pos`, `irs_display_mode`, `xpndr::state`, `mcp::mach_ias_mode`, `hsi_mode`, `cabin_pressure_ctrl`,
`wiper_position`, `FireSwitch`, `FuelControlSwitch`, `EngineStartSwitch`, `IgnitionSelectionSwitch`). Their names
sit in one table in the data segment, with `__cxx_global_var_init_<NAME>` symbols for most. A name there is a
candidate, not a proven L:var: a few are PID or data-store keys (`B757_AFDS`, `BBS_TMC`, `B757_TMC`, with suffixes
`_SETPOINT`, `_OUTPUT`, `_ERROR`, `_INTG`, `_I_MIN`, `_I_MAX`), and `BBS_RELOCATE_*`, `BBS_STATS` are CDU page keys,
not L:vars. Marks: **[R]** readable state an app could announce; **[W]** a write that could drive something;
*(xml)* named in cockpit XML as a template parameter; *(snd)* only in sound.xml; unmarked: in no package file.

**Electrics**
- [R] `B757_L_BUS_PWR_SRC`, `B757_R_BUS_PWR_SRC` *(snd)*: bus power source (sound.xml uses them as an RTPC and a
  transfer trigger).
- [R] 32 relay names, `eK104_MAIN_BAT_RELAY`, `eK105_AC_STANDBY_RELAY`, `eK114_EXTERNAL_POWER_CONTACTOR`,
  `eK107_CENTER_BUS_RELAY`, `eK108_DC_TIE_RELAY`, `eK109_STANDBY_PWR_RELAY`, `eK119_UTILITY_BUS_L_RELAY`,
  `eC902_L_BTB_RELAY`, `eC904_R_BTB_RELAY`, `eL_GEN_CONTACT_RELAY`, `eR_GEN_CONTACT_RELAY`,
  `eAPU_GEN_CONTACT_RELAY`, ... sound.xml reads three of them as L:vars (`eK104_MAIN_BAT_RELAY`,
  `eK105_AC_STANDBY_RELAY`, `eR_GEN_CONTACT_RELAY`). The rest are probably exported only with the
  `electrical_relay_lvar_export_enabled` setting (section 7).
- [R] `BBS_CB_BITS_16` (and `BBS_CB_BITS_0`..`15`, XML-known): circuit breakers packed as bits; 488 breaker names
  exist (`OVHD-...` 393, `P6-...` 95).
- [R] `B757_M730_BATT_PERCENT`, `B757_PSU_STATE`; `BBS_GPU_CART_RUN_STATE` (enum), `BBS_GPU_CART_RUN_RPM`,
  `BBS_GPU_CART_ENG_TEMP`, `BBS_GPU_CART_CURRENT_LOAD`, `BBS_GPU_CART_CAP_POSITION`; [W] `BBS_GPU_CART_CMD`,
  `BBS_GPU_CART_LOAD_CMD`.

**Hydraulics**
- [R] `B757_HYD_PUMP_ENG_L_RPM`, `B757_HYD_PUMP_ENG_R_RPM`, `B757_HYD_PUMP_ACMP_L_RPM`, `_ACMP_R_RPM`,
  `_ACMP_C1_RPM`, `_ACMP_C2_RPM` *(ACMP: snd)*: pump spin, a proxy for "pump running".

**Fuel**
- [W] `BBS_EFBX_SET_TOTAL_FUEL_LBS`, `_FUEL_LEFT_LBS`, `_FUEL_CENTER_LBS`, `_FUEL_RIGHT_LBS` (see EFB).
- `B757_BBS_FUELCUT_RR1_BULB_FAILED`, `_RR2_` (bulb failures).

**Engines**
- [R] `B757_COMBUSTION_L_ENG`, `B757_COMBUSTION_R_ENG` *(snd)*; `B757_L_ENG_STARTER_VALVE_OPEN`,
  `B757_R_ENG_STARTER_VALVE_OPEN` *(snd)*; `B757_RR_START_N1_L`, `_R` *(snd)*; `B757_ENG_1_AIR_STARTER_RATIO`,
  `B757_ENG_2_AIR_STARTER_RATIO`; `B757_ENG_FAN_1_RPM`, `_2_RPM`; `BBS_CUTIN_ENG_STARTER`;
  `B757_ENG_VARIANT` *(snd; enum: PW2037 or RB211-535E4)*; `B757_L_ENG_HANDLE_AUTO_UNLOCK`, `_R_` (fire handle
  unlocked).
- N1, N2, EPR, EGT, fuel flow and oil come from stock A: vars (`TURB ENG N1`, `TURB ENG CORRECTED N1/N2`,
  `ENG EXHAUST GAS TEMPERATURE`, `ENG FUEL FLOW PPH`, `ENG OIL PRESSURE/TEMPERATURE/QUANTITY`, `ENG VIBRATION`): no
  WASM-only engine readouts found. Fan-blade and windmill coefficients are model tuning, not state.

**APU**
- [R] `B757_APU_STATE` (enum `apu_state`; the EFB labels it APU OFF, APU START, APU RUN, APU SHUTDOWN),
  `B757_APU_FLAME`, `B757_APU_SHUTDOWN_TIME_COUNT`, `B757_APU_SHUTDOWN_TIME_LIMIT` (cool-down timer),
  `B757_APU_HANDLE_AUTO_UNLOCK`; [W] `BBS_APU_INSTANT_START`. XML-known: `B757_APU_RPM_PRC`, `B757_APU_EGT_TEMP`.

**Bleed, packs, pressurization**
- [R] `B757_DEBUG_PRESSURE_CABIN_ALT_FT`, `B757_DEBUG_PRESSURE_CABIN_CHANGE_RATE`,
  `B757_DEBUG_PRESSURE_CABIN_CHANGE_RATE_TARGET`, `B757_DEBUG_PRESSURE_CABIN_PSI`,
  `B757_DEBUG_PRESSURE_CABIN_DIFF_PSI` *(snd)*: cabin altitude, rate, differential: exactly what the 757's analog
  gauges show and a screen reader cannot.
- [R] `B757_LEFT_PACK_STATE`, `B757_RIGHT_PACK_STATE` *(snd; enum `aircon_pack_mode`)*, `B757_L_PACK_HIGH_FLOW`,
  `B757_R_PACK_HIGH_FLOW`, `B757_OVERBOARD_EXH_VALVE`, the trim air valves (`B757_OVHD_TRIM_AIR_VALVE`,
  `B757_OVHD_FLT_DK_TRIM_AIR_VALVE`, `_FWD_CAB_`, `_MID_CAB_`, `_AFT_CAB_`, `_LEFT_PACK_`, `_RIGHT_PACK_`), zone
  selectors `B757_OVHD_FLT_DK_ZONE_TEMP_SEL`, `_FWD_CAB_`, `_MID_CAB_`, `_AFT_CAB_`, `B757_OVHD_LDG_ELEV` (landing
  altitude), `B757_TEST_CABIN_ALT`, `B757_DEBUG_LEFT_BLEED_LEAK`, `_RIGHT_`.
- 62 `BBS_PRESSURE_*` names are model constants (mass rates, PID gains), not state.

**Autopilot, autothrottle, flight director**
- [R] `BBS_AP_TARGET_HEADING`, `BBS_AP_TARGET_VS`, `BBS_AP_TARGET_ALTITUDE` *(xml has `BBS_AP_Target_Altitude`:
  check live whether the sim treats them as one name)*, `BBS_VS_BLANKED`, `BBS_AIRSPEED_IN_MACH`,
  `B757_MCP_AP_SPD_MACH_SW` *(xml)*, `B757_MCP_HDG_SEL` *(xml)*, `BBS757_BANK_LIMITER` *(xml)*,
  `BBS_VNAV_PATH_DEVIATION`, `BBS_VNAV_TOD`, `BBS_VNAV_TOD_PROGRESS`, `BBS_LOC_DEV_DEG`.
- Buttons: `B757_MCP_THR_BUTTON`, `B757_MCP_THR_BUTTON_PRESS`, `B757_MCP_BCRS_1_BUTTON_PRESS`, `B757_AT_DISC_1`,
  `B757_AT_DISC_2`, `GO_AROUND_PADDLE1`, `GO_AROUND_PADDLE2` *(xml)*.
- [R] `BBS757_PLI_VISIBLE`, `BBS757_PLI_PITCH_LIMIT_DEG`, `BBS757_PLI_MARGIN_DEG` (pitch limit indicator),
  `BBS757_STICK_SHAKER_CA`, `_FO` *(snd)*, `BBS757_GS_INHIBIT` *(xml)*.
- No L:var for the active FMA modes (section 5).

**EICAS, alerts, aural triggers**
- [R] `BBS_ECAM_Mode` (enum `ecam_mode`, the lower EICAS page), `B757_SOUND_FMC_EICAS_MSG`, `B757_FIRE_BELL`,
  `B757_FWD_CARGO_FIRE_ACTIVE`, `B757_AFT_CARGO_FIRE_ACTIVE`, `B757_FWD_EQUIP_SMOKE_DETECTED`,
  `B757_TERR_FAIL_SHOWING`; buttons `B757_EICAS_EVENT_BUTTON`, `B757_EICAS_MAX_IND_BUTTON`,
  `B757_MCP_btn_master_pilot`, `B757_MCP_btn_master_copilot` *(xml)*.
- [R] 72 `BBS_PLAY_*` triggers (sound.xml plays each when it is 1 or more): `BBS_PLAY_MASTER_WARNING_A`, `_B`,
  `BBS_PLAY_MASTER_CAUTION`, `BBS_PLAY_FIRE_BELL`, `BBS_PLAY_WINDSHEAR_WARN`, `BBS_PLAY_WINDSHEAR_CAUTION`,
  14 `BBS_PLAY_GPWS_*` (PULLUP, SINKRATE, TERRAIN, TOOLOWGEAR, GLIDESLOPE, BANKANGLE, ...), 26
  `BBS_PLAY_CALLOUT_*` (2500 to 5, MINIMUMS, DH, PLUS_HUNDRED), `BBS_PLAY_FO_80_KNOTS_CALLOUT`, `_V1_`,
  `_ROTATE_`, `_POSITIVE_RATE_`, cabin chimes, flight attendant and captain announcements. 16 `BBS_SOUND_TC_*`
  (TCAS aurals: TFC, CLB, DES, LVL_OFF, ...), `B757_SOUND_RAD_ALT_*`, `B757_SOUND_WINDSHEAR_GO_AROUND`.
  These say an aural fired, not why.

**IRS**
- `B757_IRS_NUMBER_0_BUTTON`..`9`, `_ENT_`, `_DEL_` (ISDU keypad), `B757_IRS_L_SWITCH`, `B757_IRS_C_SWITCH`
  *(all xml)*. No alignment-progress L:var found (XML-known `BBS_OVHD_IRS_LIGHT_1..6` are the lights).

**Radios, audio, SELCAL**
- [R] `SIAI_SELCAL_PENDING_COM1`, `SIAI_SELCAL_PENDING_COM2` (SayIntentions SELCAL); call lights and buttons
  `B757_OVHD_L_VHF_BUTTON(_ON)`, `_C_VHF_`, `_R_VHF_(_ON)`, `_L_HF_`, `_R_HF_`, `B757_OVHD_FLT_INT_BUTTON(_ON)`,
  `B757_OVHD_GND_CALL_BUTTON(_ON)`; audio panels `B757_PED_AUDIO_1_*`/`_2_*` mic-select lights and PTT, ADF
  switches *(all xml)*.

**Doors and ground services**
- [R] `L1_PAX_DOOR_STATE_SWITCH`, `L2_`, `L3_`, `R1_`, `R2_`, `R3_`, `FWD_CARGO_DOOR_STATE_SWITCH`,
  `AFT_CARGO_DOOR_STATE_SWITCH`, `B757_DOORS_CARGO_FWD_MOVING`, `B757_DOORS_CARGO_AFT_MOVING`, `DOOR_MAIN_DOOR_POS`
  *(snd)*; [W] `BBS_EFBX_SET_DOOR`. XML-known: `B757_DOORS_PAX_L1..R3`, `B757_COCKPIT_DOOR`, `BBS757_vis_chocks`,
  `BBS757_vis_stairsfwd`, `_stairsaft`, `_fueltruck`, `_catertruck`, `_aircon`, `_airstart`, `_baggage1`,
  `BBS_757_GPU_CART_VISIBLE`, `BBS_757_GPU_AC_PWR_CONNECTED` (the EFB syncs ground services from these:
  `sync_aircraft_ground_services_from_lvars`). `B757_GND_SVC_BUTTON` *(xml)*.

**Cabin, oxygen**
- [R] `B757_TOTAL_PAX_COUNT`, `B757_PASS_OXY_L_MASKS_DEPLOYED`, `_R_`, `B757_CREW_O2_BOTTLE`, `BBS_PASSENGER_SOUNDS_ON`
  *(snd)*; [W] `B757_RESET_PASS_OXY_SYSTEM`, `B757_EMER_LTS_RESET`. 41 crew mask names (`B757_CAPT_O2_*`, `_FO_`,
  `_OBS1_`, `_OBS2_`), `BBS_OXY_MASK_TOGGLE_CAPT`, `_FO`, `_OBS1`, `_OBS2`. Cabin call buttons
  (`B757_OVHD_FWD_CABIN_CALL_BUTTON`, `_MID_`, `_AFT_`, `_ALERT_`, `_GND_`) and seat heaters *(xml)*.

**Brakes, gear, flight controls**
- [R] `B757_BRAKE_TEMP_SENS_0`..`7` and `B757_BRAKE_TEMP_CORE_0`..`7` (brake temperatures; `_CORE_0` is
  XML-known), `BBS_AUTOBRAKE_KNOB` *(xml; enum `autbrake_kb`)*, `B757_GEAR_HANDLE_MOVING` *(snd)*,
  `BBS757_FC_L_FLAP_IS_MOVING`, `_L_SLAT_`, `_R_FLAP_`, `_R_SLAT_`, `BBS_757_RAT_RPM` *(snd)*,
  `BBS_RWY_CTR_LN_X` *(snd)*. 54 `BBS_BOGIE_*` are gear-physics tuning.

**EFB, panel state, options**
- `BBS_EFB_LAYOUT`, `BBS_OPTION_TRK_UP_DISPLAY`, `TOREF_STYLE_2`, `BBS_WEAR`, `BBS_WIPER_WEAR_L/R`; [W] the 11
  `BBS_EFBX_SET_*` (total, left, center, right fuel; passenger weight, business, economy; cargo weight, forward,
  aft; door). The `EFBX` prefix suggests an external-setter interface; unverified.

**Failures**
- [W] Fire scenario: `B757_SCEN_ARM`, `_RESET`, `_KIND`, `_ZONE`, `_MODE`, `_EXTINGUISHABLE`, `_BOTTLES_REQ`,
  `_TIME_MIN`, `_ALT_FT`, `_IAS_KT`; [R] `B757_SCEN_ARMED`, `B757_DEBUG_SCEN_ARMED`, `B757_DEBUG_SCEN_FIRED`.
- [W] Detector injections: `B757_INJ_MASTER_SWITCH` plus 37 `B757_INJ_<zone>_L1/L2_FAULT/ALARM` (engine fire,
  nacelle, strut, cargo fwd/aft, APU, wheel well), `B757_USER_FIRE_MASTER_RESET`.
- The panel-state saver refuses while any `B757_INJ_*`, `B757_SCEN_ARM` or `*_BULB_FAILED` is set.

## 4. EICAS messages

All 440 texts are UTF-32 strings in one table, in this order (advisories carry the Boeing one-space indent):

- **Warnings (15)**: AFT CARGO FIRE, APU FIRE, AUTOPILOT DISC, CABIN ALTITUDE, FLAPS, FWD CARGO FIRE, GEAR NOT
  DOWN, L ENGINE FIRE, MAIN CARGO FIRE, OVERSPEED, PARKING BRAKE, R ENGINE FIRE, SPOILERS, STABILIZER, WHEEL WELL
  FIRE.
- **Cautions (42)**: ALT DISAGREE, ALTITUDE ALERT, ATT DISAGREE, AUTOPILOT, AUTOTHROT DISC, C HYD SYS PRESS, CABIN
  AUTO INOP, GEAR DISAGREE, HDG DISAGREE, IAS DISAGREE, INSTR SWITCH, L/R AC BUS OFF, L/R BLD DUCT LEAK, L/R EEC
  OFF, L/R ENG BLEED VAL, L/R ENG OIL PRESS, L/R ENG OVHT, L/R ENG SHUTDOWN, L/R FUEL SYS PRESS, L/R GEN OFF, L/R
  HYD SYS PRESS, L/R STARTER CUTOUT, LE SLAT ASYM, LE SLAT DISAGREE, LOW FUEL, SPEEDBRAKES EXT, TE FLAP ASYM, TE
  FLAP DISAGREE, TRACK DISAGREE, UNABLE RNP, UNSCHD STAB TRIM.
- **Advisories (214)**: for example L ENG BLEED OFF, MAIN BAT DISCH, BATTERY OFF, APU FAULT, L PACK OFF, L YAW
  DAMPER, AUTOBRAKES, ANTISKID OFF, FUEL CONFIG, PASS OXYGEN ON, STANDBY BUS OFF, L/R IRS ON DC, WINDOW HEAT.
- **Communication (7)**: ATC, CABIN ALERT, CABIN CALL, FMC, GROUND CALL, SELCAL, ACARS.
- **Status (162)**: for example APU OIL QTY, CABIN ALT AUTO 1, EQPT CLG FAN, L ENG FIRE LP 1, MAIN BAT CHGR,
  STBY INVERTER, TIRE PRESSURE, WARN ELEX, WINDSHEAR PRED.

No memo list was found (the 757 has none in this model). Exposure: none. `bbs::eicas_computer` holds the active
set (`insert_message`, `remove_message`, `for_each_active`, `is_message_present`, `get_message_by_enum`,
`handle_cancel`, `handle_recall`, `serialize`); `bbs::warn_elex` drives the aurals and lamps. Proxies an app can
read: XML-known `BBS_CA_MASTER_WARNING`, `BBS_CA_MASTER_CAUTION`, `BBS_FO_*`, the `BBS_PLAY_*` aural triggers,
`B757_SOUND_FMC_EICAS_MSG`, plus the per-system state vars above. Reading the message list needs the display
(an AI read of the `eicas` gauge) or a vendor change.

## 5. FMA and autopilot modes

UTF-32 texts in the PFD's FMA table, in order: autothrottle `SPD`, `EPR`, `FLCH`, `THR HLD`, `IDLE`, `GA`; pitch
`V/S`, `TO`, `ALT HOLD`, `ALT CAP`, `SPD`, `G/S`, `VNAV PTH`, `VNAV SPD`, `FLARE`, `GA`, `FLAP LIM`, `SPD LIM`;
pitch armed `G/S`, `VNAV`, `FLARE`; roll `HDG HOLD`, `TO`, `HDG SEL`, `LNAV`, `LOC`, `B/CRS`, `ROLLOUT`, `GA`; roll
armed `LNAV`, `LOC`, `B/CRS`, `ROLLOUT`; status `CMD`, `FD`; also `ATT`, `ALPHA`, `DH`, `RA`, `TCAS`, `TA ONLY`,
`TRAFFIC`, `NOT ON INTERCEPT HEADING`, `RESET MCP ALT`, `DRAG REQUIRED`, `UNABLE CRZ ALT`, `PERF/VNAV UNAVAILABLE`
(the last ones are FMC scratchpad messages). No `THR REF` (this model's thrust FMA is `EPR`).

Active modes: no L:var, event or data area. `bbs::afds` has getters only (`get_active_afds_mode`,
`get_active_pitch_mode`, `get_active_roll_mode`, `get_armed_pitch_mode`, `get_armed_roll_mode`,
`get_mode_change_flags`); `bbs::tmc` has `get_active_mode`, `get_thrust_limit_mode`. Proxies: the XML-known MCP
button vars (`B757_MCP_LNAV_BUTTON`, `_VNAV_`, `_FLCH_`, `_HOLD_1_`, `_VS_`, `_LOC_`, `_APP_`, `_SPD_`, `_CMD_1..3_`,
`AT_ARM_SWITCH`), which `afds::check_mcp_lights` probably drives; test whether their values follow the lights.

## 6. EFB (BBOS)

Apps (`bbs::app_*`): main (home), aircraft, checklist, navigation (charts and map), systems, settings, keyboard,
sign-in (Navigraph), flight model tuning (`/work/fm/fm.json` presets), loader status, overlay, modal dialogs. The EFB
draws on the `efb_l` gauge (`efb_left_bbos_*_app`); mouse input only (`mouse_callback`), plus keyboard capture for
text fields.

- **Aircraft app pages**: COCKPIT PRESETS, DOORS, CABIN LIGHT, FUEL, GND SERVICE, WEIGHT BALANCE, MAINTENANCE,
  REPOSITION, SIMVARS, and FAILURES (main page buttons `btn_cabin_door`, `btn_weight_balance`,
  `btn_ground_services`, `btn_fuel`, `btn_quick`, `btn_maintenance`, `btn_failures`, `btn_relocate`).
- **Cockpit presets**: ON GROUND / IN FLIGHT / CUSTOM / QUICK START / CIRCUITS STATE groups. Factory states, file
  to label: `coldanddark_wpb` COLD & DARK, `quickturn` TURNAROUND, `beforestart` BEFORE START, `afterstartflaps5`
  READY FOR TAXI, `readyfortakeoff` READY FOR TAKEOFF, `afterTOchecklistcomplete` AFTER TAKEOFF,
  `cruiseaboveFL180` CRUISE, `approachbaseleg` APPROACH, `beforelandingflaps30` FINAL APPROACH. Quick start: APU
  QUICK START, ENGINE QUICK START, IRS RAPID ALIGN. Circuits: PUSH ALL CBs, BATTERY, EXTERNAL POWER, popped-breaker
  list. Custom: SAVE USER CONFIG / LOAD USER CONFIG, then pick a slot.
- **Ground services**: FWD STAIR, JETWAY, AFT STAIRS, CATERING, GPU, CHOCKS, GROUND A/C, LEFT AIR START, RIGHT AIR
  START, CARGO SVC, FUEL TRUCK, PB TUG (left, straight, right; tug events above), REMOVE ALL; mode button
  `GND SRVC Mode:` MSFS or BBS.
- **Doors**: L1-L3, R1-R3, forward and aft cargo, CLOSE ALL, cockpit door.
- **Fuel and payload**: left, center, right sliders and typed values, IMPORT FROM SIMBRIEF; PAX BUS 0/16, PAX ECO
  0/160, forward and aft cargo; boarding and deboarding with progress.
- **Maintenance**: brake temperature reset, RAT deploy and stow, landing gear doors, alternate gear repair,
  hydraulic reservoir refill, oxygen (mask resets, crew bottle refill), fire bottle resets, APU oil servicing. All
  need the aircraft stopped on the ground.
- **Failures**: about 102 system failures in 12 groups (FIRES, DETECTION, ENGINES / APU, AUTOMATION, ELECTRICAL,
  AVIONICS, ICE / RAIN, DOORS, AIR CONDITIONING, FLIGHT CONTROLS, HYDRAULICS, LIGHTS) plus 192 bulb failures;
  triggers immediate, time, altitude, altitude and IAS, or flap range; random failures at 1x to 1000x.
- **Systems app**: electric, fuel, hydraulic, pneumatic diagrams, FMS, flight performance, ILS debug, LOGS (SAVE
  LOGS / AUTO SAVE to `/work/bbs-debug-saved/<YYYYmmdd_HHMMSS>.txt`), and the `ELECT LVAR DEBUG` toggle.
- **Notifications (toasts)**: short texts such as BOARDING INITIATED., BOARDING IS COMPLETE!, FUEL IS SET!, CHOCKS
  ATTACHED., GPU CONNECTED., JETWAY CONNECTED., FUEL TRUCK ARRIVED., PUSHBACK TUG ATTACHED., IRS ALIGNMENT
  INITIATED., IRS ALIGNED., CIRCUIT BREAKER POPPED!, `FAILURE ACTIVE: `, `FAILURE RESOLVED: `. Drawn only on the
  EFB; not exported.

Triggers an app could use: none for panel states (EFB clicks only). Candidates to test: `BBS_EFBX_SET_*` (fuel,
payload, door), `BBS_GPU_CART_CMD`, `BBS_APU_INSTANT_START`, `B757_GND_SVC_BUTTON`, the stock pushback K: events.
**The CDU route**: the CDU INDEX has a BLUEBIRDSIM MENU with RELOCATE (airport, area, runway, gate, coordinates,
bearing, altitude, speed, distance, TELEPORT) and STATS pages. The CDU is readable over `BBS_CDU_DISPLAY_1` and
drivable with the `B757_CDU_1_*` key L:vars (`_L1`..`_L6`, `_R1`..`_R6`, `_KEYA`..`_KEYZ`, `_NUM0`..`_NUM9`, `_EXEC`,
`_MENU`, `_INIT`, `_RTE`, `_LEGS`, `_DEPARR`, `_VNAV`, `_PROG`, `_FIX`, `_HOLD`, `_NAVRAD`, `_ATC`, `_FMCCOMM`,
`_PREV`, `_NEXT`, `_KEYCLR`, `_KEYDEL`, `_KEYSP`, `_KEYSLASH`, `_NUMPER`, `_NUMPLUS`), so a reposition needs no EFB.

**Storage**: factory states `Data/panel_states/<name>.snap` in the package; custom states
`/work/user_panel_states/<name>.snap` (with `.tmp` and `.bak`), that is
`LocalState\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work\user_panel_states\` (absent on this PC: no custom state
saved yet). Format: JSON `{"r":[...]}`, 121 positional per-system arrays with no names (subsystem `serialize`
output), so not a readable name/value list. Saving is refused while failures are active or armed, or a fire bottle
is discharged.

## 7. Settings and livery options (key names only)

`work\efb_settings.json` (40 keys; the EFB SETTINGS app, tabs GENERAL, AIRCRAFT, LIVERY, AUDIO, CABIN AUDIO, AIRAC):

| Key | Type | Likely control (from the EFB labels and help texts) |
|---|---|---|
| `failures.random_enabled`, `failures.notifications_enabled`, `failures.random_multiplier` | bool, bool, float | Random failures on/off, failure toasts, frequency 1x-1000x |
| `irs_align_time` | int | IRS ALIGNMENT TIME: INSTANT, 1 MIN, 2 MIN, 5 MIN, FULL (7 MIN+) |
| `auto_brightness`, `display_brightness` | bool, float | EFB brightness mode AUTO/MANUAL and level |
| `simbrief_id` | int | SimBrief pilot ID (a user identifier) |
| `hoppie_user_enabled`, `hoppie_logon_code` | bool, str | HOPPIE ACARS; the logon code is a credential |
| `sayintentions_acars_key` | str | SAYINTENTIONS ACARS KEY; a credential |
| `cpdlc_network` | int | CPDLC NETWORK: HOPPIE or SAYINTENTIONS |
| `show_debug_cursor` | bool | No EFB control found; a developer aid (probably draws the hit-test cursor on the WASM displays) |
| `force_keyboard_capture` | bool | LOCK KEYBOARD TO EFB: keyboard focus is captured while typing in EFB fields |
| `efb_power_on_during_init` | bool | EFB POWERED ON DURING INITIALIZATION (off: the EFB starts black until HOME) |
| `ui_theme`, `background_num` | int, int | EFB colors (DARK, LIGHT BLUE, DARK GREY, LIGHT GREY, DARK BLUE) and background |
| `automatic_cockpit_lighting` | bool | ADAPTIVE COCKPIT LIGHTING (follows daylight, including display brightness) |
| `tiller_realistic` | bool | NOSEWHEEL STEERING: REALISTIC (rudder pedals 5 degrees only, steer with the tiller) or RUDDER (full authority up to 20 kt) |
| `displays_bootup_realistic` | bool | DISPLAYS BOOTUP SPEED: REALISTIC boot sequence and timing, or FAST |
| `baro_sync` | bool | SYNC BAROMETERS (one knob sets captain, standby and first officer) |
| `cockpit_door_initial_open` | bool | INITIAL COCKPIT DOOR open or closed |
| `parking_brake_realistic` | bool | PARKING BRAKE MODE: REALISTIC (needs full braking) or EASY |
| `ground_services_mode` | int | GND SRVC Mode: MSFS or BBS |
| `loading_speed` | int | BOARDING & LOAD RATE: REALISTIC, STANDARD, QUICK, INSTANT |
| `automatic_loading` | bool | AUTOMATIC BOARDING (starts 30 minutes before departure on flight plan request) |
| `auto_connect_ground_services_on_init` | bool | AUTOMATIC GPU/JETWAY at startup |
| `auto_attach_chocks_on_init` | bool | INITIAL CHOCKS IN PLACE (next aircraft load) |
| `dirty_panel_states_enabled` | bool | DIRTY PANEL STATES: random plausible crew and maintenance discrepancies when loading Cold & Dark or Turnaround (`dirty_panel::crew_level`). Off for repeatable tests. |
| `electrical_relay_lvar_export_enabled` | bool | Systems app ELECT LVAR DEBUG: exports the 32 electrical relay states as L:vars named after the relays (`eK104_MAIN_BAT_RELAY`, ...) |
| `notification_popback_seconds` | int | NOTIFICATIONS FADE-OUT TIMER (NEVER or seconds) |
| `autobrake_notifications` | bool | LIVE AUTOBRAKE INFO (deceleration and brake efficiency toasts) |
| `charts_night_mode`, `charts_manual_icao`, `chart_view_states` | bool, str, dict | Charts app |
| `navigation_map_zoom`, `navigation_map_provider`, `navigation_map_night_mode` | int, int, bool | Map app (Navigraph or OpenStreetMap tiles) |
| `units` | int | UNITS: IMPERIAL or METRIC (systems, EFB, FMS) |
| `tod_pause_distance` | int | TOD PAUSE DISTANCE (NM): pauses the sim before top of descent |

Keys the module knows but this file lacks: `hide_cabin_when_cockpit_door_closed`, `aircraft_reposition_selection`.

`work\liveries\<registration>-opt.json`, per registration (saved automatically):
`options.general.weight_unit`, `.allow_capture_gs_before_loc`, `.vnav_takeoff_activation` (OPTION 1: VNAV waits for
a non-takeoff thrust reference; OPTION 2: VNAV selects CLB itself at 400 ft RA); `options.ehsi.up_reference` (TRACK
UP or HEADING UP); `options.model_visibility.exit_version`, `.cabin_layout`, `.satcom`;
`options.egpws_callouts.*` (26 booleans: 2500, radio_altimeter, 1000, 500, 400, 300, 200, 100, 80, 60, 50, 40, 35,
30, 20, 10, 5, plus_hundred, approaching_dh, approaching_minimums, 50_above, minimums, minimums_minimums, minimum,
decide, decision_height), matching the `BBS_PLAY_CALLOUT_*` triggers; `options.pilot_callouts.80_knots`, `.v1`,
`.rotate`, `.positive_rate`; `options.cabin_audio.announcements`, `.safety_announcements`,
`.non_safety_announcements`, `.flight_attendant_calls`, `.passenger_sounds`. EGPWS callouts play only when the
captain's DH is set above -20.

## 8. External integrations

The WASM makes its own HTTP calls (`fsNetworkHttpRequestGet/Post`, `bbs::https_wasm`):

- Hoppie: `https://www.hoppie.nl/acars/system/connect.html` (ACARS and CPDLC, `bbs::hoppie_client`, `bbs::cpdlc`).
- SayIntentions ACARS: `https://acars.sayintentions.ai/acars/system/connect.html`; SELCAL L:vars
  `SIAI_SELCAL_PENDING_COM1/2`; D-ATIS source options VATSIM and SayIntentions.
- SimBrief: `xml.fetcher.php` by user ID or username, `getroutes.js.php`, `api.simbrief.com/v2/performance/takeoff`
  and `/landing` (CDU takeoff and landing data via ACARS).
- Navigraph: device sign-in in the tablet JS (`identity.api.navigraph.com`), charts v2, ATIS v1, enroute bitmap
  tiles, navdata packages, `navdata.api.navigraph.com/info`, and the Navigraph navdata module over the Comm Bus.
- aviationweather.gov: METAR, TAF, NOTAM. OpenStreetMap tiles.
- BlueBird: `https://bluebirdsim.com/api/v1/authenticate`; BBS Nest over SimConnect client data (section 1).
- MobiFlight: `ws://localhost:8320/winwing/cdu-captain` (JS only). No other localhost port or socket in the WASM.
- GSX: no reference anywhere in the module (ground services are MSFS or BlueBird's own).

## 9. Other things a blind pilot's app would want

- **Checklists**: 9 EFB checklists (BEFORE START, AFTER START, BEFORE TAKE OFF, AFTER TAKE OFF, IN RANGE, APPROACH,
  BEFORE LANDING, AFTER LANDING, SHUT DOWN), about 60 challenge/response items in ASCII, with an INTERACTIVE mode
  that senses some items from A: vars (spoilers armed, Kohlsman, N2, on ground). Readable offline; no state exported.
- **Callouts**: pilot-monitoring callouts (80 knots, V1, rotate, positive rate) and EGPWS altitude callouts are
  built in and configurable per registration, so the app must not duplicate them when they are on.
- **Pauses**: the aircraft can pause itself at TOD (`tod_pause_distance`); the EFB can also lock the keyboard.
- **CDU text**: FMC scratchpad messages (INVALID ENTRY, NOT IN DATA BASE, ENTER IRS POSITION, ...), and a large ACARS
  page set, all reachable through the CDU Comm Bus frames.
- **Failure catalog** with plain-language help texts (repair steps such as cycling GEN CONT or waiting 5 minutes
  before PACK RESET), useful for a spoken failure briefing.
- Tooltips: none in the WASM; the cockpit XML's tooltips are in the package survey.

## Live tests to run first (L:vars the XML does not name)

1. `B757_APU_STATE` through an APU start (expect 4 states).
2. `B757_L_BUS_PWR_SRC`, `B757_R_BUS_PWR_SRC` on battery, ground power, APU, engines.
3. `eK104_MAIN_BAT_RELAY` (battery switch) and `eK114_EXTERNAL_POWER_CONTACTOR`, with and without
   `electrical_relay_lvar_export_enabled`.
4. `B757_DEBUG_PRESSURE_CABIN_ALT_FT`, 5. `B757_DEBUG_PRESSURE_CABIN_CHANGE_RATE`,
   6. `B757_DEBUG_PRESSURE_CABIN_DIFF_PSI` in a climb.
7. `B757_LEFT_PACK_STATE`, `B757_RIGHT_PACK_STATE` with the pack selectors.
8. `BBS_AP_TARGET_HEADING`, `BBS_AP_TARGET_VS`, `BBS_AP_TARGET_ALTITUDE` against the MCP windows (and
   `BBS_VS_BLANKED`).
9. `BBS_AIRSPEED_IN_MACH` with the IAS/MACH button.
10. `BBS_VNAV_PATH_DEVIATION` and `BBS_VNAV_TOD` in a VNAV descent.
11. `BBS_ECAM_Mode` with the EICAS ENG and STATUS buttons.
12. `B757_COMBUSTION_L_ENG` and `B757_L_ENG_STARTER_VALVE_OPEN` through an engine start.
13. `B757_BRAKE_TEMP_SENS_0`..`7` after a landing.
14. `BBS_GPU_CART_RUN_STATE` when the GPU is called and removed.
15. `L1_PAX_DOOR_STATE_SWITCH` and `B757_DOORS_CARGO_FWD_MOVING` when doors are operated from the EFB.

Then, as writes: `BBS_EFBX_SET_TOTAL_FUEL_LBS` (does fuel change?) and one `B757_CDU_1_*` key (does the CDU frame
change?). The `BBS_PLAY_*` triggers and `B757_TOTAL_PAX_COUNT` are worth one read each.
