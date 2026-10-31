# A350 ND, ANF and Taxi Implementation Plan (plan 08, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The pilot hears the next waypoint when the ND's TO waypoint changes (its own Ctrl+M row), the ANF's open checks are closed (SET LDG RWY, exit selection, the 2 NM draw), the taxi turn lead is judged and set, and the other displays read on 1.2.8.

**Architecture:** The waypoint call-out reads the captain's ND from the display monitor's tick through plan 01's hook (`IniBuildsA350Definition.ObserveNd`, implemented in this plan's new partial `IniBuildsA350Definition.Nd.cs`), takes the TO waypoint as `A350NdPage` reads it, and speaks a change once two reads agree, silent through a flight load's settle. The ANF checks are captures and reads; the taxi lead is measured with the virtual pilot (the real guidance code on simulated taxis) before the owner taxis one route by ear.

**Tech Stack:** C# 13 / .NET 10, xUnit with live-captured EFIS fixtures; `tools/A350Probe` (`nd`, `ndlines`, `draw`), `tools/VirtualPilot`.

**Spec:** `docs/a350/spec.md` sections 2.6, 3 (handoff 4: taxi lead tone; open checks: waypoint passing, ANF), 4, 5 (question 7); `docs/inibuilds-a350.md` "The ND window and the ANF", "The other displays"; `docs/virtual-pilot.md`.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/Display/A350NdPage.cs`, `A350NdMap.cs`, `A350NdInput.cs`, `A350GroundDisplays.cs`, `A350BtvRollout.cs`, `Aircraft/A350/A350BtvCountdown.cs`, `A350RouteDistances.cs`, `A350CalloutRows.Nd.cs`, `Aircraft/IniBuildsA350Definition.Nd.cs` (new), `Forms/A350/A350NdWindow.cs`, the `TaxiTurnLeadSeconds` line of `Aircraft/IniBuildsA350Definition.cs`; tests `A350Nd*Tests`, `A350GroundDisplaysTests`, `A350BtvTests`, `A350RouteDistancesTests`, new `A350WaypointCalloutTests.cs`; `TaxiTurnLeadTests.cs` only for the A350's line; fixtures `p08_*.json`; `docs/inibuilds-a350.md` "The ND window and the ANF", "The other displays"; `docs/a350/evidence/08-nd-anf-taxi.md` (new).
- The ND hook runs on the display monitor's pool thread: it parses and speaks through the delegate it is given, and never touches SimConnect (SIM-5, SIM-20).
- Navigraph is the owner's to sign in; never type or log its code.
- The KDTW turn onto Y on 2026-10-31 is the shared look-ahead bug (spec section 7), not the lead: never tune the lead from it.

## Review Focus

- The TO waypoint going blank (end of the route, a discontinuity, the ND in PLAN): nothing said, and the next real waypoint is said when it appears (Task 1 test).
- A torn read naming a garbage waypoint for one read: never said (two reads agree; Task 1 test).
- A flight load or an OIS save restore: the first waypoint read is the silent baseline (Task 1 test).
- The first officer's ND in another mode than the captain's: the call-out follows the captain's (Task 1 test).
- The lead changed for the A350 leaves every other aircraft's lead as it is (`TaxiTurnLeadTests`).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/08-nd-anf-taxi.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 08`, with the `Plan: 08` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The next waypoint said when the ND's TO waypoint changes

**Files:** create `Aircraft/IniBuildsA350Definition.Nd.cs`; `Aircraft/A350/A350CalloutRows.Nd.cs`; `A350NdPage.cs` if it does not yet give the TO waypoint alone; test `A350WaypointCalloutTests.cs`; fixtures `p08_efis_to_<wpt>.json` (two consecutive TO waypoints, captured in flight, unless existing `efis0_*` fixtures hold two).

**Interfaces:**
- Consumes plan 01's `partial void ObserveNd(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted, Action<string> speak)`.
- Produces: the implementation in `IniBuildsA350Definition.Nd.cs`; the row (key `A350_ND_NEXT_WAYPOINT`, label "Next waypoint call-out") in `A350CalloutRows.Nd`; the sentence (owner question 7) "Next waypoint {name}", the name as the ND draws it.
- [ ] **Steps 1-5** (tests `A_new_to_waypoint_is_said_once_when_two_reads_agree`, `A_torn_read_says_nothing`, `A_blank_to_field_says_nothing`, `The_first_read_after_a_load_is_silent`, `A_muted_row_says_nothing`); commit `feat(a350): the next waypoint is said as the ND's TO waypoint changes`.
- [ ] **Step 6 [in flight]:** Fly a few legs with the window closed: each waypoint passage says the next once; record the times beside the ND's.

### Task 2 [sim, in flight] [owner]: The ANF's open checks

**Files:** `docs/a350/evidence/08-nd-anf-taxi.md`; fixes in `A350NdPage.cs`, `A350NdMap.cs`, `A350NdInput.cs` as found; fixtures `p08_efis_anf_*.json`.

- [ ] **Step 1:** SET LDG RWY through the ND window: the runway set and read back.
- [ ] **Step 2:** With Navigraph signed in by the owner: exit selection on a real airport for BTV, read and armed.
- [ ] **Step 3:** The ANF in ROSE NAV and ARC at 2 NM at several airports and phases: find the condition under which it once did not draw (VCBI); record it, and what MSFSBA says then.
- [ ] **Step 4:** Each misread fixed test-first on its capture, commit `fix(a350): the ANF <item>`.

### Task 3 [taxi]: The taxi turn lead

**Files:** the `TaxiTurnLeadSeconds` line of `Aircraft/IniBuildsA350Definition.cs`; `TaxiTurnLeadTests.cs` (the A350's line); `docs/a350/evidence/08-nd-anf-taxi.md`.

- [ ] **Step 1:** Run `tools/VirtualPilot` (docs/virtual-pilot.md) with the A350's lead (1.7 s, the A330's value since `c62d1a5e6`) on a 90° turn, a sharp turn and a turn after a short segment at three airports; record where the tone leads the turn against the A330's run.
- [ ] **Step 2 [owner]:** The owner taxis one route with guidance and says by ear whether each turn's cue came at the right moment; record it.
- [ ] **Step 3:** If Steps 1-2 say another value, set it (test `The_a350_turn_lead_is_<value>`), commit `fix(a350): the taxi turn lead is <value> s`; else record that 1.7 s holds.

### Task 4 [sim]: The other displays on 1.2.8

**Files:** `A350GroundDisplays.cs` and its tests as found.

- [ ] **Step 1:** BAT, SFD, SND, both ISIS and the HUD read as drawn on 1.2.8 (`A350Probe displays`), the BAT display with each BATTERY TYPE (plan 06's ledger says what it changes); each misread fixed test-first, commit `fix(a350): the <display> reads <item>`.

## Handoff to plan 21

- **Pilot-facing:** "A350: the next waypoint is said as the ND's TO waypoint changes (its own Ctrl+M row)"; ANF fixes as found. Guide: the A350's ND section and the Ctrl+M list; changelog `feature`/`fix`.
- **Rule proposal:** none expected.

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
