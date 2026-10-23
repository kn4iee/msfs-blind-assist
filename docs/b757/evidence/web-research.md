# BBS 757: public facts before install (web research of 2026-10-23, about 13:00 UTC)

Web only, read only; nothing downloaded. The aircraft had **not shipped** at the time of research: bluebirdsim.com
showed a maintenance page, and the last post in the official MSFS forum thread guessed an evening (European time)
release. So there were no release-day community findings, and **no L:var, H: event, B: event or preset name was
published anywhere** reachable. Everything below is the vendor's or the press's word until measured in the sim.

## Release

- Product: "BlueBird Simulations 757 Passenger". Launch build: **757-200 only, RB211-535E and PW2000 engines**.
  The -300 follows as a free update for the native MSFS 2024 version only; a freighter is a later paid expansion
  (Just Flight product page; msfsaddons review 2026-10-22). An older msfsaddons article (2026-09-15) said the -300
  launches too; every newer source says it does not.
- **Built on the MSFS 2020 SDK**; runs in MSFS 2024 as a compatible build, with a native 2024 version promised later
  as a free upgrade (simflight 2026-10-12). Expect a 2020-style package layout (source behavior XML rather than
  compiled), which suits `tools/survey_package.py`. In MSFS 2024 the variant packs (doors, engine, winglets) list
  separately from liveries (review).
- $80; sold by BlueBird directly and through Just Flight; Xbox months later. BlueBird runs its own activation,
  download delivery and web portal (flightsim.to devlog 78); the installer and package folder names are unpublished.

## Interfacing

- **MobiFlight: the only official claim is about the CDU.** Just Flight's page says the FMS talks to MobiFlight "and
  others for CDU hardware"; devlog 48 lists MobiFlight under "CDU Hardware & ACARS Integration". No announcement of
  MobiFlight events or presets for switches was found.
- HubHop (queried with a forced refresh, 2026-10-23): no BlueBird/BBS vendor, no 757 aircraft, nothing for
  "bluebird" or "B757". MobiFlight's WinCtrl CDU supported list does not name the 757 yet.
- **MobiFlight's CDU interface** (MobiFlight developer docs, WinCtrl CDU): an aircraft pushes JSON frames to
  `ws://localhost:8320/winwing/cdu-captain` (also `cdu-co-pilot`, `cdu-observer`); each frame is
  `{"Target":"Display","Data":[...]}` with one entry per screen cell (character, color letter, size, optional
  inverse). **Inference, to measure:** the 757's "MobiFlight CDU" support may be exactly this, in which case the CDU
  text reaches whoever serves that socket (the MobiFlight connector, or our own listener when the connector is not
  running).
- **WINGFLEX** is BlueBird's exclusive hardware partner (msfsaddons 2026-10-16). WINGFLEX Bridge release notes (MSFS
  WASM component 1.1.0) add "CDU compatibility for BlueBird 757", so a third-party WASM module can read the CDU; the
  transport (client data area, L:vars, other) is undescribed. WINGFLEX's 2027 757 panels claim OpenHID, MobiFlight
  and SPAD.neXt compatibility.
- Architecture: autopilot and FMC are custom code that bypasses default MSFS systems (msfsaddons 2025-02-01), so
  stock autopilot K: events probably do nothing (unconfirmed). The CRT displays are written in C++ (msfsaddons
  2026-08-03), which means WASM gauges: no DOM to scrape for the PFD, ND or EICAS.
- Not found anywhere: an SDK or header, FSUIPC offsets, a documented client data area, a BlueBird REST or WebSocket
  server, a web or external CDU or EFB, keyboard bindings.

## Systems (vendor claims)

- FMC: custom, modelled on the Pegasus FMC; two independent FMCs, ARINC 424 legs, LNAV/VNAV, holds, missed approaches,
  autoland; every CDU page simulated; navaids from Navigraph data, with an AIRAC page in the EFB (Just Flight; fsnews
  2026-09-05).
- Displays: CRT only at launch (an LCD option comes with the freighter); two EICAS computers with over 300 messages;
  the warning electronics unit is simulated.
