# iniBuilds A350: plan index, order, file ownership and parallel workflow

**Spec:** `docs/a350/spec.md` (read it first: section 2 the first flight's findings, 3 the handoff's remaining work by
plan, 4 the team's rules, 5 the owner's questions, 6 the manual by page range). Evidence: `docs/a350/evidence/`.
Background: `docs/inibuilds-a350.md`, rules `.claude/rules/inibuilds-a350.md`, `tools/A350Probe/README.md`.
**Branch:** `feat/inibuilds-a350` on kn4iee's fork (`kn4iee/msfs-blind-assist`; `<remote>` below is your name for it,
`kn4iee` on Robin's machine). Every plan lands on this one branch, except the shared pull requests of plans 18 and 19
(their own branches off `main`). One pull request to `main` at the end (plan 21), opened only when the owner says.
Contributors rebase onto the branch and push straight to it; pushes there run no CI, so the suite runs locally after the
rebase ("Workflow for one plan"). No per-plan pull requests, changelog fragments or "User guide:" lines: plan 21 writes
them.
**Where these documents live:** `docs/a350/` on the branch. Plan 21 deletes the folder before the pull request merges.
**The owner** rules on what a plan leaves open (spec section 5, and questions found while working). Until the team names
someone else, that is Robin Kipp.

## Order

