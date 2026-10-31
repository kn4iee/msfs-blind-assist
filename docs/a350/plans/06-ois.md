# A350 OIS Window Implementation Plan (plan 06, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** In the OIS window, focus lands where a pilot expects after every action and the page never jumps under them (the owner's design round), the OIS backlog items read as a sighted pilot sees them, the sign-in code is spoken but never logged, and the agent's fixtures and capture scripts are current for 1.2.8.

**Architecture:** The OIS opens in the PMDG tablets' window (`FbwEfbForm` through `CoherentPmdgEfbClient.ForA350Ois`), its pages read by `coherent-a350-ois-agent.js`. The design round is the owner's answer to question 17, focus after an action: where focus lands is measured page by page, then set by five rules (Task 2) in the agent's page model or, A350-gated, in the window, test-first in the jsdom harness and the shell's tests on pages captured from 1.2.8. The speech log's `ForTheLog`, which already drops a field's value from "Set … to …", learns the OIS's sign-in pop-up the same way.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit; JavaScript with jsdom (`tools/a350-ois-test`, Node per its README); `tools/A350Probe ois`.

**Spec:** `docs/a350/spec.md` sections 2.5, 2.7 (the test flights' OIS finding), 3 (open checks: OIS powered, field titles, duplicated pop-ups, GROUND EQUIP colors), 4, 5 (question 17); rule [A350-12]; `docs/inibuilds-a350.md` "The OIS".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. Never type, read aloud, log, capture or commit a credential or sign-in code ([A350-12]); decline consent dialogs.
- Owned files: `Resources/coherent-a350-ois-agent.js`, `Resources/coherent-a350-fap-agent.js`, `tools/a350-ois-test/**`, `Aircraft/A350/A350OisSecrets.cs`, `A350Printout.cs`, `A350PrinterWatch.cs`, `Aircraft/A350/Display/A350SpeechLog.cs`, `Forms/A350/A350PrinterWindow.cs`; additive, A350-gated changes to `SimConnect/CoherentPmdgEfbClient.cs` (`ForA350Ois`, `ForA350Fap`) and `Forms/FBWA380/FbwEfbForm.cs` (the window every EFB shares: a change there never alters another aircraft's tablet, `tools/flypad-shell-test` green); tests `A350OisSecretsTests`, `A350SpeechLogTests`, `A350Print*Tests`, `FbwEfbFormFieldSetSpokenTests`; `docs/inibuilds-a350.md` "The OIS", "The FAP and the printer"; `docs/a350/evidence/06-ois.md` (new).
- The OPTIONS pages' on-and-off work is plan 23's; a misreading it finds in the OIS window comes here.
- Keys and the window's tab order change only by the owner's answer to question 17; no existing key moves.

## Review Focus

- The sign-in code in both shapes the OIS uses ("User Code:: …", "User Code: …"): never logged (Task 1 tests).
- A sentence that only contains the word "code" (a squawk, a QR code notice): logged as it is (Task 1 test).
- A layout change on the A350's OIS leaves the PMDG tablets, the flyPad and the FAP reading as before (Task 2: the other harnesses green).
- A page whose layout moved on 1.2.8: the harness fails on the old fixture before the agent changes (Task 4).
- An item read while the OIS is unpowered: said unpowered, never as empty fields (Task 3).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/06-ois.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 06`, with the `Plan: 06` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The sign-in code is spoken, never logged

**Files:** `Aircraft/A350/Display/A350SpeechLog.cs`; test `A350SpeechLogTests.cs`.

- `ForTheLog(string line)` also replaces the token after "User Code" and its colons with "(not logged)", keeping the rest of the sentence: "User Code: (not logged) This window will close automatically when sucessfully logged in." (the OIS's own spelling). The spoken line is untouched. Test inputs use a made-up code (`TEST-0000`), never a real one.
- [ ] **Steps 1-5** (tests `A_sign_in_code_is_not_logged`, `Both_shapes_of_the_pop_up_are_masked`, `A_line_that_only_says_code_is_logged_as_is`, `A_field_set_still_loses_its_value`); commit `fix(a350): the speech log keeps the OIS's sign-in code out`.

### Task 2 [sim] [owner]: Focus after an action

**Files:** `Resources/coherent-a350-ois-agent.js`, `tools/a350-ois-test/**`, `Forms/FBWA380/FbwEfbForm.cs` (A350-gated), `tools/flypad-shell-test/efb-shell.test.js` (its A350 tests); test per rule.

The owner's answer to question 17 (2026-10-31): what is hardest is where focus lands after an action, and the page jumping; nothing else was named. The target, as the A380's MCDU window keeps its selection (a page-change signature gate, the saved row restored):
- a press that leaves the page as it is (a switch, a choice, a field set, COMPUTE): focus stays on the control pressed, found again by its identity (its label and place), never by its row number; the result is said, focus does not move to it;
- a press that opens another page: focus on that page's first line, its title said once;
- a pop-up or dialog: focus on its first control; when it closes, back on the control that opened it;
- a list that opens under a button: focus on its first item; when it closes, back on the button;
- a page that redraws by itself (a timer, a sim change): focus stays where it was, and the window never scrolls under the pilot.
- [ ] **Step 1 [sim]:** On each OIS page, both sides, record where focus lands today after each kind of action above (through the window, by ear and by the shell's selected row), and what the page does; keep a capture of each page where it goes wrong (`capture.js`, new names).
- [ ] **Step 2 per rule:** the behavior test-first in the harness or the shell's tests, then live; `npm test` and `tools/flypad-shell-test` green (the PMDG tablets and the flyPad keep theirs: a change in the shared window is A350-gated, or it holds there too and their tests say so); commit `fix(a350): the OIS window keeps focus where <rule>`.
- [ ] **Step 3 [owner]:** The owner works through MISSION, PERFORMANCE, UTILITIES and OPTIONS by ear; record the verdict per rule, and any other layout item found then, which becomes a step here.

### Task 3 [sim]: The OIS backlog

- [ ] **Step 1:** Re-confirm each live, and fix test-first in the harness where it fails: the OIS powered and unpowered is said; every OIS field reads its title, never "field"; a pop-up's text is said once; the GROUND page's equipment colors read as words ("GPU: connected"). Commit each fix `fix(a350): the OIS <item>`.

### Task 4 [sim]: The fixtures and the agent on 1.2.8

- [ ] **Step 1:** Re-capture on 1.2.8 each page a test pins (new names), run the harness on them; each difference fixed in the agent (test first) or recorded as reading the same.
- [ ] **Step 2:** Walk every OIS page on both sides with `A350Probe ois` and the FAP with `A350Probe fap`; record the 1.2.8 differences in `docs/a350/evidence/06-ois.md`.

### Task 5: The OIS harness made portable

- [ ] **Step 1:** `capture.js` and `capture-fap.js` take their paths and port from arguments or the environment (the README says how); `npm test` green; commit `chore(a350): the OIS harness's capture scripts take their paths as arguments`.

## Handoff to plan 21

- **Pilot-facing:** "A350 OIS window: <the design round's changes>; fixes (<items>)." Guide: the A350's OIS section; changelog `improvement`/`fix`.
- **Rule proposal:** extend [A350-12]'s full text with the sign-in pop-up's masking in `A350SpeechLog.ForTheLog`.
- **Docs:** `docs/tooling.md`'s OIS harness lines (plan 20's file).

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
