# A350 OIS agent tests

Offline tests of `MSFSBlindAssist/Resources/coherent-a350-ois-agent.js` against OIS pages captured live
(iniBuilds A350 1.2.7). CI runs xUnit only, so run these by hand after changing the agent:

```
cd tools/a350-ois-test
npm install
node --test
```

## Re-taking a fixture

With the A350 loaded and the OIS showing the page (the captain's OIS is `VCockpit10`, the first
officer's `VCockpit11`; tell them apart by `ini-efb-a350-cpt`/`-fo` in the page):

```
node ../a350-probe/ceval.mjs VCockpit11 capture.js | sed -E 's/ data-a350-ois-label="[^"]*"//g; s/ class="__glassout_marker_inst"[^>]*>/>/g' > fixtures/<name>.html
```

`capture.js` keeps what is drawn, marks an element drawn with no size `data-vis="0"`, keeps small
hidden lists whole (the runway choices), drops styles except a fixed overlay's (dialogs), a colour
(door indicators, lit buttons) and an opacity (the chock pictures), and the agent's own attributes. The sed drops labels a previous agent kept, so the fixture is the OIS as
first seen.

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
| ground_light_on | GROUND with the cargo bay light on |
