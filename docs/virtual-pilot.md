# VirtualPilot — simulated landings and taxis against the real guidance code

`tools/VirtualPilot` flies landings and taxis through the **real** `TaxiGuidanceManager`
against the pilot's own navdata DB, steering **only by what a blind pilot gets**: the tone's
pan and the spoken callouts. It exists because unit tests only pin bugs we already know and the
DB sweeps check exit *tables*, not what happens second by second as an aircraft rolls through
handoff → miss check → retarget → taxi. Built 2026-09-18 after the YPPH 21 C9 false-miss flight.

Nothing touches the pilot's machine state:

- **Time is simulated.** Every clock read in the guidance code (`TaxiGuidanceManager*.cs`,
  `TaxiSteeringTone`) goes through `Utils/SimClock.UtcNow`; the harness installs
  `SimClock.Override`, so a landing takes ~10 ms, and every cooldown, persistence window and
  grace period still sees simulated seconds.
- **No audio.** `TaxiSteeringTone.Headless` skips the audio device; `ObservedPan` reports what the
  pilot would hear (signed pan, 0 when silent or paused).
- **No log writes.** `Log.Redirect` swallows every line (trace mode captures them for display).

All three hooks are `internal` and must stay **inert in the app** — never set any of them outside
the harness. The `Legacy…ForHarness` switches (`LegacyExitAnchorForHarness`,
`LegacyReversedEntryForHarness`) are the same kind of hook: each restores an earlier rule for A/B
measurement only.

> **On this branch.** The rounds below were measured on the owner's local build before its fixes
> were ported onto main (`feat/taxi-landing-port`); main's own designs replaced some of what they
> describe (the off-route runway warning now uses main's `RunwayIncursionWatch`; exit measurement is
> main's `ExitBranch`). Two things a fresh checkout needs: an OSM snapshot (`fetchosm`, or point
> `VP_OSMDIR` at an existing `runs/_osm`) — without one the named-holding-point (`HP`) departures are
> not flown at all and the graph has no OSM taxiway names, so the run is not comparable with one that
> had it; and the `frozen/` before-build project described under A/B is not in the repo — build the
> harness against an older checkout instead (`-p:MainApp=true` compiles the harness against plain
> main before the port).

## Running

```
dotnet build tools/VirtualPilot -c Release -p:Platform=x64
tools/VirtualPilot/bin/x64/Release/net10.0-windows/VirtualPilot.exe <mode> [options]
```

