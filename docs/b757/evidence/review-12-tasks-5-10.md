# Plan 12, Tasks 5-10: whole-branch review

Reviewed: `git diff 0e4770086 29091df7c` (Task 5) and `git diff 247096edb 787811c62` (Tasks 6-10), against plan 12
(Tasks 5-10, Global Constraints, Handoff, Execution record), `flight-12.md`, `panel-order-12.md`, `panel-audit-12.md`,
the ledger from "Task 5a: dispatched" on, CLAUDE.md and the 757 / variable-definitions / core-simconnect rules.

Verified here:
- The map regenerates byte-identical: `python -I tools/b757-gen/generate_b757_map.py "<package>" --out <scratch>/map.json`,
  then `cmp` against `MSFSBlindAssist/Resources/bbs757_control_map.json`: identical. I wrote nothing to the repo path.
- Full suite (isolated artifacts path): 18,991 passed, 0 failed. Generator tests: 275 OK. `tools/b757-gen/PanelSweep`
  builds with the new `Bbs757Control.Variant` / `Bbs757Variants` (0 warnings, 0 errors).
- `git status --short` was clean before and after. This file is under the git-ignored `.superpowers/`.
- BlueBird's package, read only: the shipped aircraft titles (all ten carry " PW" or " RR"); which code reads each test
  var (`BBS752.wasm` strings and every XML/JS/HTML file).

## Strengths

- **The spring press is safe on every path I traced.** The hold waits on `CancellationToken.None`, so a profile switch
  stops only the re-writes and the release still goes (`HoldSpringAsync`, Overhead.cs:647-669). A throw mid-hold
  releases at once. Each release is read back and sent again (`ReleaseSpringAsync`), then retried in the background on a
  lost link. One hold per switch (`_springHolds`) keeps a test's two buttons from fighting. Everything resumes on the UI
  context, so the gate never races.
- **Real time bounds the hold** (fix round 1): `max(asked, elapsed)` against `NowMs`, each gap capped by what is left of
  the hold and never under 1 ms. No re-write is sent once the hold's time has passed. Tests cover 40 ms waits, a 1 s
  stall and the trims' own 1 s hold (SpringControlsTests.cs:1785-1848).
- **`hold_ms` is checked strictly** (generator `apply_hold_ms` and `load_curation`). It stops the run on a stale node, a
  node also under kinds, a control that is not a button, a missing press or release, and a hold outside 500-10,000 ms.
  The run reports each hold.
- **The engine-type filter is small and pure.** `Bbs757Variant` matches PW or RR as a whole word in any case. Neither,
  both, or no title means Unknown, which shows everything. The section lists are rewritten in place and the control
  lists never change. `IsPanelControlVisible` reads the live title on every panel build, so a load from RR to PW is
  right on the next panel with no section change. The tests use the shipped map and every shipped title.
- **Speech follows CORE-7.** A P61 test says only "{test} done" and nothing during the hold. The lamp test says only its
  read-back ("Lamp test on" or "off"), never also "Lamp Test pressed". Every phrase and RPN string is invariant, and
  `DonePhrase` and the test codes are pinned under de-DE, sv-SE and tr-TR.
