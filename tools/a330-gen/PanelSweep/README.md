# A330 panel sweep

A live sweep that drives every cockpit control of the iniBuilds A330 the planner plans, through the app's own
write decision and write routes, reads each one back until it settles, and records a verdict a row (plan 04,
Task 4). Before its first write it journals every control's starting value, and it can put them all back from
that journal after a crash or Ctrl+C.

It is a standalone console tool, not part of the solution (CORE-4). It compiles the app's own files, linked and
never copied: the map (`A330ControlMap.cs`, with the map JSON embedded as the app embeds it), the write decision
(`A330Write.cs`, `A330Units.cs`), and the planner and runner core (`MSFSBlindAssist/Aircraft/A330/Sweep/`). So it
tests what the app ships, and the decisions it makes are pinned by xUnit (`A330PanelSweepPlannerTests`,
`A330PanelSweepRunnerTests`). This folder holds only the simulator link, the wiring and the dry-run report.
`IniA330Shim.cs` restates the two members of `IniA330Definition` that `A330Write.cs` names (the press hold, which a
test keeps equal to the app's, and the parking brake, which the sweep never writes).

## Build

```
dotnet build tools/a330-gen/PanelSweep -c Debug -p:Platform=x64
```

The exe is `tools/a330-gen/PanelSweep/bin/x64/Debug/net10.0-windows/A330PanelSweep.exe`. It ships both
`SimConnect_msfs_2020.dll` and `SimConnect_msfs_2024.dll` and copies the one for the running sim to
`SimConnect.dll` when it connects, as the app does. The deny list next to the exe is the one it always uses.

## Run

Look first, without the sim. A dry run never connects:

```
A330PanelSweep.exe --dry-run
A330PanelSweep.exe --dry-run --pass apu-bleed
```

It lists every control in the map's order with its planned steps, or skipped with its reason, then the counts,
a rough running time, the proofs, the snapshot size, the system state list and the MCDU brightness knobs. With
no sim it assumes each control starts at its map's first position (a guard closed, a continuous control at its
minimum); the live run plans from the aircraft's real starting reads.

The live sweep, and the restore:

```
A330PanelSweep.exe
A330PanelSweep.exe --pass apu-bleed
A330PanelSweep.exe --only A330_PACK1,A330_PACK2
A330PanelSweep.exe --from A330_FUEL1L1
A330PanelSweep.exe --restore journal-2026-12-01-1430.tsv
```

- `--pass apu-bleed`: only the packs, the engine bleeds and the crossbleed (`A330_PACK1`, `A330_PACK2`,
  `A330_ENG1_BLEED`, `A330_ENG2_BLEED`, `A330_XBLEED`), for the second pass with the APU bleed on. It refuses to
  start unless the APU bleed (`A330_APU_BLEED`) reads its On value. The APU is denied: start it and switch its
  bleed on by hand first.
- `--only K,K`: only these control keys. `--from K`: that key and every one after it in the map's order, to go
  on after the sweep stopped.
- `--vars FILE`: more L:vars for the snapshot, one name a line (`#` comments).
- `--out DIR`: where the files go (default: the current folder).
- `--restore JOURNAL`: puts every control of a journal back, last first (a switch before the guard over it),
  each by its own starting value, reads each back, and lists any that did not hold. It writes `restore-<date>.tsv`.

### Conditions for the live run (the owner runs it)

- The owner is at the keyboard, listening to the aircraft.
- The iniBuilds A330 is loaded, **on the ground, parked, engines off, external power on**.
- MSFSBA is closed, so nothing else writes to the cockpit.

The tool asks for `YES` before it connects. Then, before anything is written, it refuses if the sim says the
aircraft is airborne or an engine is running, or if the aircraft lists fewer than half of the map's input events
(the A330 is not loaded). It probes the MobiFlight calculator channel with a test value written to an L:var of
its own (`L:A330PANELSWEEP_PROBE`) and read back; if that does not answer, the sweep still runs, input events are
still written, and every press and calculator line is `not sent`.

The dry run on 2026-11-27 planned 151 units: 149 controls (223 positions, each written twice; 2 key presses; 14
guards opened) and the two families of keys (18 presses), with 132 skipped (18 of them the families' own keys),
about 18 minutes plus a minute to read the starts. Each write that moves nothing waits 5 s
more before it is called dead. **Ctrl+C** stops once the control in progress has been put back. A second Ctrl+C
stops sooner, but never inside a write: it waits until the write in progress has ended (a key's push is always
followed by its release), then exits, and `--restore` with the journal puts back what was left. In `--restore`, a
Ctrl+C finishes the control in progress and stops; a second one stops after the write in progress.

## What it does to a control

1. Before the first write: reads every control's start (its read, and an input event's own `B:` value), plans
   from those reads, and writes the journal. A control any of whose keys cannot be journaled (its input event's
   own value did not read) is skipped: what cannot be put back is never written. Then two snapshots 2 s apart:
   a variable that moves between them moves on its own, and is left out of every side effect.
