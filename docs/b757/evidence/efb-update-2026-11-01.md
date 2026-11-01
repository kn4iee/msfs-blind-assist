# BlueBird's EFB update of 2026-10-31: a full tour of the tablet (2026-11-01)

Measured 2026-11-01 on the owner's PC: MSFS 2024 1.9.21.0 (Steam) on a 1920 x 1080 screen, the 757-200 PW parked at KATL,
BBS Nest running, MSFS Blind Assist closed. BlueBird's package still says 0.1.0, but it was reinstalled on 2026-10-31:
`BBS752.wasm`, which draws the tablet, went from 52,349,022 bytes (package survey) to 29,765,919; panel.cfg is unchanged
(the tablet is still `efb_l`, 1397 x 1048). The tour ran the probe (`tools/Bbs757EfbProbe`), which compiles the app's own
reader: its `tour` (now with `PROBE_TOUR_SAVE`, an app filter and a wait for Home), then `read`/`reread` page by page.

## What changed on the tablet

- **Unchanged:** Home, every Aircraft page (Cockpit presets, Fuel, Weight & balance, Ground services, Cabin & doors,
  Maintenance, Failures), every Checklist page, Settings General, Livery options, Callouts, Cabin audio, AIRAC data. The
  Fuel page's number fields are now centered in square boxes; they read as before.
- **Settings, Aircraft:** three new rows, and the page now scrolls (Hoppie ACARS, CPDLC network, Initial chocks and Dirty
  panel states are below the fold; the scroll buttons reach them). Each was switched on and back off on the tablet;
  `efb_settings.json` was byte-identical afterwards:
  - ADAPTIVE HANDLING ASSIST, `easy_flight_enabled`, OFF/ON. Switched on, it shows CONTROL RESPONSE under it: a slider,
    `easy_flight_authority` 0-100, said GENTLE at 0, BALANCED at 50, RESPONSIVE at 100 and "N/100" between.
  - AUTOMATIC STEP CLIMBS MASTER OPTION: (EXPERIMENTAL), `automatic_step_climbs`, DISABLED/ENABLED, with a status line
    under it (OFF; DISARMED once enabled, and a notification "AUTOMATIC STEP CLIMBS: DISARMED.").
  - THROTTLE LOCKOUT, `throttle_lockout_enabled`, OFF/ON.
  - The file also gained `ai_dispatch_enabled` (false), which no Settings row shows.
