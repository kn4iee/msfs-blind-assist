# A350 OIS Window Implementation Plan (plan 06, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The OIS window is easier to move around and reads in a clearer layout (the owner's design round), the OIS backlog items read as a sighted pilot sees them, the sign-in code is spoken but never logged, and the agent's fixtures and capture scripts are current for 1.2.8.

**Architecture:** The OIS opens in the PMDG tablets' window (`FbwEfbForm` through `CoherentPmdgEfbClient.ForA350Ois`), its pages read by `coherent-a350-ois-agent.js`. The design round starts from the owner's own list of what is hard (owner question 17), page by page; each change is made in the agent (its page model: groups, headings, order) or, A350-gated, in the window, test-first in the jsdom harness on pages captured from 1.2.8. The speech log's `ForTheLog`, which already drops a field's value from "Set … to …", learns the OIS's sign-in pop-up the same way.

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

### Task 2 [sim] [owner]: The OIS window's layout and navigation

**Files:** `Resources/coherent-a350-ois-agent.js`, `tools/a350-ois-test/**` (a test per change), `Forms/FBWA380/FbwEfbForm.cs` (A350-gated) only where the change is the window's.

- [ ] **Step 1:** The owner's list (question 17's answer, copied into this plan's execution record), each item with the page it is on and what a sighted pilot sees there; capture each page on 1.2.8 (`capture.js`, new fixture names).
- [ ] **Step 2 per item:** the change test-first in the harness (the page read as it should read), then live through the window; `npm test` green, and `tools/flypad-shell-test` green for a window change; commit `fix(a350): the OIS <page> <what changed>`.
- [ ] **Step 3 [owner]:** The owner walks the changed pages by ear; record the verdict per item.

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
