# A350 MFD, KCCU and Datalink Implementation Plan (plan 09, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every KCCU key works from MSFSBA, Alt+D and Alt+M do in flight what the guide says, every ATC REQUEST and REPORT page is walked in the air, the mailbox's SEND and a real uplink are exercised with the owner's go-ahead, every weight reads in kilograms when the aircraft is set to them, and the MFD's own checklists and pages read on 1.2.8.

**Architecture:** Live checks through the MFD window and `A350Probe mfd`, `mailbox` and `hotkey`, against the aircraft's own variables (`INI_MCDU1_DIR_TO`, `INI_ATC_MSG_LIGHT`); a miss is fixed test-first on a capture in the MFD reader (`A350MfdPage`, `A350MfdInput`), the mailbox (`A350Mailbox`, `A350MailboxInput`) or the hotkeys (`A350Hotkeys`). An uplink call-out, if the aircraft lights one, gets its own row in plan 01's `A350CalloutRows.Mfd`.

**Tech Stack:** C# 13 / .NET 10, xUnit with live-captured MFD and CTR fixtures; `tools/A350Probe`.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 4: Alt+M, mailbox SEND and uplinks; open checks: X12 KCCU, KG units), 4, 5 (question 8); `docs/inibuilds-a350.md` "The MFD window", "The ATC mailbox".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. Nothing goes out to a network (SEND, a logon, a request) without the owner's go-ahead for that message (question 8); the Hoppie or SayIntentions logon is the owner's to enter.
- Owned files: `Aircraft/A350/Display/A350MfdPage.cs`, `A350MfdInput.cs`, `A350MfdWindowMessages.cs`, `A350FmsMessageAnnouncer.cs`, `A350Mailbox.cs`, `A350MailboxInput.cs`, `Aircraft/A350/A350Hotkeys.cs`, `A350CalloutRows.Mfd.cs`, `Aircraft/IniBuildsA350Definition.Hotkeys.cs`, `Forms/A350/A350MfdWindow.cs`, `A350MailboxWindow.cs`, `A350WindowsForm.cs`; tests `A350Mfd*Tests`, `A350FmsMessageTests`, `A350MailboxTests`, `A350HotkeysTests`, new tests named for their feature; fixtures `p09_*.json`; `docs/inibuilds-a350.md` "The MFD window", "The ATC mailbox"; `docs/a350/evidence/09-mfd-kccu-datalink.md` (new).
- Hotkeys stay where they are: no key moves or changes meaning; a new key is the owner's question first.
- The ATC COM REQUEST keys and OCEANIC are disabled on the ground (`AircraftVariables::GetInt(3)` 1): walk them in the air only. The EMER keys are empty handlers on 1.2.7 ("Do not chase"): re-check on 1.2.8 only.

## Review Focus

- A KCCU key pressed while the cursor is on the other screen: it moves the cursor first ([A350-4]) or says why nothing happened (Task 1).
- Alt+D with no flight plan: says so, never "pressed" (Task 1 test).
- An uplink arriving while the mailbox window is open: said once, by the window, not twice (Task 3 test).
- A weight read in pounds on a kilogram aircraft, or the reverse, anywhere: listed and fixed (Task 4).
- An MFD page whose layout moved on 1.2.8: its fixture test fails before the reader is changed (Task 5).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/09-mfd-kccu-datalink.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 09`, with the `Plan: 09` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [sim]: The KCCU keys and Alt+D (X12)

**Files:** create `docs/a350/evidence/09-mfd-kccu-datalink.md`; fixes in `A350MfdInput.cs`, `A350Hotkeys.cs`, `IniBuildsA350Definition.Hotkeys.cs` as found.

- [ ] **Step 1:** Through the MFD window and `A350Probe mfd "key:0:…"`: the arrows, BACKSPACE, OIS, MINUS, and DIR, each read back on the page (Control+arrows are confirmed); Alt+D against `INI_MCDU1_DIR_TO` and the DIR TO page it opens, with and without a flight plan.
- [ ] **Step 2:** Each miss fixed test-first (test named for the key), commit `fix(a350): the KCCU <key> key`.

### Task 2 [in flight]: Alt+M and the ATC pages in the air

**Files:** the evidence file; fixes in `A350MfdPage.cs` and the window as found; fixtures `p09_mfd_atc_<page>.json`.

- [ ] **Step 1:** Alt+M in flight opens the mailbox as the guide's A350 MFD key list says; record what is said.
- [ ] **Step 2:** In the air, every ATC COM REQUEST page (VERTICAL, LATERAL, SPEED, ADD TEXT, CLEARANCE with OCEANIC, DEPARTURE, GENERIC) and every REPORT & MODIFY page: each opens, reads its fields and keys, and types; GENERIC's state on 1.2.8 recorded (it never enabled on 1.2.7); the EMER keys re-checked.
- [ ] **Step 3:** Each misread fixed test-first on its capture, commit `fix(a350): the ATC <page> page reads <item>`.

### Task 3 [in flight] [owner]: The mailbox's SEND and a real uplink

**Files:** the evidence file; `A350Mailbox.cs`, `A350MailboxInput.cs`, `A350MailboxWindow.cs`, `A350CalloutRows.Mfd.cs` as found; tests `A350MailboxTests.cs`; fixtures `p09_ctr_mailbox_<state>.json`.

- [ ] **Step 1:** With the owner signed in to the datalink network and present, and question 8's go-ahead: a logon, one departure clearance request sent from the mailbox (SEND), and the reply worked (WILCO, STANDBY, UNABLE, ACCEPT, REJECT, CLOSE, RECALL as the uplink offers); record `INI_ATC_MSG_LIGHT` and what was said; a logoff.
- [ ] **Step 2:** If the aircraft lights an uplink and MSFSBA says nothing with the window closed: an uplink call-out (key `A350_ATC_UPLINK`, label "ATC uplink call-out", in `A350CalloutRows.Mfd`), Steps 1-5 (tests `An_uplink_is_said_once_with_the_window_closed`, `An_uplink_is_said_by_the_window_when_open`, `A_muted_uplink_row_says_nothing`), commit `feat(a350): an ATC uplink is said`.

### Task 4 [sim]: Every weight in kilograms

**Files:** the evidence file; `A350Hotkeys.cs` and the readouts it holds, as found.

- [ ] **Step 1:** With the aircraft set to kilograms: every weight MSFSBA says (the fuel, weight and readout hotkeys, the MFD's FUEL & LOAD page, the OIS LOADSHEET as the OIS window reads it) in kilograms, and in pounds when set to pounds; each wrong unit fixed test-first (with a de-DE test), commit `fix(a350): <read-out> says the aircraft's weight unit`.

### Task 5 [sim]: The MFD's own checklists and pages on 1.2.8

**Files:** fixtures `p09_mfd_<page>.json`; fixes in `A350MfdPage.cs` as found.

- [ ] **Step 1:** C/L MENU's checklists read and tick through the MFD window, end to end (the First Officer's checklists are plan 05's).
- [ ] **Step 2:** Each MFD page a test pins re-captured on 1.2.8 (`A350Probe mfdlines live 0` with `A350_SAVE`): a page that reads differently is fixed test-first on the new capture; record the differences.

## Handoff to plan 21

- **Pilot-facing:** KCCU, ATC page and mailbox fixes as found; "an ATC uplink is said" if Task 3 adds it. Guide: the A350's MFD and mailbox sections; changelog `fix`/`feature`.
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
