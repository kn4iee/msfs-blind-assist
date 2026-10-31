# A350 First Officer Proof Implementation Plan (plan 05, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** What the A350's First Officer and the automation beside it were built to do is flown and proven, or fixed test-first where a flight shows otherwise: the flap margin, the landing gate and the go-around signature (which plan 19 ports to the other Airbus types), the gear the automation raises, the lowest autopilot engage height, and every normal checklist end to end.

**Architecture:** Measurement first, in flights the owner flies (or sets up and unpauses) with the First Officer's automation on; each measurement is checked against the code's own constants (`A350FOAutoManager.ExtendWithinKts` 3, the landing gate at 1500 ft with FULL by 1300 ft, `GoAroundLeverHoldSeconds` 10) and recorded. A miss becomes a failing test on the auto manager or the checklist definitions, then a fix, then the same flight step again.

**Tech Stack:** C# 13 / .NET 10, xUnit (`tests/MSFSBlindAssist.Tests/FirstOfficer/A350*`); `tools/A350Probe` (`fo run|log|items|field`, `listen`, `sample`).

**Spec:** `docs/a350/spec.md` sections 2.6, 3 (handoff 4: `ExtendWithinKts = 3`; open checks: auto-gear, T3, all normal checklists), 4; the handoff's section 5.2 (what plan 19 ports).

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply; the First Officer rules (FO-*, FOA-15: the A350's First Officer writes only panel rows, through `IniBuildsA350Definition.ApplyForFirstOfficer`, never an L:var) apply to every change.
- Owned files: `FirstOfficer/A350/**`, `Aircraft/IniBuildsA350Definition.FirstOfficer.cs`; tests `tests/MSFSBlindAssist.Tests/FirstOfficer/A350*.cs`, `A350AutopilotEngageTests.cs`, new `FirstOfficer/A350FlapMarginTests.cs` if Task 1 finds a miss; `docs/a350/evidence/05-first-officer.md` (new).
- The universal automation (`Automation/UniversalAutomationService.cs`) is shared code: a change it needs is a handoff (plan 21, or its own pull request off `main` that never names the A350).
- The 2026-10-31 flight moved no flap and no gear by the First Officer: nothing here is proven by it.
- Spec section 4's flying rules hold for every flight here: no `msfs_load_flight`, no pause, AP and A/THR on before a critical phase, OIS SAVE NOW before the approach, the handoff's go-around re-arming (approach, BTV, autobrake).

## Review Focus

- A speed hold just above a flap target (a selected speed 2 kt above S, say): the flaps come within the margin, and never past VFE next (Task 1's flight, and a test if it misses).
- A go-around with the levers back at CL before height is regained: still read as a go-around (Task 1 Step 3).
- The gear lever already up when the automation's gear-up fires: nothing is pressed twice (Task 2).
- An autopilot push refused below the A350's own height: the automation tries again above it, never gives up silently (Task 3).
- A checklist group finished by hand ticks only: it latches as FO-8 says (Task 4).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/05-first-officer.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 05`, with the `Plan: 05` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [in flight] [owner]: The flap margin, the landing gate and the go-around, flown

**Files:** create `docs/a350/evidence/05-first-officer.md`; a fix in `FirstOfficer/A350/A350FOAutoManager.cs` only if a step misses (test `FirstOfficer/A350FlapMarginTests.cs`).

- [ ] **Step 1:** A managed approach from clean, the First Officer's automatic flaps on: for each extension record the time, IAS, its target (green dot, S, F), VFE next and the height (`a350_speech.log`, the First Officer's log via `A350Probe fo log`, the characteristic speeds read out). Expected: each step within `ExtendWithinKts` (3 kt) of its target, none above VFE next, the landing gate bringing what is left by 1500 ft and FULL heard by 1300 ft.
- [ ] **Step 2:** The same approach on a selected speed held 2 kt above S, then above F: the steps still come, and never past VFE next.
- [ ] **Step 3:** A go-around at about 1000 ft (TOGA, then CL before height is regained): the auto manager reads it as a go-around (levers at MCT or TOGA seen once, a sustained climb of 300 fpm for 5 s, height regained) and holds extensions for `GoAroundLeverHoldSeconds`; then re-arm the approach, BTV and the autobrake and land.
- [ ] **Step 4:** Each miss: Steps 1-5 on `A350FOAutoManager` (test named for the miss, built from the recorded speeds), commit `fix(a350): <what the First Officer's flaps missed>`, and the flight step again.

### Task 2 [in flight]: The gear the automation raises and lowers

**Files:** `docs/a350/evidence/05-first-officer.md`; a fix only in owned files (else a handoff).

- [ ] **Step 1:** With the automation's gear-up on, a take-off: at positive rate the automation's "Positive rate. Gear up." and the gear moving (the A350's own gear position variables and UNLK lights, plan 11's read-outs); with it off, nothing pressed. Record which command the automation sent (the stock `GEAR_UP` or the A350's own `INI.GEAR` events, from `debug.log`'s sent lines) and whether the A350 took it.
- [ ] **Step 2:** The same for gear down if the automation lowers it; the After Takeoff flow's read-only `AT_GEAR_UP_CHECK` reads UP within its 20 s.
- [ ] **Step 3:** If the A350 ignores the command the automation sends, write the handoff: the event the A350 takes, where the shared automation would need an aircraft hook, the measured times.

### Task 3 [in flight]: The A350's own lowest autopilot engage height (T3)

**Files:** `docs/a350/evidence/05-first-officer.md`; `A350AutopilotEngageTests.cs` and the A350's automation profile in owned files if the height differs.

- [ ] **Step 1:** After lift-off, with the automation's autopilot engage on: the height and time after lift-off at which the A350 first accepts AP 1 (`INI_ap1_on`), and what the automation did before it (a refused push retried, never given up silently).
- [ ] **Step 2:** If the automation's height for the A350 is below what the aircraft accepts, raise it to the measured height (Steps 1-5, test `The_a350_autopilot_engage_waits_for_its_own_height`), commit `fix(a350): the automation engages the autopilot from the A350's own height`.

### Task 4 [sim, then in flight]: Every normal checklist end to end

**Files:** `docs/a350/evidence/05-first-officer.md`; fixes in `A350ChecklistDefinitions.cs`, `A350FlowDefinitions.cs`, `A350StateEvaluator.cs` as found.

- [ ] **Step 1:** From cold and dark to securing, each First Officer checklist group (Shift+C) worked twice, once by its flow and once by hand ticks: each line reads its live value, ticks from the cockpit's state, latches when FO-8 says, and a Captain line stays a reminder. Record each group's result.
- [ ] **Step 2:** Each miss: Steps 1-5 (test named for the line), commit `fix(a350): <checklist> <line> <what it now does>`.

## Handoff to plan 21

- **Pilot-facing:** only what Tasks 1-4 fix, each in its own words; the guide's First Officer A350 notes if a step changed.
- **For plan 19:** the flown figures from Task 1 (margins, gate heights, the go-around timeline), which the other Airbus ports cite.
- **For the shared automation:** Task 2's finding if the A350 needs its own gear event.

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
