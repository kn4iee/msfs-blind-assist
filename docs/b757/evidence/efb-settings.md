# What BlueBird stores for each tablet setting

Measured 2026-10-27 on Robin's PC: MSFS 2024 1.9.21.0 (Microsoft Store), `bluebirdsim-aircraft-757-200` 0.1.0, BBS
Nest running, the 757 loaded with registration G-BBS75 (the only one installed), MSFSBlindAssist closed.

Method: taps at tablet points through the EFB agent (`Resources/coherent-bbs757-efb-agent.js`). After each tap, both
files were read again in full: every key, flattened, compared with the read before the tap. Labels and words were read
from captures of the ND pop-out mirror (`tools/Bbs757EfbProbe`, PaddleOCR). Every value was put back. At the end, both
files matched copies taken before the first tap, except `display_brightness`, which BlueBird changes itself in AUTO.

Points are tablet pixels (1397 x 1048). Words are as the tablet shows them; the app writes them in sentence case.

## 1. How BlueBird keeps its settings

- **Two kinds of file**, in `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work`:
  - `efb_settings.json`: one line of JSON, about 1 KB, for the whole aircraft.
  - `liveries\<registration>-opt.json`: one per registration flown, here `G-BBS75-opt.json`. The tablet says so
    on its Callouts and Cabin audio pages: selections save for this aircraft registration.
- **Read once, at load.** While the 757 is loaded, an edit to either file is ignored. BlueBird writes its own copy back
  over the file when its Settings app or a Settings section opens, and on every change made on the tablet. Every tap
  below was in the file when it was read 1.3 s later. Opening Settings > Livery options rewrote `G-BBS75-opt.json`,
  unchanged since 25 October although the General and Aircraft pages had been open many times that day.
- **A write while the 757 is not loaded takes** (reload test, the same day). `loading_speed` 2 and `irs_align_time` 2
  were written with the sim at the main menu. On loading, BlueBird wrote the file itself with 2 and 2, and
  Settings > Aircraft showed QUICK and 2 MIN. Ending the flight did not write the file. Not checked: an IRS alignment
  or a boarding at those rates; "Restart flight".
- **An aircraft can stay loaded behind the sim's main menu**, so the screen cannot tell whether the 757 is loaded. The
  app writes only while no simulator process runs.

## 2. Settings > General (`efb_settings.json`)

Switches at x 1167, rows y 112, 172, 232 and 352; sliders on rows y 292, 412 and 472. A tap on a slider moves its
knob to the tapped point.

| Tablet label | Key | Word: stored value |
|---|---|---|
| LOCK KEYBOARD TO EFB | `force_keyboard_capture` | ON: true, OFF: false |
| EFB POWERED ON DURING INITIALIZATION | `efb_power_on_during_init` | ON: true, OFF: false |
| EFB BRIGHTNESS MODE | `auto_brightness` | AUTO: true. Switched off: false, and the word becomes the brightness percent (no MANUAL word) |
| (the percent, when not AUTO) | `display_brightness` | float 0-1 shown as `floor(v * 100)` %: 0.99901 shows 99%, 0.51053 shows 51%. Switching AUTO off or on leaves it unchanged |
| EFB BACKGROUND | `background_num` | DEFAULT: 0; then the digit itself, 1 to 9 (x 804: 0, 826: 1, 875: 2, 900: 3, 925: 4, 950: 5, 975: 6, 1000: 7, 1025: 8, 1050 and right: 9) |
| LIVE AUTOBRAKE INFO | `autobrake_notifications` | ON: true, OFF: false |
| NOTIFICATIONS FADE-OUT TIMER | `notification_popback_seconds` | "N SEC": N, for every whole second from 3 (left end, x 898) to 30 (x 1176), about 10 px a second; NEVER: 0 (right end, x 1190) |
| EFB COLORS THEME | `ui_theme` | DARK BLUE: 0, LIGHT BLUE: 1, DARK GREY: 2, LIGHT GREY: 3. Slider order: DARK BLUE (x 904), LIGHT BLUE (975-1025), LIGHT GREY (1050-1125), DARK GREY (1150 and right). No fifth theme |

## 3. Settings > Aircraft (`efb_settings.json`)

Switches at x 1167; rows y 111 + 60 per row.

