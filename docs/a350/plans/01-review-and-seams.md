# A350 Review and Seams Implementation Plan (plan 01, first and alone)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The commits nobody independent has reviewed (`345bfd07f` to `7e6d5fbd1`) are reviewed and their real findings fixed test-first, and the three seams later plans add to exist, so plans 02-11 start in parallel from reviewed code without editing one line twice.

**Architecture:** First a review that gets the code, not anyone's conclusions; then one fix commit per Critical or Important finding, each with its failing test. Then three seams: Ctrl+M rows for new display call-outs, listed by area in a partial class whose parts the area plans own; the E/WD monitor muted by key, so plan 04 can add rows to it; and a hook for each good captain EFIS draw, so plan 08 can read the ND in the background without editing plan 07's monitor.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 3.3) and 4; `docs/a350/plans/00-index.md` "File ownership".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- The review range is the branch's own commits: `git log --first-parent --no-merges e19171d2c..7e6d5fbd1` (12 commits: `345bfd07f`, `01fe0fffb`, `c62d1a5e6`, `847bb11eb`, `1a42974ca`, `ccd29e1b9`, `a9226acf2`, `4a31320cc`, `1ea79f697`, `3b2520828`, `3fd7bf50d`, `7e6d5fbd1`); the merge `ecb6bc21d` brings `main`'s own reviewed work and is not in it.
- While 01 runs it owns every file those commits touch (`git log --first-parent --no-merges --name-only e19171d2c..7e6d5fbd1`), plus the seams' files below. Nobody else works on the branch's code until 01 records its base commit.
- A Minor finding is not fixed here: it goes in the execution record with the plan owning its file (the index's table).
- The seams add no behavior: the Ctrl+M dialog lists the same rows in the same order, nothing new is said.

## Review Focus

- A review that only re-reads the commit messages finds nothing: the reviewer gets the diff and the files around it, never this plan's guesses (Task 1).
- Output N (`a9226acf2`, `Services/NavRadioFormat.cs`) formats numbers: a comma-decimal culture must not change what is said (Task 1 checks a de-DE test exists; Task 2 adds it if not).
- `NavRadioFormat` and the `MainForm.Announcers.cs`/`MainForm.Hotkeys.cs` changes are shared code: every aircraft that reaches them is checked, not only the A350 (Task 1).
- An added Ctrl+M row with a key a variable row already has would list one setting twice (Task 3 test).
- The EFIS hook runs on the monitor's pool thread: what it calls must never touch SimConnect (SIM-5, SIM-20) (Task 5 test and doc comment).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [x] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/01-review-and-seams.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [x] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 01`, with the `Plan: 01` trailer.
- [x] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The review

**Files:** none changed; findings go in this file's execution record.

- [x] **Step 1: Run an independent correctness review** of the range, with superpowers:requesting-code-review (or the repo's `code-review` skill at high effort) on `git diff e19171d2c 7e6d5fbd1` restricted to the files the first-parent commits touch. Give the reviewer the diff, CLAUDE.md, `.claude/rules/inibuilds-a350.md` and the rule files the touched files load, and these areas to read whole, not the commit messages' claims: the final review's fixes (`1a42974ca`); Output N (`a9226acf2`: `Services/NavRadioFormat.cs`, its callers in `MainForm.Announcers.cs` and `MainForm.Hotkeys.cs`, every aircraft that reaches them); the vertical display's waypoint scales (`a9226acf2`, `A350NdPage`, `A350NdMap`, `A350GroundDisplays`); VERT REV's speed limit row (`1ea79f697`, `A350MfdPage`); one MFD value under two headings (`847bb11eb`); the ND's target names (`ccd29e1b9`); the other aircraft's refusals naming the A350 (`01fe0fffb`); the taxi lead (`c62d1a5e6`); the visual guidance biases (`3b2520828`); the First Officer's `ExtendWithinKts = 3` (`3fd7bf50d`, `A350FOAutoManager`).
- [x] **Step 2: Check CLAUDE.md's "Check the whole of what you touched"** over the same range: each spoken or formatted change has a de-DE or sv-SE test; each shared change holds on every aircraft using it; each display fix was checked over its whole page.
- [x] **Step 3: Record every finding** in "Review findings" below (file:line, what fails, the input that shows it, severity Critical / Important / Minor, owning plan), and say which the reviewer verified against the code.

### Task 2: Fix the Critical and Important findings

**Files:** the finding's own; its test in the existing test file of that code (the index's "Tests" list) or a new `A350<Feature>ReviewTests.cs`.

- [x] **Steps 1-5 per finding:** a failing test that shows the finding, the fix, the suite green, one commit each: `fix(a350): <what the finding was>` with the `Plan: 01` trailer. A finding that needs the sim to show it goes to its owning plan instead, written there as a handoff request in this file's record.

### Task 3: Seam: Ctrl+M rows for new display call-outs, by area

**Files:** Create `Aircraft/A350/A350CalloutRows.cs`, `A350CalloutRows.Ecam.cs`, `A350CalloutRows.Pfd.cs`, `A350CalloutRows.Nd.cs`, `A350CalloutRows.Datalink.cs`; modify `Forms/A350/A350MonitorManagerForm.cs`; test `tests/MSFSBlindAssist.Tests/A350/A350CalloutRowsTests.cs`.

**Interfaces:**
- Produces, for plans 04 (Ecam), 07 (Pfd), 08 (Nd) and 22 (Datalink), each owning its own part's file:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A350;
  internal static partial class A350CalloutRows
  {
      /// Every row the plans add, in area order; the rows that existed before them stay in A350MonitorManagerForm.BuildRows.
      public static IEnumerable<(string Key, string Label)> All => Ecam.Concat(Pfd).Concat(Nd).Concat(Datalink);
  }
  // A350CalloutRows.Ecam.cs (likewise Pfd, Nd, Datalink), empty until its plan fills it:
  internal static partial class A350CalloutRows
  {
      public static readonly IReadOnlyList<(string Key, string Label)> Ecam = Array.Empty<(string Key, string Label)>();
  }
  ```
- `A350MonitorManagerForm.BuildRows` appends `A350CalloutRows.All` after its last row (`A350MemoryWarning`), each as `new MonitorRow(key, label)`.
- [x] **Steps 1-5** (tests `Added_rows_follow_the_existing_display_rows`, `No_added_row_shares_a_key_with_a_variable_row_or_another_added_row`, the second built on a definition's real variables and the four lists); commit `refactor(a350): the display call-outs' added Ctrl+M rows are listed by area`.

### Task 4: Seam: the E/WD monitor muted by key

**Files:** `Aircraft/A350/Display/A350EcamMonitor.cs`, `MainForm.AircraftSwitch.cs` (its `StartA350DisplayService` only); tests `A350EcamMonitorTests.cs`.

**Interfaces:**
- `public A350EcamMonitor(Func<string, bool> muted, Action<string> speak, Func<DateTime>? now = null)`; `Observe` asks `_muted(MuteKey)`; plan 04 asks its own keys the same way.
- `StartA350DisplayService` passes `key => Settings.SettingsManager.Current.A350DisabledMonitorVariablesSet.Contains(key)`.
- [x] **Steps 1-5** (tests `The_ecam_row_mutes_by_its_key`, and the existing tests moved to the keyed constructor); commit `refactor(a350): the E/WD monitor asks its mutes by key`.

### Task 5: Seam: each good captain EFIS draw handed on

**Files:** `Aircraft/A350/Display/A350PfdMonitor.cs`; create `Aircraft/IniBuildsA350Definition.Hooks.cs`; `MainForm.AircraftSwitch.cs` (its `StartA350DisplayService` only); test `tests/MSFSBlindAssist.Tests/A350/A350EfisHookTests.cs`.

**Interfaces:**
- `A350PfdMonitor`: `public Action<A350DrawCapture, DateTime, bool>? CaptainEfisDrawn { get; set; }`, raised once per tick with the tick's good captain EFIS draw, its time and whether the call-outs are settling after a load, inside the monitor's `Read("ND", ...)` guard, after the FMA read.
- `IniBuildsA350Definition.Hooks.cs`:
  ```csharp
  public partial class IniBuildsA350Definition
  {
      /// The captain's EFIS as the display monitor drew it this tick (a pool thread: never SimConnect, SIM-5, SIM-20).
      internal void ObserveCaptainEfis(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted,
          Action<string> speak) => ObserveNd(capture, at, settling, muted, speak);

      /// Plan 08's (IniBuildsA350Definition.Nd.cs); nothing until it is written.
      partial void ObserveNd(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted, Action<string> speak);
  }
  ```
- `StartA350DisplayService` sets `a350PfdMonitor.CaptainEfisDrawn` to call `a350.ObserveCaptainEfis` with the keyed mute and the posting speak it already gives the monitors, only while `currentAircraft` is that definition.
- [x] **Steps 1-5** (tests `Each_good_captain_efis_draw_is_handed_on_once`, `A_trapped_efis_draw_is_not_handed_on`, `A_hook_that_throws_costs_only_its_own_read`); commit `refactor(a350): each good captain EFIS draw is handed to the definition`.

### Task 6: Land

- [ ] **Step 1:** Full suite green after the rebase (the index's workflow 4-5); the Ctrl+M dialog's rows for a definition are the same before and after (Task 3's first test).
- [ ] **Step 2:** In `00-index.md` "Base commits", replace "(plan 01 fills this in)" with the commit that lands this plan; commit `docs(a350): plan 01's base commit` with the trailer, push, set **Status** to landed.

## Review findings

Task 1's reviewer (Claude Opus 5.5, read-only, 2026-10-31) read the 12 commits' own diffs and each touched area whole, and
modelled the new vertical display reader over every captain EFIS fixture. Verdict: ready with fixes. Strengths it named:
`NavRadioFormat` fixes a comma-culture bug for every aircraft on Output N without changing en-US output; the FMA's
color-aware parts, the draw engine's per-class count and the MFD naming fixes are correct and tested; the 3 kt flap window
leaves VFE-next protection intact.

Fixed in Task 2 (test first, one commit each):
- **C1, Critical, VERIFIED.** `A350GroundDisplays.cs:170-177, 206-207`: the vertical display's waypoint loop gives VD
  places to range-map names drawn behind the VD and to the VD's runway label: "Vertical display: MCT, cyan: 31 NM, 24600
  feet" at FL300 (`efis0_cruise_wxt`), "VCBI, white: 1.2 NM, 1000 feet" for a field at about 26 ft (`efis0_takeoff_ready`);
  5 of the 7 fixtures where it speaks. With it, M1 (clipped names and names past the scale read with places).
- **I1, Important, VERIFIED.** `A350GroundDisplays.cs:157`: the MSA is read only while its name sits at y 900 or more; above,
  it falls into the waypoint loop: "MSA, magenta: 0 NM, 4800 feet" for a 3800 ft MSA (`efis0_omdb_landed_clipped`).
- **I2, Important, VERIFIED.** No comma-decimal culture test covers the VD's spoken decimals or its scale parsing.
- **I3, Important, VERIFIED.** `IniBuildsA350Definition.Announcements.cs:448`: the lever word's "on the ground only" check
  reads `_calloutOnGround`, true by default for a flight loaded in the air: "All thrust levers 50 percent N1" in flight.
- **I4, Important, VERIFIED.** `A350FOAutoManager.cs:209-210, 279-280`: the 10 s go-around flap hold times from the
  approach's first pass over CLB (a seen-once latch): an engine-out approach at MCT gets no hold in its go-around, and the
  landing gate can step a flap into it.
- **M8, fixed in the final review's wave**, as Review Focus line 2 asks of this plan: `NavRadioFormat.cs:24-25`'s
  glideslope figure had no comma-culture test. MainForm's generic Output N formats through the same helper, so one test
  pins every aircraft.

Minor, for the plans owning the files (not fixed here):
- M2 (plan 09): `A350MfdPage.cs:520, 527`: the CMS tab's box names can shift, or throw on a fourth labelled field. PLAUSIBLE.
- M3 (plan 21): `HotkeyGuides/iniBuilds_A350_Hotkeys.txt:141-142` still describes the old Output N; its "NOT ON THE A350
  YET" block (405-409) is out of date.
- M4: the strict flap speed waits are still described in `docs/first-officer.md:16` (plan 21) and
  `docs/inibuilds-a350.md:409` (plan 07).
- M5: the doc's DB423 example (6.8 NM against the test's 6.9): corrected with C1.
- M6 (plan 08): `TaxiTurnLeadTests.cs:26-28` ties the A350's lead to the A330's instead of pinning 1.7.
- M7: three comments left on the wrong code: `A350HotkeysTests.cs:75-76` and `IniBuildsA350Definition.Hotkeys.cs:341-343`
  (plan 07), `A350MfdPageTests.cs:28-29` (plan 09).
- M9 (plan 07): `IniBuildsA350Definition.Hotkeys.cs:182, 343-353`: the A350's Output N says nothing on an exception other
  than a timeout.
- M10 (plan 08): `A350NdMap.cs:93-99`: a flight ID drawn without a speed is no longer read as one. PLAUSIBLE.
- M11 (plan 07): `A350FmaAnnouncer.cs:57`: the FMA's flash wait covers the autothrust column only. PLAUSIBLE.
- M12 (plan 05, a design point for the owner): `A350FOAutoManager.cs:296`: with the 3 kt window, managed speed holding
  green dot can bring out flaps 1 anywhere in the approach context below 5000 ft. PLAUSIBLE.
- M13 (plan 21): `MainForm.Hotkeys.cs:326`: the DCDU refusal says the A350 has a DCDU window; its Ctrl+Shift+D is the
  ATC mailbox.

Handed on: measure the A350's `FlareAltitudeBiasFt` in the flare attitude (plan 18); check the FBW family for the same
green-dot hold (plan 19's Task 1). Outside the range, for the owner: the PMDG 737's NAV radios dialog speaks
`Nav1FreqMHz` in the PC's culture (`PMDG737Definition.cs`, about line 6990).

## Handoff to plan 21

- **Pilot-facing:** none from the seams. From the fixes:
  - The vertical display reads only its own texts:
    - the range map's names drawn behind it (MCT, BASUR, MIN) no longer get places;
    - the runway's label reads "Vertical display: VCBI 22, white: 1.1 NM, 0 feet", placed from its two lines;
    - text the clip hides is not spoken;
    - the MSA is read from its line wherever it is drawn ("MSA: about 3800 feet, magenta"), never from a runway ident.
  - After a flight loaded in the air, the levers through TLA 12 no longer say "All thrust levers 50 percent N1".
  - The First Officer's 10 s go-around flap hold starts from the go-around's own lever movement. An engine-out approach flown at MCT keeps it, and so does a go-around begun just before the approach context arms.
  - **User guide:** none, because `guide/airbus-aircraft.md:822`'s vertical display sentence still holds, and the guide describes neither the lever word's ground condition nor the hold.
  - **Hotkey guide:** none.
  - **Changelog:** none of its own; the unreleased A350's aircraft fragment covers these.
- **In-sim plan lines (CORE-5):**
  1. A flight loaded in the air, both levers through TLA 12 together: nothing at 12. On the ground: "All thrust levers 50 percent N1".
  2. A go-around below 1500 ft from an approach flown at CLB, levers to TOGA while still sinking: no flap moves for 10 s. Tests pin the MCT-to-TOGA and one-engine cases, so no engine-out flight is needed.
  3. An unticked FMA, E/WD or FMS-message Ctrl+M row still mutes. Task 4 rewired the mute and Task 5 shares it.
  4. On the stand at VCBI with the ND at 40 NM: the vertical display's MSA from its line and the "VCBI 22" line.
- **Docs left for plan 21:** `docs/first-officer.md:16`'s strict flap waits (M4). Its go-around hold sentence was corrected here.
- **Rule proposals:** none. Each guardrail added here has a test whose failure explains it:
  - the vertical display's draw order;
  - the MSA's line first;
  - the lever word asking the cache;
  - the hold's own stamp.
- **Gaps in other plans' files:**
  - `A350LeverSpeech.Thrust` still defaults `onGround` to true; only tests use the default (plan 11).
  - `A350EcamMonitorTests.cs:90` pins the one key the monitor asks; relax it when keys are added (plan 04).
  - `ObserveNd`, when it gets a body (plan 08):
    - returns once the definition is disposed;
    - tolerates a null `Pfd` or `Displays`;
    - stays fast, since it runs before the FMS-message read.

    Plan 08 describes the hand-on in its own ND section of `docs/inibuilds-a350.md`.
  - The vertical display (plan 08):
    - The time markers' place offsets.
    - arc40's clipped "20000, white" still reads as a row.
    - `efis0_omdb_landed_clipped` reads only the first segment of its MSA line. The line steps to about 5400 ft from 13 NM, but a pilot hears 3800 ft.
    - PLAUSIBLE: range-map names under the vertical display still read as plain rows through `Rest` ("MCT, cyan", BASUR, MIN). If the display covers them, a sighted pilot never sees them. Settle it with a capture.
  - `A350SpeechThreadTests` (plan 03) pins the shape of `StartA350DisplayService`'s posting speak, so its four copies stay. A hoist with an in-post `currentAircraft` check would change that test, and the existing monitors' speech in an aircraft switch's window.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** claimed
- **Claimed by:** Robin Kipp (robin24), 2026-10-31
- **Worked by:** Robin Kipp with Claude, with subagents (superpowers:subagent-driven-development)
- **Started:** 2026-10-31, `feat/inibuilds-a350` at eeb325324 (the plan set's last commit)
- **Landed:**
- **Tests at landing:**
- **Tasks:** 0-5 done, 6 at landing; `git log --grep "^Plan: 01"` lists the commits.
  - 0, the claim: `2f8a446ce`.
  - 1, the review: recorded at `3386b5b52`.
  - 2, the fixes:
    - C1 with M1: `cae5f5757`.
    - I1: `9f77eeb97`.
    - I2: `0eaad800c`.
    - Their review's round: `7832b53bb`, `41fcb6a24`, `7d06c43c8`, `85692e71d`.
    - I3: `b30630c2b`.
    - I4: `d6d23437b`, with the VFE-next pin `6ee0eb1f7`.
  - 3, the Ctrl+M seam: `8f1b0b52d`.
  - 4, the keyed mute: `ab6e9c147`.
  - 5, the EFIS hand-on: `bdcc53f41`.
  - The record: `e9b6a60e7`.
  - The final review's fix wave: `3538db526`, `879f198f6`, `9eb11fe28`, `66fd07d4e`, `446d8ceb9`, `07b460ee7`, `7f6249056`, `b0f0bc008`.
- **Waiting on the owner:**
  - the four in-sim lines in the handoff above;
  - M12, a design point for plan 05;
  - outside the A350: the PMDG 737 NAV radios dialog speaks `Nav1FreqMHz` in the PC's culture.
- **In-sim results:** none yet; the checks go into plan 21's in-sim plan.
- **Changed from the plan:**
  - **Where:** worked in the session's worktree `.claude/worktrees/a350-deploy-a41d04`, not `a350-p01`, and pushed once, at landing.
  - **Task 2** ran as three dispatches, each with its own review: the vertical display reader, the lever word, and the go-around hold.
  - **The vertical display's review round** also fixed four Minors in its own code:
    - clip-hidden texts left out of the plain rows;
    - `RunwayAt` requires both lines;
    - the MSA line has a height bound;
    - `Value` parses through `NumberParsing.TryParseExternal` (CORE-17), pinned under de-DE and sv-SE.
  - **Tests beyond the briefs:**
    - an MSA figure of its own color that the line still beats;
    - a runway line stroked twice;
    - the go-around after an earlier MCT excursion;
    - the hold's baseline rule.
  - **I3's helper** also replaced its two identical copies (`ObserveMinimums`, `ObserveBtv`), and `SeedThrust` takes a required `onGround`. Its test sits in `A350AnnouncementTests.cs` (plan 11's file), beside the definition's lever-delivery tests.
  - **I4's hold** reads the throttles in the manager; `A350StateEvaluator.cs`, outside this plan's files, is untouched.
  - **Task 4's keyed constructor** also changed `A350PfdDisplayTests.cs` (one line) and three call sites in `tools/A350Probe` (plan 03's tool, which builds on its own).
  - **Task 5:**
    - a private `Settling(at)` helper in `A350PfdMonitor`;
    - one `muted` local in `StartA350DisplayService`, shared by the E/WD monitor, the PFD monitor and the hook;
    - a fourth test pinning the settling after the FMA read.
  - **Task 3:** a doc clause on `BuildRows`.
  - **The final review** (Opus, read-only) asked for fixes: 3 Important, 6 Minor. One wave fixed them:
    - The go-around hold also stamps while the context is unarmed, so TOGA set just before it arms still holds. Its resets went, since the readings refresh every sample. Tests pin one engine's lever at idle.
    - Review Focus line 2's glideslope test (M8).
    - The Ctrl+M guard covers every A350 row, keys and labels.
    - The hand-on stops with its monitor.
    - The lever seed's definition-level test.
    - The runway line under de-DE and sv-SE.
    - Two doc corrections: `docs/first-officer.md:16`'s hold, and the vertical display's clip phrase.

    Not taken: hoisting `StartA350DisplayService`'s posting speak with an in-post aircraft check (see the handoff's last gap).
- **Rulings made while working** (question; answer; who decided):
  - The review package: the merge or the commits? The first-parent non-merge commits' own diffs. (Claude)
  - Task 3 beside Task 1's read-only review? Yes; Task 3 touches no reviewed file. (Claude)
  - M1, a Minor, fixed here? Yes, with C1: it is the same name filter. (Claude)
  - The vertical display review's Minors: Task 1's Minors go to their plans, but these were in code this plan wrote. Fixed in the fix loop. (Claude)
  - The MSA: line or figure first? The line first, and a figure only of the name's color. (Claude)
  - The forced files above (the probe, `A350PfdDisplayTests.cs`, `A350AnnouncementTests.cs`)? Kept in plan 01: plan 01 runs alone. (Claude)
  - I4's 30 and 40: the hold stamps on `FO_GA_THRUST`'s rising edge or the higher lever crossing 40. (Claude)
  - Push after each task, as spec section 4 says, or once at landing? Once, at landing: plan 01 ran alone, and nobody builds on its intermediate commits. The commits sat on one machine until then. (Claude; for the owner to confirm)
  - The final review's Minors? Fixed in its wave where they sit in plan 01's files, which freeze or pass to other plans at landing. (Claude)
  - The posting speak's hoist and in-post check? Left out: plan 03's thread test pins the speak, and the check would change existing speech. (Claude)