- **The record is good.** Frozen-file edits (BBS757Definition.cs, Bbs757DefinitionTests' Engines array) are recorded
  with the owner's approval and named in the handoff. The curation notes explain each move.

## Issues

### Critical

None.

### Important

**I1. The two "Duct Leak Fuel Quantity Test" buttons cannot test anything, yet each says "… test done".**
`MSFSBlindAssist/Aircraft/BBS757/Bbs757SpringControls.cs:409-410` (`DuctLeakFuelQtyTest`) and `:447-448` (its two buttons).
- **What is wrong:** No system in BlueBird reads `L:BBS757_DUCT_LEAK_FUEL_QTY_P61_TEST_SW`. `BBS752.wasm` holds the other
  seven P61 test vars by name (EQUIP_COOLING, YAW_DAMP, WINGAI_WINDOWHEAT, GRNDPROX_HYDGEN, TO_AND_LAND_CONFIG,
  AIRDATA_STALL_LEFT and _RIGHT) but not this one. The only XML that names it is `BBS_752_BASE_INTERIOR.xml`, twice:
  the init write at :30304 and its own switch template at :30330. No JS or HTML file names it. The flight measurement
  agrees ("nothing seen in flight", flight-12.md:136). Every other test var a button here holds (fire, CVR, squib,
  TCAS, oxygen, autoland, weather radar, ILS, radio) is read by the WASM.
- **Why it matters:** A pilot presses "Upper Duct Leak Fuel Quantity Test 3 s", waits 3 s and hears "Upper duct leak
  fuel quantity test done", but no test ran. That is the case [DBG-7] warns about: the switch has its own source but no
  systems-side reader, and a write to it sticks anyway. It is also the reason Task 1 left the weather radar knobs out
  ("nothing in the package reads them"). Their "Upper/Lower" names are a guess too ("BlueBird names neither position").
  The owner would spend ground-test time on two buttons that cannot pass.
- **How to fix (the owner's call):**
  - Preferred: drop the switch from `TestSwitches` and its two buttons from `TestButtons`, then update the counts that
    tests pin (16→14 buttons, 8→7 test reads, the "twenty five variables" test). Add a curation note in the style of the
    weather radar note.
  - Or, for sighted parity (the cockpit switch does move): keep one button with an honest phrase, such as "Duct leak and
    fuel quantity switch moved; BlueBird runs no test".
  - Either way, take it off the ground-test list in the Execution record.

### Minor

**M1. The Fire protection order contradicts the plan text and the option the owner picked, and the owner's list of open
decisions does not carry it.**
- **Where:** plan `12-panels-remaining.md:170` ("the fire and overheat tests stay in Fire protection after the handles"),
  `tools/b757-gen/curation.json:1091-1094` and the ledger's Task 7 ruling (progress.md:163).
- **What ships:** the three tests come first, then the handles. The curation note says so, and this has been the order
  since Task 4. The ruling ("the owner's question was the panel, the every-day rule governs order") is defensible and
  I do not overturn it.
- **What is wrong:** the plan still says the opposite, and the handoff's "For the owner to decide" (:247) does not list
  it, although the ledger says "flagged to the owner".
- **Why it matters:** for a blind pilot in an engine fire, Fire protection now opens with three test buttons before
  "Engine 1 Fire Handle Unlock". A Space pressed on the first control fires a 3 s test bell over the real warning. That
  is exactly what the owner's chosen wording would have avoided.
- **Fix:** correct the plan's Task 7 bullet to what shipped and add this order to "For the owner to decide". If the
  owner wants the handles first, it is a one-block reorder of `control_order["Fire protection"]`, then regenerate.

**M2. The Execution record stops at Task 9.**
- **Where:** plan `:256` ("Tasks 6-9 done on the branch, not yet pushed"), `:262` ("Tasks: 0-9 done (Tasks 4-9 added …)")
  and `:261` ("Tests at landing" gives 18,600 / 267, the Tasks 0-5 landing).
- **Fix:** add Task 10 and the current numbers (18,991 / 275), and name Task 10 in "Changed from the plan" (the frozen
  Engines test edit is recorded only in Task 10's checkbox line and in the handoff).

**M3. The handoff misses other plans' texts that name panels that no longer exist, one of them pilot-facing.**
- **Where:** `docs/b757/plans/02-gaps/03-panel-writes.md:132`. Plan 16 will turn its "Pilot-facing" changelog line,
  which says the emergency lights are on "the Overhead miscellaneous panel", into release notes. That panel no longer
  exists: the emergency lights are now on "Emergency lights and passenger oxygen".
- Also: `02-control-map.md:95`, `:146` (an in-sim step naming "Overhead miscellaneous") and `:195` ("Engine control and
  yaw damper", "Switch guards").
- **Fix:** add them to the handoff's "Frozen or other plans' files" bullet (:243).

**M4. Two doc comments are now untrue.**
- `MSFSBlindAssist/Aircraft/BBS757Definition.cs:278-279` says "a section's panels come in the owner's every-day order".
  Since Task 9 they follow the first action of BlueBird's Simplified Procedures, with the Forward panel pinned to Brakes.
- `MSFSBlindAssist/Aircraft/BBS757/Bbs757SpringControls.cs:60` says "The 757's spring-loaded switches the panels hold
  for one second". The P61 tests hold three seconds (the third bullet says so, but the summary's first line does not).
- Both are one-line edits.

**M5. In-sim check 1 names the wrong opening controls for Electrical.**
- **Where:** plan `:231` says "Electrical: Battery, EXT PWR, …".
- **What is wrong:** the panel opens "Battery Guard, Battery, EXT PWR" (each guard just before its switch; pinned in
  Bbs757ControlMapTests).
- **Why it matters:** the owner, checking by ear, will hear a different first control than the check promises.
- **Fix:** reword the check.

**M6. The 3 s held map tests send their release once and never read it back.**
- **Where:** the existing `PressAsync` path, `BBS757Definition.Interaction.cs:192`. The plan prescribes this path, so no
  change is needed for this merge.
- **What is wrong:** Task 5a made 19 test buttons hold 15 times longer (3,000 ms, not 200). Several now leave a loud
  state if their single release write is lost: the fire tests' bell, fire lights and master warning; the oxygen test
  sliders. Every one of them has a readable state var (the ledger's Task 5a note: state_var is set on all of them).
- **Suggestion:** a follow-up can read the state var after the release and resend once, as the spring buttons do. Note
  the Radio Test's release writes only an `O:` var, already a ground check.

## Declined to judge

- Whether the P61 tests should be refused in flight (the stall test shakes the stick in flight): the plan says nothing,
  BlueBird allows it, and a sighted pilot can do it.
- No spoken "started" for a P61 or held test: CORE-7 and the plan specify "{test} done" (or "{label} pressed") only.
- The order of each P61 switch's two buttons (Stall Warning before Air Data, unlike the 0-first order of the others): it
  is the brief's order, and the plan does not fix it.
- Task 9's per-panel derivation from the Simplified Procedures PDFs: the Task 9 re-review re-derived it. I did not
  reread the PDFs.
- Task 7's placement of each node by 3D position: the audit's evidence, which the Task 7 review re-derived.
- The 793 KB map diff line by line: I regenerated the map instead (byte-identical).
- A busy UI thread stretching a hold past its time: inherent to UI-context awaits (the sends must stay on the UI thread,
  [SIM-20]'s reasoning). The overrun is bounded by one gap plus the stall, and the release always follows.
- Whether MobiFlight's WASM merges two commands sent in the same sim frame (a 30 ms P61 stream beside another write):
  unmeasured. The trims' release is verified either way. See Recommendations.
- A PW's first section list after connecting naming the RR-only panel: recorded, and handed to plan 16 (MainForm is
  frozen).
- The guide, `BBS757_Hotkeys.txt`, `docs/b757.md`, CORE-19 and the changelog: plan 16's files and the shared branch's
  one-PR workflow. The handoff names them.
- "1 s" and "3 s" in the labels (NVDA reads "one s"): already an open owner decision.
- Two quick lamp-test presses can both read "Lamp test off": rare, and each read-back is true when it is made.
- The ledger's deferred minors (silent presses during the background release, the retry's 60 s counting only waits, a
  switch-back cutting a hold short, the log flood, the Radio Test's `O:` release, the Bbs757ControlMap.cs:124 comment,
  the panel-order totals). I judge none worse than recorded. The "announcer throws, so the retry never starts" gap is
  smaller than recorded: `ScreenReaderAnnouncer.Announce` and `AnnounceImmediate` catch internally
  (ScreenReaderAnnouncer.cs:232, :290), so only a test double can throw there.

## Recommendations

1. Resolve I1 before the ground tests, so the owner does not test two buttons that cannot pass.
2. Put M1 to the owner in one line: "Fire protection: the tests before the handles (now), or the handles first?"
3. Hardening for the recorded gap: in `PressSpringAsync` (Overhead.cs:544-546), start `KeepReleasingAsync` before
   `AnnounceImmediate`. It is a two-line swap that makes the safety independent of speech.
4. For the ground tests: press a trim button while a P61 test runs, and check debug.log that the trim's first release
   lands (no second release write). If a P61 test flickers through the app's path (the measurement used a harness),
   try a 20 ms re-write gap before anything else.
5. When plan 10's and plan 12's title parsers are unified (handoff), keep plan 10's `L:B757_ENG_VARIANT` fallback for
   the PW/RR filter too. A third-party MSFS 2024 livery whose title omits PW/RR now falls back to showing both pairs.
   That is safe, but it loses the feature.
6. Bring the Execution record up to Task 10 (M2) in the same commit as M1, M3 and M5.

## Assessment

**Ready to merge: With fixes.** No safety or concurrency defect: the spring press always releases, is bounded by real
time, verifies its release and survives a profile switch or a lost link. The map reproduces, and every suite is green.
Before Tasks 6-10 go to `B757`, resolve I1 (two P61 buttons that cannot test, yet report "done"). M1-M5 are record and
wording fixes that belong in the same commit.