- **01, first and alone:** the independent review of the last commits and its fixes, then the seams later plans add
  to (Ctrl+M call-out rows by area, the E/WD monitor's mute by key, an EFIS-drawn hook).
- **02-11, 22, 23, early:** the flights' fixes first (02, 03, 04, 06, 09), then each area's open checks. They run in parallel and
  never share a file.
- **12-17, audit:** the manual, cover to cover, by page range. They change no code: each writes its own ledger and new
  fixtures, and hands each gap to the plan owning the file. 12-16 may start before 01 lands; 17 flies the whole
  walk-through once 02-11 have landed.
- **18-19, shared:** the handoff's fixes for the other aircraft, each its own pull request off `main` that never names
  the A350; 19 starts once plan 05 has flown the A350's flap margin.
- **20, later:** the tools made portable, once 03 has landed.
- **21, last:** integration.

| # | Plan | Kind | After |
|---|---|---|---|
| 01 | `01-review-and-seams.md`: an independent correctness review of `345bfd07f`..`7e6d5fbd1` and its fixes; the seams | first | — |
| 02 | `02-altimeter.md`: the Altimeter window checks all three targets before it sends, follows LINKED INSTR, keeps STD; FCU changes from external hardware | early: fix | 01 |
| 03 | `03-reader-traps.md`: each draw-engine trap named, proved harmless, its retry counted; `nvgTransform`; the 1.2.8 addresses | early: fix | 01 |
| 04 | `04-ecam.md`: memo color swaps, memos said again, cabin ready repeats, reverser status, flight phases, every SD page | early: fix | 01 |
| 05 | `05-first-officer.md`: the flap margin flown; auto-gear; the lowest AP engage height (T3); every normal checklist end to end | early | 01 |
| 06 | `06-ois.md`: the OIS window's layout and navigation round; the sign-in code kept out of the speech log; the OIS backlog; 1.2.8 fixtures | early: fix | 01 |
| 07 | `07-pfd-callouts.md`: green dot, S and F on the ground (Shift+1 to 3); the PFD open checks, the PFD messages as call-outs, LVR CLB, TCAS TA/RA, metric altitude, SIM RATE/PAUSED, KG units | early: fix | 01 |
| 08 | `08-nd-anf-taxi.md`: BTV armed in one step from the ND window; waypoint passing, the ANF, the taxi turn lead | early: fix | 01 |
| 09 | `09-mfd-window.md`: the MFD window as the A380's (page buttons and list, role words, the F-PLN grid, long lists); the KCCU keys, Alt+D, Alt+M, the ATC pages in the air | early: fix | 01 |
| 10 | `10-rmp.md`: the NAVAID audio selector, IDENT, STBY RAD NAV, Output N's receivers | early | 01 |
| 11 | `11-panels.md`: the 1.2.8 control map, every split panel, SND keys, gear and IRS read-outs, the panel backlog | early | 01 |
| 12 | `12-manual-front.md`: PDF pages 1-30 | audit | — |
| 13 | `13-manual-ois.md`: PDF pages 31-40 | audit | — |
| 14 | `14-manual-fcu-mfd-efis.md`: PDF pages 52-63 | audit | — |
| 15 | `15-manual-anf-to-hud.md`: PDF pages 64-80 | audit | — |
| 16 | `16-manual-ecam-checklists.md`: PDF pages 81-86 and 124-128 | audit | — |
| 17 | `17-manual-walkthrough.md`: PDF pages 87-123, flown as one SimBrief flight | audit | 02-11 |
| 18 | `18-visual-guidance-threshold.md`: glidepath distance from the displaced threshold (pull request off `main`); then the A350's biases | shared | — |
| 19 | `19-fo-flaps-other-airbus.md`: the A350's flap margin, landing gate and go-around signature for the A380, A320 and A330 (pull requests off `main`) | shared | 05 |
| 20 | `20-tools-portable.md`: `tools/A350Probe` and `tools/a350-probe` portable and documented | later | 03 |
| 21 | `21-integration.md`: handoffs, guide, rules, hotkey guide, changelog, the 1.2.8 "do not chase" list, a regression flight, the pull request, delete `docs/a350/` | last | all |
| 22 | `22-atc-mailbox.md`: the mailbox's SEND and a real uplink with the owner's go-ahead; an uplink said with the window closed | early | 01 |
| 23 | `23-ois-options.md`: every OPTIONS option on and off (handoff 3.2), the controls the cockpit hides explained | early | 01 |

## File ownership

Work only in the files your plan owns. A change you need elsewhere: ask the plan owning the file while it is open (its
claimant), and write the change in your "Handoff to plan 21" (file, change, why) if it is still needed when you land.
"A → B" means plan B takes the file over once A has landed; never two plans at once. Once a plan has landed and no open
plan takes its files over, a later plan may change one of them for a fix its own task needs: say so in your execution
record. Paths are under `MSFSBlindAssist/` unless they start with `docs/`, `tools/`, `tests/`, `guide/`,
`changelog.d/` or `.claude/`.

| File or pattern | Owner |
|---|---|
| Every file `git log --first-parent --no-merges --name-only e19171d2c..7e6d5fbd1` lists (the reviewed commits) | 01 while it runs, then the owners below |
| `Aircraft/A350/A350CalloutRows.cs` (new), `Aircraft/IniBuildsA350Definition.Hooks.cs` (new), `Forms/A350/A350MonitorManagerForm.cs` | 01, then frozen (07 removes the PFD flags line there when it splits that row). 01 also creates the four `A350CalloutRows.<Area>.cs` parts (then 04's, 07's, 08's and 22's) and makes its seams' changes to `A350EcamMonitor.cs`, `A350PfdMonitor.cs` and `MainForm.AircraftSwitch.cs` (`StartA350DisplayService`) before anyone else starts |
| `Aircraft/IniBuildsA350Definition.cs` | 01, then frozen, except the `TaxiTurnLeadSeconds` line (08) and `GetVisualGuidanceProfile` (18) |
| `Aircraft/A350/A350TypedEntry.cs`, `A350FcuCallouts.cs`, `A350FcuWindows.cs`, `A350Readouts.cs`, `A350BaroEntry.cs` (new), `Aircraft/IniBuildsA350Definition.Interaction.cs`, `Forms/A350/A350BaroWindow.cs`, `A350FcuValueWindow.cs`, `A350FcuWindowBase.cs`, `A350AutopilotWindow.cs` | 02 |
| `Aircraft/A350/Display/A350DrawEngine.cs`, `A350DisplayService.cs`, `A350MemoryReader.cs`, `A350MemoryCopy.cs`, `A350HotPages.cs`, `A350ReaderStats.cs`, `A350ReadHealth.cs`, `A350ModuleCache.cs`, `A350ModulePatcher.cs`, `A350ModuleFacts.cs`, `A350WasmModule.cs`, `A350DrawCapture.cs`, `A350DrawState.cs`, `A350DrawLayout.cs`, `A350ShapeReader.cs`, `A350AVars.cs`, `A350ShownCapture.cs`, `A350StableValue.cs`, `A350TrapReport.cs` (new), `Aircraft/IniBuildsA350Definition.Displays.cs` | 03 |
| `tools/A350Probe/**` | 03 → 20 |
| `Aircraft/A350/Display/A350Ewd.cs`, `A350EwdText.cs`, `A350EcamMonitor.cs`, `A350EcamAnnouncer.cs`, `A350SdPage.cs`, `A350SdSymbols.cs`, `A350SdText.cs`, `Aircraft/A350/A350CalloutRows.Ecam.cs` | 04 |
| `FirstOfficer/A350/**`, `Aircraft/IniBuildsA350Definition.FirstOfficer.cs` | 05 |
| `Resources/coherent-a350-ois-agent.js`, `Resources/coherent-a350-fap-agent.js`, `tools/a350-ois-test/**`, the A350 tests in `tools/flypad-shell-test/efb-shell.test.js`, `Aircraft/A350/A350OisSecrets.cs`, `A350Printout.cs`, `A350PrinterWatch.cs`, `Aircraft/A350/Display/A350SpeechLog.cs`, `Forms/A350/A350PrinterWindow.cs`; additive, A350-gated changes to `SimConnect/CoherentPmdgEfbClient.cs` (`ForA350Ois`, `ForA350Fap`) and `Forms/FBWA380/FbwEfbForm.cs` | 06 |
| `Aircraft/A350/Display/A350Pfd.cs`, `A350PfdMonitor.cs`, `A350PfdFlagAnnouncer.cs`, `A350FmaAnnouncer.cs`, `A350FlashingLines.cs`, `A350TakeoffSpeeds.cs`, `A350Minimums.cs`, `A350SimPause.cs`, `Aircraft/A350/A350TcasCallouts.cs`, `A350TakeoffCallouts.cs`, `A350MinimumsCallout.cs`, `A350PauseAlerts.cs`, `A350CalloutRows.Pfd.cs`, `A350Hotkeys.cs`, `Aircraft/IniBuildsA350Definition.Hotkeys.cs` (the read-out hotkeys) | 07 |
| `Aircraft/A350/Display/A350NdPage.cs`, `A350NdMap.cs`, `A350NdInput.cs`, `A350GroundDisplays.cs`, `A350BtvRollout.cs`, `A350AnfData.cs` (new), `Aircraft/A350/A350BtvCountdown.cs`, `A350BtvArming.cs` (new), `A350RouteDistances.cs`, `A350CalloutRows.Nd.cs`, `Aircraft/IniBuildsA350Definition.Nd.cs` (new), `Forms/A350/A350NdWindow.cs` | 08 |
| `Aircraft/A350/Display/A350MfdPage.cs`, `A350MfdInput.cs`, `A350MfdWindowMessages.cs`, `A350FmsMessageAnnouncer.cs`, `Aircraft/A350/A350MfdPages.cs` (new), `Forms/A350/A350MfdWindow.cs`, `A350WindowsForm.cs` | 09 |
| `Aircraft/A350/Display/A350Mailbox.cs`, `A350MailboxInput.cs`, `Aircraft/A350/A350CalloutRows.Datalink.cs`, `Forms/A350/A350MailboxWindow.cs` | 22 |
| `Aircraft/A350/A350Options.cs` | 23 |
| `Aircraft/A350/A350Rmp.cs`, `A350RmpEntry.cs`, `A350RadioCallouts.cs`, `A350PanelLayout.Rmp.cs`, `Aircraft/A350/Display/A350RmpPage.cs`, `Forms/A350/A350RmpWindow.cs`, `Services/NavRadioFormat.cs` (the A350's own helper) | 10 |
| `Resources/a350_control_map.json`, `tools/a350-gen/**`, `Aircraft/A350/A350ControlMap.cs`, `A350PanelLayout.cs`, `A350PanelLayout.Names.cs`, `A350PanelLamps.cs`, `A350LampRule.cs`, `A350LampSpeech.cs`, `A350ButtonStates.cs`, `A350CommandedState.cs`, `A350CoverAlerts.cs`, `A350Doors.cs`, `A350Levers.cs`, `A350LeverSpeech.cs`, `A350StatusLines.cs`, `A350IrsAlignment.cs`, `A350ModuleWatchdog.cs`, `A350RunwayCondition.cs`, `A350WritePlan.cs`, `A350WriteOutcome.cs`, `Aircraft/IniBuildsA350Definition.Announcements.cs` | 11 |
| `docs/a350/ledger/NN-*.md` | plan NN (23 for the OPTIONS pages, the audits 12-17 for theirs) |
| `tools/a350-probe/**` | 20 |
| `docs/inibuilds-a350.md` | by section, so two plans never edit one: 02 "Typed values"; 03 "The reader", "The patch", "Questions the draw code asks the sim", "Gauges and where they draw", the address table, "Operating the displays", "The draw code's own sim reads", "Logs"; 04 "The E/WD", "The SD", "ECAM control panel"; 06 "The OIS", "The FAP and the printer"; 07 "The PFD and FMA", "Takeoff, flight phases and the T/D pause"; 08 "The other displays", "The ND window and the ANF"; 09 "The MFD window"; 22 "The ATC mailbox"; 10 "The RMPs"; 11 "Panels" with its sub-sections and "Pedestal levers"; 21 the rest (the top, "Flying the A350 for a test", "Parity with the other aircraft", "Manual coverage", "Variants") and any new section |
| `docs/tooling.md` (its A350 part) | 20 |
| `guide/**`, `HotkeyGuides/iniBuilds_A350_Hotkeys.txt`, `docs/invariants/inibuilds-a350.md`, `.claude/rules/**`, `docs/first-officer.md`, `docs/visual-guidance.md` (on this branch), `docs/user-guide.md`, `changelog.d/**`, `CLAUDE.md`, `README.md` | 21: plans hand off their lines (a rule as its proposed one-line form and full text) |
| `MainForm*.cs`, `Services/**` (except `NavRadioFormat.cs`), `SimConnect/**` (except 06's two paths), `Accessibility/**`, `Settings/**`, `Aircraft/IAircraftDefinition.cs`, `Aircraft/BaseAircraftDefinition.cs`, every other aircraft's files, `MSFSBlindAssist/MSFSBlindAssist.csproj`, `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj` | nobody on this branch: frozen; handoff requests to 21 (01 makes its seams' one MainForm change; 18 and 19 change shared code only in their own pull requests off `main`) |
| `docs/a350/plans/NN-*.md`, `docs/a350/evidence/NN-*.md` | plan NN: its own plan file (its execution record included) and evidence file |
| `docs/a350/plans/00-index.md` | 01 (the "Base commits" line) and the owner ("Adding a plan"); otherwise frozen |
| `docs/a350/spec.md`, `docs/a350/evidence/flight-2026-10-31.md` | frozen; the owner alone fills in spec section 5's Answer column |

**Tests** follow their code. Existing test files belong to the plan owning what they test: `A350TypedEntryTests`,
`A350FcuWindowsTests`, `A350FcuCalloutsTests`, `A350WindowWritesTests` 02; `A350DrawEngine*Tests`, `WasmTestModule.cs`,
`A350DisplayServiceTests`, `A350MemoryReaderTests`, `A350HotPagesTests`, `A350ReaderStatsTests`, `A350ReadHealthTests`,
`A350ReaderStatusTests`, `A350Module*Tests`, `A350WasmModuleTests`, `A350DrawLayoutTests`, `A350DrawStateTests`,
`A350ShapeReaderTests`, `A350ShownCaptureTests`, `A350AVarsTests`, `A350MemoryWarningTests`, `A350RgbaTests`,
`A350SpeechThreadTests` 03; `A350Ewd*Tests`, `A350Ecam*Tests`, `A350Sd*Tests`, `A350EngineDisplayTests` 04;
`tests/MSFSBlindAssist.Tests/FirstOfficer/A350*.cs`, `A350AutopilotEngageTests`, the cross-profile
`FirstOfficer/FbwFamilyAutoManagerSecondReviewTests.cs` (the A350 is one of its airframes) 05; `A350OisSecretsTests`,
`A350SpeechLogTests`, `A350Print*Tests` 06; `A350Pfd*Tests`, `A350FmaTests`, `A350FlashingLinesTests`,
`A350TakeoffCalloutsTests`, `A350TcasCalloutsTests`, `A350MinimumsTests`, `A350PauseAlertsTests`,
`A350MetricAltitudeTests`, `A350HudTests`, `A350HotkeysTests` 07; `A350Nd*Tests`, `A350GroundDisplaysTests`, `A350BtvTests`,
`A350RouteDistancesTests`, the A350's lines in `TaxiTurnLeadTests.cs` 08; `A350Mfd*Tests`, `A350FmsMessageTests` 09; `A350MailboxTests` 22; `A350OptionsTests` 23;
`A350Rmp*Tests`, `A350RadioCalloutsTests` 10; `A350ControlMapTests`, `A350*PanelTests`, `A350PanelLampsTests`,
`A350PanelLayoutTests`, `A350PanelConfirmationTests`, `A350Lamp*Tests`, `A350ButtonStateTests`,
`A350CommandedStateTests`, `A350CoverAlertsTests`, `A350DoorsTests`, `A350Levers*Tests`, `A350AllThrustLeversTests`,
`A350LeverSpeechTests`, `A350IrsAlignmentTests`, `A350ModuleWatchdogTests`, `A350WritePlanTests`,
`A350AnnouncementTests`, `tools/a350-gen/test_*.py` 11; `IniBuildsA350DefinitionTests`, `A350CultureTests`,
`A350CommaCultureTests`, `A350Fixtures.cs` frozen after 01 (18 adds to `A350VisualGuidanceProfileTests`). A plan's new
tests go in new files named for its feature, under `tests/MSFSBlindAssist.Tests/A350/` (05:
`tests/MSFSBlindAssist.Tests/FirstOfficer/A350*.cs`), so the A350 rules load. New fixtures are new files under
`tests/MSFSBlindAssist.Tests/A350/Fixtures/`, named `pNN_<what>.json`; never overwrite one a test pins. No test, code or
CI step may read a file under `docs/a350/`: plan 21 deletes it (copy what a test needs into a fixture).

## Measuring in the sim

A task that needs the sim says **[sim]** (on the ground) or **[in flight]**; one that needs the owner's ears, hands or
say-so says **[owner]**. Claude runs sim checks itself through `tools/A350Probe` (its `repl`, `rpn`, `keep`, `listen`,
`ecamwatch`, `displays`, `ois`, `mfd`, `nd` modes; the README) and reads through the simconnect MCP, with the sim's
owner's go-ahead for any write; the human is asked only for what needs ears, a sign-in or a flight. Never a step that
needs sight: measure instead (reads, captures, logs). A plan records what it measured in its own
`docs/a350/evidence/NN-<slug>.md`. Spec section 4's sim rules apply to every step: no `msfs_load_flight`, no pause, AP
and A/THR on before a critical phase, OIS SAVE NOW, stop by 15 GB free commit.

## Who is on which plan

Each plan's "Execution record" (at its end) holds its claim and status, so claiming never edits a shared line. Plan 01
runs first and alone; every other plan is claimed by its Task 0, before any other task: check the remote copy, claim on
your branch, push that one commit to `feat/inibuilds-a350`. A claim counts only once it is on the remote branch: one that
exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.
One person per plan. To see where every plan stands on the remote: `git fetch <remote>`, then
`git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/`.
A plan may start when the plans in its "After" column say landed.

**Adding a plan.** The owner (or someone at the owner's word) adds a new plan as `NN-<slug>.md` with the next free
number, and its row and its files to this index's tables, in one commit pushed alone. A file moves to the new plan only
while the plan owning it is not claimed; otherwise the new plan takes it over once that plan lands ("A → B"). Adding a
task to an unclaimed plan's file is the same kind of commit.

## Workflow for one plan

1. Fetch the branch and branch from it at the base commit for your plan's kind (bottom of this file):
   `git switch -c a350-pNN-<slug> <base-sha>`. An agent works in a worktree inside the repo (`.claude/worktrees/a350-pNN`),
   says so before creating it, and removes it when done (`dotnet build-server shutdown` first). Plans 18 and 19 claim on
   this branch and do their shared work on a branch off `main` (their Task 0 says how). This step is part of every
   plan's Task 0, which then pushes the claim before any other task.
2. Build once before testing: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (never the bare csproj,
   CORE-1; afterwards `MSFSBlindAssist\bin\Debug` must not exist). Close MSFSBA first if it runs (CORE-3), or test with
   `--artifacts-path` ("Running a plan with subagents"). No build under 10 GB free commit.
3. Work the tasks in order (TDD). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug
   -p:Platform=x64 --filter "FullyQualifiedName!~ClaudeRulesHookTests"` (that class alone takes about 20 minutes and
   tests only what plan 21 changes; 21 runs the whole suite). The OIS harness (06): `npm ci` then `npm test` in
   `tools/a350-ois-test`. The generator (11): `python -I -m unittest discover -s tools/a350-gen -p "test_*.py"`. Every
   commit ends with a `Plan: NN` trailer line (next to its `Co-Authored-By` line), so `git log --grep "^Plan: NN"`
   lists the plan's commits.
4. Push whenever a task is done and green (others then build on it sooner): `git fetch <remote>`, then
   `git rebase <remote>/feat/inibuilds-a350` (your commits replay on top of what others pushed; files are disjoint and
   your claim is already on the branch, so the rebase does not stop; in Task 0, a stop on the claim lines means someone
   claimed first; anywhere else, someone left their files: stop and sort it out with them).
5. After the rebase, on the combined code: build and the suite green (this run is the only check that the plans work
   together); `git diff --name-only <remote>/feat/inibuilds-a350...HEAD` lists only files you own.
6. `git push <remote> HEAD:feat/inibuilds-a350`. If it is rejected (someone pushed meanwhile), go back to step 4. Never
   force-push the branch. Update your execution record in the same push; when the plan is done, fill in its "Handoff to
   plan 21" and set **Status** to landed (or waiting on owner, naming what waits).
7. Steps for the sim need MSFS 2024 with the iniBuilds A350 1.2.8. Leave them unticked if you lack them, and list them
   under "Waiting on the owner". Never write to the sim without its owner's go-ahead (reads are fine).

**Merging `main` into the branch** happens only when a plan needs `main`'s work (plan 18's Task 6, after its pull
request merges) and before the pull request (plan 21). Whoever does it says so to the claimants first, merges (never
rebases), runs the whole suite, `ClaudeRulesHookTests` included, and pushes the merge alone. Every open plan then
rebases onto it as usual.

## Running a plan with subagents

Plans 01-11, 18-20, 22 and 23 are written for superpowers:subagent-driven-development (a fresh implementer per task, a reviewer
after each); the audits (12-17) and plan 21 are live work, worked with superpowers:executing-plans.

- One worktree per plan, inside the repo (`.claude/worktrees/a350-pNN`). In a fresh worktree, build the vPilot plugin
  once first (`dotnet build plugins/MSFSBlindAssist.VPilotPlugin/MSFSBlindAssist.VPilotPlugin.csproj -c Debug`), or the
  test build fails with MSB3030.
- Test with `--artifacts-path <worktree>/bin/a350-pNN` so parallel builds and an open MSFSBA never collide. The folder
  must be inside the repo or worktree, never the scratchpad or `%TEMP%`: about 28 tests find `MSFSBlindAssist.sln` by
  walking up from the test DLL and fail otherwise.
- Each implementer gets the task's text, the plan's Global Constraints and Review Focus, the spec sections the plan cites
  and this index's "Shared conventions", and works only in the task's files. Where a task says "Steps 1-5" with test
  names, those are the usual five: write the named tests, run them failing, implement, run the suite, commit with the
  given message.
- Task 0 [controller], the claim, is the controller's own: it creates the plan's worktree and branch and pushes the claim
  before Task 1 is dispatched, and is never given to an implementer.
- The controller never dispatches a **[sim]**, **[in flight]** or **[owner]** task or any step that writes to the sim to
  an implementer: it runs them itself with the sim's owner, or lists them under "Waiting on the owner" and works the
  rest.
- Two plans at once: separate worktrees and separate scratch folders; never two agents on one plan or one file.
- When the plan's tasks are done: one whole-plan review checks the ownership diff (step 5), that every Review Focus line
  has its test, and that the handoff and execution record are filled in. The SDD ledger stays untracked
  (`.superpowers/sdd/a350-pNN/`).

## Records: who worked a plan and how it went

- **Each plan's "Execution record":** claim and status, who worked it (and whether with subagents), start and landing
  dates and commits, the test result at landing, tasks left, what waits on the owner, in-sim results, what changed from
  the plan, and the rulings made while working (question; answer; who decided).
- **Each plan's "Handoff to plan 21":** what changes for a pilot (guide chapter, hotkey guide, changelog category),
  proposed rule lines, requests for frozen files, and gaps found in other plans' files.
- **`docs/a350/evidence/NN-<slug>.md`:** what the plan measured in the sim, with dates and the build it ran.
- **Commits:** the `Plan: NN` trailer (workflow step 3) gives every commit with its author and date.
- **Plan 21** compiles the records into the pull request description and names the last commit holding `docs/a350/`
  before deleting it; the pull request merges with its history (`gh pr merge --merge`), so the records stay reachable.

## Shared conventions

- CLAUDE.md applies in full. Read a file before changing it (CORE-16); the A350 rules A350-1 to A350-12 load with its
  code. Spec section 4 holds the team's rules.
- Every A350 write goes through the definition's existing write paths (calculator strings, VAR-5's invariant fixed-point
  numbers, unique strings for repeats, VAR-4); never a write into the sim's memory ([A350-1]).
- What is spoken is the value read back; a direct interaction is confirmed once, with what the screen reader cannot say
  (CORE-7); a background change speaks once, after a silent first reading. A display read is said only once two reads in
  a row agree ([A350-2]; `A350StableValue`, the announcers' agreement).
- Every new call-out has its Ctrl+M row: a variable's through `ExcludeFromMonitorManager` false; a display call-out's in
  its area's `A350CalloutRows.<Area>` list (plan 01's seam), muted through the monitor's key lookup. Each announcing plan
  has a test that a muted row says nothing.
- Anything spoken or formatted gets a de-DE or sv-SE test; typed numbers go through `NumberParsing.TryParseTyped`
  (CORE-17). Spelling in docs and spoken text is US English (color, center), except the cockpit's own words as drawn.
- Status text in windows is a read-only `TextBox` (VAT-13); background pushes marshal through a try/catch
  `SafeBeginInvoke` (A380C-6).
- Tests never depend on another plan's control-map labels: build in-memory rows. A display test reads a fixture
  captured live (`A350Probe displays` or `draw` with `A350_DRAW_JSON`, `ecamwatch` with a folder).
- New source files go under `MSFSBlindAssist/Aircraft/A350/`, `Aircraft/IniBuildsA350*.cs`, `Forms/A350/` or
  `FirstOfficer/A350/`, where a rule glob already loads; a new file anywhere else needs a glob in a rule file
  (`ClaudeContextBudgetTests`), which is plan 21's: ask it first.
- Nothing a pilot types into the OIS's 3RD PARTY page, and no sign-in code, is spoken beyond what the OIS shows, logged,
  captured or committed ([A350-12]).
- Each plan states what its area's 1.2.8 check found (the handoff's "Do not chase" list for its area included).

## Evidence

`docs/a350/evidence/flight-2026-10-31.md` (the first full flight), each plan's `NN-<slug>.md`, `docs/inibuilds-a350.md`
(measured 2026-10-26 to 2026-10-31 on 1.2.7), the A350 manual in the installed package's `Resources` folder.

## Base commits

- Plan 01, the audits 12-16 and plan 18 start from the commit that added this folder ("docs(a350): plans for the work
  after the first full flight"), or from the branch's head when they start.
- Early plans (02-11, 22, 23) start from the commit plan 01 records here: (plan 01 fills this in).
- Plans 17 and 19-21 start from the branch once their "After" plans have landed; record the commit you started from in
  your plan's execution record ("Started").