2. For each planned step, through `A330Write`, the app's decision, and the app's routes:
   - an input event by SetInputEvent, with the hash from the aircraft's own enumeration (an event the aircraft
     does not list is `not sent`, never `dead`);
   - a key's press as its own `_Push`, then after its hold `_Release`, over the calculator, as the app sends it;
   - a calculator line as the map gives it.
   Never a `_Set` through the calculator for a control the app sets by input event. Writes are at least 400 ms
   apart. A denied control is never written, whatever asks. A guard is opened before its switch; when the guard's
   write is not a pass (dead, mislabeled, not sent, not read), the switch is never written behind it: the row says
   `skipped: its guard did not open (<verdict>)`, the control is put back, and the sweep goes on.
3. After each write: the control's read and its proof (where the runner can read it) are polled every 100 ms
   until each has settled (two reads in a row agree, and the reading has moved; an unmoved reading counts as
   settled only once the limit passes) or 5 s pass. The time each took is recorded. A snapshot of every `INI_*`
   variable the map knows (reads, lamps, states, proofs, about 177) is taken before and after.
4. Each position is written twice in a row. The second write is watched for twice the time the first took (at
   least 1.5 s, at most 5 s); if it moves the read again, the input event toggles. A first write that moved
   nothing is not written again.
5. Then every key the control's steps wrote is put back from the journal: the control itself, then its guard,
   then an ECAM page key. An input-event control is written its input event's own starting value, never its
   read translated through the pairing being tested (an inverted pairing would put it on the wrong side), at
   most twice, and holds once both that value and its read are back. A key is pressed until its state reads its
   start, at most `pressesToCycle - 1` times; a key with no count is a push-on/push-off key, so one press. A family
   of keys that share one state is put back as one unit (below). **If a put-back did not hold, the whole sweep
   stops** (every later control reads `not reached`), and the summary says what to put back by hand and the key
   to go on from: `--from <key>`, the control the stop cut short, or else the first one not reached.

## Keys that share one state

The 13 ECAM page keys all select the page `INI_ECAM_ACTIVE_PAGE` shows, and the radio panel's 5 radio keys all
select the radio `INI_SELECTED_RADIO_CPT` shows. Pressing one of these keys again never undoes it, so they are not
swept one by one. Each family is ONE unit:

- The journal keeps the family's start once (the page or radio selected when the sweep began).
- Each key is pressed in turn, and its row says what it selected and its verdict. The key whose page or radio is
  already selected is not pressed (pressing an ECAM page key a second time can take the display back to another
  page); its row says `already there`.
- Then the family is put back by pressing ONE key: the key this run saw select the start, or else the key the map
  says selects it. If that press does not bring the start back, nothing more is pressed and the sweep stops.
- If the start is a page or radio none of the family's keys selects (or it could not be read), the family is
  skipped before any press, with the reason `its start (<value>) is not a page any of its keys selects`.

What the map says each key selects comes from the status row that names the shared state's values (the system
display page, the radio panel's selected radio), matched to the key by its name ("ECAM FUEL" selects FUEL).

If the sweep stops on a family, the summary says which page or radio to put back. Put it back by hand: press the key
for that page or radio once in the cockpit, and only that key. Then go on with `--from` and the key the summary
names. `--restore` treats a family the same way: it presses at most the one key the map names for the start, and if
that does not hold, or no key is known, it stops there and presses nothing more. Put that one back by hand, then run
`--restore` again for the rest.

## Proofs

