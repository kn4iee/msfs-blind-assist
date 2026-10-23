# BlueBird Simulations 757: investigation protocol

Prepared 2026-10-23 (release day), before the aircraft was installed, on the `B757` branch cut from `main` at
5a48f17c6. The branch lives on kn4iee's fork (`kn4iee/msfs-blind-assist`). This file says what to measure, in what
order and with which tool, and where each answer goes. Its output is `docs/b757/spec.md` (the findings, shaped like
`docs/fenix-v3/spec.md` on the `FenixV3` branch), then `docs/b757/plans/` (shaped like `docs/fenix-v3/plans/`).
Like `docs/fenix-v3/`, the whole `docs/b757/` folder is deleted by the integration plan before the branch merges.

What is already known, before install: `evidence/web-research.md` (public facts, release-day reports) and
`evidence/repo-touchpoints.md` (where a new aircraft plugs into the app, and which shared files it cannot avoid).
In short: the launch build is the 757-200 (RB211 and PW2000), built on the MSFS 2020 SDK and running in 2024 as a
compatible build; the autopilot, FMC and CRT displays are custom code (displays in C++, so WASM gauges without a
DOM); **the only official MobiFlight claim is CDU output for hardware**, not switch events; panel states, ground
services, doors, fuel and payload live in BlueBird's own EFB. No variable names were public on release day.

## Superpowers, phase by phase

| Phase | Skill | Output |
|---|---|---|
| Investigation (steps 1-6 below) | `superpowers:brainstorming` (spike, then architectural path); `superpowers:dispatching-parallel-agents` for independent probes (package survey, web, code) | `evidence/*`, `spec.md` |
| Design | `superpowers:brainstorming`: owner questions one at a time, approaches, sectioned design | owner answers recorded in `plans/00-index.md` |
| Planning | `superpowers:writing-plans` | `plans/00-index.md`, `plans/NN-*.md` |
| Implementation | `superpowers:subagent-driven-development` or `superpowers:executing-plans`, `superpowers:test-driven-development`, `superpowers:using-git-worktrees` | commits on `B757` |
| Test and finish | `superpowers:verification-before-completion`, `superpowers:requesting-code-review`, `superpowers:finishing-a-development-branch` | the in-sim run, the pull request |

## Hazards (read before touching the sim)

- **simconnect MCP reads register per-frame watches.** `msfs_get_lvar` and read-mode `msfs_execute_calculator_code`
  make the MobiFlight module evaluate that expression every frame for the rest of the sim session (until the MCP
  reconnects, which clears the default client's list). With MSFSBA running it floods `debug.log` (rotates in minutes).
  If anything on the aircraft misbehaves after probing, `msfs_disconnect` + `msfs_connect` first. Read sparingly; batch.
- **The MCP can return null or stale L:var reads until it reconnects.** Reconnect, check a stock value, read one name
  per call before trusting a null.
- **Writes are allowed on this test setup** (owner, 2026-10-23), but record every switch moved and its old value in
  the step's evidence file, and put it back, or tell the owner where it was left.
- **Never judge a write by the MCP's `set_lvar`** ([DBG-1]): it can report success while the aircraft ignores or
  reverts it. A write is proven only by a fresh read-back after the aircraft has had time to react (1-2 s), and for
  anything that animates, by the cockpit's own indication (a light, a display, a capture).
- **Byte-identical calc commands are dropped** by MobiFlight ([SIM-17]): prefix each probe write with a unique
  `<n> 0 *` (for example `17 0 * 1 (>B:SOME_EVENT_Set)`).
- **The owner is a blind pilot**: no step may need sight. For anything visual (which camera view shows the CDU, is a
  display legible for AI reads) capture the sim window with `PrintWindow` from a scratchpad PowerShell script and look
  at the PNG; never guess a framing.
- **The vendor's files are never committed**: not the package, its JS, XML or manuals. Lists of names and our own
  measurements are fine.

## Step 1: offline package survey (10 minutes, no sim needed)

1. Find the package: `Community` or `Community2024` under
   `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages` (MSFS 2024, MS Store), or wherever
   BlueBird's installer put it (a launcher may install outside Community and add a link: check `Community` for a
   junction). Note the folder name, `manifest.json` `title`, `package_version`, `minimum_game_version`.
