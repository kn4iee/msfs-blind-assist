# A350 OIS Options On and Off Implementation Plan (plan 23, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every option on the OIS's OPTIONS pages (PDF pages 41-51) has been seen on and off with MSFSBA reading and operating both states (handoff 3.2): a control the cockpit hides is explained when pressed, a behavior or display an option changes reads correctly, and each option has its ledger line.

**Architecture:** The handoff's method, one option at a time: what it writes and what it hides (the OIS script, the behavior file's `SKEL_INT_…` visibility conditions, the module's handling by name), both states toggled through MSFSBA's own OIS window as a pilot would, an `A350Option` entry wherever the cockpit hides controls, and the effect on any other plan's area handed to that plan. This plan only reads through the OIS window (plan 06's); a misreading it finds there goes to plan 06.

**Tech Stack:** C# 13 / .NET 10, xUnit; `tools/A350Probe ois`.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 3.2), 4, 6 (PDF pages 41-51); `Aircraft/A350/A350Options.cs` (the five options wired so far).

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. On OPTIONS > 3RD PARTY: structure and field lengths only; never type, read aloud, log or capture a credential ([A350-12]).
- Owned files: `Aircraft/A350/A350Options.cs`; tests `A350OptionsTests.cs` (new); `docs/a350/ledger/23-options.md` (new); `docs/a350/evidence/23-ois-options.md` (new).
- An option whose effect lands in another plan's files (LINKED INSTR's baros: 02; VSD PLAN MODE, ANF AUTO ZOOM: 08; THR RED: 07; IRS ALIGN, DEFAULT PANEL STATE, LIGHT TYPE, NO SMOKING SIGNS, TAXI CAMERA LIGHT, RUDDER CTRL TILLER, AUTO TILLER DISCO: 11; BATTERY TYPE's display: 08; DISPLAY TYPE: 03; anything the OIS window reads wrong: 06) is recorded here and handed to that plan's claimant, unless the fix is an `A350Option` entry.
- Record each option's setting before changing it, and restore the owner's settings afterwards; an option that needs a reload is reloaded only through an OIS save (never `msfs_load_flight`). Every change goes through MSFSBA's OIS window, never a direct L:var write.

## Review Focus

- An option that hides a control a hotkey still reaches: the hotkey says "{name} is not fitted…", never "not moved" (Task 1 test per option).
- An option read while the OIS is unpowered: unreadable, never "off" (Task 1).
- Two options hiding one control: the control names the one that hides it now (Task 1 test).
- An option whose effect only shows after a reload: both states seen after their reloads, not before (Task 1).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/23-ois-options.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 23`, with the `Plan: 23` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [sim]: Every OPTIONS option, on and off

**Files:** create `docs/a350/ledger/23-options.md`; `Aircraft/A350/A350Options.cs`; test `A350OptionsTests.cs`.

The options: EQUIPMENT: TAXI CAMERA LIGHT, SATCOM, BATTERY TYPE, SCS OUTLETS, FWD LAV VENT, LIGHT TYPE, NO SMOKING SIGNS, HUD COVERS, and the five already wired (fuel jettison, brake fans, standby nav display, HUD, ADF) re-checked on 1.2.8; AIRCRAFT: THR RED, IRS ALIGN, LINKED INSTR, VSD PLAN MODE, RUDDER CTRL TILLER, ANF AUTO ZOOM, AUTO TILLER DISCO, DISPLAY TYPE, WEATHERING, DEFAULT PANEL STATE; PRINTER, GPS JAMMING, the throttle's reverse range, CALLOUTS, VOLUME CONTROL, SIMULATION (AP DISCONNECT PROTECTION, PAUSE AT T.O.D and the rest), and 3RD PARTY (structure only).

- [ ] **Step 1 per option:** what it writes (its L:var, from the OIS script and `A350Probe ois`'s `eval:`) and what it changes (`SKEL_INT_…` in `A350_Interior.behavior.xml`, the module's handling by name); its ledger line: `| PDF page | Option | Variable | What it changes | MSFSBA rows or displays affected | Owning plan | Off seen | On seen |`.
- [ ] **Step 2 per option:** record the owner's setting; toggle it through MSFSBA's OIS window (an OIS save reload where it needs one); both states checked; restore; dates and results in the ledger. LINKED INSTR's baro effect is plan 02's measurement: cite it.
- [ ] **Steps 3-7**, the usual five for the `A350Option` entries (tests `<Option>_not_fitted_is_said_for_each_hidden_control`, `Every_option_variable_is_registered_once`); commit `feat(a350): the <option> option's controls are explained when not fitted` per option or group.

## Handoff to plan 21

- **Pilot-facing:** "A350: options the cockpit hides are explained when pressed." Guide: the A350's OIS options; changelog `improvement`.
- **Docs:** the ledger's summary for "Manual coverage".

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
