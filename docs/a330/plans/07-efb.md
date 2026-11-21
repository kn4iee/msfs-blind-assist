# A330 EFB Implementation Plan (plan 07, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every page of the A330's tablet (Dashboard, OFP, Ground, Payload, Panel State, Take Off, Options) is walked against the EFB checklist of spec section 4, has a captured fixture and a test of exactly what BA's window lists for it, and every field and button on it does in BA's window what it does for a sighted pilot on the tablet. The chocks and covers press is left exactly as it is.

**Architecture:** A test harness that runs the shipping agent script on captured pages in jsdom, as the app's other EFB agents have. A page is captured from the live tablet through the Coherent debugger with credentials redacted, and its expected listing is written beside it. Each checklist failure is then a failing test first and an agent fix second. How each typed field accepts a value is measured field by field, never assumed.

**Tech Stack:** JavaScript (the agent, jsdom and Node's test runner in `tools/a330-efb-test`), C# for the definition's part; `tools/coherent-eval.ps1 -Title iniEfbA330`.

**Spec:** `docs/a330/spec.md` sections 2.5, 3 (R14), 4 ("The EFB checklist"); `docs/ini-a330.md` "EFB"; `docs/pmdg-efb.md`, `docs/flypad.md`; rules [INI-9], `.claude/rules/pmdg-efb.md`, `flypad.md`, `coherent-clients.md`.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Resources/coherent-a330-efb-agent.js`, `Aircraft/IniA330Definition.Efb.cs`, `tools/a330-efb-test/**` (new), `tests/MSFSBlindAssist.Tests/A330/A330EfbTests.cs` (new), `docs/ini-a330.md`'s EFB bullet, `docs/a330/evidence/07-efb.md` (new); by the index's named exception, additive changes to `ForIniA330` in `SimConnect/CoherentPmdgEfbClient.cs`.
- **The chocks and covers press is not touched** ([INI-9]: `A.COVER_REMOVAL`, `A.coverPressable`, `A.coverPress` and its call in `A.click`). Its three rows are captured and tested as they are; plan 12 removes the press.
- One inspector connection a view ([DBG-9]): MSFSBA is closed while a page is captured.
- A fixture never holds a credential or anything the pilot typed into the Options page's third-party fields: the capture redacts them and a test fails on one.
- The shared shell (`Forms/FBWA380/FbwEfbForm.cs`, `tools/flypad-shell-test`) is frozen: a need there is a handoff.

## Measured on 2026-11-20

- With the SimBrief identifier box on the Options page empty, the Dashboard's "Import from SimBrief" does
  nothing and shows nothing. Task 4's plan download needs the
  identifier typed first, and the window must say why a press did nothing: add it to Task 3's checklist
  for the Dashboard.
- The Dashboard read through the agent that day: the Pages list, "Import from SimBrief", Flight Details
  (flight number, alternate, departure, arrival, the two weather buttons), METAR Search, the two clocks.
- MSFS 2024's own tablet is now in main (#321) and opens from the Extras menu for every aircraft. It
  does not remove chocks or covers, so the A330's temporary press stays until plan 12 Task 3.

Later the same day, with the owner's SimBrief identifier typed into the Options box through the EFB agent
(Task 4, the plan download, walked once; a long-haul plan in POUNDS):

- **The Dashboard's "Import from SimBrief" then loads the OFP**; the OFP page shows it as one block of
  text of about 190,000 characters. Task 3 for the OFP page: it must read in pieces a screen reader can
  move through, not as one item. The Dashboard's Flight Details keep showing what the FMS holds.
- **The Payload page's "Import from SimBrief" takes the OFP's numbers in the tablet's own unit.** With
  the tablet in kilograms and the OFP in pounds it set the fuel to 106797 kg (the OFP's 106,797 lb) and
  the zero fuel weight to the 168,000 kg maximum (307,553 lb, capped) and switched the page to its
  zero-fuel-weight mode. Applied, the aircraft really holds it: after the owner's own import and instant
  load the simulator read 106,797 kg of fuel and 274,796 kg, on an aircraft whose maximum is 230,000 kg.
  The owner's ruling (2026-11-20): the tablet's unit is a setting the pilot changes on the Options page;
  this is something to NOTE, not something BA does for the pilot. So the window never converts or
  changes the unit: after this import it says the planned weights with the tablet's unit and, when the
  planned total passes the page's own "Max Weight", says so (Task 4); the guide says to set the tablet's
  unit to the OFP's before importing.
- The Payload page's boxes by element id: `zfwDirectText` zero fuel weight (shown only in that mode),
  `paxText` passengers, `zfwText` the CARGO box (the id says otherwise; the agent's field table already
  knows), `fuelText` fuel. The right values typed (zero fuel weight 139504, fuel 48442) and "Apply load"
  opened "Preparing Aircraft, Select Loading Speed": Load instantly, Load fast (up to 5 minutes), Load
  realistically (up to 40 minutes). Instant: live gross weight 187946 kg, "26.3% - 4.3UP", "ZFWCG
  25.4%", and the simulator's own weight and fuel agreed.
- Ground page as read: eleven doors with their state, "Close all doors", "Doors to loading
  configuration", a cabin lights slider, GPU, Chocks, Engine Covers, Pitot Covers, the jetway, catering
  and luggage toggles, four pushback buttons. The temporary press removed the engine and pitot covers
  and the page said "Covers Removed".
- **Panel State page: its four states (Cold & Dark, On GPU, On APU, Ready for Takeoff) read as headings,
  not as buttons.** Task 3: they must be pressable, behind Task 5's confirmation.
- Take Off page: "Sync conditions from METAR" takes the runway from the FMS's flight plan, with its
  length and heading, the wind, temperature and pressure from the METAR, and the take-off weight from the
  FMS's INIT B. So the FMS is programmed before this page is used; the window says where each value
  came from. "SEND TO FMGS" is the one control in this tablet that sends anything to the FMS, and it
  sends take-off data, never a route.
- **The route.** This tablet never sends a route, but its Dashboard import is what makes the MCDU's
  uplink work: with the OFP imported, the INIT page's request prompt (shown while FROM/TO is empty)
  brings the whole plan, correct and complete (plan 03's block; the owner's own preflight). The guide's
  order is: Dashboard import, Payload (see the pounds fault above), then the MCDU's request.
  MSFS 2024's own tablet is a second, worse way: its Flight Planner takes the route and "Send to
  avionics" puts it in the A330's FMS. Measured with the app's sim tablet reader: sent with airway names, the
  aircraft dropped every waypoint on an airway and three of four ocean coordinates and picked a wrong
  navaid of the same name; sent with every fix written as a direct waypoint, the whole route arrived in
  order, less the ocean coordinates (one came nameless) and the approach, which were added on the MCDU.
  The Take Off page, walked to the end: Calculate gave flex 53, V1 150, VR 157, V2 158, trim 4.3 UP and
  a performance message, and showed a "Send to FMS" button; pressing it says "Data sent to FMGS", and the
  MCDU's PERF TAKE OFF then offers the values with `CONFIRM TO DATA*`. The window says that the MCDU
  still has to confirm.
  After a reload of the flight the FMS came up already holding that route: the aircraft takes the
  simulator's flight plan when a flight starts. In pax and cargo mode the Payload page's "Enter zero
  fuel weight" opens "Enter a ZFW" with its own box and "Set zero fuel weight", which turns the weight
  into passengers and cargo (139504 kg became 196 passengers and 6816 kg).
  This belongs to the sim tablet's own feature, not to this plan: it is handed to plan 12 as a note for
  the guide ("load the route from the sim tablet, then check the ocean points and add the approach").

## Review Focus

- A field set by its value alone that the page shows but ignores when it computes (measured on the same maker's other tablet): each field's commit method is proven by the page acting on the value, not by the value showing (Task 4).
- Two elements sharing one id (the two cover buttons do): a field or button is told apart by its label too, and no table keyed by id drops one (Task 3 test on the Ground fixture).
- A status line offered as a button, or a button whose press changes nothing: checklist point 3, tested per page (Task 3).
- Units: a weight read in kg while the page is set to lb (point 9): the unit comes from the page's unit setting in the same capture (Task 3 test with both settings).
- A credential spoken, logged or captured (point 11): Task 2's redaction and Task 3's masked-field tests.

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 07` (`Plan: 07`). Branch from the base commit the index records for early plans.

### Task 1: The harness

**Files:** Create `tools/a330-efb-test/package.json`, `package-lock.json`, `README.md`, `harness.js`, `capture.js`, `secrets.test.js`, `fixtures/`.

**Interfaces:**
- Produces: `harness.js` exports `load(fixtureName)` → `{ agent, document, listing() }`, where `listing()` is what the agent's page collection returns for the fixture, as lines; `capture.js` is the expression `tools/coherent-eval.ps1 -Title iniEfbA330 -ExprFile` runs: it returns the tablet element's `outerHTML` with every `input`'s value under the Options page's third-party section, every `input[type=password]` and every element the redaction list names replaced by `REDACTED`.

- [ ] **Step 1:** Model the harness on `tools/md11-efb-test` (the same jsdom version and test runner; the agent is loaded from `MSFSBlindAssist/Resources/coherent-a330-efb-agent.js`, never a copy). `npm test` runs with no fixtures yet and passes.
- [ ] **Step 2:** `secrets.test.js`: fails when any fixture holds a value in a third-party field, a run of 20 or more characters that looks like a key or token, or an e-mail address. Commit `test(a330-efb): the agent's harness`.

### Task 2 [sim]: A fixture a page and state

**Files:** `tools/a330-efb-test/fixtures/*.html`; the evidence file.

- [ ] **Step 1:** With MSFSBA closed, capture each page as the tablet shows it now: Dashboard; OFP with no plan and with the owner's SimBrief plan downloaded; Ground with doors closed and with one open, the GPU on and off, the three cover rows; Payload empty and during boarding or fuelling, in kg and in lb; Panel State; Take Off before and after a computation; Options, each of its sections. The page is changed through the agent's own click, or by the owner; never the desktop mouse.
- [ ] **Step 2:** For each fixture, write in the evidence file what a sighted pilot sees on that page, top to bottom: every heading, value, field, button and its state. This is the yardstick for Task 3, not the agent's current output.

### Task 3: Each page against the checklist

**Files:** `tools/a330-efb-test/<page>.test.js` (one a page), `Resources/coherent-a330-efb-agent.js`; the evidence file's checklist table.

- [ ] **Step 1:** The table: a row a page, a column a checklist point (spec section 4, points 1 to 13), each cell pass, fail (with what fails) or not applicable. Fill it by running the agent on each fixture and reading its listing against Task 2's yardstick.
- [ ] **Steps 1-5 per page:** the page's golden listing as a test (`dashboard.test.js`: `lists_the_dashboard_as_the_tablet_shows_it`, and one test a failed cell, named for the point: `a_status_line_is_text_not_a_button`, `the_current_page_is_marked`, `a_disabled_button_says_unavailable`, `weights_carry_the_pages_unit`, `the_two_cover_rows_are_told_apart`, `a_credential_field_is_masked`); fix the agent; `npm test` green; commit `fix(a330-efb): the <page> page`.
- [ ] **Step 2:** What the tablet shows as a picture (the route map, charts, the hold diagram, wear diagrams) gets one text row saying what it is and that it is a picture, where the agent says nothing today.

### Task 4 [sim]: Fields, computations and the plan download

**Files:** `Resources/coherent-a330-efb-agent.js`, tests in the page's test file; the evidence file.

- [ ] **Step 1:** For every typed field (Payload: zero fuel weight, passengers, cargo, fuel; Take Off: runway, wind, temperature, pressure, weight, flaps and whatever else the page asks; Options: each text field): set it three ways through the agent with MSFSBA closed (the value alone with `input` and `change` events; key events a character with Enter; the page's own stepper buttons) and record which one the page ACTS on: the dependent value changes, or the computation uses it. The agent's set path for that field uses the method that worked.
- [ ] **Step 2:** The whole chain a sighted pilot does, through BA's window with the owner: download the OFP; load fuel and payload from it; compute take-off performance; read the speeds, the flex temperature and the trim. Each result read back from the page and, where the aircraft takes it, from the aircraft (fuel on board, the MCDU's weights).
- [ ] **Step 3:** Each miss fixed test-first on the page's fixture.

### Task 5: Presses that change the aircraft wholesale

**Files:** `Resources/coherent-a330-efb-agent.js`, `Aircraft/IniA330Definition.Efb.cs`; tests in the page's test file and `tests/MSFSBlindAssist.Tests/A330/A330EfbTests.cs`.

- [ ] **Steps 1-5:** A Panel State load (Cold and Dark, On GPU, On APU, Ready for Takeoff), instant fuelling or boarding, and a pushback start each ask Yes or No in BA's window before the press, unless the tablet itself asks (then BA reads the tablet's question, checklist point 10); tests `a_panel_state_load_asks_first`, `the_tablets_own_question_is_read_and_answered`; a press is confirmed once, from the state read back (point 7). Commit `feat(a330-efb): confirm before a wholesale change`.

### Task 6: The connection and the developer doc

- [ ] **Step 1:** Plan 01 closed the tablet's connection on an aircraft switch (`CloseEfb`): its test still passes with this plan's changes; the connection is also released when the window hides (`SetActive(false)`), as today.
- [ ] **Step 2:** The developer doc's EFB bullet rewritten: the pages, each field's commit method, what is a picture, the harness and how to capture a fixture.

## In-sim checks (owner, by ear)

1. Open the EFB from BA at the gate: walk the seven pages; nothing reads as a nameless button, and every weight has its unit.
2. Download your SimBrief plan, load fuel and payload, compute take-off performance: BA reads the same numbers the tablet shows.
3. Load "On GPU" from Panel State: BA asks first, and the cockpit follows.

## Handoff to plan 12

- **Pilot-facing:** what each EFB page offers in BA's window; the confirmations. Guide: the A330's EFB section. Changelog `improvement`.
- **Rule proposals:** INI-17: each A330 EFB field is set by the method measured to make the page act on it (list the fields that need key events).
- **Frozen files:** `tools/flypad-shell-test` or the shell, if a page needed something the shell lacks; `docs/development.md` and CLAUDE.md's tool notes (`tools/a330-efb-test`).
- **For plan 12:** the cover press is untouched; the three rows' fixtures and tests are in `ground.test.js` for its removal task.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** not started
- **Claimed by:** none
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
