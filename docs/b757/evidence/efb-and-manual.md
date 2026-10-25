# What a full 757 flight needs from the EFB (manual of 2026-09-28, live checks of 2026-10-25)

Source: BlueBird's "757 User Manual and Procedures Guide" v1.00 (28 September 2026, 357 pages, the owner's copy;
not committed, page numbers cited), and live checks in the sim. The question: which steps of a normal flight can only
be done on BlueBird's EFB, which a screen-reader pilot cannot use (`spec.md` §6)?

## The flight data path is the CDU, not the EFB

- **SimBrief flight**: ACARS on the CDU. MENU → ACARS (2L) → OPERATIONS (2R) → PREFLIGHT (1L) → INIT FLT (1L) →
  INIT* (6R) retrieves the flight; the page then shows flight number, date, origin, destination, scheduled times,
  block, ETE and required fuel in small text (pp. 244-249).
- **Route**: ROUTE page `<REQUEST`, then on FMC COMM `RTE 1: SEND 3L; LOAD 4L` when the uplink arrives, then
  ACTIVATE and EXEC (pp. 116-118, 4182-4199 in the text). The uplink does not select procedures: departure and arrival
  are chosen on the CDU as usual.
- **Winds**: route and descent wind uplinks (WIND DATA UPLINK READY / DES FORECST UPLINK READY), loaded on the CDU
  (p. 119 onward).
- **Performance**: ACARS PERFORMANCE requests take-off and landing data from SimBrief's performance service; the
  results load PERF INIT and TAKEOFF REF through FMC COMM `UPLINK READY` prompts, reviewed with ACCEPT/REJECT on the
  CDU (pp. 250-272).
- **Weight and balance**: ACARS WGTBAL; PRELIM and FINAL arrive automatically during automatic boarding (pp. 273-283).
- Weather (METAR, TAF, D-ATIS), company messages, delays, departure and oceanic clearances (Hoppie or SayIntentions),
  flight log and post-flight reports: all ACARS pages on the CDU (pp. 284-333).

All of it is CDU text and CDU keys, which the app reads and drives (`cdu.md`). New messages announce themselves with a
chime, the EICAS "ACARS" communication message and `MSG*` at 6R.

## What still needs the EFB

| Need | When | EFB page (manual) | Outside route found | Status |
|---|---|---|---|---|
| SimBrief Pilot ID | once | Settings (p. 243) | `simbrief_id` in `work\efb_settings.json` (read -1 here) | the app could write it while the 757 is not loaded; **unmeasured** whether BlueBird reads an edited file |
| Navigraph sign-in (performance requests, charts, AIRAC) | once (tokens refresh) | Settings / AIRAC data (p. 351); CDU scratchpad "CHECK NAVIGRAPH LOGIN" when missing (p. 242) | **the sign-in runs in the hidden helper page**: a pending Navigraph device request (`user_code` 8 characters, `verification_uri` on navigraph.com, `verification_uri_complete`, 30-minute expiry) was readable on `<bbs-tablet>.nav_device_auth` while not signed in, with the helper polling (`ngPoll` true) | **proven end to end (2026-10-25)**: the code and the address with the code built in were read off the helper and given to the owner, who signed in with a screen reader in his own browser; within a minute the helper held an access token and a refresh token and stopped polling (`ngPoll` false). A new request had replaced the earlier one about every 5 minutes (`nav_auth_time_start` is `performance.now()` in ms), so the app reads the code fresh when the pilot asks. Persistence across an aircraft reload: to check |
| AIRAC update | when wanted | Settings / AIRAC data, update control (p. 351) | the module's `NAVIGRAPH_CallFunction` (`DownloadNavigationData`) over the Comm Bus; untested | optional: the FMS works on the bundled cycle 2503 ("NAV DATA OUT OF DATE"); a route built on a newer cycle may contain points the old one lacks |
| Hoppie / SayIntentions ACARS codes | once | Settings | `hoppie_*`, `sayintentions_acars_key`, `cpdlc_network` in the settings file (credentials: never display or log) | as SimBrief ID |
| Fuel | each flight | Aircraft / fuel (p. 338) | **`BBS_EFBX_SET_FUEL_LEFT_LBS` works** (wrote 10000: left tank 1,476.6 → 1,488.4 gal, setter read back -1); the `_TOTAL_` setter was consumed but changed nothing; stock tank variables refused | per-tank loading from the app looks feasible; center and right, and the total, to measure |
| Passengers and cargo | each flight | Aircraft / W&B (p. 338); START BOARDING in the top bar (p. 336); automatic boarding setting | `BBS_EFBX_SET_PAX_*`, `_CARGO_*` (untested); `automatic_loading` setting (starts boarding about 30 minutes before departure once a flight plan is requested) | to measure |
| Ground power | each flight, optional | Ground Services, "EFB GROUND POWER" (p. 69): connect the GPU, then EXT PWR on the overhead | the APU (measured: works without the EFB); stock `EXTERNAL POWER AVAILABLE:1` read 1 at the gate | APU route is enough |
| Pushback | each flight | "the EFB, MSFS or the chosen third-party add-on" (pp. 1784, 5223) | MSFS's own pushback and GSX (the app already supports both elsewhere); the module drives `K:TUG_*` itself | covered without the EFB |
| Doors | each flight | Cabin & Doors (p. 340); door messages on EICAS | `BBS_EFBX_SET_DOOR`, door state vars (`*_DOOR_STATE_SWITCH`) | to measure |
| Cockpit presets | optional | Aircraft / cockpit presets (p. 337) | none (no variable or event loads a panel state) | the pilot flies the flows; the app's First Officer can do them |
| EFB OFF | shutdown checklist item | | | cosmetic |
| Checklists | each flight | Checklist (p. 348) | the same normal checklist is a page of the manual (p. 201) | the app's own checklist from the manual's items |