- **Navigation:** Weather & NOTAM's DEPARTURE | ARRIVAL | MANUAL and METAR | TAF | ATIS | NOTAM, and Charts' STAR | APP |
  TAXI | SID | REF (with a SimBrief plan loaded also DEPARTURE | ARRIVAL | MANUAL above it), are rows of tabs: the
  selected tab gray (or, on Charts' categories, a lighter blue), the others one blue band. The manual of 2026-10-12
  already names the views, so the bands may be the new part; no capture of these pages from before exists.

## Found and fixed

- **Wide tab bands were not pressable.** ARRIVAL | MANUAL is one band 666 px wide and TAF | ATIS | NOTAM 750, past the
  reader's 520 px button limit: the tabs read as plain text. A blue band of a button's height wider than 520 is now a
  row of tabs when its text falls into two clusters or more (a gray bar that wide, FLIGHT INFO, DEPARTURE WEATHER, and
  the flat slate of Failures' FAILURE | STATUS | ACTIONS header (31, 41, 54) stay headings).
- **A selected tab in the middle splits its band:** with ARRIVAL selected, DEPARTURE | ARRIVAL | MANUAL is three boxes
  touching (0-1 px apart); a box touching a tab on its line is a tab, and three or more touching boxes are a row of
  tabs, the odd-colored one selected. Failures' tabs stand 10-12 px apart and stay buttons.
- **Charts lost SID and REF:** the white chart filled most of the page, so the page's color was taken as white and the
  dark page joined every box. When the commonest color is bright and at least 12 % of the page is dark (17.6 % with the
  chart blank), the page's color is now the commonest dark one. The chart itself is now left out ("The chart itself is
  not read."): its print was noise, and boxes drawn in it read as buttons.
- **A row's selected tab hid another row's:** the shade test that finds TAXI selected was skipped on any page where
  another row had a gray selected tab; it now skips only that row. A box tinted as its band (SID alone when OCR missed
  REF) is no longer taken for the selected tab.
- **Systems' SELECT list was never offered:** its lines ("24 - ELECTRICAL", "28 - FUEL", "29 - HYDRAULICS",
  "36 - PNEUMATICS", x 1100-1395, y 88-240) are now menu entries named by their diagram, the shown one selected; a
  press lands on "Systems, Fuel" and closes the list. The vocabulary snapped "24 - ELECTRICAL" to the Failures page's
  ELECTRICAL; a chapter line now stays as read.
- **The three new settings and Control response** are in the catalog (`Bbs757EfbSettings`, the tablet's order), so the
  settings window offers them with the simulator closed and a tablet press is judged by BlueBird's file; Control
  response is a whole number 0-100, inactive while Adaptive handling assist is off. A file from before the update reads
  those rows as unreadable and every other row as before.
- **Words:** METAR, TAF, ATIS, SID and STAR stay in capitals.

Verified live after the fixes: all seven Weather tabs offered and ARRIVAL and TAF pressed where the reader placed them;
Charts' TAXI read selected, SID pressed and then read selected; Systems' list read and Fuel pressed.

## Found, not fixed (later work)

- **The boarding box's "»" pauses and resumes boarding** (tapped live: BOARDING PAUSED, then BOARDING… 16 MIN). The top
  bar reads the box as text while boarding runs, so the window cannot pause it. The icon was there on 2026-10-29 too.
- **Weight & balance's sliders** (business and economy passengers, forward and aft cargo) set the load directly; the
  reader reads only their figures (plan 18 left the load pages' sliders for later).
- **Charts' REF** is purple on blue: OCR missed it in 2 of 5 reads (once as "EF", dropped as noise), so it is sometimes not
  offered until the page is read again. Charts' small DAY button (42 px) is no box the reader finds.
- **The probe's tour lost an app tap while Home slid in** (Navigation and Settings, 2 of 5); `tour` now waits 1.5 s
  after Home. The app's window waits for the pilot, and says "The tablet did not show …" when a tap is lost.

## Quick start features (the owner's questions)

- IRS RAPID ALIGN (Aircraft, Cockpit presets) aligned the IRS at once ("IRS ALIGNED." 14:28Z); the reader offers it.
- No quick way to finish boarding outside Settings: Boarding & load rate set to INSTANT finished the remainder at once
  (the box left the top bar; "JETWAY DISCONNECTED."). It was set back to STANDARD (`loading_speed` 1).
- FETCH SIMBRIEF OFP with Automatic boarding on started fueling and boarding at once (top bar "DEP -00:11",
  "BOARDING… 17 MIN").

## Screen size

Nothing the reader does depends on the sim's resolution: it reads and taps in the tablet's own pixels (1397 x 1048).
The pop-out is parked left of the whole virtual desktop at exactly the tablet's width (`Bbs757EfbPopout.Park`), and a
pop-out of any other shape is mapped back to the tablet (`TabletSourceRect`, tested in
`The_capture_maps_any_window_shape_to_the_tablet`). Only making the pop-out depends on the window: the Right-Alt click
lands on the ND at fractions of the client height (measured on 1278 x 1360; the owner's 1920 x 1080 worked), with two
fallback points. Other sizes (ultrawide, 4K, windowed) are not measured.

## The simulator hang during the tour

The sim stopped answering a few seconds after the probe tapped Home on an already shown Home page, while the probe was
idle; it was restarted. `AsoboReport-Crash-3864339699.txt` (14:04:26Z): `Where="HangDetector"`, `Code=0xC0000194` (a
main-thread deadlock), every WASM module "Ready", BlueBird's included; not a WASM crash. A HangDetector report with no 757
loaded exists from 2026-10-31. Nothing ties it to the probe; the rest of the tour, page by page, ran without one.

## Handoff to plan 16

- Pilot-facing (CORE-19): the guide's 757 EFB chapter and `BBS757_Hotkeys.txt` can say that Weather & NOTAM's and
  Charts' tabs, and Systems' diagram list, are pressable; the settings window's new rows. Changelog `improvement`.
