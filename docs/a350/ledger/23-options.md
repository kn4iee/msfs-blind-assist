# Plan 23 ledger: the OIS OPTIONS, each on and off ("Airbus A350 Manual 1.5", PDF pages 40-51)

Every option on the OPTIONS tabs, A350 1.2.8, 2026-11-03. Each was set to another value through MSFSBA's own OIS client
(`A350Probe ois`, the same agent as the OIS window, [A350-6]'s typed keys), read back, put back and read back again: every
option came back to the owner's value (MASTER VOLUME, already at its maximum of 100, was stepped to 99 and back). The
**Variable** is the L:var that changed between a dump of the 250 `INI_` variables the OIS script names taken just before
the set and one taken just after (`opt_vars.py`, values that drift on their own left out); "none seen" means no named
variable moved within those seconds: the OIS keeps that option in its own storage (`DataStore` `useropt_a350_*`), or
writes it later (THR RED's write lands 4 s after the field, its timer `0xfa0`). **Off seen / On seen**: the two states as
the OIS window read them back. The 3RD PARTY tab is structure only ([A350-12]); its fields are in plan 13's ledger.

The cockpit hides controls for four options only (`A350_Interior.behavior.xml` 1.2.8: its option visibility conditions read
`INI_OPTION_FUEL_JETTISON`, `INI_OPTION_BRAKE_FANS`, `INI_OPTION_FO_ISIS`, `INI_IS_HUD`, and `INI_OPTION_DISPLAY_OLD`,
which swaps the display bezels and hides no control). Those four are `A350Options`; `A350OptionsTests` pins every hidden
control refusing aloud while its option is off. Writing those tests found the HUD's glareshield keys unreachable (a
display panel of the same name replaced their panel): fixed in 79637e529.

| PDF page | Option | Variable | What it changes | MSFSBA rows or displays affected | Owning plan | Off seen | On seen |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 40 | AIRCRAFT: THR RED, ft (400-10000) | `INI_ACCEL_HEIGHT_OPTIONS` (written 4 s later) | the take-off thrust reduction and acceleration height the FMS starts from | the MFD window, PERF T.O: THR RED, ACCEL (read "1060 FT" live, plan 15) | 07 | 1001 | 1000 |
| 40 | AIRCRAFT: IRS ALIGN (REAL, FAST, INSTANT) | `INI_IRS_ALIGNMENT_TIME` | how long the IRSs take to align | the ADIRS panel's IRS align status line (time remaining, "IRS aligned"), plan 11 | 11 | FAST | REAL |
| 40 | AIRCRAFT: LINKED INSTR (NONE, CPT & FO, CPT/FO & ISIS) | `INI_LINKED_INSTRUMENTS` | one baro knob sets the other side's (and the ISIS's) | the Altimeter window (Ctrl+B) and the baro rows; plan 02 measured each linking | 02 | CPT & FO | NONE |
| 40 | AIRCRAFT: VSD PLAN MODE | `INI_VSD_PLAN_MODE` | the vertical display under the ND in PLAN mode | the ND read (plan 08) | 08 | NO | YES |
| 41 | AIRCRAFT: RUDDER CTRL TILLER | `INI_NSW_SETTING` | the rudder axis steers the nosewheel on the ground too | none (the pilot's own axes; MSFSBA's taxi guidance tells, the pilot steers) | — | YES | NO |
| 41 | AIRCRAFT: ANF AUTO ZOOM | `INI_ANF_AUTO_ZOOM` | the airport map zooms by itself | the ND window's airport map (plan 08) | 08 | NO | YES |
| 41 | AIRCRAFT: AUTO TILLER DISCO | none seen | tiller inputs cut above 70 % N1 | none (the pilot's own axes) | — | YES | NO |
| 40 | AIRCRAFT: DISPLAY TYPE (NEW, OLD) | `INI_OPTION_DISPLAY_OLD`, inverted from its name: NEW writes 1, OLD 0 | the displays' bezels (the behaviour file's `SKEL_INT_OPTION_DISPLAY_VISIBILITY_NEW/OLD`); no control hidden | the display reader: with OLD the Captain PFD and ND read as with NEW (2026-11-03), the same draw code | 03 | OLD | NEW |
| 40 | AIRCRAFT: AIRCRAFT WEATHERING | none seen (`INI_WEATHERING` named in the script) | the model's wear | none | — | 1 | 0 |
| 40 | AIRCRAFT: DEFAULT PANEL STATE | none seen (kept by the OIS) | the state a new flight loads in | none (plan 17's flight starts cold and dark) | — | AUTO | COLD & DARK |
| 41 | OIS SETTINGS: AUTO EFB BRIGHTNESS | `INI_EFB_AUTO_BRIGHTNESS` | the OIS screen's brightness by day and night | none | — | NO | YES |
| 41 | OIS SETTINGS: CHARTS TYPE (LIDO, FAA, NAVIGRAPH) | `INI_CHART_TYPE` (1 LIDO, 3 NAVIGRAPH) | the chart source on TERML CHART | the OIS window's chart list (plan 06; the charts are pictures) | 06 | LIDO | NAVIGRAPH |
| 41 | OIS SETTINGS: AUTO DAY/NIGHT CHARTS | `INI_EFB_AUTO_CHARTS` | the chart colours by day and night | none (pictures) | — | NO | YES |
| 41 | OIS SETTINGS: AIRCRAFT WEIGHT UNITS (KGS, LBS) | `INI_IS_METRIC` | the units of the loadsheet and the MFD's weights | the OIS window's LOADSHEET and the MFD window's FUEL&LOAD read as shown; each OIS side keeps its own T.O PERF units (plan 06) | 06 | LBS | KGS |
| 41 | OIS SETTINGS: EFB BOOT TIME | `INI_EFB_FASTBOOT` | how long the OIS takes to start | the OIS window's "OIS NOT AVAIL" while it boots | 06 | FAST | REAL |
| 41 | OIS SETTINGS: DISPLAY REFRESH RATE (20, 30, 60 FPS) | `INI_DISPLAY_REFRESH_RATE_OPTION` | the cockpit displays' frame rate | none (the reader copies memory, not frames) | — | 20FPS | 30FPS |
| 41 | OIS SETTINGS: AUTO SAVE | `INI_AUTOSAVE_ENABLED` | the OIS's automatic saves | the OIS window's AUTOSAVE list | 06 | DISABLED | ENABLED |
| 41 | OIS SETTINGS: AUTO SAVE NOTIFICATIONS | `INI_AUTOSAVE_NOTIFICATIONS_ENABLED` | the "saved" notice on the OIS | the OIS window's notices (spoken as they appear) | 06 | DISABLED | ENABLED |
| 41 | OIS SETTINGS: AUTO SAVE INTERVAL | `INI_AUTOSAVE_INTERVAL` | minutes between saves | none | — | 11 | 10 |
| 41 | OIS SETTINGS: PREFERRED SEAT SIDE | `INI_PREFERRED_SEAT` | which OIS side holds the Situation Manager | the OIS window on that side (plan 06) | 06 | FO | CPT |
| 42 | EQUIPMENT: STANDBY NAV DISPLAY | `INI_OPTION_FO_ISIS` | the second ISIS unit (the SND) shown or not | `A350Options`: the SND panel's seven rows refuse aloud while off; the SND display | 23 | OFF | ON |
| 43 | EQUIPMENT: TAXI CAMERA LIGHT | `INI_OPTION_TAXI_CAM`, `INI_OPTION_Wing_Taxi_Cam` | the fuselage taxi camera lights on the model | none (no cockpit control) | — | OFF | ON |
| 42 | EQUIPMENT: ADF | `INI_OPTION_ADF` | the ADF receivers | the RMP's NAV page ADF lines and the RAD NAV page (plans 09, 10) | 10 | OFF | ON |
| 43 | EQUIPMENT: SATCOM | `INI_OPTION_SATCOM` | the satellite antenna on the model | none | — | OFF | ON |
| 42 | EQUIPMENT: BATTERY TYPE (Ni-Cad, Li-Ion) | `INI_BATTERY_OPTION`, `INI_OPTION_BATTERY_EXHAUST` | the battery model and its exhaust | the BAT display's voltages (plan 08's BAT read) | 08 | Li-Ion | Ni-Cad |
| 43 | EQUIPMENT: SCS OUTLETS (2, 4) | `INI_OPTION_SUPP_COOLING_3`, `_4`, `_INLET` | the supplementary cooling outlets on the model | none | — | 2 | 4 |
| 44 | EQUIPMENT: FWD LAV VENT | `INI_OPTION_LAV_VENT` | the forward lavatory vent on the model | none | — | OFF | ON |
| 44 | EQUIPMENT: LIGHT TYPE (LED, HID) | `INI_OPTION_LIGHT_LED_HID` | the nose, turnoff, landing and wing lights' look | none (the switches are the same) | — | HID | LED |
| 42 | EQUIPMENT: BRAKE FANS | `INI_OPTION_BRAKE_FANS` | the brake fans button | `A350Options`: the BRK HOT row refuses aloud while off | 23 | OFF | ON |
| 42 | EQUIPMENT: NO SMOKING SIGNS (AUTO, ON) | `INI_NOSMOKING_SIGN_MODE` | whether NO SMOKING follows the seat belts or stays on | the Signs panel's no smoking row reads the switch either way | 11 | ON | AUTO |
| 42 | EQUIPMENT: HUD | `INI_IS_HUD` | the HUD, its covers and its glareshield keys | `A350Options`: ten rows refuse aloud while off; the HUD displays dark | 23 | OFF | ON |
| 42 | EQUIPMENT: HUD COVERS | `INI_HUD_COVER_LEFT`, `INI_HUD_COVER_RIGHT` | the HUD covers on, as the Captain and First Officer HUD cover rows set them | the HUD Unit panels' cover rows | 11 | OFF | ON |
| 45 | PRINTER: F-PLAN INIT | `INI_PRINTER_AUTOPRINT_FPLAN_INIT` | prints the flight plan data on receipt | the FAP window's printer (plan 06) | 06 | NO | YES |
| 45 | PRINTER: T.O DATA | `INI_PRINTER_AUTOPRINT_TODATA` | prints the take-off data on receipt | the same | 06 | NO | YES |
| 45 | EXTERNAL: WING HOOKS | `INI_OPTION_HOOKS` | the model's wing hooks | none | — | OFF | ON |
| 45 | EXTERNAL: SHARKLETS (SHORT, LONG) | `INI_OPTION_WINGLET_SHORT`, `INI_OPTION_WINGLET_LONG` | the wingtip model | none | — | LONG | SHORT |
| 45 | EXTERNAL: FUEL JETTISON | `INI_OPTION_FUEL_JETTISON` | the jettison system and its two buttons | `A350Options`: ARM and ACTIVE refuse aloud while off | 23 | OFF | ON |
| 45 | EXTERNAL: SSA PROBE | `INI_OPTION_NOSE_PROBE` | the nose probe on the model | none | — | OFF | ON |
| 46 | GPS JAMMING: REAL WORLD GPS JAMMING | none seen (`INI_GPS_JAMMING` named in the script) | GPS faults in the real world's jammed areas | the ECAM and the MFD messages read as they come (plans 04, 09) | 04 | DISABLED | ENABLED |
| 46 | THROTTLE CALIBRATION: REVERSER ON AXIS | `INI_THROTTLE_MAPPING_USE_REVERSE_ON_AXIS` | the throttle axis's lowest part is reverse | the Thrust Levers rows are unaffected (they write the levers' requests) | — | YES | NO |
| 47 | THROTTLE CALIBRATION: USE GLOBAL DEADZONE | `INI_EFB_TC_GLOBALDEADZONE` | one deadzone for every detent | none | — | YES | NO |
| 47 | THROTTLE CALIBRATION: DZ % | none seen | the deadzone's size | none | — | 6 | 5 |
| 47 | SOUND/CABIN: CABIN LANGUAGE | `INI_CABIN_LANGUAGE_PACK` | the cabin announcements' language | none | — | English (UK) | English (US) |
| 47 | SOUND/CABIN: NOISE CANCELLING HEADPHONES | `INI_AUDIO_HEADPHONES` | quieter ambient sound | none | — | ON | OFF |
| 47 | SOUND/CABIN: ENGINE TAKE OFF WHINE | `INI_AUDIO_TO_WHINE` | a sound | none | — | OFF | ON |
| 47 | SOUND/CABIN: GEAR BUMP NOISE | `INI_AUDIO_GEARBUMP` | a sound | none | — | OFF | ON |
| 47 | SOUND/CABIN: CABIN CHIMES IN COCKPIT | `INI_AUDIO_CHIMES_IN_COCKPIT` | the cabin chimes heard in the cockpit | none (MSFSBA's own sign call-outs are separate) | — | ON | OFF |
| 47 | SOUND/CABIN: AUDIBLE THRUST DETENT | `INI_AUDIO_THRUST_DETENT` | a louder detent click | none (MSFSBA says the lever's detent) | — | OFF | ON |
| 48 | CALLOUTS: RA CALLOUTS 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400, 500, 1000, 2000, 2500 (18 boxes) | `INI_<n>_CALLOUT_OPTION`, one each | the aircraft's own radio altitude call-outs | none (MSFSBA's altitude call-outs are its own, Ctrl+M) | — | each unchecked | each checked |
| 48 | VOLUME CONTROL: MASTER, INTERIOR ENGINE, EXTERIOR ENGINE, MANIPULATOR, ANNOUNCEMENTS, SYSTEMS, ENVIRONMENT, GROUND SURFACE, AURAL CALLOUTS, LOW FREQ GAIN, COCKPIT WIND (11 sliders) | none seen in the seconds after (the script names `INI_AUDIO_VOL_*`) | the aircraft's sound levels | none | — | each one step | each restored |
| 51 | SIMULATION: AP DISCONNECT PROTECTION | `INI_AFK_MODE` | pauses the sim when the AP drops under time compression | MSFSBA's pause sentence ("the simulation has been paused", `A350PauseAlerts`), plan 07 | 07 | NO | YES |
| 51 | SIMULATION: PAUSE AT T.O.D / DIST (nm) | `INI_PAUSE_AT_TOD_ENABLED` | pauses the sim before the top of descent | the same sentence, "Top of descent pause" (plan 07 Task 5) | 07 | NO | YES |
| 51 | SIMULATION: PAUSE AT T.O.D DIST (nm) | `INI_PAUSE_AT_TOD_DISTANCE` | how far before it | none | — | 31 | 30 |
| 51 | SIMULATION: AUTO GPU DISCO | `INI_AUTO_GPU_DISCONNECT` | the GPU goes when the beacon comes on | the OIS window's GROUND page; ELEC EXT AVAIL | 06 | YES | NO |
| 51 | SIMULATION: AUTO STEP CLIMB | `INI_AUTO_STEP_CLIMB_ENABLED` | step climbs flown by themselves | the FMA and FCU call-outs as for any climb | 07 | YES | NO |
| 51 | SIMULATION: TIME COMPRESSION (OFF, AUTO) | `INI_AUTO_TIME_COMPRESSION_ENABLED` | the sim rate rises by itself in cruise | the PFD's red "SIM RATE" line read (plan 07) | 07 | AUTO | OFF |
| 51 | SIMULATION: MAX COMPRESSION (2X, 4X) | `INI_AUTO_TIME_COMPRESSION_RATE` | its ceiling | the same | 07 | UP TO 4X | UP TO 2X |
| 51 | SIMULATION: SIM RATE REMINDER | `INI_OPTION_SIM_RATE_POPUP` | the reminder of a raised rate | the PFD read | 07 | OFF | ON |
| 51 | SIMULATION: AUTO IMPORT STEP CLIMB | `INI_AUTO_INSERT_STEP_CLIMBS` | SimBrief's step climbs put in the plan | the MFD window's F-PLN (step points as rows) | 09 | ON | OFF |
| 51 | SIMULATION: AUTO IMPORT WINDS | `INI_AUTO_IMPORT_WINDS` | SimBrief's winds put in the plan | the MFD window's wind pages | 09 | ON | OFF |
| 51 | SIMULATION: AUTO DEROTATE | `INI_AUTO_DEROTATE` | the nose lowered for the pilot after touchdown | none (the pilot's own controls; the rollout call-outs as before) | — | ON | OFF |
| 51 | SIMULATION: PERFORMANCE MODE | `INI_PERF_ENABLE`, `INI_CABIN_PERF_MODE` | some model detail left out | none | — | OFF | ON |
| 51 | SIMULATION: CONDENSATION FX | `INI_FX_PERF_MODE` | condensation effects on the model | none | — | OFF | ON |
| 51 | SIMULATION: CPNY FPLN REQUEST (REAL, INSTANT) | `INI_OPTION_COMPANY_FPLN_DELAY_INSTANT` | how soon the company flight plan arrives | the MFD window's INIT request and the mailbox (plans 09, 22) | 09 | INSTANT | REAL |
| 49-50 | 3RD PARTY: STREAMER MODE, the SimBrief, SayIntentions and Hoppie fields, NAVDATA, CONNECT NAVIGRAPH, UPDATE DATA, RESET NAVIGRAPH | not toggled (credentials) | the sign-ins | the OIS window: fields named by length under STREAMER MODE ([A350-12]); plan 13's ledger | 13 | — | — |
