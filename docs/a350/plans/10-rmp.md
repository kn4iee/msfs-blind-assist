# A350 RMP Implementation Plan (plan 10, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The RMP's open items are closed: the NAVAID audio selector and IDENT work and read, STBY RAD NAV is measured and put to the owner, Output N's receivers are proven to follow the MFD's RAD NAV tuning, and the RMP pages read on 1.2.8.

**Architecture:** Live checks through the RMP window (Ctrl+Shift+R) and the pedestal's RMP panels (`A350PanelLayout.Rmp`), read back from the RMP display (`A350RmpPage`) and the A350's own variables; a miss is fixed test-first on a capture.

**Tech Stack:** C# 13 / .NET 10, xUnit with live-captured RMP fixtures; `tools/A350Probe` (`rmpwin`, `draw`, `repl`).

**Spec:** `docs/a350/spec.md` sections 3 (open check W4), 4, 5 (question 9); `docs/inibuilds-a350.md` "The RMPs"; the iniBuilds A350 FAQ for STBY RAD NAV.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/A350Rmp.cs`, `A350RmpEntry.cs`, `A350RadioCallouts.cs`, `A350PanelLayout.Rmp.cs`, `Aircraft/A350/Display/A350RmpPage.cs`, `Forms/A350/A350RmpWindow.cs`, `Services/NavRadioFormat.cs` (the A350's helper; every aircraft that calls it is checked when it changes); tests `A350Rmp*Tests`, `A350RadioCalloutsTests`; fixtures `p10_*.json`; `docs/inibuilds-a350.md` "The RMPs"; `docs/a350/evidence/10-rmp.md` (new).
- STBY RAD NAV has no cockpit writer on 1.2.7: a row writing its L:var is a shortcut no sighted pilot has, so it is built only on the owner's answer to question 9.

## Review Focus

- An audio selector already in the wished state: nothing pressed, its state said (Task 1).
- IDENT on a navaid with no ident (out of range): what the cockpit gives, never a stale ident (Task 1).
- `NavRadioFormat` under de-DE: Output N's frequencies read with points (Task 3 test, if the format changes).
- An RMP page whose layout moved on 1.2.8: its fixture test fails before the reader changes (Task 4).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/10-rmp.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 10`, with the `Plan: 10` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [sim]: The NAVAID audio selector and IDENT (W4)

**Files:** create `docs/a350/evidence/10-rmp.md`; fixes in `A350Rmp.cs`, `A350RmpEntry.cs`, `A350RmpPage.cs`, `A350PanelLayout.Rmp.cs`, `A350RmpWindow.cs` as found; fixtures `p10_rmp_navaid_*.json`.

- [ ] **Step 1:** Each NAVAID audio key (ADF 1 and 2, LS, VOR 1 and 2, MKR) on each RMP, from the RMP window and the panel: it switches, the RMP display shows it, MSFSBA reads its state; IDENT with a tuned station in range and out of range.
- [ ] **Step 2:** Each miss fixed test-first (test named for the key), commit `fix(a350): the RMP <key>`.

### Task 2 [sim]: STBY RAD NAV, measured and put to the owner

**Files:** the evidence file.

- [ ] **Step 1:** Measure what selects STBY RAD NAV and tunes on it: the order tried before (on the LS page press ADK1 first, active goes to `!!!.!!`, then digits), the variables that move (`A350Probe sample`), and what the iniBuilds FAQ says a pilot does.
- [ ] **Step 2:** Write the finding and put question 9 to the owner with it; build only what the answer says (Steps 1-5 if a row is ruled in).

### Task 3 [sim, in flight]: Output N's receivers

**Files:** the evidence file; `Services/NavRadioFormat.cs` and the A350's N path as found.

- [ ] **Step 1:** Tune VOR 1 and 2 and the LS through the MFD's RAD NAV page: do the stock NAV 1 and 2 receivers Output N reads follow (the A350 keeps its own `INI_VOR1_FREQUENCY` and `INI_VOR2_FREQUENCY`)? Output N says the LS as `a9226acf2` made it.
- [ ] **Step 2:** A receiver that does not follow: N reads the A350's own frequency (test first, de-DE included), commit `fix(a350): Output N reads the A350's own navaid tuning`; every other caller of `NavRadioFormat` checked.

### Task 4 [sim]: The RMP pages on 1.2.8

- [ ] **Step 1:** Each RMP page a fixture pins (`rmp_*`) re-captured on 1.2.8 under a new name: a page that reads differently is fixed test-first; record the differences.

## Handoff to plan 21

- **Pilot-facing:** RMP fixes as found; STBY RAD NAV as the owner rules. Guide: the A350's RMP section; changelog `fix`.
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
