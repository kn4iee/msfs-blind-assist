# First Officer Flaps on the Other Airbus Types Implementation Plan (plan 19, shared)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** On the FBW A380, the FBW A32NX and the Headwind A330, the First Officer's flaps no longer wait forever on a managed approach that holds at about each flap speed, and a go-around is told by the levers, not vertical speed alone: one pull request off `main` for the three (the owner's answer to question 11), each aircraft's change built from its own variables, in its own commits, after the symptom is shown on it.

**Architecture:** Each of `FbwA380FOAutoManager`, `FbwA320FOAutoManager` and `HwA330FOAutoManager` extends at `ias < gd`, `ias < s`, `ias < f` with no margin and shares a go-around detection from vertical speed alone. The fix plan 05 flew on this branch is the model, not a template (CLAUDE.md "Fix the aircraft the request is about"): an extension margin on each step, a landing gate bringing the rest of the flaps by a height whatever the speed, the go-around's lever signature (levers at MCT or TOGA seen once, a sustained climb, height regained) with a hold on extending after it, VFE-next protection kept. Each port is built from that aircraft's lever and speed variables, tested, flown and reviewed on its own.

**Tech Stack:** C# 13 / .NET 10, xUnit (the cross-profile `FbwFamilyAutoManagerSecondReviewTests` and each aircraft's FO tests).

**Spec:** `docs/a350/spec.md` sections 3 (handoff 5.2), 4 (confidentiality), 5 (questions 10 and 11); plan 05's flown figures (its "Handoff to plan 21": for plan 19); the first-officer-automation and first-officer-airbus rules.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. **No pull request names the A350**: the defect and the fix are described on each aircraft's own terms, and its figures come from that aircraft's flights. It opens only with the owner's OK (question 10, given once the measurements are done), as one pull request for the three aircraft (question 11, 2026-10-31), to `oasis1701/msfs-blind-assist`, from one branch off `main` (`fix/airbus-fo-flap-margin`), with CLAUDE.md's PR workflow (changelog fragment after the number, the "User guide:" line, an in-sim test plan the owner runs with a part per aircraft). Each aircraft's change is its own commits with its own tests, so a reviewer can take one and leave another.
- After: plan 05 has flown the A350's margin, landing gate and go-around (its Task 1); until then nothing here starts past Task 1.
- Files, each pull request's own on `main`: `FirstOfficer/FBWA380/FbwA380FOAutoManager.cs`, `FirstOfficer/FBWA320/FbwA320FOAutoManager.cs`, `FirstOfficer/HWA330/HwA330FOAutoManager.cs`, their tests, and on `main`'s side `tests/MSFSBlindAssist.Tests/FirstOfficer/FbwFamilyAutoManagerSecondReviewTests.cs` (on this branch that file is plan 05's; the merge of `main` into the branch reconciles the two).
- The Headwind A330 duplicates the A32NX profile under `HwA330ParityTests` (FOA-3): its port keeps that parity or names the divergence with its reason.
- Nothing on this branch changes in this plan except this file.

## Review Focus

- A selected speed held just above a flap speed: the step comes within the margin (each aircraft's test).
- A go-around whose levers are back at CL before the height is regained: still a go-around (each aircraft's test).
- A climb on an approach with the levers at CL (a level-off, a step climb): never a go-around (each aircraft's test).
- VFE next unread (NaN): no extension (FOA-7's hold), on every aircraft.
- The A32NX and A330 parity: `HwA330ParityTests` green after the A330's port.

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/19-fo-flaps-other-airbus.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Claim on this branch.** On a branch of `feat/inibuilds-a350`, set this file's **Status** to claimed, **Claimed by** to your name and **Started** to today's date; commit only that change (`docs(a350): claim plan 19`, `Plan: 19` trailer).
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop.

### Task 1 [in flight] [owner]: The symptom on each aircraft

- [ ] **Step 1:** On each of the A380, the A32NX and the A330, a managed approach with the First Officer's automatic flaps on: does a flap step wait while the speed holds at about its target? A climb on the approach with the levers at CL: read as a go-around? Record each (speeds, times, heights) in the execution record; an aircraft without the symptom gets no pull request.

### Task 2: The A380's port

**Files:** `FirstOfficer/FBWA380/FbwA380FOAutoManager.cs` (its extension cases at about lines 262-273 on `main`); its tests.

- [ ] **Step 1:** `git switch -c fix/airbus-fo-flap-margin origin/main` in its own worktree (Tasks 3 and 4 add to the same branch).
- [ ] **Steps 2-6**, the usual five (tests `The_A380s_flaps_come_within_the_margin_on_a_speed_hold`, `The_A380s_landing_gate_brings_the_flaps_by_its_height`, `The_A380s_go_around_stands_when_the_levers_left_TOGA_before_the_height_came`, `The_A380s_climb_with_the_levers_at_climb_is_no_go_around`, `An_unread_VFE_next_holds_the_A380s_flaps`), the margin, gate height and hold from the A380's own flown figures (Task 1) and its own variables; commit `fix(fbw-a380): the First Officer's flaps come within a margin of their speed, and a go-around is told by the levers`.

### Task 3: The A32NX's port

**Files:** `FirstOfficer/FBWA320/FbwA320FOAutoManager.cs`; its tests.

- [ ] **Steps 1-5** as Task 2's, on the same branch, with the A32NX's own variables and figures (tests named `The_A32NXs_…`); commit `fix(fbw-a32nx): …` as Task 2's.

### Task 4: The A330's port

**Files:** `FirstOfficer/HWA330/HwA330FOAutoManager.cs`; its tests; `HwA330ParityTests` (its allow-list only if a divergence is named, with its reason).

- [ ] **Steps 1-5** as Task 2's, on the same branch, keeping the A32NX parity (tests named `The_A330s_…`); commit `fix(hw-a330): …` as Task 2's.

### Task 5 [owner]: The pull request for the three

- [ ] **Step 1:** With the owner's OK once Tasks 2-4 are green: push the branch to `origin`, open one pull request (no A350 anywhere) with each aircraft's Task 1 figures and an in-sim plan in three parts (per aircraft: one managed approach, one go-around), then its changelog fragment once the number is known and its "User guide:" line. Record the PR in this file's execution record.

## Handoff to plan 21

- **Pilot-facing:** the pull request's own changelog on `main`; nothing on this branch.
- **For the merge of `main`:** the cross-profile test file's two sides reconciled when `main` is merged into this branch.

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
