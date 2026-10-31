# Visual Guidance from the Displaced Threshold Implementation Plan (plan 18, shared)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Visual guidance measures the glidepath from the runway's threshold, not its physical start, so it no longer runs high wherever the threshold is displaced (78 ft high on one measured runway, on every aircraft); the biases measured on displaced runways are recalibrated; and, once the fix is on this branch, the A350's own biases are measured again from the threshold.

**Architecture:** Part A is a pull request off `main` for every aircraft, which never names the A350: a pure helper gives the glidepath's origin (the runway's start moved along its heading by `Runway.ThresholdOffset`), used for glidepath distance only, each of `VisualGuidanceManager`'s runway-start call sites reviewed for whether it wants the threshold (glidepath) or the start (cross-track is the same centerline either way; rollout and flare may want the start). Part B, on this branch after `main` with the fix is merged in, recalibrates the A350's `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` from the coupled ILS data of `3b2520828`.

**Tech Stack:** C# 13 / .NET 10, xUnit; flown ILS approaches for the measurements.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 5.1), 4 (confidentiality), 5 (question 10); `docs/visual-guidance.md`; rules VG-15 and the visual-guidance rule file.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. **Part A never names the A350** (code, comments, tests, commit messages, the pull request): it describes the defect on its own terms, and its measurements come from released aircraft. It opens only with the owner's OK (question 10), to `oasis1701/msfs-blind-assist`, from a branch off `main`; CLAUDE.md's whole PR workflow applies there (changelog fragment after the PR number, the "User guide:" line, an in-sim test plan).
- Part A's files are `main`'s: `Services/VisualGuidanceManager.cs`, `Services/RunwayGlidepathOrigin.cs` (new), their tests, `.claude/rules/visual-guidance.md` (the new file's glob), `docs/visual-guidance.md`, and any aircraft's `GetVisualGuidanceProfile` whose biases Task 4 recalibrates. Part B's files on this branch: `GetVisualGuidanceProfile` in `Aircraft/IniBuildsA350Definition.cs`, `tests/MSFSBlindAssist.Tests/A350/A350VisualGuidanceProfileTests.cs`, `docs/a350/evidence/18-visual-guidance-threshold.md` (new).
- `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` stay two values, measured apart (VG-15). The recalibration: bias = altitude − threshold elevation − d·tan 3°, with d from the threshold.
- `Runway.ThresholdOffset` is read by `LittleNavMapProvider` from `offset_threshold`; its unit is the one the provider stores (check it there before using it).

## Review Focus

- A runway with no displaced threshold: every number is what it was (Task 2 test).
- A threshold offset on the far end only (the reciprocal runway's): never applied to this end (Task 2 test).
- Cross-track error on a displaced runway: unchanged (Task 3 test).
- The flare and rollout logic: still from the runway's start where it wants the start, by a decision written at each call site (Task 3 review).
- An aircraft whose bias was measured on a displaced runway and is not recalibrated: listed in the pull request, never silently left (Task 4).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/18-visual-guidance-threshold.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Claim on this branch.** On a branch of `feat/inibuilds-a350`, set this file's **Status** to claimed, **Claimed by** to your name and **Started** to today's date; commit only that change (`docs(a350): claim plan 18`, `Plan: 18` trailer).
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. Then, for Part A, `git fetch origin` and `git switch -c fix/visual-guidance-threshold origin/main` in a worktree of its own.

### Task 1 [in flight] [owner]: The defect shown on a released aircraft

**Files:** none on `main`; the measurements in Part A's pull request description and this plan's evidence file.

- [ ] **Step 1:** A coupled ILS to a runway with a displaced threshold on a released aircraft (the FBW A320 or A380): visual guidance's glidepath error against the ILS's, every 0.5 NM from 5 NM (`landing_assist.log`); and one to a runway without one. Expected: the displaced runway reads high by about tan 3° times the offset.

### Task 2: The glidepath's origin

**Files:** create `Services/RunwayGlidepathOrigin.cs`, and add it to `.claude/rules/visual-guidance.md`'s `paths:` in the same commit (`ClaudeContextBudgetTests`); test `tests/MSFSBlindAssist.Tests/RunwayGlidepathOriginTests.cs`, added to the same rule file's test globs if its globs name tests.

**Interfaces:**
- `static (double Lat, double Lon) RunwayGlidepathOrigin.Of(Runway runway)`: `StartLat`/`StartLon` moved along `Heading` by `ThresholdOffset` (in meters after the unit check); the start itself when the offset is 0 or unknown.
- [ ] **Steps 1-5** (tests `No_offset_gives_the_start`, `An_offset_moves_the_origin_along_the_heading`, `The_ideal_altitude_at_a_distance_is_measured_from_the_threshold`, `Only_this_ends_offset_applies`); commit `fix: visual guidance's glidepath starts at the displaced threshold` (no A350 anywhere).

### Task 3: Each call site, deliberately

**Files:** `Services/VisualGuidanceManager.cs` (the runway-start uses at about lines 861, 864, 936, 952, 1089, 1131, 1232, 1420, 1429, 1510, 1540, 1650, 1654 on `main`); its tests.

- [ ] **Step 1:** At each, decide: glidepath distance or altitude (the threshold, through `RunwayGlidepathOrigin.Of`), cross-track (either: unchanged), flare and rollout (the start, unless the measurement says otherwise); a one-line comment says which and why.
- [ ] **Steps 2-6**, the usual five (tests `Cross_track_is_unchanged_on_a_displaced_runway`, `The_glidepath_error_on_a_displaced_runway_matches_the_ils`); commit `fix: visual guidance measures its glidepath from the threshold at every glidepath use`.

### Task 4: The biases measured on displaced runways

**Files:** `docs/visual-guidance.md`; each affected aircraft's `GetVisualGuidanceProfile` on `main`; their profile tests.

- [ ] **Step 1:** List each aircraft's `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` with the runway it was measured on (`docs/visual-guidance.md`, the commits that set them); for each measured on a displaced threshold, recompute with the formula above from its recorded data, or mark it for a new flight.
- [ ] **Step 2:** Update the values and the doc (VG-15: two values each); commit `fix: biases measured on displaced runways recalibrated from the threshold`.

### Task 5 [owner]: The pull request off `main`

- [ ] **Step 1:** With the owner's OK (question 10): push the branch to `origin`, open the pull request (no A350 anywhere), add its changelog fragment once the number is known, its "User guide:" line, and an in-sim plan: one ILS to a displaced and one to a non-displaced threshold on a released aircraft. Record the PR in this file's execution record.

### Task 6 [in flight]: The A350's biases from the threshold (on this branch)

**Files:** `GetVisualGuidanceProfile` in `Aircraft/IniBuildsA350Definition.cs`; `A350VisualGuidanceProfileTests.cs`; create `docs/a350/evidence/18-visual-guidance-threshold.md`.

- [ ] **Step 1:** Once `main` with Part A is merged into this branch (the index's rule for merging `main`), recompute the A350's two biases (52 and 15 since `3b2520828`, measured on a displaced runway) from that commit's data with d from the threshold; set them (test `The_a350_biases_are_measured_from_the_threshold`); commit `fix(a350): the visual guidance biases measured from the threshold`.
- [ ] **Step 2 [in flight]:** One coupled ILS to a displaced and one to a non-displaced threshold on the A350: the glidepath error within a few feet of the ILS's on both; record it.

## Handoff to plan 21

- **Pilot-facing (this branch):** none beyond Part A's own changelog on `main`.
- **Docs:** the A350's line in "Parity with the other aircraft" (visual guidance's biases, now measured).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

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