A position's name is proven only by an indication the control does not write itself: a read of the control's own
variable can read back exactly what was written while the aircraft ignored it. The runner reads a proof of kind
`lamp` or `simvar` itself (shown while it reads over 0.5; it should show at the position whose read value is the
proof's `when`). It judges the proof against both the position the control was in and the one written: the name is
proven only when the proof moved into the state the written position should show. Between two positions the proof
cannot tell apart (a memo off at both), it says nothing and the read decides; a proof that should have changed and
did not makes the write dead; a proof not read before the write is never counted as having moved. A proof of kind `ewd_memo` or `sd_page` needs the display reader, which this tool does not
reach: its row says `needs the display reader`, its verdict is taken from the read alone and is never `proven`,
and **the owner checks that proof by ear in the session**. Today only predictive windshear (`A330_WX_PWS`, the
PRED W/S OFF memo) has a proof, of that kind. Wiring the display reader in is a follow-up, once Step 0's proof
table exists.

## Verdicts

- `proven`: the proof moved into the state the position's name gives.
- `inverted`: the proof moved the other way: it followed the other name.
- `read-only`: the read moved to the position written, and no proof the runner can read tells this position from
  the one before.
- `dead`: nothing moved in 5 s (not the read, not the proof, no other variable); or the proof should have changed
  and did not while the read alone moved (the aircraft may have ignored the write).
- `lagged`: a pass (`proven` or `read-only`, named in the note) that settled only after 1.5 s. Still a pass; its
  time goes to plan 06.
- `toggle`: the same position written a second time moved the read again.
- `not sent`: refused before it left (an input event the aircraft does not list, the calculator not answering,
  the simulator refusing the packet). Never called dead.
- `mislabeled`: the read went to another position than the one written, or to none of them, or it did not move
  while other variables did, or the proof changed between two positions where it should not (Step 5's
  "mislabeled": a position's input or value, or the control's read, is wrong).

Other rows: `already there` (the control read the target, or a family's key was the one selected: nothing
sent), `not read before the write`, `at start` and `restored` (a put-back), `RESTORE FAILED: ...`,
`skipped: ...` (`skipped: its guard did not open (...)` among them), `not reached (...)`, `interrupted (...)`,
`error: ...`.

## The files

`sweep-<yyyy-MM-dd-HHmm>.tsv`, tab-separated, UTF-8, one row a position written (twice), a press, a put-back, or
a control not swept. No file of an earlier run is ever overwritten: a second run in the same minute writes
`sweep-<stamp>-2.tsv` and `journal-<stamp>-2.tsv` (then `-3`, and so on), the pair always with the same suffix, and
`--restore` writes `restore-<stamp>.tsv` the same way. Every number is written in the invariant culture. The columns: `key`, `label`, `position`,
`sent`, `read back` (after the first write), `settle ms`, `proof before`, `proof after`, `verdict`, then `note`,
`second read back`, `proof settle ms`, `aircraft value`, `side effects` (every other variable that moved:
`NAME before -> after`, a candidate proof; radio panel 1's NAV backup is the known side effect) and
`system state`.

The system state recorded with each row is these controls' own reads: external power A and B (`A330_EXT_A`,
`A330_EXT_B`), the generators (`A330_GEN1`, `A330_GEN2`, `A330_APU_GEN`), the bleeds (`A330_APU_BLEED`,
`A330_ENG1_BLEED`, `A330_ENG2_BLEED`) and the hydraulic pumps (`A330_HYD_GREEN`, `A330_HYD_GREEN_ENG2`,
`A330_HYD_BLUE`, `A330_HYD_YELLOW`, `A330_HYD_GREEN_ELEC_OFF`, `A330_HYD_BLUE_ELEC_OFF`,
`A330_HYD_YELLOW_ELEC_OFF`).

The aircraft value is recorded for the two continuous controls that have one besides their read: the landing
elevation knob (`A330_LDG_ELEV`, with `INI_LANDING_ELEVATION`) and the weather radar gain (`A330_WX_GAIN`, with
`INI_WXR_GAIN`). Each continuous control goes to its minimum, middle and maximum.

`journal-<same stamp>.tsv`: one `start` line a control, written before the first write. An input-event control:
its event and that event's own value, its read and the read's value. A key: its event, its `pressesToCycle`, its
state and the state's value. A family of keys: `family`, its state and the state's start, once. Numbers are
round-trip. A comment line names the variables that moved on their own.

## Before the owner's session

- **The MCDU brightness knobs** (`A330_CPT_MCDU_BRT`, `A330_FO_MCDU_BRT`): their minimum blanks that MCDU, which
  the MCDU and display readers need on. They are journaled and put back like any other; if the sweep stops while
  one is at its minimum, run `--restore` with the journal.
- **The ECAM page keys and the radio keys** are two families, each swept and put back as one unit (above). If the
  sweep stops on one, press the one key the summary names in the cockpit, and nothing else of that family.
- **The guard pairings** are put back by their input events' own values, like every other control; the guard is
  closed after its switch has been put back.
- The proof of predictive windshear is checked by ear (above).
- The second pass, with the APU bleed on, is `--pass apu-bleed`.

## The deny list

`denylist.txt`: the controls the sweep never writes, one `key TAB reason` a line, the reason saying what
writing it would do. A test fails on a key that is not a control of the map or one of the definition's own
controls, and another on a removed line for a control that cannot be undone or moves the aircraft, so a rename
cannot silently drop a control's protection. The planner also skips a switch whose guard is denied, and never
sweeps a guard row on its own: its switch opens it first and it is closed last, unless it was open at the start.

Controls the aircraft does not offer yet (gravity gear extension, passenger oxygen, the evacuation command, the
cargo and cabin doors) are comment lines at the end of the deny list, to be denied the day they appear.

## Not tested in the sim

The planning and the runner's decisions are pinned by the xUnit tests on in-memory controls and a fake
aircraft, and the dry run runs; the live half has never run against the simulator. Its first run is the
owner's (plan 04, Task 4 Step 4).