| Row y | Tablet label | Key | Word: stored value |
|---|---|---|---|
| 111 | ADAPTIVE COCKPIT LIGHTING | `automatic_cockpit_lighting` | AUTOMATIC: true, MANUAL: false |
| 171 | TOD PAUSE DISTANCE (NM) | `tod_pause_distance` | off: -1. Switched on, it stores 40; the number field at (1250,171) opens a keypad in columns x 1098, 1216 and 1333 (7 8 9 at y 293, 4 5 6 at 368, 1 2 3 at 443, - 0 . at 518, BKSP DEL OK at 593); typing 120 then OK stored 120. Switched off: -1 |
| 231 | NOSEWHEEL STEERING | `tiller_realistic` | REALISTIC: true, RUDDER: false |
| 291 | DISPLAYS BOOTUP SPEED | `displays_bootup_realistic` | REALISTIC: true, FAST: false |
| 351 | SYNC BAROMETERS | `baro_sync` | ON: true, OFF: false |
| 411 | INITIAL COCKPIT DOOR | `cockpit_door_initial_open` | OPEN: true, CLOSED: false |
| 471 | PARKING BRAKE MODE | `parking_brake_realistic` | REALISTIC: true, EASY: false |
| 531 | IRS ALIGNMENT TIME | `irs_align_time` | INSTANT: 0 (x 930), 1 MIN: 1 (969-1000), 2 MIN: 2 (1043-1080), 5 MIN: 5 (1116-1150), FULL(7 MIN+): 9 (1190) |
| 591 | BOARDING & LOAD RATE | `loading_speed` | REALISTIC: 0 (x 945), STANDARD: 1 (975-1045), QUICK: 2 (1070-1120), INSTANT: 3 (1145-1190) |
| 651 | AUTOMATIC BOARDING | `automatic_loading` | ON: true, OFF: false |
| 711 | AUTOMATIC GPU/JETWAY | `auto_connect_ground_services_on_init` | ON: true, OFF: false |
| 771 | HOPPIE ACARS | `hoppie_user_enabled` | OFF: false (2026-10-25). Not switched on today: that could log on to Hoppie with the owner's code |
| 831 | CPDLC NETWORK | `cpdlc_network` | HOPPIE: 0, SAYINTENTIONS: 1 |
| 891 | INITIAL CHOCKS IN PLACE | `auto_attach_chocks_on_init` | ON: true, OFF: false |
| 951 | DIRTY PANEL STATES | `dirty_panel_states_enabled` | ON: true, OFF: false |

## 4. Home > AIRCRAFT app (`efb_settings.json`)

The AIRCRAFT app is at (320,190) on the Home page; its menu is at x 130.

| Where | Tablet label | Key | Word: stored value |
|---|---|---|---|
| GROUND SERVICES (130,351), button (405,910) | GND SRVC Mode | `ground_services_mode` | BBS: 0, MSFS: 1 |
| FAILURES (130,531), RANDOM tab (849,173), slider y 485 | RANDOM FAILURE FREQUENCY, shown "FREQUENCY: N.Nx (1x-1000x)" | `failures.random_multiplier` | float 1.0-1000.0, `10^(3t)` for the knob's place t from 0 to 1 along a track from about x 305 to 1255 (fitted to the readings): x 290: 1.0, 400: 1.9952623 (10^0.3, "2.0x"), 600: 8.54, 800: 36.57, 1000: 156.58, 1200: 670.37, 1275: 1000.0 |
| same tab, button (460,1004) | RANDOM FAILURES: ON / OFF | `failures.random_enabled` | ON: true, OFF: false |
| same tab, button (839,1004) | NOTIFICATIONS: ON / OFF | `failures.notifications_enabled` | ON: true, OFF: false |

## 5. Settings > Livery options (`liveries\<registration>-opt.json`)

Three rows, switches at x 1167; the page does not scroll.

| Row y | Tablet label | Key | Word: stored value |
|---|---|---|---|
| 111 | UNITS | `options.general.weight_unit`, and `units` in `efb_settings.json` | IMPERIAL: "lbs" and `units` 1; METRIC: "kg" and `units` 0. One tap writes both files; a banner "Unit system set to Metric" (or Imperial) covers the row for a few seconds |
| 171 | EHSI UP REFERENCE | `options.ehsi.up_reference` | HDG: "hdg" (switch on), TRK: "trk" |
| 231 | VNAV TAKEOFF BEHAVIOR | `options.general.vnav_takeoff_activation` | OPTION 1: "vnav_button" (VNAV engages without CLB thrust and selects CLB itself), OPTION 2: "clb_button" (VNAV waits for the crew to select a thrust reference other than takeoff, normally CLB) |

The global `units` has no row of its own: the AIRAC data page has none, and UNITS here is the only control that
changes it.

Not on the tablet for G-BBS75, kept by BlueBird: `options.general.allow_capture_gs_before_loc` (true),
`options.model_visibility.exit_version` (1), `options.model_visibility.cabin_layout` (1),
`options.model_visibility.satcom` (0).

## 6. Settings > Callouts (`liveries\<registration>-opt.json`)

