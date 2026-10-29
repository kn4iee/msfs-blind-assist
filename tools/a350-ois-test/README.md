# A350 OIS agent tests

Offline tests of `MSFSBlindAssist/Resources/coherent-a350-ois-agent.js` against OIS pages captured live
(iniBuilds A350 1.2.7). CI runs them in the efb-readers job of `.github/workflows/tests.yml`; run them by hand
after changing the agent:

```
cd tools/a350-ois-test
npm ci
npm test
```

Each scrape in a test reads the page in full: the agent's dirty gate answers `{unchanged:true}` when its observer saw
no change, and jsdom delivers mutations as microtasks, so `run.js` marks the page changed before each scrape unless a
test passes `{ gate: true }` (`gate.test.js`).

## Re-taking a fixture

With the A350 loaded and the OIS showing the page, capture it from the side you want. Both OIS views are titled
`VCockpitNN - iniEfbA350` and their numbers follow load order, so a view's number is no side ([A350-8]): `ois:cpt`
and `ois:fo` find a side's view by the page it loads (`ini-efb-a350-cpt` or `-fo`, `coherent.mjs`'s `oisView`).
MSFSBlindAssist must not hold that view's socket at the time: Coherent allows one per page ([DBG-9]), and an OIS window's client keeps its view after the window closes,
until the aircraft is switched or MSFSBlindAssist quits.

```
node ../a350-probe/ceval.mjs ois:fo capture.js | sed -E 's/ data-a350-ois-label="[^"]*"//g; s/ class="__glassout_marker_inst"[^>]*>/>/g' > fixtures/<name>.html
```

`capture.js` keeps what is drawn, marks an element drawn with no size `data-vis="0"`, keeps small
hidden lists whole (the runway choices), drops styles except a fixed overlay's (dialogs), a colour
(door indicators, lit buttons), an opacity (the chock pictures), a percentage width (fluid bars), and the agent's own attributes. The sed drops labels a previous agent kept, so the fixture is the OIS as
first seen.

A fixture never keeps the owner's credentials. `capture.js` saves blank every password field and every text field
in a row labelled as an ID, key, logon, password, token or PIN (OPTIONS > 3RD PARTY prints the SimBrief user ID,
the SayIntentions API key and the Hoppie logon), and saves a Navigraph login code ("Code: ...") as `0AAA000A`;
`secrets.test.js` fails if a fixture still holds one. Read a new fixture before committing it all the same.

| Fixture | Page |
| --- | --- |
| toperf_airp_ok | T.O PERF, airport accepted ("<OK>" in the AIRP cell), no runway |
| toperf_rwy_ok | T.O PERF computed, runway 04 picked |
| toperf_err | T.O PERF after COMPUTE with no wind ("ERR: NO WIND INPUT") |
| plan | FLT OPS PLAN, empty |
| plan_toast | FLT OPS PLAN with a notie toast up |
| plan_popup | FLT OPS PLAN, FROM VCBI, the FROM RWY options pop-up open, the Navigraph notice |
| menu_turnaround | FLT OPS MENU with the TURNAROUND START confirmation open |
| loadsheet | LOADSHEET, empty (cabin and hold cards, ZFW and FUEL LOAD, tank figures) |
| ldgperf | LDG PERF computed, VCBI 22 |
| enroute | ENROUTE map, map-type panel slid out |
| terml_chart | TERML CHART before a search (icon controls) |
| terml_chart_list | TERML CHART after searching VCBI (the sim's charts) |
| ground | GROUND: passenger doors armed, cargo doors closed, chocks off, pushback arrows |
| ofp | OFP: the SimBrief OFP, a PRE of 3145 lines broken by newlines in its text (no BR) |
| ground_light_on | GROUND with the cargo bay light on |
| aoc | COMPANY COM: the tree, one new message |
| aoc_message | COMPANY COM with that message open |
| maintenance | MAINTENANCE: battery gauges, fluid bars, tyre and brake boxes |
| menu | FLT OPS MENU with the header, PANEL STATES closed |
| opt_callouts | OPTIONS, CALLOUTS (the RA callout switches) |
| opt_volume_control | OPTIONS, VOLUME CONTROL |
| opt_throttle_calibration | OPTIONS, THROTTLE CALIBRATION |
| situations | The captain's Situation Manager, SITUATIONS (system disabled) |
| failures | The captain's Situation Manager, FAILURES |
| card_neighbour | Hand-made: a situation card beside a bottom-bar button |
| opt_sound_cabin_options | OPTIONS, SOUND/CABIN OPTIONS (CABIN LIGHTING with nothing picked) |
| opt_navigraph_login | OPTIONS, 3RD PARTY, LOGIN WITH NAVIGRAPH: the user-code pop-up over the QR code |
| opt_simulation | OPTIONS, SIMULATION: PAUSE AT T.O.D YES with its 30 NM box, time compression AUTO up to 4X |
| opt_navigraph_login (STREAMER MODE on) | `streamer.test.js` turns STREAMER MODE on in the fixture: the IDs hidden |
