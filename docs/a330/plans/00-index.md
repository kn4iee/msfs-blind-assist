# iniBuilds A330: plan index, order, file ownership and parallel workflow

**Spec:** `docs/a330/spec.md` (read it first: section 2 where the aircraft stands, 3 the requirements by
plan, 4 the team's rules, 5 decisions and the owner's questions, 6 what is still to measure). Evidence:
`docs/a330/evidence/`. Background: `docs/ini-a330.md`, rules `.claude/rules/inibuilds.md`.
**Branch:** `feat/inibuilds-a330-fs2024` on kn4iee's fork (`<remote>` below is your name for it). Every
plan lands on this one branch. One pull request to `main` at the end (plan 12), opened only when the
owner says. Contributors rebase onto the branch and push straight to it; pushes there run no CI, so the
suite runs locally after the rebase ("Workflow for one plan"). No per-plan pull requests, changelog
fragments or "User guide:" lines: plan 12 writes them.
**Where these documents live:** `docs/a330/` on the branch. Plan 12 deletes the folder before the pull
request merges.
**The private reference and the display reader:** the display reader is a program built from the team's
private A330 reference: its code, its own plan, the notes and the probe scripts live there, and so does
anything that reads the aircraft's files. The owner gives access. This repository holds only the app's
side (plan 02) and never says how the reader works. A plan's code tasks run on captured frames and need
no access; a task that needs the reader program running says so.
**The owner** rules on what a plan leaves open (spec section 5, and questions found while working).

## Order

- **01, first and alone:** the review of the branch's commits and its fixes, then the seams and the file
  split every later plan builds on.
- **02, 04-07, early:** they run in parallel after 01 and never share a file. 02 is the app's side of the display reader;
  04 the panels; 05 the FCU and autopilot; 06 the First Officer; 07 the EFB.
- **03, 08-10, later:** each starts when the plan it builds on has landed (column "After"). 03 the MCDU,
  08 the ECAM displays and 09 the other displays stand on 02's contract, and their sim tasks on the reader program; 10 fills the remaining read-outs
  and app-wide members.
- **11, the flight:** one flight, gate to gate, once 03-10 have landed. It changes no code: each gap goes
  to the plan owning the file.
- **12, last:** integration, the pull request.

| # | Plan | Kind | After |
|---|---|---|---|
| 01 | `01-foundation.md`: an independent review of the branch's commits and its fixes; the hooks, the definition split by owner, the tests split by owner, the A330 in every all-aircraft test | first | — |
| 02 | `02-reader-client.md`: the app's side of the display reader: the frame contract, the client that starts the reader program and receives its frames, the status and its one spoken notice, the live display window, a replay source for tests | early | 01; its Task 6 needs the reader program |
| 03 | `03-mcdu.md`: the MCDU window on the reader, both sides, the scratchpad and FMS messages, the keys never verified; the pixel reader, its glyph file and training tool removed; a spoken message, not an AI read, when the reader cannot read | later | 02; its sim tasks need the reader program |
| 04 | `04-panels.md`: every input event accounted for, names and positions from the aircraft's own strings, a live sweep of every control, the missing panels, the order by daily use | early | 01 |
| 05 | `05-fcu-autopilot.md`: the FCU, autopilot and altimeter windows proven and fixed, knob call-outs, the autopilot members the app's automation uses | early | 01 |
| 06 | `06-first-officer.md`: the First Officer at parity with the other Airbus profiles, in the simulator's own procedure order, every write seen to take effect | early | 01; its Task 5 after plan 04's Task 6 |
| 07 | `07-efb.md`: every EFB page against the EFB checklist, fixtures and tests, the connection closed on a switch (the chocks and covers press left alone) | early | 01 |
| 08 | `08-ecam.md`: the E/WD and lower ECAM windows, with the ECAM keys in them, and their call-outs | later | 02; its sim tasks need the reader program |
| 09 | `09-pfd-nd-displays.md`: the FMA's words read from the PFD, the PFD window, the ND, ISIS and DCDU windows, with their controls in them, and their call-outs | later | 02; its sim tasks need the reader program |
| 10 | `10-readouts-parity.md`: the missing hotkeys and definition members, the comma-decimal tests | later | 01; its display-fed read-outs after 08 and 09 |
| 11 | `11-flight.md`: one flight gate to gate with the First Officer; the measurements only a flight gives | audit | 03-10 |
| 12 | `12-integration.md`: handoffs, the release carrying the reader program, the chocks and covers press removed, rules, docs, guide, hotkey guide, changelog, the pull request, delete `docs/a330/` | last | all |

