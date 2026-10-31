# A350 Panels Implementation Plan (plan 11, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The control map is generated from the installed 1.2.8 package with every change from 1.2.7 explained, every split Captain and First Officer panel is walked once, the SND keys are named by what they do, the gear and IRS read-outs are proven, and the panel backlog (CVR, oxygen, ELT, chronos, trims, lights one at a time) is closed.

**Architecture:** The generator (`tools/a350-gen`) reads the package's behavior files into `Resources/a350_control_map.json`; its `diff_maps.py` lists what 1.2.8 added, removed or renamed. The live checks drive each panel's rows through MSFSBA's own code (`A350Probe live`, `keep`, `listen`), read back after 2 s and restored; a miss is fixed in the map's curation or the definition's lamp and announcement tables, test-first on in-memory rows.

**Tech Stack:** Python 3 stdlib (`tools/a350-gen`), C# 13 / .NET 10, xUnit; `tools/A350Probe`.

**Spec:** `docs/a350/spec.md` sections 1 (1.2.8), 3 (open checks: panels and SND keys, gear and UNLK, T8 IRS, the panel backlog), 4; `docs/inibuilds-a350.md` "Panels" and its sub-sections, "Pedestal levers"; rules [A350-5], [A350-7].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply; [A350-5] (positions named from each control's own tooltip expression) and [A350-7] (a key whose input event only animates) hold for every regenerated row.
- Owned files: `Resources/a350_control_map.json`, `tools/a350-gen/**`, `Aircraft/A350/A350ControlMap.cs`, `A350PanelLayout.cs`, `A350PanelLayout.Names.cs`, `A350PanelLamps.cs`, `A350LampRule.cs`, `A350LampSpeech.cs`, `A350ButtonStates.cs`, `A350CommandedState.cs`, `A350CoverAlerts.cs`, `A350Doors.cs`, `A350Levers.cs`, `A350LeverSpeech.cs`, `A350StatusLines.cs`, `A350IrsAlignment.cs`, `A350ModuleWatchdog.cs`, `A350RunwayCondition.cs`, `A350WritePlan.cs`, `A350WriteOutcome.cs`, `Aircraft/IniBuildsA350Definition.Announcements.cs`; their tests (the index's list) and `tools/a350-gen/test_*.py`; `docs/inibuilds-a350.md` "Panels" with its sub-sections and "Pedestal levers"; `docs/a350/evidence/11-panels.md` (new).
- A `live` pass never drives what a restore cannot undo (the README's list: fire buttons and agents, IDG disconnects, RAT MAN ON, engine masters, thrust levers, gravity gear, fuel jettison, MASK MAN ON): those by hand with `keep`, with the owner's go-ahead.
- Tests never depend on the generated map's labels (in-memory rows).
- A regenerated map that renames a row's key breaks every Ctrl+M setting and test pinning it: keep a 1.2.7 key where the control is the same (the generator's curation), and list every key that had to change.

## Review Focus

- A control 1.2.8 removed: its row goes and nothing else pins its key (Task 1 test on the diff).
- A control whose positions 1.2.8 renumbered: its names come from its new tooltip expression, never the old numbering (Task 1, [A350-5]).
- A light that two rows share: said once, by the row that owns it (Task 4).
- "IRS aligned" at a flight load: never said (Task 3 test).
- Two lights changing in one batch: said one at a time, in the panel's order (Task 4 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/11-panels.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 11`, with the `Plan: 11` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The control map from 1.2.8

**Files:** `Resources/a350_control_map.json`, `tools/a350-gen/**` (curation only where 1.2.8 needs it); create `docs/a350/evidence/11-panels.md`.

- [ ] **Step 1:** Generate the map from the installed 1.2.8 package with `tools/a350-gen/generate_a350_map.py` into a scratch file; `diff_maps.py` against the committed 1.2.7 map; write every added, removed, renamed or re-positioned control in the evidence file with its cause in the behavior files.
- [ ] **Step 2:** Curate where a 1.2.7 key can be kept for the same control; commit the regenerated map with its "measured on" version set to 1.2.8 (`chore(a350): the control map from 1.2.8`); the generator's tests and the suite green.
- [ ] **Step 3:** Each change that needs a row's behavior to change (a removed control a hotkey drives, a new control with no row): Steps 1-5 on in-memory rows, commit per change.

### Task 2 [sim]: Every split Captain and First Officer panel, and the SND keys

**Files:** the evidence file; fixes in `A350PanelLayout*.cs`, the map's curation, `A350LampSpeech.cs` as found.

- [ ] **Step 1:** Each panel split into Captain and First Officer parts, walked once with `A350Probe live <panel>` and by ear through MSFSBA: every row's name heard, every position operating and read back, restored afterwards.
- [ ] **Step 2:** With the standby navigation display option fitted: what each SND key does on the display; a key whose placard words mislead is named by its function (the owner hears the names first).
- [ ] **Step 3:** Each miss fixed test-first (test named for the row), commit `fix(a350): the <panel> <row>`.

### Task 3 [sim]: The gear and IRS read-outs (T8)

**Files:** `A350StatusLines.cs`, `A350PanelLamps.cs`, `A350IrsAlignment.cs`, `IniBuildsA350Definition.Announcements.cs` as found; tests `A350IrsAlignmentTests.cs`, `A350PanelLampsTests.cs`.

- [ ] **Step 1:** The gear: `INI_GEARn_POSITION` on the stand (does it read 50?), in transit and locked, and the UNLK lights; what MSFSBA's gear read-outs and lights say for down, up and in transit.
- [ ] **Step 2:** A real IRS alignment from OFF: the order in which `INI_IRS1_ALIGNED` to `INI_IRS3_ALIGNED` and `INI_IRS_ALIGNED` come up, the unit of `INI_IRSn_TIME_REMAIN`; "IRS aligned" said once, nothing at a flight load.
- [ ] **Step 3:** Each miss fixed test-first, commit `fix(a350): <read-out>`.

### Task 4 [sim]: The panel backlog

**Files:** the evidence file; fixes in owned files as found.

- [ ] **Step 1:** Re-confirm each, and fix test-first where it fails: the CVR, oxygen (all four seats) and ELT tests' effects (sound, indication, ECAM) readable; the chronos on both sides; rudder and pitch trim auto-announced, each with its Ctrl+M row; lights changing together said one at a time. (The OIS GROUND page's equipment colors are plan 06's.)
- [ ] **Step 2:** Commit each fix `fix(a350): <item>`; record each item's result.

## Handoff to plan 21

- **Pilot-facing:** the 1.2.8 map's visible changes (new or renamed rows); panel fixes as found. Guide: the A350's panels; the hotkey guide if a hotkey's row changed; changelog `fix`/`improvement`.
- **Rule proposal:** none expected; a measured trap in the 1.2.8 diff proposes its line.

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