- Autopilot: three flight control computers, autoland, auto trim. No autothrottle detail found.
- IRS: realistic or instant alignment, drift, radio updating.
- Electrics: over 500 circuit breakers mapped, about 200 working at launch. Hydraulics L, C, R and the RAT; bleed and
  packs; pressurization with two automatic modes and one manual; brake temperature; ice accretion; TCAS II.
- Failures: sources disagree (a suite after launch, or about 200 to 280 set from the EFB at launch).
- Weather radar: not at launch (native 2024 version). Terrain display needs the external "NEST" app.
- ACARS/CPDLC: Hoppie (departure clearance, air-to-air), SimBrief OFP and performance uplink, METAR/TAF/ATIS; CPDLC
  with Hoppie and SayIntentions, BeyondATC later. No printer mentioned.
- **EFB**: BlueBird's own tablet by the captain's window. Pages: cockpit presets, fuel, weight and balance, ground
  services, cabin and doors, reposition, maintenance, ACARS dashboard, OFP, weather and NOTAMs, map, Navigraph
  charts, performance calculator, synoptics, failures. **Panel states live only in the EFB**: Cold & Dark,
  Turnaround, Ready for Taxi, Ready for Take-off, Cruise, Approach, Final Approach. A reviewer found its nested menus
  confusing, which matters doubly for a screen reader.
- GSX: no profile at review time (BlueBird says one is in progress). Doors through the EFB, with EICAS door messages;
  three flight attendant call zones; GND CALL handles ground power.

## Documentation

- "BlueBird_757_Master_Manual_v1.00": 357 pages, about 200 MB, posted on BlueBird's Discord 2026-10-14, written by an
  active 757/767 pilot; normal, abnormal and supplementary procedures, ACARS, EFB. Whether it lists variables, and
  whether it ships in the package, is unknown. (Reading a PDF here: render pages with WinRT `Windows.Data.Pdf` from
  Windows PowerShell 5.1; the Read tool cannot.)
- A user on AVSIM asked whether the manual includes hardware-binding L:vars; no answer was visible. AVSIM and Reddit
  were unreachable from the research tools.

## Sources

- https://bluebirdsim.com/b757
- https://www.justflight.com/product/bluebird-simulations-b757
- https://forums.flightsimulator.com/t/bluebird-simulations-757/499752?page=28
- https://msfsaddons.com/2026/10/22/bluebird-simulations-boeing-757-review-an-incredibly-deep-high-fidelity-airliner-but-does-it-justify-its-premium-price/
- https://www.simflight.com/2026/10/12/bluebird-simulations-b757-msfs-release-date/
- https://msfsaddons.com/2025/06/15/bluebird-simulations-announces-price-for-757-details-development-roadmap-and-767-progress/
- https://msfsaddons.com/bluebird-757/
- https://flightsim.to/project/19/bluebird-simulations-boeing-757/devlog/48/bluebird-simulations-757-the-ultimate-feature-breakdown
- https://docs.mobiflight.com/game-controllers/winctrl/winctrl-cdu/ and its `dev-instructions/` page
- https://msfsaddons.com/2026/10/16/bluebird-picks-wingflex-as-exclusive-hardware-partner-with-dedicated-757-panels-coming-in-2027/
- https://www.wingflex.com/pages/wingflex-bluebird-757-767
- https://github.com/wingflexsim/Bridge-Software/releases
- https://msfsaddons.com/2026/08/03/bluebird-posts-its-deepest-757-breakdown-yet-confirms-failures-arrive-post-launch/
- https://fsnews.eu/navigraph-previews-bluebirds-757-for-msfs-2024/
- https://msfsaddons.com/?p=52033 (the manual)

## Questions this leaves for the sim

1. Package folder, SimObject titles, ATC model, variant keys.
2. Are switches L:vars, B: input events, H: events, or a custom bus? Do stock K: events (lights, transponder, baro,
   autopilot) move anything?
3. CDU: does the aircraft push frames to `ws://localhost:8320/winwing/cdu-*`, export a client data area, or both? How
   are CDU keys pressed?
4. EFB: HTML in Coherent (reachable on :19999) or a WASM gauge?
5. What the WASM exposes: EICAS message text, FMA modes, engine values.
