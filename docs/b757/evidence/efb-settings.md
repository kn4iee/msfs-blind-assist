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

## 10. Handoff to plan 16

The Settings tab is built and reviewed: commits `4287a3663` (sections 1 to 9 above) to `7d7d6194d` on
`b757-efb-settings`, then the one that adds rule B757-17 and this section. This work is not one of the numbered gap
plans, and it edited none of the files plan 16 owns for the docs: `docs/b757.md`, `guide/**`,
`MSFSBlindAssist/HotkeyGuides/BBS757_Hotkeys.txt` and `changelog.d/**`. This section says what each needs. Line numbers
are as of `7d7d6194d`: check them against the file before editing. The in-sim checks (last list) have not been run.

**What changed for a pilot** (the text for `docs/b757.md`'s EFB section, the hotkey guide and the guide):

- The 757 EFB window (Input Shift+T) has two tabs, Flight (today's controls) and Settings. Ctrl+Tab switches between
  them.
- Settings shows every BlueBird tablet setting in seven sections (General, Aircraft, AIRAC data, Failures, Livery
  options, Callouts, Cabin audio), read from BlueBird's own files. While no simulator runs, a change is saved to the
  files at once and BlueBird applies it when the 757 next loads. While any simulator process runs, or the app cannot
  tell whether one does, everything is read-only and an attempt says "Close the simulator to change BlueBird's
  settings." A status box at the top of the tab says which case it is (with work folders under both simulators, its
  closed-simulator text ends "Changes go to the MSFS 2024 folder.", or the MSFS 2020 folder), and a simulator that
  starts or closes while the window is open is said once ("Simulator started: BlueBird's settings are read-only." and
  "Simulator closed: BlueBird's settings can be changed.").
- A switch is a check box, the tablet's named values are a choice list, and a number or text is a box with a Set
  button (Enter sets too; it says `<label> saved`, or "Couldn't save <label>: <reason>"). The SimBrief ID, Hoppie
  logon code and SayIntentions ACARS key show in plain text. A row can also read "Other (stored value N)", "Not
  readable in this BlueBird version", "Differs between registrations" (the Livery options, Callouts and Cabin audio
  sections change every registration's file) or, for EFB brightness while its mode is Auto, "Automatic".
- The Livery options, Callouts and Cabin audio sections start with a line naming the registrations whose files their
  rows change ("Applies to 1 registration: G-BBS75", "Applies to 3 registrations: A, B, C"; with no registration file
  yet, "BlueBird has no registration options yet. Fly a livery once to create them.").
- Escape closes an open list first (a choice list on the Settings tab, the Cockpit preset list on the Flight tab) and
  closes the window only when no list is open; it closed the window at once before.
- Sign in to Navigraph and Update nav data moved to the Settings tab (they still need the 757 loaded). An "EFB status"
  box there shows the same last message as the Flight tab's Status box, the Navigraph code included.
- Removed from the Flight tab: the SimBrief ID box with Set SimBrief ID (the ID is set once on the Settings tab with the
  simulator closed; without it the CDU's ACARS INIT does nothing) and Set steering to rudder (Nosewheel steering is a
  Settings > Aircraft row). Fetch SimBrief flight and Load fuel and payload from SimBrief now say, with no ID set,
  "Your SimBrief ID isn't set. Close the simulator and set it on the Settings tab." (it replaces "Set your SimBrief ID
  first.").
- The tab copies the tablet's own rules (section 9): Units writes two keys; switching on one callout of an exclusive
  group says `<label> turned off` for the one it replaced; the two announcement categories say "Turn on Announcements
  to change this." while Announcements is off.
- Before a file's first write, the app copies it to `%APPDATA%\MSFSBlindAssist\backups\bbs757\<folder key>\<file name>`
  (the folder key is 8 hex digits of SHA-256 of the source folder's path), once for each file for the life of the EFB
  window (made at the first Shift+T, closed when the profile switches away from the 757). A window made again after a
  profile switch copies the already-changed file on its first write, replacing the earlier copy.

**Stale places** (they describe the removed buttons, the window as one page, or the app never writing BlueBird's
files):

- `MSFSBlindAssist/HotkeyGuides/BBS757_Hotkeys.txt`, about lines 296-303, 361-363 and 387-395: the SimBrief ID box and
  Set SimBrief ID (296-303), "Set your SimBrief ID first." in Load fuel and payload's text (361-363) and Set steering to
  rudder (387-395). The "757 EFB (Shift+T):" intro (259-264) also describes one page, with the Status box "at the
  bottom", and lists Sign in to Navigraph and Update nav data (265-295) as its buttons: describe the two tabs and put
  those two entries under Settings. Line 408 (Escape, "Close the window") is stale too: Escape closes an open list
  first.
- `guide/boeing-aircraft-and-the-md-11.md`, lines 488, 491 and 494: the same three. Also 470 (set your SimBrief ID in
  the EFB window), 482 (the window as "a column of buttons, a Status box and a Close button", and "Escape closes the
  window") and 486-487 (Navigraph and nav data). The chapter "The EFB" (`#boeing-757-efb`) gains the Settings tab.
- `guide/hotkey-reference.md`, line 541: the same two buttons in the list. Also 537-538 (Tab moves to "the SimBrief ID
  box"; Enter in it sets the ID) and 539 ("Escape — Close.": it closes an open list first). The section
  (`#efb-window-input-shift-t`) gains the Settings tab.
- `docs/b757.md`, lines 264-265, 276-277, 293-301 and 335: 264 says `Bbs757WorkFolder` reads `tiller_realistic`, and 265
  that it never writes BlueBird's files; it now reads only `simbrief_id`, `units` and the installed cycle, and the app
  writes BlueBird's files while no simulator runs (B757-17). 276-277 are the SimBrief ID entry, 293-301 Set steering to
  rudder (and `Bbs757EfbLayout.MayClickSteering`, which no longer exists), 335 "Set your SimBrief ID first." The file
  map (lines 18-23) calls `Bbs757WorkFolder` "read only" and lists none of the new files. The EFB section gains the
  Settings tab: what it writes and when (B757-17), the guard, the safety copy, the catalog (sections 2 to 8), the
  tablet's three rules (section 9), what is spoken and what is read-only.
- `docs/b757/plans/02-gaps/11-efb-rework.md`, lines 147-148 (plan 11's own text): tablet toggles judged on a variable
  "(services, GPU, steering per B757-2)". Steering is no longer tapped, and B757-2 now covers the chocks tile only.

**The rule and the places that cite this file.**

- B757-17 is in (`.claude/rules/b757.md`; full text in `docs/invariants/b757.md`), added here although the index
  reserves the rule lines of those two files for plan 16. B757-5 to B757-16 are promised to other plans (plan 16's
  Task 2 lists B757-5 to B757-11), so the rule file runs from B757-4 to B757-17 until they land: do not renumber. This
  work also took the steering half out of B757-2 (`Bbs757EfbLayout.MayClickSteering`,
  `Bbs757WorkFolder.ReadTillerRealistic` and the tablet reader's steering branch are gone). Plan 11's handoff
  (`11-efb-rework.md`, line 112) proposes extending B757-2 with "each settings switch on its file key": with steering
  gone and the reader read-only, the app taps no settings switch, so check that the extension still has something to
  guard.
- Plan 16 removes `docs/b757/` before the merge. These comments cite this file by path: `Bbs757SimGuard.cs` line 25,
  `Bbs757SettingsFile.cs` line 34, `Bbs757EfbSettings.cs` lines 152 and 182, `BBS757EfbForm.Settings.cs` line 11 and
  `Bbs757EfbSettingsTests.cs` line 9. Ten more cite only "evidence section N", so `git grep "docs/b757/"` will not
  find them: `Bbs757EfbSettings.cs` lines 228, 436, 464, 488, 499, 516, 530, 552 and 583, and
  `Bbs757EfbSettingsTests.cs` line 10. Four cite "the spec", which is not committed (`docs/superpowers` is
  git-ignored): `Bbs757EfbSettings.cs` lines 184 and 500, and `Bbs757SimGuardTests.cs` lines 10 and 21. B757-17's full
  text restates sections 1 and 9 and stands alone, so point the comments about section 1 or 9 there. Sections 2 to 8
  (each tablet label, key and stored value) are the authority for the catalog and `Bbs757EfbSettingsTests`: fold them
  into `docs/b757.md` first.

**Notes for plan 11's owner and plan 16.**

- `Bbs757EfbBridge` (the tablet screen reader) is built read-only (`MainForm.BBS757.cs` line 100; `ReadOnly` defaults to
  true). Its steering branch was removed with the live steering button, so read-only is now the only thing that keeps
  the reader from tapping a settings toggle such as NOSEWHEEL STEERING: keep it read-only, or add a read-before-tap
  guard if presses come back.
- The names `Bbs757EfbSettings.cs` and `Bbs757EfbSettingsTests.cs` are taken. The index (`00-index.md`, lines 63 and 85)
  and plan 11's Task 3 (`11-efb-rework.md`, lines 73-75) reserve them for a settings-file reader that never writes, and
  plan 11's execution record says Task 3 was replaced, not built. This work built files of those names as the settings
  catalog, and it writes BlueBird's files. It also added files no row of the index names: `Bbs757SettingsFile.cs`,
  `Bbs757SimGuard.cs`, `Bbs757SettingsRows.cs`, `Bbs757SettingsSave.cs` and `Forms/BBS757/Bbs757LockableComboBox.cs`,
  with their tests; and `Forms/BBS757/BBS757EfbForm.Settings.cs`, which the index's `BBS757EfbForm*.cs` row (line 62)
  already gives to plan 11. The existing files it changed all belong to plan 11's EFB work: `BBS757EfbForm.cs`,
  `Bbs757EfbLayout.cs` (the SimBrief ID keypad entry and the steering sequence are gone), `Bbs757EfbBridge.cs`,
  `Bbs757EfbElements.cs`, `Bbs757WorkFolder.cs` (`ReadTillerRealistic` is gone; `ExistingFolders`, `SimulatorOf` and
  `RegistrationFiles` are new) and `Bbs757EfbTests.cs`.
- Also seen, older than this work: `Bbs757WorkFolder`'s readers (`ReadSimBriefId`, `ReadUnits`, `ReadInstalledCycle`)
  call `TryGetProperty` and `GetString` outside a guard. A hand-edited file with an unpaired surrogate escape in a key
  name or value would throw out of them (BlueBird itself writes plain ASCII). `Bbs757SettingsFile.ValueAt` shows the
  guard.

**User guide (CORE-19) and the changelog.**

- The PR line: while plan 16 has not updated the guide, `User guide: needed: 757 EFB window` (plan 16 updates `guide/`
  with the rest of the 757); once plan 16 bundles the guide, its own `User guide: updated: <chapters>` line covers the
  757 EFB window chapter.
- Proposed changelog fragment (category `aircraft` or `feature`, as plan 16 bundles it): "The 757 EFB window has a
  Settings tab: every BlueBird tablet setting can be read and changed with the screen reader while the simulator is
  closed, and BlueBird applies it at the next load."

**Still to run in the simulator** (this work's in-sim plan and what its reviews added; plan 16's Task 5 can take them
into the owner's pass):

1. Simulator closed: Shift+T, Ctrl+Tab to Settings. Check the status box, the section list, the tab order and what is
   spoken. Change Nosewheel steering, Boarding & load rate, IRS alignment time, Units, one callout, the SimBrief ID and
   a Hoppie logon code; each choice keeps focus. Switch on Radio altimeter (2500): "2500 turned off" is spoken. Switch
   Announcements off: the two categories say "Turn on Announcements to change this." Optional: Hoppie ACARS on (never
   tapped on the tablet).
2. The locked lists: arrows, Left/Right, the Page keys, Home/End, a letter, Alt+Down, F4, the wheel and a click each
   speak "Close the simulator to change BlueBird's settings." once and change nothing; a held key speaks once;
   Alt+letter and Escape still work (Escape closes an open list, not the window).
3. A refused save puts the stored value back (an arrow commit and an open-list commit). An open list, an arrow, then
   Tab away: nothing saved, nothing said? (to see)
4. NVDA's order: "checked" before "2500 turned off"; focus never moves after a save, "Simulator closed" or opening.
5. Simulator start and close: spoken within about a second; the status box follows; the tab is editable again, with
   what BlueBird left.
6. With the 757 loaded: the values set in 1 are in effect (both files read; Settings > Aircraft, Livery options,
   Callouts and AIRAC data captured). Sign in to Navigraph and Update nav data work from the Settings tab, and the
   Navigraph code is readable in the tab's EFB status box.
7. The premise of the group rule (`Bbs757SettingsSave`: a callout missing from a livery file counts as off): with the
   simulator closed, remove `options.egpws_callouts.minimum` from `G-BBS75-opt.json`, load the 757, read the file (is
   the key absent, added off or added on?), then restore the key.
8. Timing: a five-member callout group save with the guard checking before every write (about 6 ms a check).
9. Layout at the owner's DPI, measured and not looked at: the Flight tab without a scroll bar; the Settings rows scroll
   with focus.
10. From the catalog's own notes and section 9, not in the list above: what was never measured. Clear the SimBrief ID,
    the Hoppie logon code and the SayIntentions ACARS key on the tab (the safety copy then holds the real values), load
    the 757 and read what BlueBird holds (-1, "" and "" expected; section 8 was not tapped). With the 757 loaded, set
    and then clear each of those three boxes on the tablet itself, and switch Hoppie ACARS on and off if the owner
    agrees (on, it could log on to Hoppie with his code), diffing both whole files after every change as in section 1:
    section 9's check was not made on these four controls, so whether any of them writes a second key is unmeasured.
    Then close the simulator and set the three again on the tab (the safety copy holds them). Set the TOD pause
    distance to 1 and to 999 and see what the tablet shows (the range is a ruling; the tablet's keypad limits were not
    measured).
