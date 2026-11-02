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

- **Pilot-facing:** plan 05 changed only the First Officer's automatic flaps on the A350 (category fix): flaps 1 now comes at green dot on a
  level deceleration once the approach phase is active (before, only after a descent of 300 ft), up to 10,000 ft above the ground in the
  approach phase. The guide's row ("Extends and retracts the flaps by the speed tape") still holds: no guide change.
- **For plan 19:** the flown figures (evidence 05, Task 1 and Step 4): every step within 3 kt of its target (flaps 1 at green dot, 2 at S,
  3 at F, FULL at F once the gear is down or at the 1,500 ft landing gate); no step above VFE next; on a go-around at 1,960 ft the lever
  went to 0 18 s after TOGA, past the 10 s hold, and the FMS phase went from 6 to 3, not 9. The fixes to port: arm extension on the FMS's
  own approach phase on a level segment (never while climbing), with a 10,000 ft AGL ceiling in that phase; a level approach at 5,000 ft
  over a sea-level field otherwise sits on a 5,000 ft AGL ceiling.
- **For the shared automation:** none: the stock `GEAR_UP` the automation sends was taken by the A350 on three take-offs.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** landed
- **Claimed by:** Francesco Tissera, with Claude, 2026-11-02
- **Worked by:** Claude (Opus 5.5) in the live sim, 2026-11-02
- **Started:** 2026-11-02, from `a5a33328f`
- **Landed:** 2026-11-02
- **Tests at landing:** 17,816 passed (`ClaudeRulesHookTests` left to CI) at `f9527e830`
- **Tasks:** 1: Steps 1-3 flown, one miss (no flaps 1 on a level deceleration in the approach phase) fixed test-first in `2d3b12274`
  (`A350FlapMarginTests`) and flown again on a fresh flight (every step level, within 3 kt); that flight found a second miss (the 5,000 ft AGL
  ceiling at a 5,000 ft approach), fixed test-first in `f9527e830` and flown again (flaps 1 on the first sample inside the margin). 2: the stock `GEAR_UP` the shared automation sends is taken; no handoff. 3: the A350 accepts AP 1
  from 5 ft; no change. 4: every group worked from its flow, After Takeoff, Approach and Landing in flight on the fresh flight.
- **Waiting on the owner:** nothing.
- **In-sim results:** `docs/a350/evidence/05-first-officer.md`.
- **Changed from the plan:** Task 2 measured with the take-off script sending the stock event the automation sends, not the automation itself
  (the probe runs the First Officer only).
- **Rulings made while working:** the approach phase arms the flaps only below 5,000 ft AGL and never while climbing, so a departure level-off
  (climb phase) or a go-around's first seconds extend nothing; cost if wrong: an approach phase activated above 5,000 ft AGL waits for the
  descent, as before the fix. The second fix's ceiling in the approach phase is 10,000 ft AGL (the speed limit's height): a deceleration in
  the approach phase between 5,000 and 10,000 ft AGL now extends flaps 1 at green dot, as the manual's "CONF 1 at green dot" asks; cost if
  wrong: a pilot who wants to stay clean that high in the approach phase sees flaps 1 there.
