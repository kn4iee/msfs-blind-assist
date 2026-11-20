# A330 Integration Implementation Plan (plan 12, last)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own.

**Goal:** Everything the other plans handed over is applied; a release carries the display reader program beside the app; the temporary chocks and covers press is removed (owner, 2026-11-19: "removal of this before pr and merged into main"); the rules, the developer doc, the pilot's guide, the hotkey guide and the changelog say what a pilot now has; `docs/a330/` is gone; and one pull request to `main` is open when the owner says.

**Architecture:** Collect the handoffs, apply the frozen-file requests in one place, then write the durable record: rules as one line plus full text, facts folded from the evidence files into the developer doc, the guide written for a pilot. Delete the working folder last, after proving nothing reads it.

**Tech Stack:** C# 13 / .NET 10, xUnit, JavaScript (the EFB agent and its harness), Markdown.

**Spec:** `docs/a330/spec.md` sections 3 (R20, R21); every plan's "Handoff to plan 12" and "Execution record"; `changelog.d/README.md`, `docs/user-guide.md`; rules [CORE-6], [CORE-19], [INI-9], CLAUDE.md "Adding or changing a rule".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. This plan may change any file on the branch; it changes another plan's file only for a handoff request or a fix its own task needs, and says so in the record.
- The pull request is opened only when the owner says. The changelog fragment's number is read from the opened pull request, never guessed.
- Public text (rules, docs, guide, commit messages, the pull request) names no contributor, branch or pull request as an example of a mistake, never says where the team's private reference is, and gives no other add-on as the source of a name.
- A fact a test, a comment or a doc cites from `docs/a330/` is folded into `docs/ini-a330.md` or a rule's full text before the folder is deleted.

## Review Focus