## File ownership

Work only in the files your plan owns. A change you need elsewhere: ask the plan owning the file while it
is open (its claimant), and write the change in your "Handoff to plan 12" (file, change, why) if it is
still needed when you land. "A → B" means plan B takes the file over once A has landed; never two plans
at once. Once a plan has landed and no open plan takes its files over, a later plan may change one of
them for a fix its own task needs: say so in your execution record. Paths are under `MSFSBlindAssist/`
unless they start with `docs/`, `tools/`, `tests/`, `guide/`, `changelog.d/` or `.claude/`.

| File or pattern | Owner |
|---|---|
| Every file the branch's commits touch (`git diff --name-only $(git merge-base origin/main HEAD) HEAD`) | 01 while it runs, then the owners below |
| `Aircraft/IniA330Definition.Hooks.cs` (new), `Aircraft/IniA330Definition.Handlers.cs`, `Aircraft/A330/A330SharedKeys.cs` (new), `Aircraft/A330/A330CalloutRows.cs` (new), `Forms/IniA330/A330MonitorManagerForm.cs`, `MainForm.IniBuilds.cs` | 01, then frozen |
| `Aircraft/A330/Display/**` (new), `Aircraft/IniA330Definition.Displays.cs` (new), `MainForm.IniBuilds.Displays.cs` (new), `Forms/IniA330/A330DisplayWindow.cs` (new) | 02, then frozen for 03, 08 and 09 to build on (a fix they need: ask 02's claimant, else handoff) |
| `Aircraft/IniA330Definition.Mcdu.cs` (01 creates it by moving), `Aircraft/A330/Mcdu/**` (new), `Aircraft/A330/A330CalloutRows.Mcdu.cs`, `Forms/IniA330/IniA330CduForm.cs`, `CduFormProfile.cs`; to delete: `Aircraft/A330/A330CduScreenReader.cs`, `A330CduPopout.cs`, `A330CduGlyphLibrary.cs`, `A330CduFrame.cs`, `CduLayout.cs`, `Resources/a330-cdu-glyphs.bin`, `tools/a330-cdu-ocr/**` | 03 |
| `Aircraft/IniA330Definition.cs`, `Aircraft/IniA330Definition.Panels.cs` (01 creates it by moving), `Aircraft/A330/A330ControlMap.cs`, `Resources/a330_control_map.json`, `tools/a330-gen/**` | 04 |
| `Aircraft/IniA330Definition.Fcu.cs` (01 creates it by moving), `Aircraft/A330/A330Fcu*.cs` (new), `Forms/IniA330/IniA330AutopilotWindow.cs` | 05 |
| `Aircraft/IniA330Definition.FirstOfficer.cs` (01 creates it by moving), `FirstOfficer/IniAirbus/**` | 06 |
| `Aircraft/IniA330Definition.Efb.cs` (01 creates it by moving), `Resources/coherent-a330-efb-agent.js`, `tools/a330-efb-test/**` (new) | 07 → 12 (the chocks and covers press) |
| `Aircraft/IniA330Definition.Ecam.cs` (01 creates it by moving), `Aircraft/A330/Ecam/**` (new), `Aircraft/A330/A330CalloutRows.Ecam.cs`, `Forms/IniA330/A330EwdWindow.cs`, `A330SdWindow.cs` (new) | 08 |
| `Aircraft/IniA330Definition.FlightDisplays.cs` (01 creates it by moving), `Aircraft/A330/A330Fma.cs`, `Aircraft/A330/Pfd/**`, `Nd/**`, `Isis/**`, `Dcdu/**` (new), `Aircraft/A330/A330CalloutRows.Pfd.cs`, `.Nd.cs`, `.Datalink.cs`, `Forms/IniA330/A330PfdWindow.cs`, `A330NdWindow.cs`, `A330IsisWindow.cs`, `A330DcduWindow.cs` (new) | 09 |
| `Aircraft/IniA330Definition.Readouts.cs` (01 creates it by moving), `Aircraft/A330/A330Readouts*.cs` (new) | 10 |
| `docs/a330/evidence/NN-*.md`, `docs/a330/plans/NN-*.md` | plan NN: its own evidence file and plan file (its execution record included) |
| `docs/a330/plans/00-index.md` | 01 (the "Base commits" line) and the owner ("Adding a plan"); otherwise frozen |
| `docs/a330/spec.md` | frozen; the owner alone fills in section 5's Answer column |
| `docs/ini-a330.md` | by section, so two plans never edit one: 02 a new "Displays"; 03 "MCDU" (its two bullets); 04 "Panel layout" and the controls bullets; 05 the FCU, FCU dialogs and Altimeter STD bullets; 06 "First Officer"; 07 the EFB and chocks bullets; 08 "ECAM pages"; 09 "Flight modes"; 12 the rest and any new section |
| `guide/**`, `HotkeyGuides/iniBuilds_A330_Hotkeys.txt`, `docs/invariants/inibuilds.md`, `.claude/rules/**`, `docs/first-officer.md`, `docs/development.md`, `docs/tooling.md`, `changelog.d/**`, `CLAUDE.md` | 12: plans hand off their lines (a rule as its proposed one-line form and full text). 01 alone adds the new path globs to `.claude/rules/inibuilds.md` (its Task 6) |
| `MainForm*.cs` (except the two `MainForm.IniBuilds*.cs`), `Services/**`, `SimConnect/**`, `Accessibility/**`, `Settings/**`, `Forms/**` outside `Forms/IniA330/`, `Aircraft/IAircraftDefinition.cs`, `Aircraft/BaseAircraftDefinition.cs`, every other aircraft's files, `MSFSBlindAssist/MSFSBlindAssist.csproj`, `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj`, `MSFSBlindAssist.sln` | nobody on this branch: frozen; handoff requests to 12. The named exceptions: 01's seam calls in `MainForm.AircraftSwitch.cs` and the all-aircraft test lists; 03 removes the one `a330-cdu-glyphs.bin` line from the csproj; 06 adds the A330's rows to the cross-profile First Officer tests; 07's additive change to `ForIniA330` in `SimConnect/CoherentPmdgEfbClient.cs`; 10's `Services/DefAnnounceMuteSets.cs` line if its Task 3 needs one |

**Tests** follow their code. Plan 01 moves the existing tests into `tests/MSFSBlindAssist.Tests/A330/`,
split by owner: `IniA330DefinitionTests.cs` (frozen after 01), `A330ControlMapTests.cs` 04,
`A330FcuTests.cs` 05, `A330FmaTests.cs` 09, `A330ReadoutTests.cs` 10, `A330CduScreenReaderTests.cs` 03
(which deletes it). `tests/MSFSBlindAssist.Tests/FirstOfficer/IniAirbusFoTests.cs` is 06's.
`InputEventReadRequestIdTests.cs` and `TransitionAltimetersTests.cs` are shared: frozen. A plan's new
tests go in new files named for its feature under `tests/MSFSBlindAssist.Tests/A330/` (06:
`tests/MSFSBlindAssist.Tests/FirstOfficer/IniAirbus*.cs`), so the A330 rules load. New fixtures are new
files under `tests/MSFSBlindAssist.Tests/A330/Fixtures/`, named `pNN_<what>.json`; never overwrite one a
test pins. No test, code or CI step may read a file under `docs/a330/`: plan 12 deletes it (copy what a
test needs into a fixture).

## Measuring in the sim

A task that needs the sim says **[sim]** (on the ground) or **[in flight]**; one that needs the owner's
ears, hands or say-so says **[owner]**. Spec section 4's rules apply to every step. Claude runs sim checks
itself: reads and cockpit keys through the simconnect MCP (calculator path, `execute` mode for a write,
one read a call), displays through the reader program's `frame` and `capture` commands, the EFB through
`tools/coherent-eval.ps1 -Title iniEfbA330` with MSFSBA closed. The human is asked only for what needs
ears, a sign-in or a flight. A plan records what it measured in its own
`docs/a330/evidence/NN-<slug>.md`, with the date and the simulator's version (the `FlightSimulator2024.exe`
file version). Each plan ends with "In-sim checks (owner)": a few numbered scenarios, only what needs the
owner, never one a feature.