2. Run `python -I docs/b757/tools/survey_package.py "<package folder>" <scratch folder>` (reads only; tested on the
   iniBuilds A300 and PMDG 777 packages). Copy `summary.md`, `variants.tsv`, `displays.tsv`, `input-events.tsv`,
   `events.tsv`, `lvars.tsv`, `tooltips.tsv`, `templates.tsv`, `endpoints.tsv` and `documents.tsv` into `docs/b757/evidence/package/`.
   The `wasm-strings/` folder stays in the scratch folder: grep it, quote only names.
3. Answer from the survey, in `evidence/package-findings.md`:
   - **Variants**: every `title`, `atc_model`, `icao_type_designator`, `ui_variation` (757-200 / -300, engines,
     freighter, winglets). This is what plan 01's variant detection keys on.
   - **Displays**: which instruments are HTML (`html_ui/...`, Coherent views: DOM scrapable) and which are WASM gauges
     (`wasm_module=...`, only AI reads or exported data). Name the CDU, EICAS, PFD/ND, standby, EFB gauges.
   - **Control names**: are the cockpit's controls B: input events (compiled `*.behavior.xml` IDs with tooltip keys),
     L:vars driven by templates, H: events, or a custom event bus like the MD-11's `L:CEVENT`? Which template family
     builds the cockpit (`templates.tsv`)? If tooltips carry state maps (like the MD-11's `TOOLTIPID`), a generator
     like `tools/md11-gen` can build the control map.
   - **Endpoints**: any `localhost`/`127.0.0.1` port, WebSocket, `fetch` (an EFB server, a web CDU).
   - **WASM strings**: client data area names (`ClientData`, dotted names), CDU/FMC text exports, MobiFlight-style
     custom events, L:vars only the WASM knows.
   - **Manuals**: which PDFs ship (names only); the cockpit/MCDU chapters are the source for wording.

## Step 2: live identity and transports (aircraft loaded at a gate, cold and dark, then on ground power)

1. Connect the MCP (`msfs_connect`); `msfs_get_aircraft_snapshot` identity: `TITLE`, `ATC_TYPE`, `ATC_MODEL`, per
   variant you can load.
2. **B: catalog for free:** start MSFSBA from `C:\MSFSBA` (any profile) with the 757 loaded; it appends every input
   event the aircraft exposes to `%APPDATA%\MSFSBlindAssist\logs\input_events.txt` when it connects. Copy the newest
   block (from its `# InputEvent catalog` header) into `evidence/input-events-live.txt`. Close MSFSBA again before MCP
   probing (log flooding, see Hazards).
3. **L:var list:** `msfs_list_lvars` (the module caps a reply at 1,000 names; page with `filter_prefix` on the 757's
   prefixes from step 1) into `evidence/lvars-live.txt`. Diff against the package's `lvars.tsv`: names only the runtime
   knows come from the WASM.
4. **Coherent views:** `tools/coherent.ps1 views` into `evidence/coherent-views.txt`. For each 757 view (CDU, EICAS,
   EFB, ND …) one `eval` of `document.body.innerText.slice(0, 2000)`: is the text in the DOM, or is it a canvas?
5. **Local servers:** `Get-NetTCPConnection -State Listen` with owning process names, once before loading the 757 and
   once after; a new listener owned by the sim or a BlueBird process is an EFB or data server (as the Fenix's 8083 was).
6. **HubHop:** `msfs_list_hubhop_aircraft` with `refresh=True`, then `msfs_search_hubhop` vendor/aircraft: presets
   usually appear within days of a release; BlueBird's own MobiFlight presets may ship there.
7. **MobiFlight events:** if BlueBird publishes a MobiFlight event list after release (none was public on
   2026-10-23), check three by firing them (calc path, unique prefix) and reading back the state they should change.
8. **The CDU's MobiFlight output.** MobiFlight's WinCtrl CDU interface has the aircraft push screen frames to
   `ws://localhost:8320/winwing/cdu-captain` (`cdu-co-pilot`, `cdu-observer`). Grep the WASM strings for `8320`,
   `winwing`, `cdu-captain`; with the MobiFlight connector closed, look for the sim's connection attempts to port 8320
   (`Get-NetTCPConnection -RemotePort 8320`); if it tries, serve that path from a scratchpad listener on 127.0.0.1:8320
   and log a few frames while paging the CDU. Frames are `{"Target":"Display","Data":[...]}`, one entry per cell.
   Also look for the transport WINGFLEX's Bridge uses (its WASM reads the 757 CDU): a client data area name in the
   WASM strings. Record in `evidence/cdu.md`.
9. **Stock K: events:** fire a handful the pilot would expect to work (landing lights, beacon, transponder code,
   baro set, autopilot heading) and note which move the 757's own switches; the autopilot is custom, so expect not.

## Step 3: control sweep, panel by panel (writes allowed, everything restored)

For every cockpit control, find (a) its state source (L:var, B: event value, stock SimVar), (b) a write path that
moves it exactly like a cockpit click, (c) whether a light or display confirms it. Record one row per control in
`evidence/panel-sweep.md`: panel, control, positions and their values, state var, write path tried, result (moved /
reverted / no effect / two steps), confirming light, notes. Try write paths in this order and stop at the first that
works like the click: B: input event `_Set` with a value (or `_Inc`/`_Dec`), H: event, L:var over the calc path, L:var
data-def write, K: event. Use `docs/troubleshooting-playbook.md`'s decision tree for anything that resists.

**Read the state from an L:var or SimVar wherever one exists.** The app can fire B: events (`TrySetInputEvent`) but
never reads a B: event's value: `SimConnect_GetInputEvent` and `SubscribeInputEvent` are unused. If a control's state
lives only behind its B: event, say so in its row; the foundation plan then needs a B: read path, and the count of
such controls decides whether it is worth building.

Panels, in the order a cold-and-dark start needs them (a natural split for parallel plans):

1. Overhead: battery and electrics (ground power, APU generator, bus ties, standby power), APU (start, bleed),
   fuel (pumps, crossfeed), hydraulics (engine/electric/RAT, PTU), engine start and ignition, bleed air and packs,
   pressurization (auto/manual, landing altitude), anti-ice, window heat, probe heat, exterior and interior lights,
   signs (seat belts, no smoking), wipers, emergency lights, IRS mode selectors, yaw dampers, EEC, fire panel and tests.
2. Glareshield: MCP (speed, heading, altitude, V/S, every mode button, A/T arm, F/D, CMD A/B/C, bank limit, the
   selector values: how to set a number exactly, see below) and EFIS control panels (minimums, baro, mode, range,
   map items).
3. Forward panels: gear, autobrake, flaps indication, display source selectors, standby instruments, clocks.
4. Pedestal: thrust levers and reversers (read only unless safe), fuel control switches, flaps, speedbrake, parking
   brake, stab trim and cutouts, radios (VHF, HF, NAV, ADF), transponder and TCAS, weather radar, audio panels,
   aileron/rudder trim, cargo fire, evacuation.

Exact values: for every selector with a number (MCP speed, heading, altitude, V/S, baro, course, radios), find
whether a direct "set to N" write exists (an L:var the aircraft accepts, a `_Set` B: event with a value) or only
step events. A direct set is what the app wants; if only steps exist, measure the step size and whether bursts drop
steps (the MD-11 jammed at end stops under bursts).

## Step 4: displays and the FMS

1. **CDU/FMC**: is the screen text readable (the MobiFlight frames of step 2.8, a client data area like the MD-11 and
   PMDG, a Coherent DOM like the HS787)? How are keys pressed (B: events per key, H: events, an L:var bus)? Record a
   page of text plus how it maps to the 24-column grid in `evidence/cdu.md`. The CDU is the one display a blind pilot
   cannot do without; if no text source exists, stop and raise it with the owner before planning.
2. **EICAS**: the displays are C++ WASM gauges, so look for the alert messages as data (L:vars, client data, WASM
   strings with message texts) before falling back to AI reads. List the engine values (N1, N2, EPR for the RB211 and
   PW2000, EGT, FF, oil) with their units and agreement with stock SimVars, as the Fenix's IAE engines disagreed with
   stock.
3. **PFD/ND and standby**: AI display reads need camera views that frame each display legibly. Measure the view
   indices by setting `CAMERA VIEW TYPE AND INDEX` and capturing the window (the measurement, not `cameras.cfg`, is
   the truth). Record in `evidence/displays.md`.
4. **Autopilot state**: which vars say the active pitch, roll and thrust modes (FMA) and A/P / A/T engagement.

## Step 5: everything else a sighted pilot uses

**The EFB first.** BlueBird's tablet holds the cockpit presets (Cold & Dark, Turnaround, Ready for Taxi, Ready for
Take-off, Cruise, Approach, Final Approach), ground services, cabin and doors, fuel, weight and balance, performance,
ACARS/OFP, maintenance and failures; a sighted reviewer already found its menus hard to follow. Find whether it is
HTML in a Coherent view (`coherent.ps1 views`, then the Fenix v3 `efb-tour.md` method: read every page as a screen
reader would) or a WASM gauge (then: which L:vars or events drive each page's actions). Record per page in
`evidence/efb-tour.md`.

Then the rest: GSX (no profile at release), options and settings (livery-level equipment, units), failures, cabin
calls and sounds, ACARS/CPDLC (Hoppie, SayIntentions), checklists. Each gets a short note in `evidence/extras.md` with
its data source and whether a screen reader could use the vendor's own page.

## Step 6: findings and plans

1. Write `docs/b757/spec.md` in the Fenix v3 spec's shape: what the app can read and drive (by system), what it
   cannot (and why), what needs owner decisions, the few checks that still need the sim, the evidence file list.
2. Owner design pass (`superpowers:brainstorming`): one question at a time; answers go in the index's "Owner answers"
   table.
3. `superpowers:writing-plans`: `plans/00-index.md` (plan table, file ownership, claims, workflow, conventions,
   evidence list, base commit) and one plan per independent area.

## Tentative plan shape (confirm after step 3)

The split follows file ownership, as on `FenixV3`: one foundation plan first and alone, parallel plans that each own
their own files, an integration plan last and alone. Proposed profile code `BBS_757` and file prefix `BBS757` (no
`PMDG_`, `FENIX_` or `HS_` prefix: prefix checks exist, `evidence/repo-touchpoints.md` §1).

| # | Plan | Owns |
|---|---|---|
| 01 | Foundation: definition skeleton as partials with one file per panel area, profile registration and every shared file (menu, `SwitchAircraft`, Shift+M/Shift+T routing, mute gates, settings, hotkey list, FO window row, test lists), the write helper for whatever transport step 3 proves (B: events and/or calc path; `MSFSBA_BRIDGE_PROBE` if it writes L:vars), variant detection, rule file, a "757 windows" list if there will be several windows | the shared files, then frozen |
| 02-05 | One plan per panel area from step 3 (overhead; MCP and EFIS with the autopilot window and readouts; forward panels; pedestal and radios) | its own `BBS757Definition.<Area>.cs` partial and tests |
| 06 | CDU/FMC window (likely fed by the MobiFlight CDU frames, step 2.8) | `Forms/BBS757/BBS757Cdu*`, its service |
| 07 | EICAS, engine values and AI display reads | `BBS757DisplayReads.cs`, `Forms/BBS757/BBS757Eicas*` |
| 08 | EFB: panel-state presets, ground services, doors, fuel and payload, made usable (an accessibility layer if it is HTML, as Fenix v3 plan 03; our own window if it is WASM) | `Forms/BBS757/BBS757Efb*`, its script or service |
| 09 | First Officer: flows, checklists, take-off calls | `FirstOfficer/BBS757/**`, `BBS757TakeoffCallouts.cs` |
| 10 | Integration: hotkey guide, pilot's guide, docs and rules, changelog, the full in-sim run, the pull request, delete `docs/b757/` | last |

Whether 02-05 are four plans or two depends on how many contributors join; the shape of 06-08 depends on what steps 4
and 5 find. The -300 (native 2024 only) and the freighter come later: plan 01's variant detection must leave room for
them without guessing their names.
