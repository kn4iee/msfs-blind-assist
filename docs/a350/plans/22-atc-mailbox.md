# A350 ATC Mailbox Implementation Plan (plan 22, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The ATC mailbox's SEND and a real uplink are exercised with the owner's go-ahead, every reply key on an uplink works from the mailbox window, and an uplink that arrives with the window closed is said once, on its own Ctrl+M row.

**Architecture:** The mailbox is drawn by the CTR (`SD::DrawMailbox`) and read by `A350Mailbox`; `A350MailboxInput` presses through the captain's KCCU MAILBOX key and the Coherent mouse route; `A350MailboxWindow` (input Ctrl+Shift+D) shows it. Nothing here was exercised past a request built without SEND (2026-10-30). This plan sends one exchange on the owner's network, records what the aircraft does (`INI_ATC_MSG_LIGHT`, the mailbox's texts), and adds the uplink call-out on plan 01's `A350CalloutRows.Datalink` if the aircraft lights an uplink that MSFSBA does not say.

**Tech Stack:** C# 13 / .NET 10, xUnit with live-captured CTR fixtures; `tools/A350Probe mailbox`.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 4: mailbox SEND and uplinks), 4, 5 (question 8); `docs/inibuilds-a350.md` "The ATC mailbox".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. Nothing goes out to a network (SEND, a logon, a request) without the owner's go-ahead for that message (question 8, answered 2026-10-31: yes, one exchange with the owner signed in and present); the Hoppie or SayIntentions logon is the owner's to enter, never typed, logged or captured ([A350-12]).
- Owned files: `Aircraft/A350/Display/A350Mailbox.cs`, `A350MailboxInput.cs`, `Forms/A350/A350MailboxWindow.cs`, `Aircraft/A350/A350CalloutRows.Datalink.cs`; tests `A350MailboxTests`, new `A350UplinkCalloutTests.cs`; fixtures `p22_*.json`; `docs/inibuilds-a350.md` "The ATC mailbox"; `docs/a350/evidence/22-atc-mailbox.md` (new).
- The ATC COM pages on the MFD (where requests are built) are plan 09's window; this plan works the mailbox on the CTR.

## Review Focus

- An uplink arriving while the mailbox window is open: said once, by the window (Task 2 test).
- Two uplinks in a row: each said once, in order (Task 2 test).
- A reply key the uplink does not offer (STANDBY on a free-text message): not shown and not pressed (Task 1).
- The mailbox unpowered (`MAILBOX NOT AVAIL`): an uplink call-out says nothing then (Task 2 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/22-atc-mailbox.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 22`, with the `Plan: 22` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [in flight] [owner]: One real exchange

**Files:** create `docs/a350/evidence/22-atc-mailbox.md`; fixtures `p22_ctr_mailbox_<state>.json`; fixes in owned files as found.

- [ ] **Step 1:** With the owner signed in to the datalink network, present, and question 8's go-ahead: a logon, one departure clearance request sent from the mailbox (SEND), the uplink worked with the reply keys it offers (WILCO, STANDBY, UNABLE, ACCEPT, REJECT, CLOSE, RECALL), a logoff; record `INI_ATC_MSG_LIGHT`, each mailbox state and what MSFSBA said, and keep a capture of each state.
- [ ] **Step 2:** Each miss fixed test-first on its capture (test named for it), commit `fix(a350): the mailbox <item>`.

### Task 2: An uplink said with the window closed

**Files:** `A350Mailbox.cs`, `A350CalloutRows.Datalink.cs`; test `A350UplinkCalloutTests.cs`.

- [ ] **Steps 1-5**, only if Task 1 shows the aircraft lights an uplink and MSFSBA says nothing with the window closed (tests `An_uplink_is_said_once_with_the_window_closed`, `An_uplink_is_said_by_the_window_when_open`, `Two_uplinks_are_said_in_order`, `A_muted_uplink_row_says_nothing`, `An_unpowered_mailbox_says_nothing`): the row key `A350_ATC_UPLINK`, label "ATC uplink call-out", in `A350CalloutRows.Datalink`; commit `feat(a350): an ATC uplink is said`.

## Handoff to plan 21

- **Pilot-facing:** mailbox fixes as found; "an ATC uplink is said (its own Ctrl+M row)" if Task 2 adds it. Guide: the A350's mailbox section and the Ctrl+M list; changelog `feature`/`fix`.

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