## Who is on which plan

Each plan's "Execution record" (at its end) holds its claim and status, so claiming never edits a shared
line. Plan 01 runs first and alone; every other plan is claimed by its Task 0, before any other task:
check the remote copy, claim on your branch, push that one commit to the branch. A claim counts only once
it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's
claim reaches the branch first, the plan is theirs. One person per plan. To see where every plan stands on
the remote: `git fetch <remote>`, then
`git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a330-fs2024 -- docs/a330/plans/`.
A plan may start when the plans in its "After" column say landed.

**Adding a plan.** The owner (or someone at the owner's word) adds a new plan as `NN-<slug>.md` with the
next free number, and its row and its files to this index's tables, in one commit pushed alone. A file
moves to the new plan only while the plan owning it is not claimed; otherwise the new plan takes it over
once that plan lands ("A → B").

## Workflow for one plan

1. Fetch the branch and branch from it at the base commit for your plan's kind (bottom of this file):
   `git switch -c a330-pNN-<slug> <base-sha>`. An agent works in a worktree inside the repo
   (`.claude/worktrees/a330-pNN`), says so before creating it, and removes it when done
   (`dotnet build-server shutdown` first). This step is part of every plan's Task 0, which then pushes the
   claim before any other task.
2. Build once before testing: `dotnet build MSFSBlindAssist.sln -c Debug` (never the bare csproj, CORE-1).
   Close MSFSBA first if it runs (CORE-3), or test with `--artifacts-path` ("Running a plan with
   subagents").
3. Work the tasks in order (TDD). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj
   -c Debug -p:Platform=x64 --filter "FullyQualifiedName!~ClaudeRulesHookTests"` (that class is slow and
   tests only what plans 01 and 12 change; they run the whole suite). The generator (04):
   `python -I -m unittest discover -s tools/a330-gen -p "test_*.py"`. The EFB harness (07): `npm ci` then
   `npm test` in `tools/a330-efb-test`. Every commit ends with a `Plan: NN` trailer line (next to its
   `Co-Authored-By` line), so `git log --grep "^Plan: NN"` lists the plan's commits.
4. Push whenever a task is done and green: `git fetch <remote>`, then
   `git rebase <remote>/feat/inibuilds-a330-fs2024` (files are disjoint and your claim is already on the
   branch, so the rebase does not stop; in Task 0, a stop on the claim lines means someone claimed first;
   anywhere else, someone left their files: stop and sort it out with them).
5. After the rebase, on the combined code: build and the suite green (this run is the only check that the
   plans work together); `git diff --name-only <remote>/feat/inibuilds-a330-fs2024...HEAD` lists only files
   you own.
6. Before the push, check what goes out: with access to the private reference, run its push check on the
   commits you are about to push and reword every hit; without access, re-read your diff and your commit
   messages against spec section 4, "The display reader". A push is public the moment it lands and cannot
   be taken back. Then `git push <remote> HEAD:feat/inibuilds-a330-fs2024`. If it is rejected, go back to
   step 4. Never force-push the branch. Update your execution record in the same push; when the plan is done, fill in
   its "Handoff to plan 12" and set **Status** to landed (or waiting on owner, naming what waits).
7. Steps for the sim need MSFS 2024 with the A330. Leave them unticked if you lack it, and list them under
   "Waiting on the owner".

**Merging `main` into the branch** happens only before the pull request (plan 12) or when a plan needs
`main`'s work. Whoever does it says so to the claimants first, merges (never rebases), runs the whole
suite, `ClaudeRulesHookTests` included, and pushes the merge alone.

## Running a plan with subagents

Plans 01-10 are written for superpowers:subagent-driven-development (a fresh implementer per task, a
reviewer after each); plans 11 and 12 are live work, worked with superpowers:executing-plans.

- One worktree per plan, inside the repo (`.claude/worktrees/a330-pNN`). In a fresh worktree, build the
  vPilot plugin once first
  (`dotnet build plugins/MSFSBlindAssist.VPilotPlugin/MSFSBlindAssist.VPilotPlugin.csproj -c Debug`), or the
  test build fails with MSB3030.
- Test with `--artifacts-path <worktree>/bin/a330-pNN` so parallel builds and an open MSFSBA never collide.
  The folder must be inside the repo or worktree: about 28 tests find `MSFSBlindAssist.sln` by walking up
  from the test DLL and fail otherwise.
- Each implementer gets the task's text, the plan's Global Constraints and Review Focus, the spec sections
  the plan cites and this index's "Shared conventions", and works only in the task's files. Where a task
  says "Steps 1-5" with test names, those are the usual five: write the named tests, run them failing,
  implement, run the suite, commit with the given message.
- Task 0 [controller], the claim, is the controller's own and is never given to an implementer.
- The controller never dispatches a **[sim]**, **[in flight]** or **[owner]** task or any step that writes
  to the sim to an implementer: it runs them itself with the sim's owner, or lists them under "Waiting on
  the owner" and works the rest.
- Two plans at once: separate worktrees and separate scratch folders; never two agents on one plan or one
  file.
- When the plan's tasks are done: one whole-plan review checks the ownership diff (step 5), that every
  Review Focus line has its test, and that the handoff and execution record are filled in.

## Records: who worked a plan and how it went

- **Each plan's "Execution record":** claim and status, who worked it, start and landing dates and commits,
  the test result at landing, tasks left, what waits on the owner, in-sim results, what changed from the
  plan, and the rulings made while working (question; answer; who decided; Claude's rulings are the
  owner's to reverse).
- **Each plan's "Handoff to plan 12":** what changes for a pilot (guide chapter, hotkey guide, changelog
  category), proposed rule lines, requests for frozen files, and gaps found in other plans' files.
- **`docs/a330/evidence/NN-<slug>.md`:** what the plan measured in the sim.
- **Plan 12** compiles the records into the pull request description and names the last commit holding
  `docs/a330/` before deleting it; the pull request merges with its history (`gh pr merge --merge`).

## Shared conventions

- CLAUDE.md applies in full. Read a file before changing it (CORE-16); the A330 rules INI-1 to INI-9 load
  with its code. Spec section 4 holds the team's rules.
- Every A330 write goes through the definition's existing write paths (`SetInputEvent`, `PushEvent`,
  `PressByValue`, `Write` with its sequence prefix; invariant fixed-point numbers, VAR-5; [INI-1]); never
  a write of any other kind. Nothing in this repository reads the A330's displays itself: the app only
  receives frames from the reader program, which never changes anything in the sim.
- A display window's press of a cockpit control is the panel's own press
  (`IniA330Definition.PressFromWindow`, plan 01), never a write of its own.
- What is spoken is the value read back; a direct interaction is confirmed once, with what the screen
  reader cannot say (CORE-7); a background change speaks once, after a silent first reading, through the
  non-interrupting announce.
- A display read is spoken or shown only from a COMPLETE frame (plan 02's `A330Frame.Complete`), and a
  call-out only once two complete frames in a row agree.
- Colours and hidden list rows (spec M14, M15): the reader does not give them yet. Build and test on the
  2026-11-20 fixtures, which stand for what the reader will give; at run time a frame with no colour gets
  no colour word and drives no call-out that needs a colour, and a frame whose `HiddenRowsLeftOut` is false
  shows and speaks nothing that could be a hidden row; the window says what it waits for. Plan 02 holds
  the rule; plan 12 checks both before the pull request.
- Every new call-out has its Ctrl+M row: a variable's through `ExcludeFromMonitorManager` false; a display
  call-out's in its area's `A330CalloutRows.<Area>` list (plan 01's seam). Each announcing plan has a test
  that a muted row says nothing.
- Anything spoken or formatted gets a de-DE or sv-SE test (`TestCulture.Run`); typed numbers go through
  `NumberParsing.TryParseTyped` (CORE-17). Cockpit words are said as the aircraft writes them.
- Status text in windows is a read-only `TextBox` (VAT-13); background pushes marshal through a try/catch
  `SafeBeginInvoke` (A380C-6); SimConnect is touched only from the UI thread (SIM-5, SIM-20).
- Tests never depend on another plan's control-map labels: build in-memory rows. A display test reads a
  fixture captured with the reader program's `capture` command: a frame in plan 02's contract form.
- New source files go under `MSFSBlindAssist/Aircraft/A330/`, `Aircraft/IniA330Definition*.cs`,
  `Forms/IniA330/` or `FirstOfficer/IniAirbus/`, where a rule glob loads; a new file anywhere else needs a
  glob in a rule file (`ClaudeContextBudgetTests`), which is plan 12's: ask it first.
- Names and order come only from the sources spec section 4 allows. Public text (code, comments, docs,
  commit messages) never names the private reference's location, never says how the display reader works,
  and never names a contributor as an example of a mistake or another add-on as the source of a name. The
  simulator's checklist text may be quoted (spec D13, owner 2026-11-21).

## Evidence

Each plan's `docs/a330/evidence/NN-<slug>.md`; `docs/ini-a330.md` (measured 2026-10-16 to 2026-11-19); the
two data files the private reference hands over (the controls' names; the order of controls and flow steps).

## Base commits

- Plan 01 starts from the commit that added this folder ("docs(a330): plans for the parity work").
- Early plans (02, 04-07) start from the commit plan 01 records here: e5c9d2b6a (plan 01's last code commit,
  2026-11-21).
- Later plans (03, 08-12) start from the branch once their "After" plans have landed; record the commit you
  started from in your plan's execution record ("Started").