Switches at x 1247, words ON: true, OFF: false. Headings PILOT CALLOUTS, EGPWS CALLOUTS (with the note that the
captain's DH must be above -20 for EGPWS callouts to play), RADIO ALTITUDE and APPROACH / MINIMUMS. The page scrolls:
ten agent wheels (800,700, +2000 each) reach the end.

| Tablet label | Key | Row y at the top | Row y at the end |
|---|---|---|---|
| 80 KTS | `options.pilot_callouts.80_knots` | 206 | |
| V1 | `options.pilot_callouts.v1` | 258 | |
| ROTATE | `options.pilot_callouts.rotate` | 310 | |
| POSITIVE RATE | `options.pilot_callouts.positive_rate` | 362 | |
| 2500 | `options.egpws_callouts.2500` | 550 | |
| RADIO ALTIMETER (2500) | `options.egpws_callouts.radio_altimeter` | 602 | |
| 1000, 500, 400, 300, 200, 100, 80 | `options.egpws_callouts.1000` ... `.80` | 654, 706, 758, 810, 862, 914, 966 | |
| 60 | `options.egpws_callouts.60` | 1018 | 128 |
| 50, 40, 35, 30, 20, 10, 5 | `options.egpws_callouts.50` ... `.5` | | 180, 232, 284, 336, 388, 440, 492 |
| PLUS HUNDRED (DH + 100) | `options.egpws_callouts.plus_hundred` | | 594 |
| APPROACHING DH (DH + 100) | `options.egpws_callouts.approaching_dh` | | 646 |
| APPROACHING MINIMUMS (DH + 80) | `options.egpws_callouts.approaching_minimums` | | 698 |
| FIFTY ABOVE (DH + 50) | `options.egpws_callouts.50_above` | | 750 |
| MINIMUMS | `options.egpws_callouts.minimums` | | 802 |
| MINIMUMS MINIMUMS | `options.egpws_callouts.minimums_minimums` | | 854 |
| MINIMUM | `options.egpws_callouts.minimum` | | 906 |
| DECIDE | `options.egpws_callouts.decide` | | 958 |
| DECISION HEIGHT | `options.egpws_callouts.decision_height` | | 1010 |

**Exclusive groups.** Switching one callout ON switches the others in its group OFF; switching one OFF leaves the
whole group off. Every member was switched on at least once while another member was on:

- 2500 and RADIO ALTIMETER (2500)
- PLUS HUNDRED (DH + 100) and APPROACHING DH (DH + 100)
- MINIMUMS, MINIMUMS MINIMUMS, MINIMUM, DECIDE and DECISION HEIGHT

Every other callout changes only its own key.

## 7. Settings > Cabin audio (`liveries\<registration>-opt.json`)

Switches at x 1247, words ON: true, OFF: false.

| Row y | Tablet label | Key |
|---|---|---|
| 176 | ANNOUNCEMENTS | `options.cabin_audio.announcements` |
| 228 | SAFETY RELATED ANNOUNCEMENTS | `options.cabin_audio.safety_announcements` |
| 280 | NON-SAFETY RELATED ANNOUNCEMENTS | `options.cabin_audio.non_safety_announcements` |
| 332 | FLIGHT ATTENDANT CALLS | `options.cabin_audio.flight_attendant_calls` |
| 384 | PASSENGER SOUNDS | `options.cabin_audio.passenger_sounds` |

**The two categories follow ANNOUNCEMENTS.** The page says to turn announcements ON to adjust them. While
ANNOUNCEMENTS is OFF, taps on SAFETY RELATED and NON-SAFETY RELATED change nothing; switching ANNOUNCEMENTS off leaves
their stored values as they were.

## 8. Settings > AIRAC data (`efb_settings.json`)

Rows: SIMBRIEF ID NUMBER (with LOGOUT), HOPPIE LOGON CODE, SAYINTENTIONS ACARS KEY, the latest and installed cycle with
REMOVE, and the Navigraph sign-in state with LOGOUT. No units row. Not tapped today: the codes are typed on the tablet's
keyboard, and the values are the owner's.

## 9. What the app must copy from the tablet

The settings tab writes the files directly, so it must apply the tablet's own rules itself:

1. **UNITS writes two keys**: `weight_unit` in every registration file and `units` in `efb_settings.json`.
2. **The three exclusive callout groups** (section 6): switching one on switches the rest of its group off.
3. **The announcement categories** (section 7) cannot change while ANNOUNCEMENTS is off.

No other control changed a second key: every General, Aircraft, AIRCRAFT app, Livery options, Callouts and Cabin audio
control was checked against both whole files (only HOPPIE ACARS was left alone).