## The EFB can be driven from outside after all (measured 2026-10-25)

- **Clicks**: the EFB gauge `efb_l` lives in the Coherent view whose title contains `BBSNavigraphMap` (VCockpit
  numbers change on every load: resolve by content). Its `<wasm-sim-canvas>` element forwards mouse input with
  `Coherent.call("WASM_MOUSE_MOVE" | "WASM_MOUSE_DOWN" | "WASM_MOUSE_UP" | "WASM_CLICK", guid, clientX, clientY,
  button)` (the element's own `onMouseMove`, `OnMouseDown`, `OnMouseUp`, `onClick`). Calling those four with page
  coordinates (gauge rect origin + EFB-local x,y in its 1397 × 1048 space) clicks the tablet exactly like the mouse:
  measured on the home screen (Settings), the Settings menu (General, AIRAC data), the SimBrief ID field (opens an
  on-screen numeric keypad; the top bar then shows "KEYBOARD LOCKED" until OK), the AIRAC UPDATE button, Home, and
  the Aircraft app. No camera move and no real mouse.
- **Seeing it**: the EFB's picture is an `<img>` (`coui://html_UI/Pages/VCockpit/Core/WasmGaugeLiveView_<n>`) in the
  same page. Coherent's debugger has no `Page.captureScreenshot` and in-page canvas copies return nothing, but a
  temporary copy of that `<img>` laid over the left ND's rectangle (`nd_l`, same page, `pointer-events:none`) makes
  the ND screen show the tablet, readable in a window capture of the ADI/EHSI instrument view (index 0) cropped at full
  resolution. It showed even with the ND unpowered. `SimConnect_CameraSetRelative6DOF` does nothing in MSFS 2024.
- Measured EFB-local positions (home screen and Settings): Settings icon (1073, 190); Settings menu General (90, 179),
  AIRAC data (95, 486); AIRAC page SimBrief ID field (842, 126), UPDATE (1291, 337); keypad OK (1052, 565); Home
  (19, 62); Aircraft icon (320, 190).
- **AIRAC update, end to end**: after a reload with the Navigraph login stored, the AIRAC page showed "LATEST CYCLE:
  2610", "INSTALLED CYCLE: ----" and a green UPDATE button (before the reload it showed "----" for both: the check runs
  at load). One click: within 4 s "NAVDATA UPDATED! PLEASE RELOAD FLIGHT TO APPLY.", installed cycle "2610 | EXP:
  2026-10-28", files `work\NavigationData\db.s3db` and `cycle.json`. The CDU IDENT page still showed 2503 until the
  reload (no automatic update at load).
- **After the update and before the reload the aircraft misbehaved**: the APU (running, generator on line) shut down
  with its FAULT light on, refused to restart after a fault reset (START sprang back to ON, no starter), and the EFB's
  APU QUICK START did nothing visible. Treat the flight as needing the reload the EFB asks for; the app should say so.
- **Cockpit presets are clickable** (Aircraft app → COCKPIT PRESETS): ON GROUND Cold & Dark, Turnaround, Before Start,
  Ready for Taxi, Ready for Takeoff; IN FLIGHT After Takeoff, Cruise, Approach, Final Approach; CUSTOM Save / Load user
  config; QUICK START APU, Engine, IRS rapid align; CIRCUITS STATE (all breakers pushed in, PUSH ALL CBs) and two
  Battery / External Power tiles that did not react to a click (status, or not available here). So panel states CAN be
  loaded by the app, through clicks: to measure once the aircraft is healthy.

