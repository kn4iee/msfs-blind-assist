# A350 Manual Audit: The Walk-through, Flown Whole (plan 17, audit)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to work this plan task by task (it is live work, not code). Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, comes first.

**Goal:** The manual's walk-through (PDF pages 87-123, "Operations and Techniques") is flown as one SimBrief flight from cold and dark to securing, the way a blind pilot would fly it with MSFSBA alone, every step on a ledger line with where MSFSBA reaches it and whether it worked, and every gap handed to the plan owning its code.

**Architecture:** One flight, after the early plans (02-11) have landed, so it flies their fixes: each walk-through step done through MSFSBA's own windows, panels, hotkeys and call-outs (never a tool call doing the pilot's work), checked against what the step says the cockpit then shows. This is also the handoff's "full SimBrief A-to-B alone like a real pilot"; recent online A350 tutorials are cross-checked for steps the manual leaves out. The audit changes no code.

**Tech Stack:** MSFSBA itself; `tools/A350Probe` for reads only; Git Bash `pdftotext` and Windows PowerShell 5.1 `Windows.Data.Pdf` for the pages.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 3.1 and "Last"), 4, 6.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply; its sim rules above all: the owner (or the pilot flying) flies; never fly by tool calls; AP and A/THR on and OIS SAVE NOW before each critical phase; no `msfs_load_flight`; no pause unless the human says; stop by 15 GB free commit (checkpoint first).
- The SimBrief, Navigraph and datalink sign-ins are the human's; the audit never types a credential.
- Owned files: `docs/a350/ledger/17-walkthrough.md` (new), fixtures `p17_*.json`, `docs/a350/evidence/17-manual-walkthrough.md` (new). No code, test or other doc changes.
- After: plans 02-11 landed (the build flown records its commit).

## Review Focus

- A step the pilot can only do through a tool, not MSFSBA: a gap, never "proven".
- A step that works but needs the pilot to know an undocumented key or window: proven, with a guide gap for plan 21.
- A call-out a sighted pilot would be interrupted by that was not said, or one said that would not interrupt them: a gap for its plan.
- A phase the flight leaves out (a hold, a go-around): flown on purpose, as the walk-through names it.
- A restored save in the air: the AP, A/THR and THR LK state it comes back with is re-engaged before going on (the handoff's flying facts).

---

### Task 0: Claim this plan

A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/17-manual-walkthrough.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch from the branch's head once 02-11 have landed. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and that commit. Commit only that change: `docs(a350): claim plan 17`, with the `Plan: 17` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step.

### Task 1: The walk-through as a list of steps

**Files:** create `docs/a350/ledger/17-walkthrough.md`.

- [ ] **Step 1:** Extract PDF pages 87-123 (`pdftotext -layout -f 87 -l 123`) and render the pages whose pictures carry steps; list every step in order (cockpit preparation, FMS set-up, before start, pushback and start, after start, taxi, before take-off, take-off, climb, cruise, approach preparation, approach and landing, after landing, parking, securing).
- [ ] **Step 2:** Add the steps recent online A350 tutorials do that the manual leaves out, marked as such.
- [ ] **Step 3:** One ledger line per step: `| PDF page | Step | How the pilot does it in MSFSBA | Result | Evidence | Owning plan |`; commit and push the empty ledger.

### Task 2 [in flight] [owner]: The flight

- [ ] **Step 1:** A SimBrief flight planned and loaded by the pilot, cold and dark at a gate; the build's commit recorded.
- [ ] **Step 2:** Each step done through MSFSBA, its line filled in as it is done (proven live, gap, or not modelled with its evidence); a capture kept as `p17_<step>.json` where a fix will need it; OIS SAVE NOW before each critical phase.
- [ ] **Step 3:** The logs kept (`a350_speech.log`, `a350_display.log`, `debug.log`, the feature logs) and read after the flight for what the pilot did not hear; anything found added to the ledger.

### Task 3: Gaps handed on

- [ ] **Step 1:** Each gap line names its owning plan: told to its claimant if open, else listed under "Gaps for the owner" below.
- [ ] **Step 2:** Done when every step has its line; set **Status** to landed (or waiting on owner, naming what waits).

## Gaps for the owner

(Task 3 fills this in.)

## Handoff to plan 21

- **Docs:** the walk-through's summary for "Manual coverage"; the guide's A350 chapter checked against what the pilot needed to know.
- **The regression flight:** plan 21's last flight follows this ledger.

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