- A pilot who started engines with BA's Engine Covers press finds the row is now a status line: the guide says how the covers come off instead, and the First Officer's start flow still names covers that are on (Task 3).
- A release built without the reader program would ship an A330 whose MCDU cannot be read: the bundle check fails the run instead (Task 2).
- A sentence in a rule, a doc or the pull request that explains how the reader works: Task 7 Step 2's search.
- A rule that names code which no longer exists (the pixel reader's [INI-4]) or a rule line over 400 characters: `ClaudeContextBudgetTests` and `ClaudeRulesHookTests` (Task 4).
- The guide describing a key by what the code does instead of what the pilot hears (Task 6: each new key pressed once while writing).
- `main` moved under the branch: the merge's conflicts in shared files (the all-aircraft test lists, the csproj, `MainForm.AircraftSwitch.cs`) keep both sides (Task 7).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 12` (`Plan: 12`). Start once every other plan says landed.

### Task 1: Handoffs and frozen files

- [ ] **Step 1:** Read every plan's "Handoff to plan 12" and execution record. One list in this file's record: each request (file, change, why, from which plan) and what was done with it.
- [ ] **Step 2:** Apply the frozen-file requests, one commit a request, each with a test where it changes behavior. Tool notes: `docs/development.md` and CLAUDE.md's counts of projects that build on their own (`tools/a330-gen/PanelSweep` added, `tools/a330-cdu-ocr/harness` gone if plan 03 removed it).

### Task 2: The release carries the reader program (R21)

**Files:** `.github/workflows/release.yml` and the preview workflow; a new `tools/ReaderBundle/` (a console check, as `tools/LayoutBundle` is for the layouts) or a step in `tools/LayoutBundle` if the owner prefers one tool; `docs/updates.md`.

- [ ] **Step 1:** With the owner: the release workflow checks out the private reference at the reader's branch with the deploy key the layouts step already uses (or a second key if the owner wants one), publishes the reader program as the reader's own README says, and copies `A330DisplayReader.exe` beside the app before the zip is made. The private source is never uploaded as an artifact, and no step prints it.
- [ ] **Step 2:** The bundle check fails the run when the program is missing, does not start, or answers `--version` with a protocol number other than `A330ReaderProtocol.Version`. Steps 1-5 for the check's pure part (`A_missing_program_fails_the_bundle`, `A_protocol_mismatch_fails_the_bundle`).
- [ ] **Step 3:** The same in the preview workflow. A build from source without the private reference still builds and runs; its A330 displays say "the display reader is not installed" (plan 02), and `docs/development.md` says how a contributor with access places the program beside a Debug build.
- [ ] **Step 4:** A pull request that edits a workflow file cannot be merged with the command-line token: the owner merges it on the web. Say so in the pull request.

### Task 3: Remove the chocks and covers press (R20)

**Files:** `Resources/coherent-a330-efb-agent.js`, `tools/a330-efb-test/ground.test.js` and its fixtures, `docs/ini-a330.md`, `docs/invariants/inibuilds.md`, `.claude/rules/inibuilds.md`, `guide/airbus-aircraft.md`.

- [ ] **Steps 1-5:** tests first, in `ground.test.js`: `chocks_engine_covers_and_pitot_covers_are_status_lines_not_buttons` ("Chocks: placed" / "removed" from the state the page shows), `a_press_on_a_cover_row_does_nothing_and_writes_nothing`, `the_GPU_stays_a_button`; then delete `A.COVER_REMOVAL`, `A.coverPressable`, `A.coverPress` and its call in `A.click`, leaving the three rows `static`; `npm test` green; commit `refactor(a330-efb): remove the temporary chocks and covers press`.
- [ ] **Step 2:** [INI-9] rewritten in both rule files to what stays true: the three rows are status text because the tablet's own buttons do nothing in MSFS 2024, and BA never clicks them or writes the covers itself. The developer doc's two bullets rewritten to match (the measurements stay; the press goes).
- [ ] **Step 3:** The guide: the EFB section and "Things to know" say the three rows are read-only and how a pilot removes chocks and covers until BA's simulator tablet can (the simulator's own ground services; the First Officer's engine start says so when the engine covers are on). Ask the owner for the wording if the simulator tablet has merged by then.

### Task 4: Rules

**Files:** `.claude/rules/inibuilds.md`, `docs/invariants/inibuilds.md`, CLAUDE.md (the A330's row in "Where things live").

- [ ] **Step 1:** [INI-4] retired: delete its line and rename its heading `## INI-4 (retired: the pixel reader it guarded was replaced by the display reader)`. Skip if plan 03 kept the pixel reader.
- [ ] **Step 2:** Each plan's rule proposals (INI-10 onward) as one line of at most 400 characters naming its key type or method, with its full text, measurements and dates under its heading. No rule's line or full text says how the display reader works: it says what the app's side must and must not do. Before writing a line, Read the file that declares what it guards and confirm a glob of the rule file matches it; a rule whose code is called from another area's files is mirrored there word for word.
- [ ] **Step 3:** CLAUDE.md's row for `ini-a330.md` reworded (display windows fed by the display reader; the MCDU no longer pixel-read). `ClaudeContextBudgetTests` and `ClaudeRulesHookTests` green.

### Task 5: The developer doc

- [ ] **Step 1:** `docs/ini-a330.md` read whole and rewritten where plans changed things, section by section in the index's ownership order, then the top, "Unverified" (what plan 11 proved is struck; what remains stays, with why) and "Probe side effect". Its "Displays" section describes the app's side only.
- [ ] **Step 2:** Fold in from `docs/a330/evidence/` every measurement a rule, a test or a comment cites, with its date and the simulator's version.

### Task 6: The pilot's guide and the hotkey guide (CORE-19)

**Files:** `guide/airbus-aircraft.md` (`#airbus-ini-a330`), `guide/hotkey-reference.md`, `guide/first-officer.md`, `HotkeyGuides/iniBuilds_A330_Hotkeys.txt`.

- [ ] **Step 1:** The A330's chapter in the shape of the other Airbus chapters, with a "Special windows" section: the MCDU (no pop-out, both sides, messages, what is said when its screen cannot be read), the E/WD, system display, PFD, ND, ISIS and DCDU windows and the cockpit controls in each, the FCU windows, the EFB's pages, the panels' order, the First Officer. Written for a pilot: what to press and what is heard. The "has not yet been flown" note goes if plan 11 flew.
- [ ] **Step 2:** Both hotkey lists: every key any plan added, in the other Airbus aircraft's order; press each once while writing its line.
- [ ] **Step 3:** The guide builds (`tools/GuideBuilder`) and its tests pass.

### Task 7: `main`, the whole suite, and the owner's pass

- [ ] **Step 1:** Tell the claimants; `git fetch origin`; merge `origin/main` into the branch (never rebase); resolve; build; the whole suite, `ClaudeRulesHookTests` included; the generator's tests; `npm test` in `tools/a330-efb-test`; push the merge alone.
- [ ] **Step 2:** Before the owner's pass, search the branch's whole diff against `main` and its commit messages for anything that says how the display reader works or quotes the simulator's checklist text (`git diff origin/main...HEAD` and `git log origin/main..HEAD`; the private reference's push check does this search over the whole branch); each hit is reworded or, in a commit message, raised with the owner.
- [ ] **Step 3 [owner]:** The owner's in-sim pass on the merged branch with a release-style build (the reader program beside the app), a few scenarios only: (1) gate to engines running with the First Officer, the MCDU and the EFB; (2) a circuit or short flight: flight modes, the FCU windows, the E/WD call-outs, the ND window; (3) after landing: the panels the owner uses most, in their new order. Record the result.

### Task 8: The pull request, the changelog, and the folder

- [ ] **Step 1:** When the owner says: `gh pr create` to `main` from the fork's branch. The description, compiled from the records: what changes for a pilot first; that the displays are read by a separate display reader program a release carries (and nothing about how); what was compared against which aircraft; what was flown and when; what remains unverified; `User guide:` is not needed (the guide is in the pull request).
- [ ] **Step 2:** Read the pull request's number. Add `changelog.d/<number>-inibuilds-a330.aircraft.md`, written for a pilot (the aircraft, its windows, its First Officer, what it needs); commit and push.
- [ ] **Step 3:** Delete the working folder: `git grep -n "docs/a330/"` must print nothing outside the folder itself (fix what it prints by Task 5 Step 2's folding); `git rm -r docs/a330`; build and the whole suite green; commit `docs(a330): remove the plans folder before merge`, its body naming the commit before it; name that commit in the pull request's description.
- [ ] **Step 4:** After the required check passes and the owner approves: `gh pr merge --merge` (the history keeps the records). Then the leftovers: the `a330-pNN-*` branches, the worktrees, the `bin/a330-*` output.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** not started
- **Claimed by:** none
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**

### Handoffs applied

(Task 1 fills this in: request, from which plan, what was done.)