### After the next reload (same day)

- **Cycle 2610 is active**: CDU IDENT "NAV DATA ACTIVE B572610001 OCT01OCT28/26", no "NAV DATA OUT OF DATE".
- **Loading a preset by click works**: Aircraft app → BEFORE START (EFB-local 433, 319): within about 12 s the battery
  was on, the APU running (`B757_APU_STATE` 2), both AC buses at 32 V from the APU, the EFB notified "L1 DOOR
  CLOSED", and the Battery tile turned green (the tiles show state: blue when off).
- **The ACARS pages read and drive cleanly from the CDU** (captain): MENU → `<ACARS` (2L) → `OPERATIONS>` (2R) →
  `<PREFLIGHT` (1L) → `<INIT FLT` (1L); the INIT FLIGHT page shows its empty fields as `☐` boxes and `INIT*` at 6R.
  Every page was legible from the Comm Bus frames.
- **Without a SimBrief ID, INIT\* does nothing** (pressed twice; no frame, no "VHF IN PROG", no message), while other
  keys kept working. The module's own help text: "Set a SimBrief ID in Settings, then fetch an OFP from the ACARS
  Dashboard." It fetches with SimBrief's per-pilot fetcher (`xml.fetcher.php`), not the Navigraph login, so the
  shared Navigraph/SimBrief account does not replace the ID. The order is: SimBrief ID (EFB Settings → AIRAC data,
  numeric keypad) → FETCH SIMBRIEF OFP (EFB Navigation app → ACARS Dashboard) → ACARS INIT on the CDU.

### SimBrief, end to end (same session, after the SimBrief ID was set)

All EFB steps by synthetic click, all CDU steps by held key L:vars, every result read from the mirror capture or the
CDU frames:

1. EFB Settings → AIRAC data → SimBrief ID field (842, 126) → keypad digits → OK (1052, 565): the field showed the ID
   and BlueBird wrote it to `work\efb_settings.json` (`simbrief_id`) at once. Keypad digit positions (EFB-local):
   7 (823, 255), 8 (936, 255), 9 (1052, 255), 4 (823, 334), 5 (936, 334), 6 (1052, 334), 1 (823, 413),
   2 (936, 413), 3 (1052, 413), 0 (936, 489), BKSP (823, 565), DEL (936, 565), OK (1052, 565).
2. EFB Home → Navigation (503, 190) → ACARS DASHBOARD (135, 179) → FETCH SIMBRIEF OFP (441, 120): notification
   "SIMBRIEF OFP RECEIVED"; the dashboard filled (KSEA 16L MONTN3 → KDEN 34R FLATI5, FL350, 938 NM, AIRAC 2610,
   block fuel and weights, full route string). A modal then asked to switch the aircraft from KG to LBS to match the
   plan (YES at 390, 658; answered YES: "Unit system set to Imperial"). The top bar then offered START BOARDING.
3. CDU MENU → ACARS → OPERATIONS → PREFLIGHT → INIT FLT → INIT* (6R): "VHF IN PROG", then FLT 4RK, date 25, KSEA →
   KDEN, scheduled out 00:10 / in 02:48, block 02:35, ETE 02:10, required fuel 25.6.
4. CDU RTE: RTE 1 with FLT NO already filled (BVI4RK); REQUEST `<SEND` (3L) → "SENT" → "ROUTE 1 UPLINK READY" with
   ROUTE UPLINK `<LOAD` / `PURGE>` (4L/4R) within about 15 s → LOAD: RTE 1 KSEA → KDEN, 3 pages (MONTN2 SEA, DIRECT
   NORMY, J90 MWH, DIRECT KU87M, DIRECT IDA, …) → ACTIVATE (6R) → EXEC: "ACT RTE 1".

Open detail: the OFP names the MONTN3 departure, the uplinked route MONTN2 (to check against the 2610 database and
SimBrief's cycle). Departure and arrival runways and procedures are selected on the CDU afterwards, as the manual says.
Not yet run: the ACARS PERFORMANCE requests (take-off and landing data), which use the Navigraph login.

## Answer to "how critical is the EFB for a full flight"

Not critical once it is set up. Every flight-data step (flight, route, winds, performance, weight and balance, weather,
clearances) runs on the CDU. What remains EFB-only is one-time setup (SimBrief ID, Navigraph sign-in, ACARS codes:
each has a plausible outside route above, to prove), per-flight loading (fuel has a working outside route; payload and
doors to test; automatic boarding may cover them), and conveniences with alternatives (GPU: APU; pushback: MSFS or
GSX; presets: the pilot's own flows). The AIRAC update is optional for flying but matters for matching SimBrief routes.
