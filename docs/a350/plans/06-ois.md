# A350 OIS Implementation Plan (plan 06, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The OIS's sign-in code is spoken but never logged, every option on the OIS's OPTIONS pages has been seen on and off with MSFSBA reading and operating both states (handoff 3.2), the OIS backlog items read as a sighted pilot sees them, and the agent's fixtures and capture scripts are current for 1.2.8.

**Architecture:** The speech log's `ForTheLog` already drops a field's value from "Set … to …"; it learns the OIS's sign-in pop-up the same way. The options are worked one by one by the handoff's method (what the option writes and what it hides, both states toggled through MSFSBA's own OIS window, an `A350Option` where the cockpit hides controls), recorded in this plan's ledger. OIS reading fixes are made in the agent, test-first in the jsdom harness on pages captured from 1.2.8.

**Tech Stack:** C# 13 / .NET 10, xUnit; JavaScript with jsdom (`tools/a350-ois-test`, Node per its README); `tools/A350Probe ois`.

**Spec:** `docs/a350/spec.md` sections 2.5, 3 (handoff 3.2; open checks: OIS powered, field titles, duplicated pop-ups, GROUND EQUIP colors), 4, 6 (PDF pages 41-51); rule [A350-12]; `docs/inibuilds-a350.md` "The OIS".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. Never type, read aloud, log, capture or commit a credential or sign-in code; on OPTIONS > 3RD PARTY, structure and field lengths only ([A350-12]); decline consent dialogs.
- Owned files: `Resources/coherent-a350-ois-agent.js`, `Resources/coherent-a350-fap-agent.js`, `tools/a350-ois-test/**`, `Aircraft/A350/A350Options.cs`, `A350OisSecrets.cs`, `A350Printout.cs`, `A350PrinterWatch.cs`, `Aircraft/A350/Display/A350SpeechLog.cs`, `Forms/A350/A350PrinterWindow.cs`; additive, A350-gated changes to `SimConnect/CoherentPmdgEfbClient.cs` (`ForA350Ois`, `ForA350Fap`) and `Forms/FBWA380/FbwEfbForm.cs`; tests `A350OisSecretsTests`, `A350SpeechLogTests`, `A350Print*Tests`, new `A350OptionsTests.cs`; `docs/a350/ledger/06-options.md` (new); `docs/inibuilds-a350.md` "The OIS", "The FAP and the printer"; `docs/a350/evidence/06-ois.md` (new).
- An option whose effect lands in another plan's files (LINKED INSTR's baros: 02; VSD PLAN MODE, ANF AUTO ZOOM: 08; THR RED: 07; IRS ALIGN, DEFAULT PANEL STATE, LIGHT TYPE, NO SMOKING SIGNS, TAXI CAMERA LIGHT, RUDDER CTRL TILLER, AUTO TILLER DISCO: 11; BATTERY TYPE's display: 08; DISPLAY TYPE: 03): this plan records it and hands the fix to that plan's claimant, unless the fix is an `A350Option` entry.
- Record each option's setting before changing it, and restore the owner's settings afterwards; an option that needs a reload is reloaded only through an OIS save (never `msfs_load_flight`).
- Every option change goes through MSFSBA's OIS window as a pilot would (Shift+T or Ctrl+Shift+T), never a direct L:var write.

## Review Focus

- The sign-in code in the other shape the OIS uses ("User Code:: …", "User Code: …"), and the code alone on a line: none of them reaches the log (Task 1 tests).
- A sentence that only contains the word "code" (a squawk, a QR code notice) keeps its words in the log (Task 1 test).
- An option that hides a control the pilot can still press from a hotkey: the hotkey says "{name} is not fitted…", never "not moved" (Task 2 test per option).
- An option read while the OIS is unpowered: said as unreadable, never as "off" (Task 3).
- A 1.2.8 page whose layout moved: the harness fails on the old fixture before the agent is changed (Task 4).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/06-ois.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 06`, with the `Plan: 06` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The sign-in code is spoken, never logged

**Files:** `Aircraft/A350/Display/A350SpeechLog.cs`; test `A350SpeechLogTests.cs`.

**Interfaces:**
- `ForTheLog(string line)` also replaces the token after "User Code" and its colons with "(not logged)", keeping the rest of the sentence: "User Code: (not logged) This window will close automatically when sucessfully logged in." (the OIS's own spelling). The spoken line is untouched (only the log goes through `ForTheLog`).
- Test inputs use a made-up code (`TEST-0000`), never a real one.
- [ ] **Steps 1-5** (tests `A_sign_in_code_is_not_logged`, `Both_shapes_of_the_pop_up_are_masked`, `A_line_that_only_says_code_is_logged_as_is`, `A_field_set_still_loses_its_value`); commit `fix(a350): the speech log keeps the OIS's sign-in code out`.

### Task 2 [sim]: Every OPTIONS option, on and off

**Files:** create `docs/a350/ledger/06-options.md`; `Aircraft/A350/A350Options.cs` (an option per hidden control set); test `A350OptionsTests.cs`.

The options (PDF pages 41-51): EQUIPMENT: TAXI CAMERA LIGHT, SATCOM, BATTERY TYPE, SCS OUTLETS, FWD LAV VENT, LIGHT TYPE, NO SMOKING SIGNS, HUD COVERS, and the five already wired (fuel jettison, brake fans, standby nav display, HUD, ADF) re-checked on 1.2.8; AIRCRAFT: THR RED, IRS ALIGN, LINKED INSTR, VSD PLAN MODE, RUDDER CTRL TILLER, ANF AUTO ZOOM, AUTO TILLER DISCO, DISPLAY TYPE, WEATHERING, DEFAULT PANEL STATE; PRINTER, GPS JAMMING, the throttle's reverse range, CALLOUTS, VOLUME CONTROL, SIMULATION (AP DISCONNECT PROTECTION, PAUSE AT T.O.D and the rest), and 3RD PARTY (structure and field lengths only).

- [ ] **Step 1 per option:** find what it writes (its L:var, from the OIS script and `A350Probe ois`'s `eval:`) and what it changes (visibility conditions `SKEL_INT_…` in `A350_Interior.behavior.xml`, the module's handling by name); write the ledger line: option, variable, what it changes, MSFSBA rows or displays affected, owning plan.
- [ ] **Step 2 per option:** record the owner's setting; toggle it through MSFSBA's OIS window (an OIS save reload where the option needs one); check both states: a control the cockpit hides gets an `A350Option` (its control ids, `Plural` for the wording); a behavior or display that changes reads correctly (or is handed to its plan); restore the setting; write both states' dates and results in the ledger.
- [ ] **Steps 3-7** for the `A350Option` entries, the usual five (tests `<Option>_not_fitted_is_said_for_each_hidden_control`, `Every_option_variable_is_registered_once`); commit `feat(a350): the <option> option's controls are explained when not fitted` per option or group.

### Task 3 [sim]: The OIS backlog

**Files:** `Resources/coherent-a350-ois-agent.js`, `tools/a350-ois-test/**` (a test per item, fixtures captured on 1.2.8 with `capture.js`).

- [ ] **Step 1:** Re-confirm each live, and fix test-first in the harness where it fails: the OIS powered and unpowered is said; every OIS field reads its title, never "field"; a pop-up's text is said once; GROUND page equipment colors read as words ("GPU: connected").
- [ ] **Step 2:** Commit each fix `fix(a350): the OIS <item>` with its harness test; `npm test` green.

### Task 4 [sim]: The fixtures and the agent on 1.2.8

**Files:** `tools/a350-ois-test/fixtures/**`, `tools/a350-ois-test/**` tests, `Resources/coherent-a350-ois-agent.js` if a page changed.

- [ ] **Step 1:** Re-capture on 1.2.8 each page a test pins (new names, `capture.js`), and run the harness on them: every difference from the 1.2.7 page is either fixed in the agent (test first) or recorded as the same reading.
- [ ] **Step 2:** Walk every OIS page on both sides with `A350Probe ois` and the FAP with `A350Probe fap`; record the 1.2.8 differences in `docs/a350/evidence/06-ois.md`.

### Task 5: The OIS harness made portable

**Files:** `tools/a350-ois-test/capture.js`, `capture-fap.js`, `README.md`.

- [ ] **Step 1:** No hard-coded paths, user names or ports beyond the debugger's default: they come from arguments or the environment, with the README saying how; `npm test` green; commit `chore(a350): the OIS harness's capture scripts take their paths as arguments`.

## Handoff to plan 21

- **Pilot-facing:** "A350: options the cockpit hides are explained when pressed; OIS fixes (<items>)." Guide: the A350's OIS section; changelog `fix`/`improvement`.
- **Rule proposal:** extend [A350-12]'s full text with the sign-in pop-up's masking in `A350SpeechLog.ForTheLog`.
- **For other plans:** each option's effect on their files (the ledger's "owning plan" column), sent to their claimants as found.
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