| mode | what it flies |
|---|---|
| `landings` | every listed exit of every runway: once **following the tone**, once **rolling straight past** (skipped for End exits / exits < 600 m from the runway end) |
| `taxi` | per airport, K plans: gate → runway (lineup) and landing-exit vacate point → gate; each flown on the **shortest path** and again on an **ATC-style clearance** (the shortest route's taxiway names in order) |
| `wrongturn` | follows a gate → runway route, then deliberately turns into a branch that leads to a runway hold line not on the route; the app must say "off route" first |
| `missturn` | follows a route to the junction before a hold on the way, where the taxiway it came in on carries straight on off the route, and rolls straight on (once at 10 kt, once at 20 kt): the turn onto the hold's connector missed. Only where the hold node lies 25 to 150 m to the side of that taxiway. The app must answer with the off-route recalculation, not with a hold-short stop on the taxiway. `runall.sh` flies it with 40 plans per airport |
| `rwystop` | a route loaded while stopped on a runway, midway across the widest gap between two exits, to a random gate, with and without a clearance. `VP_LEGACY_RWYSTART=1` and `VP_LEGACY_TURNSIDE=1` restore the older route start and opening turn for an A/B |
| `missedexit` | per runway, two flights where the pilot rolled past the planned exit and vacated at the next one, then loaded the gate route with the clearance given for the planned exit. The flight that prompted it (VTBS 02R, 2026-11-18) vacated one exit EARLY, which this mode does not fly; that one is replayed with `--trace ICAO:from:…`. Where the two exits are 300 m or more apart, the same clearance is also loaded while still on the runway, 150 m past the planned exit (`MISSEDEXIT_RWY`) |
| `all` | landings + taxi |
| `departures` | per runway end: named (OSM) holding-point departures (`HP`, flown free and on a clearance), intersection departures (`IX`), full-length backtracks where the runway list would say "(backtrack required)" (`BT`); per airport: gate → gate taxis across the field (`G2G`) and ATC clearances with explicit "hold short of runway X" instructions (`HSCLR`) — each also voiced as a written and a spoken-NATO clearance and fed to the SayIntentions import's own parser (`CLR_*`) |
| `turnpicks` | per taxi plan, the clearance a controller would give, then, for every row and both sides, the Turn pick a pilot could set on that row: each is loaded as the planner loads it and what became of it is recorded (already made, charted, off-chart leg, or refused and why); a pick that changes the route is flown following the tone, and flown once more with the pilot taking the charted way at the junction instead. `turn_report.py runs/NAME` summarises it |
| `legends` | Progressive Taxi legs that end "hold short of taxiway B", loaded as the form loads them (the junction first by taxi distance, wingspan 36 m). Per airport, up to K pairs (A, B), in a seeded order: "via A" from a node on A 250 to 600 m before the nearest junction, facing it; and, where A meets B on both sides of a T at M where taxiway Z joins A, "via Z, A" from Z 150 to 400 m before M with the Turn on A set Left, then Right. The pilot brakes on any line ending "Stop."; the flight ends at a standstill after the leg has ended. Where the leg ended and where the aircraft stood are measured by the harness, along the route and on over the graph to the junction the app holds short of (its destination node, read by reflection). One row per leg in `<out>.legends.csv` (with the countdown lines heard, the last segment's length and taxiway, and the heading against it when the leg ended); the share ended and the standstill distances are printed and written into the findings footer |
| `crossthenhold` | Progressive Taxi legs "cross runway R, hold short of taxiway B" (the KCLE shape: P across 24L, then L): per airport, up to K pairs (A, B) in a seeded order where the way along A to the A/B junction the form picks crosses one runway R whose far pavement edge is 150 m or less before the junction. The start is a node on A 150 to 1,200 m before the junction, facing it, whose route as the form loads it ("via A", no destination, hold short of taxiway B, wingspan 36 m) crosses R first; the leg is flown with that crossing cleared (`clearedCrossings: [R]`) and measured as a `legends` leg, plus: HoldShort entered at R's stop, the "Crossing runway R" line, the "… cleared to cross." countdown, and where the stop for B lies against R (metres past its far edge, clear of every runway's margin, its 25 m approach clear). One row per leg in `<out>.crossthenhold.csv`; the summary is printed and written into the findings footer |
| `fetchosm` | downloads the raw Overpass answer for each airport into `runs/_osm/ICAO.json` (one polite request at a time — Overpass rate-limits hard, expect ~1 airport a minute), and the X-Plane Gateway's apt.dat for it into `runs/_osm/ICAO.apt.dat`, the app's second online source. Every mode then builds the app's OWN online-augmented graph from that snapshot, the two sources in the app's order (names, holding points); a snapshot without the `.apt.dat` flies OSM alone, `VP_NOOSM=1` flies plain navdata, `VP_OSMDIR` points elsewhere. The snapshot pins the data so a before/after run measures the code, not OSM churn |

Options: `--airports N` (busiest by taxi-path count, default 100), `--icao A,B`, `--shard i/n`
(run n processes in parallel — the hooks are process-global, so parallelise by process, never by
thread), `--taxi-per-airport K`, `--out file`, `--clear-first-crossing` (`taxi` and `turnpicks`: every
flight is flown again with the first runway its route crosses cleared, below), `--crossing-keys` (`taxi` and
`turnpicks`: every flight is flown three times more, pressing Continue or the Hold short key at its first crossing,
below, "Crossing keys"), and
`--trace ICAO:RWY:EXIT[:straight]` / `--trace ICAO:taxi:N[:clearance]` for one scenario frame by
frame (speech, state changes, routes, the harness's view of the runway frame).

`--trace ICAO:from:<lat>,<lon>,<heading>:<text in the gate's name>[:A,B,C]` loads a route where a real
flight loaded it, with the clearance it had, taken from the taxi and router logs.

**Which navdata.** `--sim fs2020|fs2024` (or `VP_SIM`) picks the simulator whose database is flown,
found where the app finds it (`DatabasePathResolver`); `--db file` (or `VP_DB`) names a database by
path. With neither, the MSFS 2020 database is flown when it is installed (the rounds below up to the
fifth flew it) and the MSFS 2024 one when it is not. The run prints the choice and writes it into the
findings header. **A before/after pair only compares when both runs fly the same database**: the
airports, the plans drawn and every finding differ between the two. The two simulators draw the same
airport differently, so measure a routing or guidance change on both.

Every run writes `<out>` (findings) and `<out>.results.txt` (one outcome line per landing).
`tools/VirtualPilot/compare.py BEFORE.results.txt AFTER.results.txt` diffs two runs landing by
landing — **run it before and after any change to rollout / exit logic; "no degrades" means the
WORSE column is explained case by case.** Run outputs go under `tools/VirtualPilot/runs/` (ignored).

Trace forms: `ICAO:RWY:EXIT[:straight]` (a landing), `ICAO:taxi:N[:clearance]` (the Nth taxi
plan), `ICAO:dep:<text>` / `ICAO:any:<text>` (every departures / every taxi-or-departures scenario
whose description contains the text — e.g. `KORD:dep:IX Y4 L 5`), `ICAO:turn:<text>` (the same for
`turnpicks`, e.g. `LTFM:turn:turn Right onto N1`), `ICAO:missturn:<text>` (the same for `missturn`,
e.g. `KDTW:missturn:before the hold n2753`, with `--taxi-per-airport 40` as the run flies it), and
`ICAO:pick:<lat>,<lon>,<heading>:<runway>:<clearance>[:miss]`: one clearance from a position to a
runway, a Turn pick written `=L` or `=R` after its taxiway, `:miss` to take the charted way at the
junction instead. The KBOS incident of 2026-11-14 is
`KBOS:pick:42.363953,-71.006981,170:33L:H,C=L`. ATC's "hold short of runway X" after a taxiway is
written `@X` (`OMDB:pick:25.251047,55.363293,120:30R:K,K7@30L`): it is loaded as that row's
hold-short pick, as the SayIntentions import seats it, and the clearance is voiced through the
import's own parser. `ICAO:legend:<text>` flies every `legends` leg of the
airport as the run does (a stop can insert a node into the shared graph, so a leg depends on the ones
before it) and prints the matching ones frame by frame, with the graph two edges around the stop
(e.g. `KDTW:legend:turn Right onto Y hold short of V`); `ICAO:crosshold:<text>` does the same for
`crossthenhold` (e.g. `KCLE:crosshold:via S cross runway 06R hold short of L`). With
`--clear-first-crossing`, `ICAO:taxi:N` flies the plan with nothing cleared first, untraced, then traces it
cleared; in `ICAO:any:` and `ICAO:turn:` a cleared flight's description ends "[first crossing cleared]" and an
excursion flight's pick reads "[leaves the route before the turn]". With `--crossing-keys` a key flight's description
ends "[Continue after the first countdown call]", "[Hold short key after the first cleared-to-cross call]" or "[Hold
short key just past the line]" (the last two after "[first crossing cleared]"), and its trace prints the press ("KEY
EARLY: …") and its `KEYS` line. `VP_SHOWCLR=1` prints the voiced
clearances, `VP_STUBSTATS=1` the destination entry-stub length and hold label of every departure.

### A/B against a frozen build

Comparing two runs only means something when both fly the SAME scenarios, and a harness change
(a new check, a different random draw) changes the scenarios. So the "before" is the current
harness compiled against the OLD app: freeze the harness output folder before touching the app,
then build `tools/VirtualPilot/frozen` against it (its csproj header has the four steps;
`OLDAPP` compiles out harness calls to app APIs the old build lacks) and run
`VP_EXE=tools/VirtualPilot/frozen/out/VirtualPilot.exe tools/VirtualPilot/runall.sh BEFORE`.
Scenario keys can still drift when the app's own output feeds scenario selection (the
intersection list's order picks which intersections are flown) — `compare_codes.py` prints the
"only before / only after" counts; read those rows as new scenarios, not regressions. A Turn pick
whose outcome changes is flown under another kind (`TURN_NO_LEAD` is not flown at all,
`TURN_CHARTED` and `TURN_MISS` are), so its findings land in those rows: count a code's finding
lines in both runs as well.

Each taxi scenario also writes a `ROUTE|kind|icao|key|metres|taxiway names|node hash` line beside
its `CODES` line, and `py tools/VirtualPilot/compare_routes.py runs/BEFORE runs/AFTER [n]` lists
every route that changed, with its length and taxiway names before and after and the findings
that went and came on it. A run made before the ROUTE line existed has none to compare.

The usual loop: `tools/VirtualPilot/runall.sh NAME` (landings + taxis + wrong turns, 100 airports,
6 processes, ~10 min — it BUILDS the harness first, because the harness is not in the solution and
a solution build leaves a stale binary), then
`py tools/VirtualPilot/compare_codes.py runs/BEFORE runs/AFTER [n]`, which lists per scenario
every finding code that disappeared or appeared. Trace one scenario with
`--trace ICAO:RWY:n<nodeId>` (exit names repeat — KMSP 04 has three B junctions) or
`--trace ICAO:taxi:N --taxi-per-airport 10` (N as numbered by the run); `VP_ALLLOG=1` adds the
app's log lines, and the landing trace prints the exit path in the runway frame.

## The pilot model

`PilotModel`: 0.3 s reaction lag; yaw rate from the pan, limited by speed (0.15 g lateral, max
15°/s) and yaw acceleration; holds heading when the tone is silent; 15 kt taxi, 9 kt on a hard
pan, 10 kt after "Slow…". In **lineup** (precision tone: 1° activation / 15° full pan) it reads the
pan as an error estimate and corrects proportionally — the turn-hard-on-any-pan taxi model
limit-cycles against a precision tone, which is a harness artefact, not an app bug. It stops at
hold shorts and presses Continue after 2 s, stops on "Lined up", and follows the rollout tone at a
decelerating speed (Normal/End exits 18 kt at the junction, High-speed 35 kt).

## What it checks, and how much to trust each

| finding | meaning | confidence |
|---|---|---|
| `LANDING_FALSE_MISS` | "Missed X" / retarget while the pilot followed the tone. The detail says how far the pilot was from the route and where the route leaves the centreline | high when on-route ≤ 5 m |
| `STRAIGHT_MISS_NOT_CALLED` / `_LATE` / `STRAIGHT_ROLL_ARRIVED` | rolling straight past is not called, called > 200 m late, or reported as vacated | high |
| `LANDING_NOT_ARRIVED`, `LANDING_RAN_OFF_END` | never reached an arrival | medium — check the trace |
| `EXIT_ROUTE_HAIRPIN` | the route off the runway turns > 120° within 150 m of the handoff: an exit listed for this direction that needs a U-turn (KPHL 35 E4) | high |
| `HANDOFF_ROUTE_STARTS_BACKWARDS` | the handoff route's first leg points > 120° off the heading | high |
| `UTURN_SPOKEN_ON_RUNWAY` / `UTURN_ON_FLAGGED_TURNBACK_EXIT` | "Make a U-turn" spoken on the runway — on an unlabelled exit (a bug) or one the list labels "(sharp turn back)" (warned) | high |
| `TOUCHDOWN_NAMES_OTHER_TAXIWAY` | at touchdown the first guidance line names a taxiway that is not the chosen exit ("Taxiway A. Steering guidance active." before "…exit taxiway C9") | high (every landing) |
| `OFF_ROUTE_WARNING_ON_ROUTE` | "Warning: approaching runway X, off route" with the aircraft ≤ 15 m from its route | high |
| `HOLD_LINE_CALLED_BEHIND` | "Passing the X hold line." about a line every node of which within 45 m is behind the pilot, or "Crossing X." about one with no runway named X entered in the next 150 m | high for "Passing"; "Crossing" over-reports a route that turns round and crosses through the line, the U-turn taking more than 150 m (KORD 10L P4) |
| `CROSSING_CALLED_NO_RUNWAY` | "Crossing X." and no runway pavement in the next 200 m of travel | medium: any runway clears it, so a call naming the wrong runway (LTFM "17R at B9B" before 17L) is not caught |
| `HOLD_LINE_CALL_REPEATED` | the same hold-line call twice within 150 m with no hold stop between | medium: a route through the same line twice by design repeats it (OTHH "P6, P7, P6") |
| `RUNWAY_CROSSED_UNANNOUNCED` | a runway entered on the way (not the destination) with no "Crossing" naming it from 150 m before the entry to 100 m after | information: hundreds on every build, from routes along a runway with no stop and calls spoken earlier; read the A/B difference |
| `WRONG_TURN_SILENT` | a deliberate wrong turn reached a runway hold line with no warning | high |
| `MISSED_TURN_STOPPED_BEHIND_LINE` | `missturn`: stopped for the hold short more than 26 m from its node while more than 10 m farther from the hold's runway than the node is (the harness's own sum against `RunwayIncursionWatch.GuardedRunway`): a stop on the taxiway the pilot never left | high |
| `MISSED_TURN_STOPPED_AT_LINE` / `_HELD_IN_CIRCLE` / `_REROUTED` / `_NOTHING` | `missturn`: stopped beside the node but as near the runway as it is, or at a hold that guards no runway; stopped inside the 25 m circle; answered by the off-route recalculation ("Route changed", or "Off route." with why the route was kept); neither within 300 m | information |
| `HOLD_STOP_ON_RUNWAY_PAVEMENT` | the pilot stopped for a hold short while standing on runway pavement (`RunwayShape`) | high |
| `RUNWAY_ENTERED_UNHELD` / `DEST_…` | runway pavement entered > 250 m after the last hold (edge flicker and lineup onto the destination runway excluded) | medium |
| `CLEARANCE_ROUTE_DETOUR` | the constrained router over the shortest route's own taxiway names is 1.3× + 200 m longer — usually unnamed connectors a clearance cannot name vs the `FindRunwayBridge` 200 m cap (PGUA "via A": 450 m of unnamed connector) | medium, often by design |
| `TAXI_RECALC_WHILE_FOLLOWING` | "Route changed" while following the tone | medium |
| `TAXI_OFF_PAVEMENT`, `LANDING_OFF_PAVEMENT` | > 12 m beyond the nearest taxi edge's half-width | **low** — the graph has no apron polygons, so open aprons read as grass |
| `LINEUP_WRONG_PLACE` | "Lined up" > 120 m along the runway from where the pilot selected (the intersection's listed distance; the full-length lineup point) | medium — a precise lineup turn at 6 kt takes 30-60 m |
| `HOLD_NAMED_OTHER_RUNWAY` / `HOLD_FAR_BEFORE_RUNWAY` / `HOLD_WITHOUT_RUNWAY` | the stop named a runway other than the one then entered; it was > 220 m before it; or a runway stop was never followed by any runway | high |
| `DEST_HOLD_FAR_FROM_ENTRY` | the destination hold is > 600 m of taxiing before the runway entry | high |
| `HOLDPICK_NOT_SET` | an explicit "hold short of runway X" the app could not set (it says so) | medium — the harness derives the picks from the SHORTEST route, the clearance route can differ |
| `HP_HELD_AWAY_FROM_POINT` / `HP_HOLD_NOT_NAMED` / `HP_ROUTE_MISSES_POINT[_WARNED]` | a named-holding-point departure held > 30 m from the painted line picked / held there without naming it / the route never passes it (`_WARNED`: the app said so) | medium — hold placement stays navdata-authoritative by design, and a painted line BEYOND the navdata hold is correctly never reached |
| `CLR_{WRITTEN,SPOKEN}_{TAXIWAYS,HOLDS,RUNWAY}_WRONG` | the voiced clearance parsed to different taxiways / hold shorts / runway | high for spellable names; navdata names ATC would never say ("N-North") are noise |
| `LINEUP_LEAVES_MAPPED_PAVEMENT`, `LINEUP_NEVER_ALIGNED` | the lineup intercept leaves every mapped edge; never "Lined up" in 400 m | **low** — many sceneries stop the entry taxiway at the hold line (KIAH SA), so the ground between hold line and runway is unmapped |
| `TURN_PICK_SILENT`, `TURN_PICK_BROKE_THE_LOAD` | a Turn pick was neither applied nor said; a clearance that loads without the pick fails with it | high |
| `TURN_HONORED_WRONG_SIDE`, `TURN_MET_WRONG_SIDE`, `TURN_BOTH_SIDES_MET` | the route turns the other way onto the row's taxiway than the pick it honored or called already made (the harness's own sum of heading changes along that taxiway, a contradiction of more than 25°); both sides read as already made | high for the first two; the third is a straight-on join onto a taxiway that then bends both ways |
| `TURN_TRAVELS_THE_OTHER_ARM`, `TURN_CROSSES_THE_OTHER_ARM` | a route that honored a pick later runs along, or passes a node of, the arm of that taxiway the pick turned away from (worked out by the harness from the graph; the destination node does not count) | high — refused by the app since the sixth round, so either is a regression |
| `OFFCHART_NOTE_MISSING`, `TURN_CHARTED_NOT_CONFIRMED` | an off-chart leg, or a charted pick that changed the route, with no word of it at load | high |
| `TURN_PICK_LOST_WHILE_FOLLOWING` | "was missed" or "could not be kept" while the pilot followed the tone | medium — exit→gate plans start facing away from their route, and a first-row pick at a stand is judged from the harness's arbitrary start heading |
| `OFFCHART_LEG_NOT_TAXIED`, `OFFCHART_LEG_NOT_ANNOUNCED`, `OFFCHART_LEG_OFF_PAVEMENT` | arrived without coming within 25 m of the middle of the leg; a leg of 40 m or more that is not the route's first segment taxied with no "Off-chart leg" sentence; more than 12 m beyond the nearest taxi edge while on the leg | medium; the last is **low**, as `TAXI_OFF_PAVEMENT` (the app's own pavement map has the aprons this index lacks) |
| `HOLD_COUNTDOWN_LATE` / `HOLD_COUNTDOWN_MISSING` | the first hold-short countdown line came more than 30 m (along the route) after the outer tier (15 s at the speed then, 300 to 600 ft) was crossed while taxiing, or no countdown line came before the stop at all. The harness measures the distance itself, through every segment to the next segment ending at a hold short (`CountdownTiming`). In `legends` and `crossthenhold` the next stop can also be the leg's end (judged when the leg ends) or a stop cleared across (judged once the cursor is past it), each by its own words ("Hold at", "Route ends", "Runway 04L at C in") | high |
| `HOLD_CALLED_PASSING_BEFORE_STOP` | "Passing the … hold line." or "Crossing runway …" said within 80 m before stopping at a line the countdown names the same way | high for the next stop's own node; the remaining cases are a second hold node of the same line passed before the stop |
| `TURN_NOW_WITHOUT_ADVANCE` / `TURN_ADVANCE_OTHER_WAY` | a "… now" with no advance notice heard since the previous one though more than 150 m were taxied; an advance notice whose side (left, right, "continue") is not the side of the "… now" that follows it (`TurnTiming`) | high / medium: two junctions close together, and turns near the 20 degree "slight" threshold, read as the other side |
| `TURN_MISS_SAID_MISSED` / `_SAID_NOT_KEPT` / `_NOT_SAID` / `_NO_REROUTE` / `_NOT_FLOWN` | the miss flight: the route change said so; changed with no word about the pick; never changed in 150 m (the pilot then follows the tone again); the pilot never came within 5 m of the junction | information; `_NOT_SAID` is the known limit (a pick counts as made once the segment index passes its join) |
| `LEGEND_PICK_INDEX`, `LEGEND_OFFCHART_SPLIT`, `LEGEND_ENDS_OFFCHART` | a `legends` route as loaded: a Turn pick's segment indices no longer name its departure and join nodes; two off-chart segments in a row (the stop split the leg); the route ends on an off-chart leg. A leg that ends on a kept pick's own link (no node before its turn could stop it, TWH-2) has no segment from the join and is not reported, nor is an off-chart leg that ends the route with its pick kept, so announced | high |
| `LEGEND_DROPPED_PICK_SPOKEN` | a `legends` leg ends before its picked turn (the pick is off the route, refused by nothing: `SILENT`), yet the load said "… onto taxiway A, as picked." or an off-chart note for it | high: the pilot would hear about a turn never made |
| `LEGEND_PICK_NOT_FIRST_JUNCTION`, `LEG_PASSES_B_JUNCTION`, `LEG_PICK_LINK_CROSSES_B`, `LEG_PASSES_B_IN_RUNWAY_MARGIN` | the route passes a node on B before its stop: between a pick that changed the route and the stop; without such a pick; on the charted link to the picked turn, before it; at a node inside a runway's clear margin, which the app skips by design (the leg then holds short of a later meeting) | high. Judged along the route, not by a walk of the graph, which at a Y-shaped junction goes back up the branch the route left (EGLL A9E); every node on B is classified, each with its own finding, so an informational one cannot hide a later breach |
| `LEG_NOT_ENDED`, `LEG_AT_OR_PAST_JUNCTION`, `LEG_ROUTE_REFUSED`, `LEG_JUNCTION_NOT_FOUND`, `LEG_APP_JUNCTION_UNREAD` | no `ProgressiveHold` before the time budget, or 15 s stopped after "Stop." with nothing held; ended or stood at or past the junction; the load refused; no node on B within 400 m of the stop; the app's junction (`_destinationNodeId`) could not be read, so the harness's own pick was measured to | medium: the pilot model brakes on every "Stop.", also a runway hold's |
| `LEG_ENDED_ON_RUNWAY` | a `legends` leg ended, or its countdown said "Slow down" or "Stop", with the aircraft on runway pavement (`RouteRunwayCrossings.RunwayUnder`, no margin) | high: a leg never ends on a runway, and BA never asks for a stop on one (spec 8.5, 8.8) |
| `XC_HELD_AT_CLEARED_CROSSING`, `XC_NOT_APPLIED` | a flight with a crossing cleared (`--clear-first-crossing`, `crossthenhold`) entered HoldShort at that crossing's stop, which no longer holds for any runway; the load cleared nothing | high |
| `XC_LINE_NOT_SAID`, `XC_LINE_SAID_TWICE`, `XC_STOP_NOT_REACHED` | the aircraft came within 30 m of the cleared stop and no "Crossing runway R" was said within 100 m of it once guidance was past the stop's segment; more than one was; the flight never came within 30 m of it (a route change took it elsewhere) | high for the first two |
| `XC_APPROACH_NOT_SAID` | a fully cleared stop's line came with no "… cleared to cross." countdown before it, not counting a line passed as guidance started | medium: a stop segment shorter than the 25 m capture radius can be passed before any tier is due |
| `XC_CROSSING_SAID_BEFORE_ITS_SEGMENT` | "Crossing runway R" said while guidance was still on a segment before the stop's: the incursion watch's own line for an on-route hold node within 40 m that is not yet the countdown's, which stage 1's build already speaks before an ordinary hold short | information |
| `XC_OTHER_STOPS_DIFFER` | the hold-short stops other than the cleared one are not the same, in the same order, as the same flight's with nothing cleared | high |
| `XC_RECALC_KEPT`, `XC_KEPT_TAIL_KEPT`, `XC_RECALC_STOP_BACK`, `XC_RECALC_CLEARED_ELSEWHERE`, `XC_RECALC_STOP_MOVED_SAID` / `_UNSAID`, `XC_RECALC_ENTRY_STOP`, `XC_RECALC_CROSSED_WITHOUT_STOP`, `XC_RECALC_NOT_CROSSED` | a route change before the cleared stop: the new route clears it again at the same node (with a Turn pick's kept tail: `KEPT_TAIL`); a stop again at that node; the runway cleared at another crossing; a crossing of the runway held at another node, said ("Runway R is a stop again.") or not; the new route enters the runway instead, with a stop; crosses it with no stop (which the route change must say, HLD-17); no longer meets it | `STOP_BACK`, `CLEARED_ELSEWHERE` and `_UNSAID` high; the rest information |
| `XC_EXCURSION_NOT_FLOWN`, `XC_EXCURSION_NO_REROUTE`, `XC_EXCURSION_PICK_LOST` | the excursion flight (below): the pilot never reached the junction it leaves from; left the route and guidance never changed it; the route change said the pick was missed or could not be kept | medium: the excursion is the harness's construction (not the pick outcome DETOUR, the app's "long detour" refusal, whose CODES lines are `TURN_DETOUR`) |
| `XC_B_STOP_BEFORE_RUNWAY`, `XC_B_STOP_IN_RUNWAY_MARGIN` | `crossthenhold`: the stop for B lies before R (the route never reaches R's pavement), or inside a runway's clear margin | high |
| `KEY_EARLY_NOT_CLEARED`, `KEY_EARLY_HELD_AT_STOP`, `KEY_EARLY_LINE_NOT_SAID`, `KEY_EARLY_LINE_SAID_TWICE` | `--crossing-keys`, Continue right after the first countdown call: the press did not answer "Cleared across runway R. No stop."; HoldShort was entered at that stop anyway; no "Crossing runway R" within 100 m of the stop once guidance was past its segment, or more than one | high |
| `KEY_HOLD_NOT_RESTORED`, `KEY_HOLD_NOT_HELD_AT_STOP`, `KEY_HOLD_ON_RUNWAY_PAVEMENT` | the crossing cleared, the Hold short key right after the first "… cleared to cross.": it did not answer "Holding short of …"; the stop put back was never held; held there with the aircraft on runway pavement (`RunwayUnder`, no margin, any frame of that hold) | high |
| `KEY_PAST_NOT_SAID`, `KEY_PAST_HELD_AT_STOP` | the crossing cleared, the Hold short key just past the line: it did not answer "Past the hold line of …"; HoldShort was entered at that stop after it | high |
| `KEY_EARLY_NOT_PRESSED`, `KEY_HOLD_NOT_PRESSED`, `KEY_PAST_NOT_PRESSED` | the press never came: no countdown call for the stop heard while taxiing on its segment, no "cleared to cross" for it, never more than 10 m past it; nothing else of that flight is judged | information: the stop entered in the frame of its first call, a line passed as guidance started, or a route change before the stop |

## Findings of the first run (2026-09-18, 100 busiest fs2020 airports)

4,857 landing runs, 1,960 taxis, 409 wrong turns.

**Fixed:**

1. **Centreline-stub false misses** — many sceneries draw the exit taxiway down the runway
   centreline before it turns off (KORD 27L M 80 m, ZSPD 35R B7 99 m, EGLL 27L N7 40 m after a
   3 m jog). The route tone correctly said "continue"; the miss check at junction + 100 ft said
   "Missed". `LandingExitPathFollow.OnAxisRunMetres` now extends both miss detectors by that run
   (hairpins and paths that come back across the centreline excluded). **996 → 194** false misses,
   "never arrived" 227 → 48, off-pavement 671 → 466, ran off the end 226 → 50. Per-landing diff:
   ~1,550 better; 5 changed for the worse, all on broken exit geometry (KSDF 29 F, KCVG 18C K,
   PGUA 24L B: false "missed" → a different false "missed"; KLAX 25L A7 14 m off pavement).
   The trade, accepted: rolling straight past a stub exit is called "missed" later (median 59 m,
   p90 107 m, max 201 m), because until the drawn exit leaves the centreline "rolled past" and
   "following the exit" are the same position; 101 of those retargets pick a later exit (mostly
   because the old pick was 5–75 m ahead).
2. **False "approaching runway X, off route"** — the on-route set was the REMAINING route only,
   so the hold line just crossed dropped out the moment it was passed; hold lines beside the route
   (entry stubs off a parallel taxiway) tripped it too. Now: the whole route counts, a line behind
   the aircraft is not "approaching", and a line beside the route is silent while the aircraft is
   within 8 m of its route and heading along it (±30°). **1,121 → 239** of 1,960 taxis. Wrong-turn
   A/B (408 deliberate wrong turns): 0 warnings lost, 2 gained.

## Second round (2026-09-18, same 100 airports)

Every change below was A/B-measured with `compare_codes.py`; against the start of the round,
nothing new appears except what is listed as accepted.

1. **Touchdown callout named the wrong taxiway** on every landing ("Taxiway A. Steering guidance
   active." before "…exit taxiway C9") — `StartGuidance(announceStart: false)` at touchdown.
2. **U-turns on the runway at the handoff: 41 → 1** (KPHL 27R K4, a data mismatch). The
   landing-exit anchor retries a route that starts backwards, points back on its first leg or
   hairpins within 150 m (`LoadRoute`, see [RTE-32] in `docs/invariants/taxi-routing.md` for every gate and why). Exits whose own path
   turns back are labelled "(sharp turn back)" in the list, warned at touchdown and not picked by
   default (`PathTurnsBack`). Route hairpins 272 → 106.
3. **Destination hold on runway pavement** (EFHK 22R WD, KSLC 14): the stop moves back to the
   last node clear of every runway. 6 → 0.
4. **False misses 194 → 66**: the stub allowance no longer drops for an exit that loops back once
   well out on the apron (44), plus the retry above.
5. **False "off route" 239 → 115**: the on-route tracking test looks three segments back (a pilot
   stops short of a hold line and Continue moves the index past it).
6. Harness: "ran off the end" only counts inside the runway strip (End exits run on beside it).

**Accepted, recorded:**

- 260 U-turns on exits labelled "(sharp turn back)": the pilot was told at touchdown, and the
  alternatives (loops through other taxiways that hairpin 50 m later, or an 860 m detour at
  ZGGG 20L A) are worse.
- Offset stubs (OOMS 26R A, 7 m off the centreline for 87 m): widening the 6 m band trades each
  fix for a straight roll called "missed" > 200 m late. Stubs > 200 m (KLAX 25L A7) hit the cap.
- ZGGG 20L B: the retry lets the 300 ft early handoff go ahead and the pilot misses the curve at
  48 kt — disabling the retry there costs two other exits.
- ZSSS 18L (island start, uncharted crossing, B drawn over the runway) and KPHX 25R E12 (E11/E10
  drawn 260 m along 25R): the route itself runs on runway pavement.
- A geometric "hold line further from the runway than the aircraft" silence for off-route warnings
  fixed 19 and silenced two real wrong turns at RKTP 03R — removed, do not rebuild.

**Open (not fixed — each needs a decision):** the remaining 66 false misses and 115 off-route
warnings, 71 recalcs while following (mostly a harness artefact: exit→gate taxis start facing away
from their route), lineup from a set-back hold cutting across unmapped ground, clearance detours.

## Third round — taxi and departures (2026-09-18, same 100 airports)

New scenario families (`departures` mode) and the OSM snapshot. Old build vs new, frozen-build A/B,
13,774 scenarios on navdata (landings, taxis, wrong turns, departures) plus the named-holding-point
runs at the 20 airports with an OSM snapshot. Landings and wrong-turn warnings are unchanged
(25 silent wrong turns before and after, all pre-existing).

**Fixed:**

1. **Lineup from a set-back hold cut across the grass.** After Continue the intercept law steered
   for the centreline straight from the hold line (KORD 04R Y4: 95 m out, "Turn left" — 137° — at
   the line, "Lined up" 500 m past the intersection picked). The tone now follows the cut-off entry
   stub onto the pavement first (`TaxiRoute.RunwayEntryStub`, kept only when it reaches the
   destination runway). Grass lineups **2,104 → 238**, wrong-place lineups **801 → 212**,
   never-aligned 52 → 30.
2. **The destination hold was the latest hold anywhere on the route** — another runway's hold or this
   runway's far end: WIII 06 at "runway 07L at N8M" 612-868 m out, RPLL 31 2.5 km out, OTBH 16L 6 km
   out. Now only a hold within 200 m of the entry, or within 1,000 m naming this runway, counts; else
   the synthetic back-off, which must now be 10 m clear of every runway edge (VIDP 27 via D6 stopped
   on the edge). Phantom holds 207 → 107.
3. **Holds that named the wrong runway** 32 → 2: the entry stub can roll onto an intersecting runway
   before the destination (KSLC 32/35, KCOS, KSTL, KDTW) — now named at the stop ("Hold short of
   Runway 32 and runway 35") — and the countdown used the scenery's reciprocal designator
   ("Hold short runway 22L at Y4" then "Stop. Hold short of Runway 04R").
4. **Intersection distances were overstated on angled entries** (EGLL 09L AB11, KORD 04R Y4): now
   quoted from where the taxiway reaches the pavement on the aircraft's side, forward only.
5. **False "off route" warnings** 228 → 138: the on-route tolerance is 12 m, not 8 (a pilot rounding a
   junction 8-9 m wide — KORD G/G2, RPLL F5); wrong-turn A/B 391/391 still warned.
6. **Named holding points:** OSM labels that describe a hold kind ("28C APCH", "GP HOLD LINE", "ILS")
   are no longer offered as departure entries (KORD "28R APCH" stopped the pilot on runway 04R), and
   a holding point the route cannot be pinned through is now SPOKEN instead of log-only (EDDF 18 via P8).
7. Harness: the spoken-clearance check (the SayIntentions parser recovers taxiways, hold shorts and
   runway from written and NATO-spoken clearances at every airport — it does, apart from water runways
   "04W" and navdata names no controller says); a crossing through a runway intersection is one
   crossing, not two entries (CYYC C over 35L/11).

**Measured and rejected:** handing the lineup over to the centreline intercept 12 m BEFORE the
pavement edge (to shorten the lineup turn on narrow runways — YPDN 36 overshoots a 30 m runway by
2 m): 59 new findings for 24 fixed, it cuts the corner on angled entries.

**Open after this round:** see the fourth round below.

## Fourth round — the open list (2026-09-18 evening, same 100 airports)

A/B against a frozen build of the start of the round (`tools/VirtualPilot/frozen`, which now copies
the frozen dependencies itself and only compiles `OLDAPP` out with `-p:OldApp=true`).

**Harness corrections first** — each changed what a finding means, not the app:

- Taxi-planner routes are now loaded with `announceSummary: true`, as the form does. That flag also
  runs the route-start checks, so KVAD Parking 45 (a stand joined to the network only across runway
  19R) is correctly REFUSED by the app ("reaching it means crossing runway 19R where there is no
  taxiway data") — it was an artefact, not an unheld runway entry. Refusals are recorded as
  `TAXI_ROUTE_REFUSED` (31: honest island messages).
- A wrong turn the app answers by RE-ROUTING through the line is scored on the new route:
  `WRONG_TURN_REROUTED_NO_RUNWAY` (no runway beyond the line) / `_HELD` / `_UNHELD` (the only bad one).
  Scenarios whose "junction" lies beyond the lineup are skipped (KMCI).
- Rolling from one runway's pavement straight onto another's is one crossing (CYYC C over 35L/11).

**Fixed (125 findings, 0 new on navdata; on the full OSM set — 98 airports, 3,457 departures incl.
692 named-holding-point runs — 49 fixed / 12 changed, every changed one a variant of the same
scenario's old finding or a safer route: OIII 11L via B8 no longer drives onto 11L and back out to
hold; KPDX 03 via 4E no longer takes a 4.7 km detour to pass the painted point, and says so):**

1. **Clearances that repeat a taxiway** — both readings built, the one that follows the clearance
   taken unless it loops (see [RTE-33] in `docs/invariants/taxi-routing.md`). OTBH 16L "A, C, A" 9.1 → 2.9 km, ZPPP 04L "A, B, A"
   6.2 → 3.9 km, EDDF/LEMD clearances the old router abandoned for shortest path now followed.
2. **"Crossing runway X" for a hold line the route never goes past onto a runway** → "Passing the
   runway X hold line" (KDTW F, an approach-area line).
3. A holding-point route's summary read "Route to  via …" (the pinned route lost its name).
4. Water runways: "runway 04W" parsed as 04.

**Measured and rejected** (details in [HLD-15], `docs/invariants/runway-holds.md`): a dead-ahead exception to the off-route tracking
silence (204, then 148 false warnings), a cost on taxiing along runways, pinning intersection
departures through the picked taxiway, stopping at a picked painted line behind the navdata hold.

**Still open — each is the scenery or the pilot model, not a code path left unmeasured:**

- 10 silent wrong turns: a missed turn and a wide turn are the same geometry when the hold line is
  20-30 m past the junction (YPDN A2/A3 ×3, OETF, KEWR, LEMD, KATL, KPHL, KFBG, KMCI).
- Stops on runway pavement (8) and unheld entries (9) where the ROUTE drives along a runway (KLAX
  H8/H9 down 07R, KABQ along 08/26, USSS M, OOMS A): the shortest path uses runway pavement as a
  taxiway and there is no clear node to stop at.
- YBBN 19L "A6": that intersection is reached along A4S; A6's own approach lies beyond the junction.
- Painted holding points behind the navdata hold line: the stop stays navdata-authoritative.

## Painted-line hold audit (2026-09-23)

Set `VP_AMDBDIR` to an unpacked AMDB Bridge airport cache (one folder per ICAO with
`taxiwayholdingposition.geojson`; the free X-Plane-Gateway/OSM database from
github.com/Vihaan2012-cmyk/Free-Airport-Mapping-DB) and every taxi/departure also checks each hold
stop against the painted RUNWAY hold lines (catstop 1):

- `HOLD_PAST_PAINT_NAVDATA_HOLD_UNUSED` — the stop is on the runway side of every painted line for
  that runway, and our navdata HAS a hold node within 40 m of the line: our placement logic.
- `HOLD_PAST_PAINT_NO_NAVDATA_HOLD` — same, but our navdata has no hold there: the two sceneries
  disagree.
- `HOLD_WELL_SHORT_OF_PAINT` — stopped more than 120 m before the first painted line.

X-Plane positions are ~20 m off MSFS, so every comparison carries a 30 m allowance, and only lines
off the entered runway's pavement, on the stop's side, within 250 m and nearer it than any other
runway count. Unset, the audit is off and the harness is unchanged. Result and the fix it drove:
docs/taxi-guidance.md, "Runway holds on a diagonal approach".

## Fifth round — what the pilot hears (2026-09-25, same 100 airports)

New harness checks: `SPOKEN_DIRECTION_CONTRADICTS` (opposite turn directions in one frame, or
"Curving X" over a hard opposite tone) and `VP_BURST=1` (prints every frame that speaks more than
one line — each `AnnounceImmediate` cuts the previous one off, so only the last is heard). The
taxi trace also prints the route's start node and every hold line within 150 m (`HSNEAR`, with
distance from the nearest centreline).

**Fixed (0 new findings; wrong-turn warnings 385/385, same distances):**

1. **One utterance per frame** (`SpeakNow` / `ComposeFrameUtterance`). ~12,000 frames spoke 2-4
   interrupting lines; the pilot heard only the last ("In 30 metres, slight right." → "Slight right
   now." → "Slow for turn.": the direction was never heard). Safety lines go first; an advance notice
   in the same frame as its "… now" is dropped.
2. **"Curving X" deferred while the tone asks for a turn the other way ≥ 25°** — 89 → 2
   contradictions (EGLL 27R A7/A10E/AB11: "Turn left to come around." + "Curving right.").
3. **Junction callouts wait until the pilot has come round after the initial turn cue**
   (`_initialTurnPending`) — the route summary's "Turn left to come around" was cut off by "Left."
   / "Left now." for a junction behind the aircraft.
4. **Off-route runway warning**: the LOADED route's start node counts as on-route (a stand 31 m
   short of LTFM A11A; recalcs excluded — whitelisting them lost 19 wrong-turn warnings), the pilot
   standing at it is ignored, and "approaching" needs ≥ 2 kt of closing speed on the line. False
   warnings on route 147 → 77.
5. Wording: callouts capitalised, no "continue now.", no "sharp slight right … 160 degrees".
6. Hold-short countdown speaks only the deepest tier already crossed ("in 55 metres." then
   "in 55 metres. Slow down." 0.1 s apart at LTFM B9B).
7. Post-handoff, well off to the side of a leg running beside the runway with the next turn toward
   that side, the tone steers at the leg's end (KTPA 28 N: silent tone under "Sharp right now").

Follow-up the same evening (A/B against the round's end, 0 new findings, wrong turns 385/385):

8. **Vacate stops past the landed runway's own hold line on a curving exit**
   (`RunwayVacateResolver.ExtendPastOwnHoldAhead`). EGLL 09L A1 stopped 22 m short of "A1, Runway
   27R" at 141 m because the greedy walk needs every step further out and A1 curves (120 → 123 →
   120 m). Gates, each measured: a line NAMED for the landed runway, ≤ 180 m out (1,446 → 1,332
   moves before this cap; 01G was moving to 252 m), ≤ 80 m of taxiing, never more than 10 m back
   toward the runway, a ≤ 60 m hop past the line must exist (KCVG 09 C's only onward leg is 298 m
   and the planner then started the route 250 m away), and not on an exit whose path turns back
   (`LandingExitDestination.Resolve`; RJAA 34L A8, CYYC 11 A got worse). Whole DB
   (`tools/LandingExitDestinationSweep`): 497 exits at 229 airports move, all outward (+5 to +120 m,
   median 46 m), none closer, no vacate verdict changes — Heathrow's N5E/N4W/S4E/S6/N6/A6 stopped at
   ~90 m against painted lines at ~145-150 m.
9. **Handoff route anchor follows the exit's own path** (`ExitPathStartAnchor`): KCLT 18 "D6"
   leaves via R, and the nearest D6 node was on D6's other arm beside the runway — the route opened
   with a leg that panned the tone away from the exit and the pilot heard "Missed taxiway D6". Only
   where the path beyond the junction carries no node of the exit's name, and never on a turn-back
   exit (overriding every off-path anchor fixed 59 findings and made 43).
10. **The post-handoff miss retarget uses the undershoot scan's 11 ft/kt lead** — "Retargeting taxiway
    R, 25 metres ahead" at 25 kt. A junction of the missed exit's own taxiway is exempt (KSLC 35 K8
    is rescued by it), a close exit is still taken when nothing further qualifies, and
    `RetargetLandingExit` tries it last when every further exit fails to route (KEDW 23L past C).
    314 straight-roll retargets now name an exit a median 100 m ahead instead of 35 m.

## Sixth round — route starts off the pavement (2026-11-08, same 100 airports, plain navdata)

A/B with both legacy switches on and off, 6,893 scenarios. `TAXI_OFF_PAVEMENT` went away on 610 scenarios and appeared on 22. In `rwystop` alone, 843 of 1,124 flights were flagged before and 265 after, `TAXI_NOT_ARRIVED` went from 12 to 1 and stops on runway pavement from 9 to 4. Silent wrong turns stayed at 14.

The first attempt took the nearest exit whatever the route did next, which added 10 stops short of the runway just left (KFLL 10L Q); an exit whose route comes back onto the runway is now skipped. The walk target also aimed the roll beside the runway where the first segment leaves at an angle, so the tone now aims at the start node until the aircraft arrives.

Accepted: 13 new `TAXI_RECALC_WHILE_FOLLOWING` against 32 gone. `RUNWAY_ENTERED_UNHELD` in `rwystop` rose from 17 flights to 25, but 22 of the 25 name the runway the flight started on, which the check reads as an entry after drifting to the edge, not a real entry.

## Seventh round — a clearance that starts behind the aircraft (2026-11-19, 100 busiest airports of each navdata, plain navdata)

Two flights on 2026-11-18 left the pavement following the tone: VTBS 02R (cleared E7, vacated one exit early at E10 after the rollout switched to it at 20 kt) and VHHH 07L (cleared C8, stopped on the runway 600 m before it). Both replay exactly with `--trace ICAO:from:…` on the 2024 navdata. `missedexit` flies the other way round, past the planned exit and off at the next; the early case rests on the VTBS replay alone. The sixth round's runway start already covers VHHH; VTBS needed RTE-38 and RTE-39 ([route-runway-start.md](invariants/route-runway-start.md)).

A/B against a frozen build, modes `missedexit taxi rwystop departures wrongturn`, on both navdata sets:

| | 2024 | 2020 |
|---|---|---|
| scenarios | 7,974 | 8,013 |
| `TAXI_OFF_PAVEMENT` gone / appeared | 196 / 4 | 165 / 4 |
| `RUNWAY_ENTERED_UNHELD` gone / appeared | 38 / 1 | 36 / 1 |
| `HOLD_FAR_BEFORE_RUNWAY` gone | 37 | 35 |
| `missedexit` flights more than 40 m off the pavement, before / after | 135 / 30 | 144 / 46 |
| scenarios changed outside `missedexit` | 24 of 6,868 | 18 of 6,893 |

Leaving out every cleared taxiway the route does not drive was measured first and rejected: at VTBS it dropped the cleared D6 and took D7.

A review of that change found the same out-and-back on a route loaded while still on the runway past the cleared exit, where the runway start (RTE-37) had kept RTE-39 from running. `MISSEDEXIT_RWY` flies it: 603 flights on 2024 and 640 on 2020. With the check run from the runway start too, `TAXI_OFF_PAVEMENT` went away on 8 and 7 of them, and no code appeared in any of the 8,577 and 8,653 scenarios.

Accepted: 21 (2024) and 20 (2020) new `TAXI_RECALC_WHILE_FOLLOWING` against 10 and 8 gone, and the four new `TAXI_OFF_PAVEMENT` on each. In the cases traced (KIAD 19R U3, LTFM 35R C14, KATL 27R L, ZBAD 17L C4, VTBS 20R F3) the lead-in is now kept and starts behind the aircraft, so the flight opens with "Taxiway X is behind you" and a turn the pilot model swings wide on, where it used to be sent 180 to 380 m in a straight line. At ZBAD the route change during that turn has no stop left for the runway just vacated, and says so. Three 2020 `rwystop` flights gained `RUNWAY_ENTERED_UNHELD_WARNED`; the one traced, VTBS 20R, now loads a route that leaves by the exit behind it and announces that the runway it stands on has no hold short point. The other new codes were not traced one by one.

## Eighth round — review fixes to the route start (2026-11-19, 100 busiest airports of each navdata, plain navdata)

A/B of the review fixes against a frozen build of the seventh round's end, the same harness, modes `missedexit taxi rwystop departures wrongturn`: every scenario matched, 8,579 on fs2024 and 8,516 on fs2020. Kept, with no finding new on either database:

| | 2024 | 2020 |
|---|---|---|
| `TAXI_OFF_PAVEMENT` gone | 4 | 6 |
| `HOLD_WITHOUT_RUNWAY` gone | 0 | 2 |
| `RUNWAY_ENTERED_UNHELD` gone | 0 | 1 |
| `TAXI_RECALC_WHILE_FOLLOWING` gone | 0 | 1 |

The traced gains come from the near-cleared test ([RTE-37](invariants/route-runway-start.md#rte-37)): a first cleared taxiway within 75 m cancels the runway start only when it is on the runway or at its edge. KIAH 15L (WM, 43 m off the pavement before, 0 after) and KBNA 31 (K, 99 m before, 2 after) now start at an exit along the runway instead of a node across the grass.

With the OSM snapshot loaded (fs2024, modes `missedexit taxi rwystop departures`, the 37 busiest airports fetched, 3,425 scenarios), measured because RTE-38 and RTE-39 find the junction by the names on its nodes and online names add more: the review fixes against the seventh round's end, `TAXI_OFF_PAVEMENT` gone on 1, nothing new. The route-start work as a whole against the build before it (that harness compiled against the older app), `TAXI_OFF_PAVEMENT` gone on 396 and new on 13. None of the three new ones traced reads a junction wrongly from an online name: KORD 04L E is a runway start, KMSP 04 M6 flies the same route with a different opening turn, and LTFM 35R B12B leaves out a taxiway at a real out-and-back.

Measured and rejected along the way, each with its own A/B:

- Reading the turn-back over 30 m of route either side of the junction ([RTE-39](invariants/route-runway-start.md#rte-39)): 4 scenarios better and 3 worse. KDEN 07 B4 reaches the cleared taxiway by a 3.3 m spur and back, which the smoothed reading missed, and EDDB 24R K1 gained three findings. Every reversal the per-node test flagged in those flights was a real out-and-back over one edge, 3.3 to 56 m long.
- The opening turn's stand evidence from the stand rows instead of the graph's Parking nodes ([STR-24](invariants/taxi-steering.md#str-24)): more opening turns flipped, at gate departures (KMCI gates 25 and 26 on fs2020) and after an exit vacate (KPIT 28C exit C, both databases), and those flights left the pavement or were re-routed where before they had not.
- Preferring a start that follows the clearance over every start ahead: KLSV 21L took an exit behind and turned round on the runway, 99 m off the pavement. The preference now applies only among the exits one way.
- Splitting the exits by bank with the six-place cap unchanged: the two halves of a crossing took two places and KBNA 02L (fs2020) lost the only exit whose route stayed off the runway. A twin on the other bank no longer takes a place.

**Open:** OSM hold names like "C@10/28" (KTPA, KDFW, KONT, KABQ) are read out verbatim. EGLL 09R S6
→ Gate 223 routes back up S6 onto the runway and along it (the known "runway used as a taxiway"
residual).

## Sixth round — Turn picks, on MSFS 2024 navdata (2026-11-15, 100 busiest fs2024 airports)

The first round flown on `fs2024.sqlite` (`--sim fs2024`; the harness could only open the MSFS 2020
database before). Plain navdata (`VP_NOOSM=1`), 12 processes, under two minutes a run.

**No pick, before and after the Turn pick branch** (`all wrongturn departures`, the harness of the
branch's database change against base `6684fab3a`, then the final harness against the final app):
11,963 scenarios (3,705 landings, 5,835 taxis), the same in both; 18,225 result lines and 20,970
finding lines, not one different. A clearance with every Turn on Automatic routes and is guided
exactly as before.

**Every pick** (`turnpicks`): 10,008 picks on 931 clearances, 2,805 flights. No pick was dropped in
silence, none broke a load, none is honored or called already made with the route turning the other
way, and no honored route touches the arm of the taxiway it turned away from. What became of them:
already made 3,033; no such turn 3,907; does not lead there 2,029; long detour 31; honored on a
charted link 482; honored with an off-chart leg 526 (median 59 m, 90 % under 115 m, 391 of them 40 m
or longer). The sweep tries BOTH sides of every row, so most picks point away from the destination by
construction; the refusal rate says nothing about a pilot's own picks. In the 1,004 flights of
honored picks, a pick adds no runway entered without a hold, no stop on runway pavement and no
flight that fails to arrive.

**Found and fixed, each flown first:**

1. **The KBOS incident route was refused.** `KBOS:pick:…:33L:H,C=L` on the whole airport: "turning
   left onto taxiway C does not lead to Runway 33L". Taxiway C has 400 m with no name 900 m east of
   the junction, and a blocked node switched gap chaining off for its whole taxiway. The unit-test
   replica of the junction had no gap. Now only a hop over the block is refused ([TURN-5]); the route
   flies: the note, the hold short of 04R at H, "Off-chart leg. No taxi line for the next 95 metres…",
   the left turn, 33L. 30 of 396 "does not lead" refusals were this.
2. **A turn onto the next taxiway counted as this row's.** "Already made" walked 80 m along the
   route: a straight-on join followed by a left onto the next row read as "left, already made", and
   the pick vanished without a word. It now walks the row's own taxiway only.
3. **Picks "honored" by loops**, about 1,500 of the 2,515 the first cut honored ([TURN-4],
   [TURN-6]): a 153° reversal 55 m after the turn (LTFM "right onto N1"); a loop back through a node
   the route had passed (WMKK "left onto G"); a trip back up the stand's own lead-in (ZJHK "right
   onto C", where guidance then skipped the loop altogether and steered onto the charted way with the
   pick still reading as made); 116 m out along a taxiway and straight back over the same nodes (ZBAD
   "right onto L2", and 486 others); a turn and straight back past the junction on other nodes.
4. **The long way round onto the same taxiway's other arm**, which the first design allowed. Of the
   1,013 picks still honored after 3, 379 travelled along the other arm and 169 more crossed it. One
   never arrived: KCLE "left onto G7" at a runway exit went back across the runway just vacated and
   round to G7's other arm, and a recalculation on that lap began it again. By the owner's ruling the
   rest of a picked route no longer touches that arm ([TURN-6]); the pick gets "does not lead to".
5. **A pick honored along a runway.** OMDB "left onto N": onto runway 12L/30R at N8 with "Passing
   the hold line", 370 m along it, unheld and unwarned. A pick no longer adds a stretch along a
   runway ([TURN-7]); the same pick now gets a 70 m off-chart leg and flies clean.

**Measured, left as it is:**

- **What an honored pick costs.** A charted pick adds a median 204 m to the route (a quarter add
  over 610 m, 68 of 482 over 1 km); an off-chart pick a median 32 m, and 298 of 526 leave the route
  within 50 m of its charted length. The bound is twice the charted route plus 500 m.
- **A missed turn is not always called missed.** Of 512 flights where the pilot took the charted way
  at the junction, 209 heard "was missed", 142 heard "Route changed" with no word about the pick (it
  counts as made once the segment index passes its join, about 25 m before a crossing node), and in
  161 the route never changed within 150 m and the pilot followed the tone again. KBOS, taking the
  connector as on the day: the tone hard left throughout, and after 60 m "Route changed… The left
  turn onto taxiway C was missed."
- **Three rows where both sides read as already made** (RPLL G2, MMMX L): a straight-on join onto a
  taxiway that bends slightly one way and, 140 m on, the other. Nothing changes and nothing is said.
- **Three picks lost while following the tone** ("was missed" or "could not be kept"): each a
  first-row pick or an exit-to-gate plan that the harness starts facing away from its route.
- In the miss flights, three runway entries with no hold: the pilot, deliberately off the route, drove
  on at a runway the charted way leads to (KATL M6: "Warning: approaching runway 09L at M6, off
  route." was spoken, and the recalculated route said "with no hold short point").

## Leg ends and taxiway holds (2026-11-24, 2024 navdata, plain navdata)

Stage 1 of the Taxi UI Redesign: a leg with no destination is counted down and ended like a hold
short (`ProgressiveLegEnd`), never on runway pavement, and "hold short of taxiway B" stops a
wingspan-based setback before the first junction the route reaches, never on, inside or behind a
runway, nor where the 25 m before it touch a runway's clear margin (`TaxiwayHoldPlacement`; 34.8 m for
a 36 m wingspan). The setback constants are assumptions the owner has not yet confirmed in the sim.

**Nothing else moved.** The branch before the stage (`f7d5c13ba`) against its end (`61a3e554f`), the
same harness in both, modes `all wrongturn turnpicks rwystop missedexit`, the 100 busiest airports:
23,940 scenarios in both runs, none only in one; 40,378 result lines and 4,726 finding lines, not one
different. No existing mode flies a leg with no destination or a taxiway hold.

**Where the legs end** (`legends --airports 40 --taxi-per-airport 3`, 38 of the 40 airports gave a
leg; 113 plain legs and 61 turn legs flown both ways; re-run after the stage's review fixes, which
moved two stops beside a runway, below, and again after the owner's "Stop before the turn" ruling of
2026-11-25, in brackets where it changed a figure):

| | without picks | with picks | picks that changed the route |
|---|---|---|---|
| legs | 113 | 122 | 42 [20] |
| ended | 112 (99.1 %) | 122 (100 %) | 42 [20] (100 %) |
| ended or stood at or past the junction | 0 | 0 | 0 |
| stood before the junction: min / median / p90 | 18.4 / 54.5 / 69.5 m | 16.4 / 56.5 / 75.5 m [32.4 / 60.1 / 81.0 m] | 16.4 / 55.6 / 82.1 m [47.9 / 60.3 / 89.2 m] |
| the leg ended: min / median | 24.6 / 62.0 m | 21.1 / 63.6 m [40.1 / 66.5 m] | 21.1 / 61.6 m [56.6 / 66.9 m] |
| the stop at load, median before the junction | 37.3 m | 35.5 m [40.8 m] | 34.8 m [40.7 m] |

Distances are along the route and on over the graph to the junction the app holds short of. What
became of the picks: already made 24 [8], charted 35 [18], off-chart leg 7 [2], no such turn 20, does
not lead there 36, and [38] dropped because the leg now ends before the turn (`SILENT` in the CSV).
No `LEGEND_*` finding: every pick's indices still name its departure and join after the stop is
placed, no off-chart leg is split or ends a route, no picked route passes a node on B between its
turn and its stop, and no pick the leg ends before is said as made (`LEGEND_DROPPED_PICK_SPOKEN`, added
with the ruling). No leg needed a route change. The run is deterministic: four shards give the same
235 rows as one process.

**Stop before the turn** (2026-11-25). A Turn pick no longer holds a taxiway stop on the picked taxiway
(TWH-2): "Left onto A" stops where "via A" does, and a leg end that would lie on, or at the far end of, a
pick's off-chart leg or connector ends at that turn's departure node when one before the turn is usable,
else stays there with the pick kept (`TaxiwayHoldPlacement.PlaceLegEnd`; no leg here needed that fallback).
The same command before (at `4741b2b5c`, byte-identical to the stage-1 CSV) and after: the plain legs and
every other pick unchanged, exactly 38 picked legs changed, all now ending on Z before the turn with the
pick dropped and nothing said about it (17 charted, 16 already made, 5 off-chart). None of the eight
picked legs that stood under 25 m from the junction still does (ZUTF "V6, K" to K2 both ways: 16.4 and
16.6 to 52.5 and 70.9 m; OTHH, WMKK, KJFK, KMIA, EGLL; ZSPD "C2, P2, left onto P2, hold short of C",
19.0 to 146.3 m: its stop is now the departure node of its 59 m off-chart leg, 128.5 m before C along the
graph). Flown with plain `Choose` for the leg end too, ZSPD ended the leg on the off-chart leg itself
(`LEGEND_ENDS_OFFCHART`), its "Off-chart leg" sentence never said; an interim fix that re-placed that stop
with the old floor kept it at the C junction, the aircraft 19.0 m from C (its point to insert 1.1 m before a
P2 node found no edge within reach).

**On a runway.** No leg ended, and no "Slow down" or "Stop." of a leg end was spoken, with the aircraft
on runway pavement (`LEG_ENDED_ON_RUNWAY`: 0). Before the review fixes the same check found two, both
plain legs that cross a runway just before B, both "Slow down" spoken on the pavement at 15 kt: RKSI
"via C9, hold short of B3" (33L; the stop was an existing node 51 m before the junction, whose 25 m
approach lay over the runway's margin) and KLSV "via A, hold short of F" (03L; an inserted point 35 m
before it). A stop is now usable only when the 25 m before it is clear of every runway's margin, and a
leg end never ends, nor says "Slow down" or "Stop", on runway pavement: RKSI now stops at a point
inserted 35 m before B3 and heard "Slow down" 40 m out, off the runway; KLSV stops at the F junction
itself (`stop_m_before_junction_at_load` 0), the leg ended 25 m and the aircraft stood 18 m before it.
Every other row of the CSV, and every CODES line, is as at the stage's end.

**Measured, left as it is** (traced with `--trace ICAO:legend:…`):

- **The countdown depends on the last segment.** It runs only while the aircraft is on the route's
  last segment, and the 25 m capture radius usually ends the leg before the "Stop." tier is due, so the
  closing sentence takes its place. Of the 234 legs that ended, 84 [93] heard "in N metres" and then "Slow
  down" (last segment median 100 m [97 m]), 115 [119] "Slow down" only (15 m), 10 [14] "Stop." only (6 m),
  and 25 [8], all turn legs, no countdown line at all before "Hold short of taxiway B. Set a new route
  when cleared." (6.5 m [7.6 m]: a stop a few metres past a node, LTFM "B7, B8A", VHHH "H, H8", which now
  stop before the turn). None heard three lines.
- **A stop less than 25 m past a turn ends the leg before the turn.** The capture is a straight line
  to the stop, so it reaches round the corner: 45 [16] turn legs ended with the aircraft still more than
  45° off the last segment (LFPG "Q2, Y6": the stop 17 m along Y6, the leg ended on Q2 before the 110°
  turn, "Taxiway Y6. Hold short of taxiway Q." spoken together; since the ruling that leg stops on Q2,
  and EGLL "A9W, A9E, left onto A9E" is one of the 16). The aircraft stops short of B either way.
- **A stop moves back before the turn onto A when the junction with B lies less than the setback past
  it.** In 46 of the 56 refused picks the route ends on Z, not A (KLSV "E, F": J is 12 m along F, the
  stop 35 m back on E; KORD "N3, N" to A10). Before the 2026-11-25 ruling a pick applied held the stop
  on A, at the junction or the first node past the turn; now it does not, and 38 more picked legs end on
  Z (83 of the 122).
- **Nine legs stood under 25 m from the junction** [one since the ruling], eight turn legs whose stop
  is at the junction or within 5 m of it: the picked turn lies less than the setback before B (ZUTF
  "V6, K" to K2, KJFK "B, MB" to P), or the setback point falls on the off-chart leg (OTHH "A5, A6";
  ZSPD and WMKK are off-chart legs too, not traced). They stood 16 to 23 m before it along the route, 15
  to 19 m in a straight line: the capture radius and the braking. All eight now stop before the turn;
  ZSPD "C2, P2", whose stop fell after its off-chart leg, at its join, B 50 m on, now ends where that leg
  starts. The one left is KLSV "via A, hold short of F", whose stop is the junction because runway 03L
  lies just before it (18.4 m). With a 36 m wingspan the nose is about 20 m ahead of the point measured,
  so at such a stop it can be on B, not just near its edge.
- **A stop before the turn is set back along the route only.** Where Z itself carries on and meets B
  just past the turn, the stop can stand close to that other meeting: LTFM "B7, right onto B8A, hold
  short of A7" stops at the B7/B8A junction n5655, 16 m along B7 from n5093 on A7; KDEN "D, right onto
  D8, hold short of WC" stops at the D/D8 junction n2742, 7 m along D from WC at n3738. These and the
  other new stops at a fork before the turn are most of the legs whose junction the harness's own walk
  finds elsewhere (below).
- **Two picked routes reach their turn over B** [one since the ruling]. The charted link to the turn
  crosses B before the join, and the leg then holds short of B a few metres on
  (`LEG_PICK_LINK_CROSSES_B`): KDEN "D, D8, right onto D8, hold short of WC" crossed WC at n3738
  ("Crossing taxiway WC.") and stopped at the WC junction n3678, 8 m from it, the aircraft 8.6 m from
  that node (now it stops before the turn, above); EGLL "A9W, A9E, left onto A9E, hold short of A"
  drives 6 m along A to its turn. The leg still stops before the first meeting with B past the turn,
  which is all the acceptance check asks.
- **A loop near the end ends the leg at its apex.** ZBAD "via Z3" reverses 165° three segments before
  the end; KDTW "K, Y, right onto Y" is a picked route that reverses about 170° 94 m after the join,
  just past TURN-4's 80 m. The straight-line capture fires as the aircraft passes near the stop on the
  way out, before the loop is driven: 112 and 99 m before the junction along the route, 63 and 56 m in
  a straight line.
- **83 [97] legs stood more than 60 m back**: an existing node up to 40 m beyond the setback is taken
  before a new one is inserted, and the capture radius ends the leg 25 m before the stop. A stop before
  the turn whose setback point falls on an off-chart leg goes back to its departure node, 91 m before
  the junction at RJAA "K, E2" and 128.5 m at ZSPD "C2, P2" (along the graph; 109 m along the route).
- **KPHL "via G, hold short of E" did not end**: it starts 34 m before the runway 17 hold line at G.
  Standing still, the runway countdown says "Hold short runway 17 at G in 35 metres. Slow down." and
  then "Hold short runway 17 at G. Stop." (the stopped-in-zone line), but the hold's standstill capture
  is 30 m, so guidance stays in Taxiing and the pilot model, braking on "Stop.", never moves. A runway
  hold's behaviour and the pilot model's, not the leg end's.
- The turn legs' starts are chosen from the graph and checked with the router alone; `LoadRoute`'s own
  route can join A elsewhere (KORD "C1, C" joins C 125 m before M), and 56 of the 122 picks were
  refused (no such turn 20, does not lead there 36). The refusals were not traced one by one.
- The junction is the app's own (`_destinationNodeId`, read by reflection): the harness's walk from
  the stop picked another node in 19 legs, all refused picks whose stop sits at a fork before the turn;
  since the ruling in 45, the 26 more all picks the leg now ends before.

## Cleared crossings (2026-11-25, 2024 navdata, plain navdata)

Stage 2 of the Taxi UI Redesign: a runway crossing the pilot was cleared for (`LoadRoute(...,
clearedCrossings:)`, `ClearedCrossingHolds`) loses its stop but is still counted down ("Runway 17L at C11 in 40
metres, cleared to cross."), named at its line ("Crossing runway 17L at C11."), clearable early by Continue and
put back by the Hold short key. All runs: `--sim fs2024`, `VP_NOOSM=1`, the 40 busiest airports, 3 plans each.

**Nothing else moved.** Stage 1's end (`678c943ad`) against stage 2's (`a89c3babf`), the same harness in both,
modes `taxi wrongturn turnpicks rwystop missedexit`: 3,404 scenarios in both runs, none only in one; 4,941 result
lines and 1,036 finding lines, not one different. The harness below, without its new option, flies those five
modes to the same lines again, and `legends` to the same 235 rows, codes and findings as stage 1's final run.

**`--clear-first-crossing`** (`taxi`, `turnpicks`). After each flight, the same plan is loaded once, the first
crossing not already passed is read from the route's `RunwayEvents` (route order: where the route reaches the
runway), and the plan is loaded again on a fresh manager with `clearedCrossings: [that runway]`, as the planner
hands it over. That flight has its own CODES kind (`TAXI_XC`, `TURN_CHARTED_XC`, …) and an `XC|` results line, and
is compared with the flight just flown with nothing cleared. A plan whose route crosses no runway is not flown
again. The flights with nothing cleared are byte-identical to a run without the option. For the kept-tail
recalculation, every pick that changed the route also gets an **excursion** (`TURN_EXCURSION`, then
`TURN_EXCURSION_XC`): from a junction at least 100 m into the route and 60 m before both the turn and the cleared
stop, the pilot drives a branch off the route (45 to 135 degrees off, 120 m long and 60 m away from the route, clear
of every runway) and holds that heading until guidance re-routes. The stop is then either in the kept tail (at or
after the turn's departure) or on the stretch the recalculation builds afresh.

| | flights | held at the cleared crossing | its line said once, at the line | and the watch's "Crossing runway R" before the countdown too | "… cleared to cross." before the line | other stops as with nothing cleared |
|---|---|---|---|---|---|---|
| `taxi` (shortest route and clearance) | 92 | 0 | 90 of 90 | 27 of 92 | 76 of 76 | 92 |
| `turnpicks` (no pick, charted, off-chart, miss) | 224 | 0 | 212 of 213 | 58 of 224 | 197 of 197 | 224 |
| `turnpicks` excursions | 48 | 0 | 39 of 39 | 9 of 48 | 39 of 39 | 48 |

Every first crossing cleared was a single-runway stop (352) or the route's start hold (12); none was a stop shared
with another runway. "Its line said once, at the line" counts the flights that reached the stop with the crossing
still cleared there (a route change can move it, below), and only what is said once guidance is past the stop's
segment: none of those missed it (`XC_LINE_NOT_SAID` 0, `XC_STOP_NOT_REACHED` 0). It leaves out the incursion
watch's own "Crossing runway R" said about 40 m before, while guidance is still on the segment before the stop's,
which 94 of the 364 flights heard as well, so they heard "Crossing runway R" twice (below, "Measured, left as it
is"). The countdown's "cleared to cross" is counted where a countdown is due:
not at a start hold, and not where the line was passed as guidance started (18 flights; at UUWW and KNZY the two come
in one utterance, "Runway 01 at M1 in 0 metres, cleared to cross. Crossing runway 01 at M1."). The one flight with
more than one line is a miss flight (ZSPD Gate 77, "left onto P1", taking the charted way), whose charted way
passes the cleared stop off the route: the line, then "Crossing runway 35R at P1." again 0.1 s and 10 s later from
the incursion watch, which the line latches off only for the stop's own node (the same flight with nothing cleared
hears the watch's line before its countdown and again after its Continue).

**Route changes before the cleared stop.** 34 recalculations kept a Turn pick's tail. 24 of them, all after an
excursion, cleared the stop again at its node: all 11 with the stop in the kept tail, and 13 whose stop lay on the
stretch rebuilt before the turn but came back at the same node. The other 10 kept the tail with the stop before the
turn, and the rebuilt stretch met the runway at another stop (KPHL, 6), not at all (WMKK, 2) or with no stop (OEJN,
2). 13 more (miss
flights, no tail) cleared it again at its node. 22 flights lost the clearance in all: those 10, 8 where the pick was
missed or "could not be kept" so no tail was kept, and 4 with no pick (KLAX, KPHL). The new route stops for that
runway at another node and says so, "Runway 09L is a stop again." (16); no longer meets it (3: WMKK twice, OMDB);
crosses it with no stop, said "with no hold short point for runway 34C" (2, OEJN, below); or now
enters it at B17 with a stop and a countdown, but no "is a stop again", which `DroppedOnRecalculation` says only for
a crossing (1, KLAX, a miss flight). No route change brought a stop back at the cleared node, cleared another
crossing of the runway, or moved a crossing's stop without a word.

**Cross then hold** (`crossthenhold`, stage 1's final review: KCLE, P across 24L, then hold short of L): 50 legs
at 18 airports, the runway's far edge 18 to 149 m (median 101) before the B junction. With nothing cleared, every
route stopped for the runway first. Cleared: 38 at a stop, 12 at the start hold. No leg entered HoldShort at the
runway; every leg said its "Crossing runway" line once at the line, and all 38 counted down said "cleared to
cross" first; in 4 of the 50 the incursion watch's "Crossing runway R" came before the countdown as well, so those
heard it twice; every leg ended (none at or past the junction, standstill 4.9 to 78.2 m before it, median 52.0 m);
`LEG_ENDED_ON_RUNWAY` 0. Every stop for B lies past the runway, 16.8 to 113.8 m (median 63.6) beyond its far
pavement edge, and clear of every runway's margin. KCLE "via S, hold short of L": "Runway 24L in 30 metres, cleared
to cross.", "Crossing runway 24L.", "Hold short taxiway L in 55 metres. Slow down.", "Hold short of taxiway L. Set a
new route when cleared.", the aircraft at rest 56 m before L. Four shards give the same 50 rows as one process.

**Measured, left as it is:**

- **"Crossing runway X" before the countdown.** In 94 of the 364 flights (and 4 of the 50 legs) the incursion
  watch says "Crossing runway 17L at C11." while guidance is still on the segment before the stop's, about 40 m
  out, and the line follows: "Crossing runway 17L at C11." / "Runway 17L at C11 in 40 metres, cleared to cross." /
  "Crossing runway 17L at C11." (LTFM). Stage 1's build says the same watch line before an ordinary hold:
  "Crossing runway 17L at C11." then "Hold short runway 17L at C11 in 40 metres. Slow down." The watch skips only
  the countdown's own node, the current segment's end, and the stop segments are short (20 to 40 m).
- **A recalculation that the no-op guard drops is adopted when the cleared stop is not on the new route** (the
  RTE-22 amendment). KPHL and KLAX: an early recalculation as the pilot model leaves the stand, discarded with
  nothing cleared, is adopted cleared; the new route meets the runway at another stop and says "Runway 09L is a stop
  again." (KPHL, 6 picked flights; its 2 plain flights adopt the same recalculation with nothing cleared too) or ends
  a crossing-on-the-runway artefact (KLAX, 2: the flight with nothing cleared stops on 25L's pavement at H8; cleared,
  the new route crosses once at H9).
- **Eleven `crossthenhold` stops are the B junction itself**, all where the runway's far edge is 65 m or less
  before it, as stage 1 placed KLSV "via A, hold short of F": no setback point had its 25 m approach clear of the
  margin.
  In four (LTFM "H1, G6A", KLSV "A, B", EDDF "M11, R7", KDTW "V, Z": the edge 18 to 33 m out) even the junction's
  25 m approach crosses the margin, and the aircraft stood 4.9 to 18.4 m before the junction; with a 36 m wingspan
  the nose can be on B.
- The excursion is the harness's construction: 12 of 48 never reached their junction, 3 got no route change, 3
  heard "could not be kept" (12, 4 and 3 with nothing cleared). At OEJN Parking 137 the recalculations were refused
  for 38 s ("heads backwards") and the route finally adopted crosses 34C with no stop, said "with no hold short
  point for runway 34C", before the pilot model drives onto it (`RUNWAY_ENTERED_UNHELD`, with nothing cleared too).

## Crossing keys (2026-11-25, 2024 navdata, plain navdata)

Stage 2's final review: Continue before the line and the Hold short key had no integration measurement.
**`--crossing-keys`** (`taxi`, `turnpicks`: the plain flights, the Turn pick base, and every pick that changed the
route, each after its `--clear-first-crossing` flights) flies every plan three times more at its first crossing (the
first not already passed, in route order, as `--clear-first-crossing` reads it), each with its own CODES kind
(`TAXI_KEY_EARLY`, `TURN_CHARTED_KEY_HOLD`, …) and a `KEYS|` results line (where the aircraft was at the press, the
key's answer, the holds at that stop, its lines). The key's answer is put back into the capture, so the next frame
hears it like any other line.

- **EARLY**: nothing cleared; Continue (Input Y, `ContinuePastHoldShort`) in the frame the first countdown call for
  that stop is heard ("Hold short runway 22R in 120 metres."), guidance taxiing on the stop's segment. Flown where the
  stop is the crossing's own, on a segment, for that runway alone, not the pilot's own hold and not on the route's last
  segment (what `CanClearEarly` allows). Expected: "Cleared across runway 22R. No stop.", no HoldShort there, and its
  "Crossing runway 22R." said once, at the line.
- **HOLD**: the crossing cleared at load (as `--clear-first-crossing`); the Hold short key (Input Alt+Y,
  `HoldShortOfNextClearedRunway`) in the frame of the first "… cleared to cross." naming the runway. Flown where the
  clearing took a stop off whole (not a start hold, not a shared stop). Expected: "Holding short of …", HoldShort
  entered at that stop, never with the aircraft on runway pavement.
- **PAST**: cleared as for HOLD; the key at the first frame the aircraft is more than `StopPassedToleranceMetres`
  (10 m) beyond the stop node along the route (`ClearedCrossingHolds.MetresPastStop`): just past the line as the key
  itself judges it. Within that, off the runway's pavement, the aircraft is still at the line and the key puts the stop
  back, by design (`ClearedCrossingHoldShortKeyTests`). Expected: "Past the hold line of …", no HoldShort there.

All runs: `--sim fs2024`, `VP_NOOSM=1`, `--airports 40 --taxi-per-airport 3`, `taxi` and `turnpicks` one process each.
The flights with no key (236 `taxi` and 3,339 `turnpicks` result lines, 88 and 388 finding lines) are the same, line
for line, as a run without the option.

| | key flights (29 airports) | pressed | answered | held at that stop | its line | where pressed |
|---|---|---|---|---|---|---|
| EARLY | 220 | 195 | 195 "Cleared across runway R. No stop." | 0 | said once, at the line, in 195 of 195 | 28 to 116 m from the stop node (median 56) |
| HOLD | 220 | 211 | 211 "Holding short of R. N metres." | 211, none on runway pavement | | 0 to 116 m from the stop node (median 53) |
| PAST | 220 | 214 | 214 "Past the hold line of R." | 0 | | 10.0 to 17.8 m past the stop node (median 10.4), none on runway pavement |

Re-run the same day after the owner's "Ignore it" ruling (a Continue before the line within 2 s of the Continue that
resumed taxiing does nothing, `PairedCrossingHoldWindow.IgnoresEarlyContinue`), with the "Stop before the turn" and
"Warn at Calculate" changes in the build too: every `taxi` and `turnpicks` result and finding line the same. No key
flight presses that soon after a resume: the double press the ruling settles is not flown.

`taxi` flew 87 of each (261), `turnpicks` 133 (399). Not flown: the plans whose route crosses no runway (287 per
variant), whose first crossing is a start hold (10 per variant), and in `taxi` 4 clearance flights whose probe found no
route and 1 refused route (per variant). No `KEY_` finding but the not-pressed ones:

- **EARLY, 25 not pressed.** In 23 the stop was entered with no countdown call heard while taxiing on its segment
  (in the frame its first call came, or with none), so there was no rolling approach to press in: a stop segment
  shorter than the 25 m capture radius ("Stop. Hold short of runway 09L.
  Press continue when cleared. Hold short runway 09L in 20 metres. Stop.", KPHL N; 15 m at OMDB) or an exit route
  starting at the stop (KSLC Q, KNZY F, KMIA S, KLSV E, OEJN H1, OETF C, UUWW M1). In 2 (KPHL) a route change before
  the stop took the route off it.
- **HOLD, 9.** 3 routes start at the cleared line, said as guidance starts with nothing to count down (KSLC, OEJN,
  OETF); in 6 the [RTE-22]-exception recalculation moved the cleared stop before it was reached and said "Runway 09L is
  a stop again." (KPHL 4, KLAX 2; "Measured, left as it is" above).
- **PAST, 6.** The same 6 route changes.

In 58 of the 195 EARLY flights the incursion watch also said "Crossing runway R" before the countdown (above). At
UUWW (exit M1 → Parking 82) the HOLD key came in the first frame, with "Runway 01 at M1 in 0 metres, cleared to
cross. Crossing runway 01 at M1.": the aircraft stood at the line, off the runway, so the key put the stop back
("Holding short of runway 01 at M1. 0 metres.") and it held there.

## Ninth round — a hold passed beside its node, and the missed turn (2026-11-27, 100 busiest fs2024 airports, plain navdata)

PR 369 (the hold taken beside its node, [HLD-24](invariants/hold-abeam.md#hld-24), and a route that ends far from its runway spoken as the end of the route) and its review fixes, against main: three frozen builds (main, the PR as authored, the final tree) with the same harness source. `VP_NOOSM=1`, as no OSM snapshot is on this machine; fs2024 only, as no fs2020 database is installed.

**The standing modes** (`all wrongturn departures rwystop missedexit turnpicks`), 26,571 scenarios, every one matched, main against the final tree:

| code | gone | appeared |
|---|---|---|
| `TAXI_NOT_ARRIVED` | 5 | 0 |
| `HOLD_STOP_ON_RUNWAY_PAVEMENT` | 1 | 0 |
| `LINEUP_NEVER_ALIGNED` | 11 | 0 |
| `LINEUP_LEAVES_MAPPED_PAVEMENT` | 11 | 0 |
| `HOLD_WITHOUT_RUNWAY` | 9 | 1 |
| `HOLD_NAMED_OTHER_RUNWAY` | 2 | 0 |

- The five flights that now arrive are the hold taken beside its node: KCLE, KSLC, KSFO and VHHH twice, all `missedexit` or `rwystop` routes that double back on a hold or start beside one. At VHHH the route runs out along a J5 spur and back to the hold node, a 180 degree turn; the pilot swings round 42 m from the node, main never latched the hold and the flight circled for 7 km. The stop gone from runway pavement is KSFO, where the aircraft comes at the 19R hold on C from the side and is now stopped level with it, 9 seconds before main stopped it on the pavement. The one code that appeared (KSLC 34R) is a stop at the route's start that main makes as well; the check only reports it once a flight completes.
- The 11 lineups are five routes that stop short of their runway more than 400 m to the side of it (KDEN South 14 to 17L, KNYL to 35, DABO to 34, KCOS to 13, RKSI to 34R, each on a clearance or with a Turn pick). Main and the PR as authored held short of the runway there (at KDEN, more than 1,500 m from it) and ran 400 m of lineup tone across unmapped ground. They now count down to the end of the route and stop there: "Stop. End of this route. It does not reach Runway 17L. Reprogram the taxi route, including the taxiway that connects to the runway." With an end-state readout patched into the harness for this round (not committed), 7,617 taxi, departure, runway-stop and missed-exit flights: no flight that lined up before ends this way.
- Wrong turns: 399 warned and 11 silent before and after.

**Missed turns** (`missturn`, new this round). The review's question was whether the new capture stops a pilot who misses the turn onto the hold's connector and rolls on along the taxiway. It did: 1,502 missed turns, 40 plans an airport, 751 at 10 kt and 751 at 20 kt.

| | main | the PR as authored | final |
|---|---|---|---|
| `MISSED_TURN_STOPPED_BEHIND_LINE` | 0 | 61 | 0 |
| `MISSED_TURN_STOPPED_AT_LINE` | 0 | 7 | 7 |
| `MISSED_TURN_HELD_IN_CIRCLE` | 11 | 11 | 11 |
| `MISSED_TURN_REROUTED` | 1,352 | 1,288 | 1,345 |
| `MISSED_TURN_NOTHING` | 139 | 135 | 139 |

The fix is [HLD-25](invariants/hold-abeam.md#hld-25): not taken while more than 10 m farther from the hold's runway than the hold node AND with the tone at full pan. It changes none of the 26,571 standing scenarios, and the 11 stops the capture adds to those 7,617 flights are all kept at the same places.

**Measured and rejected:**

- Holding the capture back while the off-route clock is running, so that the recalculation answers first: it took away only 24 of the 70 stops beside a hold. In the other 46 the clock was not running when the aircraft came level with the node; why was not traced.
- Either half of [HLD-25] alone. Behind-the-line alone loses the VHHH and KSFO stops (21 to 35 m behind, the tone leading the pilot round), which are three of the five flights that now arrive. The tone alone loses KCLE, where the tone is at full pan with the aircraft 45 m past the line.
- A plain distance-from-the-runway test with a wider margin: the VHHH stops (22 m behind) and the nearest missed turn (26 m behind, KORD R) are 4 m apart.

**Open:** at KDEN the clearance made of the shortest route's own taxiway names (SC, L, EA, M, EC, P, ED) is routed 1.9 km along ED past the 17L hold line and never reaches the runway, where the shortest route does in 6.6 km. That is the constrained router, on main as well; the route now at least ends as the end of the route. Not flown: a missed turn above 20 kt, and either database with an OSM snapshot.

## Tenth round — hold-line calls about a line already passed (2026-11-27, 100 busiest fs2024 airports, plain navdata)

KIAD, 2026-11-26: stopped at 01C's hold line on Y5 after landing, the pilot planned the route to the gate and, 20 m down Y5 taxiing away from the runway, heard "Crossing runway 01C at Y5." `--trace KIAD:from:38.9538724,-77.4583657,166:B 71:Y5,Y,B` reproduces it on plain fs2024 navdata (the same 23 segments and start node as the app's log). The fix is [HLD-26](invariants/runway-holds.md#hld-26). Four checks are new this round (`HOLD_LINE_CALLED_BEHIND`, `CROSSING_CALLED_NO_RUNWAY`, `HOLD_LINE_CALL_REPEATED`, `RUNWAY_CROSSED_UNANNOUNCED`, in the table above); the "before" is that harness compiled against main 21a22c81d in a scratch worktree.

**The standing modes** (`all wrongturn departures rwystop missedexit`), 14,765 scenarios, every one matched:

| finding | main | after |
|---|---|---|
| `HOLD_LINE_CALLED_BEHIND` | 485 | 56 |
| `CROSSING_CALLED_NO_RUNWAY` | 289 | 101 |
| `HOLD_LINE_CALL_REPEATED` | 363 | 219 |
| `RUNWAY_CROSSED_UNANNOUNCED` | 874 | 872 |
| `OFF_ROUTE_WARNING_ON_ROUTE` | 50 | 51 |
| wrong turns warned / silent | 384 / 11 | 384 / 11 |

Landings (`compare.py`): nothing better or worse. By scenario, 429, 188, 145 and 3 gone; appeared, one each, all traced:

- `HOLD_LINE_CALL_REPEATED`, OTHH G2G via P6, P7, P6: the route passes the P6 line twice by design. Main spoke "Passing the P7 hold line." about a line behind the aircraft between the two P6 calls; without it the two read as one call repeated.
- `RUNWAY_CROSSED_UNANNOUNCED`, KATL 26L via D, C, B, D: the second crossing's hold line was already spoken about on the curve before it ("Crossing runway 08R at D.", 39 m off, before its countdown) and is latched, so "Continue" there brings no call. Main's call at that "Continue" was about another line 24 m behind the aircraft on the loop already flown, "Crossing" only through the not-found default. The pre-countdown call is the one in the KEWR AA report (a hold line spoken about before its own countdown), on main as well.
- `OFF_ROUTE_WARNING_ON_ROUTE`, PHNL 22L vacated at E: the aircraft starts facing the wrong way 5 m short of runway 04L's hold line, which is not on its route, and rolls at it while turning round: "Warning: approaching runway 04L at E, off route." Main said "Crossing runway 04L at E." there, about a line behind the aircraft, on a route that never crosses 04L.

Built in steps, each flown, each step's new findings traced: behind the nose alone lost the crossing call after "Continue" where the hold capture stops an aircraft beside the node (57 `RUNWAY_CROSSED_UNANNOUNCED`, KFLL 28R at A6); then "Passing" said about a start line behind (UUWW 24 at A5), a set-back line guarding no runway (OEJN H1), a node behind the search window called "Crossing" (KMCO 17R exit H7, four flights) and the nose swinging back past a line already flown (KATL 08R at D). The 56 left are main's as well: 46 "Crossing" calls about a line behind the aircraft whose runway the route still runs onto, reached more than the check's 150 m of travel later, after the U-turn at the route start (KORD 10L P4, at the load) or after a stop at the next line for the same runway (UUWW 01 at M1, 20 m into the turn); and 10 "Passing" calls at the load by an aircraft standing on a line its route's first node snapped past (OEJN 16R A2). One of each traced. Extending the 20 m start ignore to cover those was flown and rejected ([HLD-26]).

**Open:** a hold line spoken about before its own countdown, then latched, gets no call after "Continue" (KATL 08R at D; the KEWR AA report); a far-side line called "Crossing" for the next runway rather than its own (LTFM "Crossing runway 17R at B9B." before 17L, `RouteReachesRunwayAfter` takes any runway); and a route whose first node snaps behind an aircraft standing on a hold line (OEJN 16R A2); a line drawn as two nodes a few metres apart can still be spoken about once per node while its crossing is under way (only the node spoken about is latched). Not flown: fs2020, an OSM snapshot.

**Not yet flown, added after this round:** a passed line the aircraft heads back at off its route is spoken about as before (`IsPassedLineBehind`); "passed on the route" needs more than 25 m of route as well as three segments; both route walks test a segment by `RunwayShape.IsRunOntoBy` (a crossing drawn with no node on the pavement counts) and count a segment that starts within 150 m; the start hold is judged first only on the first segment; `HoldLineCall` reads every sentence of the frame's utterance, so the four checks above may count more on both builds. The tables above predate these; re-fly the standing modes before reading them as this branch's.

## Eleventh round — the countdown and the turn notices measured along the route (2026-11-27, 100 busiest fs2024 airports, plain navdata)

A pilot's KEWR departures to 04L via AA heard the hold-short countdown only 84 ft out ("Hold short runway 04L at AA in 75 feet. Stop."), "Passing the runway 04L at AA hold line." about the line they were to stop at, and turns with no advance notice. The FS2024 navdata splits AA into 28, 6.4 and 2.1 m pieces at the hold, and the countdown, the turn notices and "Slow for turn" measured only to the current segment's end. They now measure along the route ([RTE-41](invariants/taxi-position.md#rte-41)). New harness checks: `HOLD_COUNTDOWN_LATE`/`_MISSING`, `HOLD_CALLED_PASSING_BEFORE_STOP`, `TURN_NOW_WITHOUT_ADVANCE`, `TURN_ADVANCE_OTHER_WAY` (table above); `--trace KEWR:pick:40.6873459,-74.1859335,177.4:04L:A10,F3,A12,A,AA` replays the 21:29 departure's start (plain navdata routes it via A11, as A10 and F3 meet only in the online data, but the approach to the 04L hold is the same).

Flown before the tenth round's change ([HLD-26]) reached main: frozen main with the same harness against the change, `taxi wrongturn turnpicks departures missturn`, 19,258 scenarios, every one matched (flights per code):

| code | main | first version | final |
|---|---|---|---|
| `HOLD_COUNTDOWN_LATE` | 4,063 | 0 | 0 |
| `HOLD_COUNTDOWN_MISSING` | 5 | 0 | 0 |
| `HOLD_CALLED_PASSING_BEFORE_STOP` | 1,586 | 354 | 355 |
| `TURN_NOW_WITHOUT_ADVANCE` | 647 | 22 | 7 |
| `TURN_ADVANCE_OTHER_WAY` | 951 | 460 | 403 |
| `OFF_ROUTE_WARNING_ON_ROUTE` | 67 | 61 | 62 |
| `SPOKEN_DIRECTION_CONTRADICTS` | 7 | 4 | 4 |
| `WRONG_TURN_WARNED` / `_SILENT` | 384 / 11 | 375 / 12 | 376 / 12 |
| `MISSED_TURN_REROUTED` / `_NOTHING` / `_HELD_IN_CIRCLE` / `_STOPPED_AT_LINE` | 1,308 / 151 / 12 / 7 | 1,307 / 153 / 11 / 7 | 1,307 / 153 / 11 / 7 |
| `RUNWAY_ENTERED_UNHELD` / `HOLD_STOP_ON_RUNWAY_PAVEMENT` | 48 / 27 | 47 / 27 | 47 / 27 |
| `TAXI_NOT_ARRIVED` | 1 | 3 | 3 |

The final column adds what a review of the first version found: a junction judged on the route over 20 m or more either side and junction points less than 10 m apart taken as one (a corner drawn as two kinks, a piece drawn off the line and back), the "… now" bend measured from the segment the aircraft is on rather than the cursor's, and the incursion guard silent about the next stop only within the countdown's 600 ft along the route or once its countdown has begun. Silencing it only once a countdown line had been said was flown too and dropped: on a curving approach the node comes within the guard's 40 m before the outer tier (EFHK 34, 97 m along the route), and "Passing the runway 34 hold line." came 0.4 s before the countdown's first line, 367 flights. The one more `HOLD_CALLED_PASSING_BEFORE_STOP` (KCLE 10 at E2) is a second hold node of that line, no longer hidden by the cooldown the first version's silent latch opened while the stop was still far along the route; the one more `OFF_ROUTE_WARNING_ON_ROUTE` (ZSPD 34L at F1, between "Slow down" and "Stop") comes with the turn changes, the flight running 2 s apart from the first version's, and is the same with either guard gate.

- `TURN_ADVANCE_OTHER_WAY` by kind (first version): an advance notice on the OPPOSITE side to its "… now" 539 → 247 scenarios, "continue" before a left or right 403 → 205. The remainder is mostly two junctions close together (the notice of the first, the "… now" of the second) and turns just past the 20 degree "slight" threshold.
- The 354 `HOLD_CALLED_PASSING_BEFORE_STOP` left are a different case: a second hold node of the same line that the route passes before its stop, about 60 m out (LTFM "Crossing runway 17R at B9B." before the stop at B9B's other node). Silencing those would widen the "Crossing …" silence [HLD-21] forbids: not done.
- The wrong-turn and missed-turn changes are flights that differ in time, not in the guard: the countdown's "Slow down" and the earlier "Slow for turn" change the pilot model's speed. The new silent ones (YPDN 18 at A2, OETF 25 at D) were flown only with the change: main's pilot passed more than 4 m from the junction and never made the wrong turn. Both are silenced by the cooldown an on-route line ("Passing the runway 18 at A2 hold line.", another node of the same line) opens, which is [HLD-19] as it stands; PHNL 26R at S is the harness driving on while guidance holds short. The two new `TAXI_NOT_ARRIVED` (OETF Parking 43 → 07) pass the 07 hold after a 150 degree hairpin 3 m wider than main's flight, outside the 12 m capture.

**Measured and rejected:** skipping the next stop's node in the incursion guard (instead of latching it in silence). It freed the false off-route warnings the wrong "Passing" line's cooldown had masked, for a neighbouring line of the same runway in the countdown's last seconds (KSLC "Warning: approaching runway 16L at G, off route." between "Slow down" and "Stop" at H): `OFF_ROUTE_WARNING_ON_ROUTE` 67 → 98, and silent wrong turns 11 → 15. And the advance notice's word from the aircraft's heading alone, said up to 300 m out: "In 120 metres, continue onto taxiway L." before "Sharp left now, taxiway L." (KATL, a right-hand curve into a sharp left), and from a stand "In 110 metres, sharp right onto taxiway E2, 120 degrees." for a sharp left (KDTW D 3): opposite-side notices fell only to 303.

Replayed from the pilot's own frames through the app's real provider stack (the same online data: 115 segments, 1,783 m), the 21:29 departure now hears "Hold short runway 04L at AA in 300 feet." at 299 ft, "… in 175 feet. Slow down." at 168 ft, "… in 100 feet. Stop." at 90 ft, and "In 400 feet, sharp right onto taxiway F3, 90 degrees." 10 s before F3; the 17:33 one "… in 350 feet." at 338 ft and "In 450 feet, sharp right onto taxiway F2, 90 degrees.".

## The countdown for leg ends and cleared crossings (2026-11-27, 40 busiest fs2024 airports, plain navdata)

Merging the eleventh round into the Taxi UI Redesign branch left that branch's own stops counted down on their own segment alone: the end of a leg with no destination ([STR-25](invariants/taxi-steering.md#str-25)) and a stop the pilot was cleared across ([CLR-1](invariants/cleared-crossings.md#clr-1)). They now take the same along-route countdown (`RouteAhead.NextCountdownStop`), Continue before the line and the Hold short key judge the countdown's stop rather than the cursor's segment ([CLR-3](invariants/cleared-crossings.md#clr-3)), and the incursion guard latches that stop in silence as it does a hold short ([RTE-41](invariants/taxi-position.md#rte-41)). `CountdownTiming` now judges the `legends` and `crossthenhold` legs too, with the leg's end and a cleared stop as next stops (the table above). The same harness was built against the merge (`cd71b8875`) and against the carry-over; `VP_NOOSM=1 --airports 40 --taxi-per-airport 3`:

| code | merge | carry-over |
|---|---|---|
| `legends` (235 legs): `HOLD_COUNTDOWN_LATE` / `_MISSING` | 136 / 7 | 0 / 0 |
| `legends`: legs ended, at or past the junction, `LEG_ENDED_ON_RUNWAY` | 233, 0, 0 | 233, 0, 0 |
| `legends`: lines heard before the end, outer and Slow down / Slow down only / Stop only / none | 92 / 121 / 12 / 8 | 231 / 1 / 0 / 0 (and 1 the outer line only) |
| `crossthenhold` (50 legs): `HOLD_COUNTDOWN_LATE` / `_MISSING` | 35 / 1 | 0 / 0 |
| `crossthenhold`: `XC_CROSSING_SAID_BEFORE_ITS_SEGMENT` | 4 | 1 |
| `crossthenhold`: "Crossing runway" lines 0 / 1 / 2+, "cleared to cross" said, held at the runway | 0 / 50 / 0, 38, 0 | 0 / 50 / 0, 38, 0 |
| `taxi --clear-first-crossing` (328 flights): `XC_CROSSING_SAID_BEFORE_ITS_SEGMENT` | 26 | 11 |

Every other code is unchanged in all three runs. The `XC_CROSSING_SAID_BEFORE_ITS_SEGMENT` left are each about another hold node of the same runway (KLAX G: a node of the 25L line on the route just before the cleared stop, which the guard latched in silence), the second-hold-node case [HLD-21](invariants/runway-holds.md#hld-21) keeps. For a leg end or a cleared stop the pass check reads the countdown's own sentence, not the frame's whole utterance, which can carry a "Passing the … hold line." about another line the route passes (EGLL A9W: "Hold short taxiway A in 60 metres. Slow down. Passing the runway 27R at A9W hold line.", the leg ending 36 m on, read as a `HOLD_CALLED_PASSING_BEFORE_STOP` before the fix); hold shorts are judged as in the eleventh round.

## Twelfth round — a bridge between cleared taxiways that never meet (2026-11-27, 100 busiest fs2024 airports, plain navdata)

The pilot's KEWR 17:33 departure, "A11, A12, A, AA" to 04L: A11 and A12 share no node, the router's bridge joined A12 at the dead end of a curved branch, and the route turned onto F2 (never cleared) and went 95 m out and back ([RTE-42](invariants/taxi-bridge.md#rte-42)). `--trace KEWR:pick:40.6825084,-74.1851425,94.7:04L:A11,A12,A,AA` replays it on plain navdata, where F2 has no name: before, "In 120 metres, sharp right, 100 degrees. … Sharp right now." onto the link and "In 100 metres, sharp right, 180 degrees." for the turn back, 1,384 m to the hold; after, "Curving right." twice along A12, 1,240 m. New in the harness this round: the `ROUTE` result line and `compare_routes.py` (above); the "before" is that harness compiled against main 2a2d7d648 in a scratch worktree.

`taxi wrongturn turnpicks departures`, 17,480 scenarios matched (304 only before, 496 only after, nearly all Turn-pick scenarios whose pick changed outcome). Of 6,889 routes compared, 325 changed at 65 airports: 259 shorter (median 82 m) and 66 longer (median 50 m). Outside the Turn-pick scenarios 126 changed, 121 of them shorter; the longer ones are RJTT "F9, T, U" to 22 (+992 m: it now reaches the runway, where it stopped at U's far end after going out to U's dead end and back), KSLC "1WN, A4, B, A9, A, A11" to 16R (+566 m: it fell back to the shortest path before and now follows the clearance, with the gap-chained A9/A spur the old routes also have), KEWR gate to gate (+55 m), KABQ (+41 m) and KDAL (+37 m). 208 findings went and 30 came in the matched scenarios, each on a route that changed; none is a stop on runway pavement, an unheld runway entry or a flight that no longer arrives:

| finding | gone | came |
|---|---|---|
| `TAXI_OFF_PAVEMENT` | 50 | 0 |
| `TURN_ADVANCE_OTHER_WAY` | 27 | 3 |
| `TAXI_RECALC_WHILE_FOLLOWING` | 26 | 2 |
| `HOLD_WITHOUT_RUNWAY` | 21 | 0 |
| `LINEUP_LEAVES_MAPPED_PAVEMENT` / `LINEUP_NEVER_ALIGNED` | 16 / 9 | 0 |
| `CLEARANCE_ROUTE_DETOUR` | 13 | 0 |
| `RUNWAY_CROSSED_UNANNOUNCED` | 9 | 4 |
| `HOLD_STOP_ON_RUNWAY_PAVEMENT` / `RUNWAY_ENTERED_UNHELD` | 4 / 1 | 0 |
| `OFF_ROUTE_WARNING_ON_ROUTE` | 4 | 4 |
| `HOLD_LINE_CALLED_BEHIND` / `HOLD_LINE_CALL_REPEATED` / `HOLD_CALLED_PASSING_BEFORE_STOP` | 0 / 3 / 4 | 4 / 3 / 1 |
| `CROSSING_CALLED_NO_RUNWAY` / `HOLD_FAR_BEFORE_RUNWAY` | 5 / 1 | 1 / 1 |
| `HOLDPICK_NOT_SET`, `HOLD_NAMED_OTHER_RUNWAY`, `TURN_NOW_WITHOUT_ADVANCE`, `RUNWAY_ENTERED_UNHELD_WARNED`, `TURN_PICK_LOST_WHILE_FOLLOWING` | 8 | 0 |
| `TURN_MISS_*` (information) | 7 | 7 |

The new ones, traced: the routes that lost an out-and-back show guidance's known open cases on their new shape. ORBD 30 exit D to Parking 15 and 16 (164 m shorter, a route change while following gone) now hear "Crossing D, Runway 12." about a line 20 m behind at the start, the tenth round's open case; OTBH 16R exit E (416 m shorter, its stop on 16L/34R pavement gone) and KFLL "B8, A5, A" with a pick hear one hold line drawn as two nodes called twice; OIII gate to gate "D, B" (947 m shorter, four findings gone) hears "Passing" about a second node of the line it then stops at. YPDN 29 exit C4 now drives the cleared Y2 and is warned "off route" 13 m from it, past the 12 m tolerance; WIII and KJFK the same with a pick or a deliberate miss. KMIA, KABQ and KPIT enter a runway with its call spoken outside the check's window (information, hundreds on every build); RKSI holds 221 m before 15R/33L (the check's line is 220 m); MMMX and YBBN with a pick now drive a cleared taxiway they skipped and hear a notice of the other side; EBBR with a pick says "Crossing C6." about a taxiway's hold line. KSLC's route change while following is its A9/A spur.

Turn picks: 298 of 10,182 changed outcome, 250 of them picks refused before as "does not lead to", whose way on went out and back ([TURN-4]) and now does not: 120 honoured with an off-chart leg, 93 charted, 22 refused as a detour and 11 already made. Those are flown for the first time, and their flights bring 6 stops for a hold on runway pavement (LTFM "turn right onto C11" twice, MMMX B6, ZSSS H4, KLAX L twice) and 5 deliberate miss flights entering a runway unheld (KMSP, KATL), the category the Turn-pick round records; the same miss flights bring 16 of the 21 new `OFF_ROUTE_WARNING_ON_ROUTE` lines (the runway warning while the pilot drives the charted way 3 to 15 m off the loaded route, as main's 18 miss-flight lines do: KATL M6 "Warning: approaching runway 09L at M6, off route."); one counts as `TURN_PICK_SILENT` only because the pick's row moved when RTE-39 left the first taxiway out (OMAA "turn left onto K5": the off-chart note is in the summary). Wrong turns 376 warned and 12 silent, before and after.

The router alone, on every clearance at those airports whose consecutive taxiways share no node (64,297 routes), is measured in [RTE-42](invariants/taxi-bridge.md#rte-42): out-and-backs in controller-style clearances 817 → 92.

Measured and changed along the way, each flown: the seam's out-and-back unwinding first had no floor, and a route that went out to its first cleared taxiway and straight back lost it altogether, so route loading refused the lead-in (ZJHK gate to gate "S2, B, B10" began 430 m away in a straight line); and the route as scored was first kept only for a fallback, which left KABQ "R, Q, P, M, B" 35 m longer with Q undriven.

After review (the same day, the same modes, airports and navdata): the shared-loop test, the leg's beginning read back from `AppendBridgeLeg` and the summary sentence's meeting read from the route. Flown again from that build (its `MSFSBlindAssist.dll` byte for byte the one committed): of 7,314 routes none changed against the first version, every finding line is the same, and against main it is the run above, scenario for scenario. The router alone changes 9 of about 105,000 routes, listed in [RTE-42](invariants/taxi-bridge.md#rte-42). With online names on, the pilot's KEWR departure, replayed from an OSM and X-Plane Gateway snapshot of 2026-11-27 (the harness's `fetchosm` now saves both), built the graph the pilot flew (2,628 nodes, 5,624 edges, the same bridge from A11 node 394 to A12 node 693): "Taxiway A12." … "Curving right." … "Crossing taxiway F2." … "Curving right." … "In 80 metres, continue onto taxiway A.", 1,240 m to the hold, where the pilot heard the turn onto F2 and the turn back; the 21:29 departure "A10, F3, A12, A, AA" needs no bridge and is the pilot's route, 115 segments and 1,783 m.
