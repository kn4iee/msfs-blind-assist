# A350 ECAM Implementation Plan (plan 04, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The E/WD call-outs say each memo once and in the color the cockpit shows, a memo that comes back after an alert is not said again, the reversers are called as a pilot monitoring calls them, and the ECAM's open checks (cabin ready, flight phases, every SD page) are closed.

**Architecture:** First capture what the E/WD draws when COMPANY MSG and WXR/PWS OFF swap colors (`A350Probe ecamwatch` at 2 Hz in cruise, beside a window capture of the cockpit): either the reader pairs a text with the wrong fill, or the aircraft draws the flip. Then the fixes sit in the E/WD's parse and its announcer (`A350Ewd`, `A350EcamAnnouncer`), test-first on the captured frames: a color change said only once it holds (owner question 4), a memo line back within 10 s not said again (question 3), and a reverser call-out on its own Ctrl+M row (plan 01's `A350CalloutRows.Ecam`, muted by key).

**Tech Stack:** C# 13 / .NET 10, xUnit with live-captured CTR fixtures; `tools/A350Probe` (`ecamwatch`, `ecamlisten`, `displays`, `sdtext`).

**Spec:** `docs/a350/spec.md` sections 2.3, 3 (open checks: `INI_CABIN_IS_READY`, engine and reverser status, flight phase, W3), 4 and 5 (questions 3 and 4); `docs/a350/evidence/flight-2026-10-31.md` "E/WD memos"; `docs/inibuilds-a350.md` "The E/WD", "The SD".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/Display/A350Ewd.cs`, `A350EwdText.cs`, `A350EcamMonitor.cs`, `A350EcamAnnouncer.cs`, `A350SdPage.cs`, `A350SdSymbols.cs`, `A350SdText.cs`, `Aircraft/A350/A350CalloutRows.Ecam.cs`; tests `A350Ewd*Tests`, `A350Ecam*Tests`, `A350Sd*Tests`, `A350EngineDisplayTests`, new `A350MemoHoldTests.cs`, `A350ReverserCalloutTests.cs`; fixtures `p04_*.json`; `docs/inibuilds-a350.md` "The E/WD", "The SD", "ECAM control panel"; `docs/a350/evidence/04-ecam.md` (new).
- The colors as drawn are the cockpit's words: "COMPANY MSG, green", "WXR/PWS OFF, amber", never renamed.
- If Task 1 shows the wrong color comes from how the engine records a fill (`A350DrawEngine.cs`) or how rows group (`A350DrawLayout.cs`), those are plan 03's files: agree the change with its claimant, or make it once 03 has landed (the index's rule), and say so in the record.
- The owner's answers (2026-10-31): a memo line back unchanged within 10 s is not said again (question 3); if the aircraft itself flips a memo's color, the change is said once it has held 3 s (question 4).
- A line holding red or amber in the main column is always said when it appears, however recently it went: only memos are held.

## Review Focus

- A real caution that clears and comes back within 10 s (main column, amber) is said again (Task 3 test `An_amber_caution_back_within_10_s_is_said_again`).
- A memo whose color really changes and stays (COMPANY MSG green to amber for a new message, if the aircraft does that) is said once it holds 3 s (Task 2 test).
- A muted E/WD row (Ctrl+M) says nothing for the memo hold's late sentences either (Task 2 test).
- One reverser deploying, the other not (an engine shut down): one sentence naming the one (Task 4 test).
- A flight load in the middle of a hold: the new flight's first reads are its silent baseline, nothing held over from the last (Task 3 test with `Rebaseline`).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/04-ecam.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 04`, with the `Plan: 04` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [in flight]: Catch a memo swap and the cabin-ready repeats

**Files:** create `docs/a350/evidence/04-ecam.md`; fixtures `tests/MSFSBlindAssist.Tests/A350/Fixtures/p04_ctr_memo_*.json`.

- [ ] **Step 1:** In cruise (AP and A/THR on, COMPANY MSG and WXR/PWS OFF both shown), run `A350Probe ecamwatch 1800 <folder>` beside MSFSBA, and `A350Probe sample` on `INI_CABIN_IS_READY` (and any memo variable Step 3 names) over the same 30 minutes.
- [ ] **Step 2:** At each swap the watch prints, capture the sim window (PrintWindow, the "capture the sim window" technique) within a second and look at the E/WD's memo column: which color does the cockpit show? Keep two captures of a swap and one of the steady state as fixtures (`p04_ctr_memo_steady.json`, `p04_ctr_memo_swap_1.json`, `_2.json`).
- [ ] **Step 3:** Find where the swapped color comes from in the capture: each text's recorded fill against the draw's call order (`A350Probe draw` on the capture's frames, `A350_SHAPES=1`), and note whether CABIN READY or COMPANY MSG repeat with `INI_CABIN_IS_READY` or alone.
- [ ] **Step 4:** Write the finding: (a) the reader pairs a text with another's fill, or (b) the aircraft draws the flip; and what repeats and why.

### Task 2: Memo colors read and said as they hold

**Files:** `A350Ewd.cs` or `A350EcamAnnouncer.cs` by Task 1's finding (plan 03's files only as the Global Constraints say); tests `A350EwdTests.cs`, `A350MemoHoldTests.cs`.

**Interfaces:**
- For (a): `A350Ewd.Parse` pairs each memo text with its own fill; the swap fixtures read "COMPANY MSG, green" and "WXR/PWS OFF, amber".
- For (b), owner question 4: `A350EcamAnnouncer` says a line whose only change is a color once the new color has held `ColorHold` (3 s, `public static readonly TimeSpan ColorHold`), and nothing when it reverts within it; text changes keep today's two-reads agreement.
- [ ] **Steps 1-5** (tests `The_swap_capture_reads_each_memo_in_its_own_color` for (a) or `A_color_flip_that_reverts_within_3_s_says_nothing` and `A_color_change_that_holds_is_said_once` for (b), and `A_muted_ecam_row_says_no_held_line`); commit `fix(a350): E/WD memos are said in the color the cockpit holds`.

### Task 3: A memo that comes back is not said again

**Files:** `A350EcamAnnouncer.cs`, `A350EcamMonitor.cs`; test `A350MemoHoldTests.cs`.

**Interfaces:**
- `A350EcamAnnouncer` keeps each agreed line that went, with the time it went; a line back unchanged (text and colors) within `MemoReturn` (10 s, `public static readonly TimeSpan MemoReturn`) is taken back silently, unless it holds red or amber in the main column (a warning or caution is always said). The memo column's lines (x 1240 to 1480, `A350Ewd`'s `InMemoColumn`) are memos whatever their color.
- `Reset` (a flight load, `A350EcamMonitor.Rebaseline`) forgets the lines that went.
- [ ] **Steps 1-5** (tests `The_landing_memo_back_after_an_ap_off_warning_is_not_said_again`, built from the 2026-10-31 sequence; `A_memo_back_after_10_s_is_said`; `An_amber_caution_back_within_10_s_is_said_again`; `A_flight_load_forgets_the_lines_that_went`); commit `fix(a350): an E/WD memo back within 10 s is not said again`.

### Task 4 [in flight]: The reversers, as a pilot monitoring calls them

**Files:** `A350EcamMonitor.cs`, `A350Ewd.cs` (the engine block's REV texts), `Aircraft/A350/A350CalloutRows.Ecam.cs`; tests `A350ReverserCalloutTests.cs`; fixtures `p04_ctr_rev_*.json`.

- [ ] **Step 1 [in flight]:** Capture the engine block through a landing's reverse (`A350Probe ecamwatch` from 50 ft to taxi speed): what it draws for each engine (REV in transit, deployed, stowed) and its colors; keep `p04_ctr_rev_transit.json`, `p04_ctr_rev_deployed.json`, `p04_ctr_rev_stowed.json`.
- The call-out, from those fixtures: "Reverse green" once when every running engine's REV reads green; "Reverse {1|2} amber" when one stays amber past 3 s; nothing else (the levers' own sentences stay `A350LeverSpeech`'s). Its row: key `A350_ECAM_REVERSERS`, label "Reverser call-outs", in `A350CalloutRows.Ecam`, muted through the monitor's key lookup.
- [ ] **Steps 2-6**, the usual five on Step 1's fixtures (tests `Both_reversers_green_say_reverse_green_once`, `One_amber_reverser_is_named`, `A_muted_reverser_row_says_nothing`, `Reverse_green_reads_the_same_under_sv_SE`); commit `feat(a350): the reversers are called from the E/WD`.

### Task 5 [sim]: Flight phases and every SD page

**Files:** `docs/a350/evidence/04-ecam.md`; fixes in `A350SdPage.cs`, `A350SdSymbols.cs`, `A350SdText.cs` as found (tests `A350SdPageTests.cs`, `A350SdSymbolsTests.cs`); fixtures `p04_ctr_sd_<page>_<ground|air>.json`.

- [ ] **Step 1:** `INI_EWD_FLIGHT_PHASE` through one flight (seen so far: 2, 3, 12, 13): record each value with the phase it marks (from cold and dark to the gate), for window titles and any phase call-out the owner wants (a docs line; no code unless the owner rules one).
- [ ] **Step 2:** Every SD page through the ECP picker and the SD reader (CRUISE, PRESS, COND, FUEL, ELEC DC, ELEC AC, HYD, BLEED, ENG, APU, F/CTL, WHEEL, DOOR, STATUS and any 1.2.8 adds), on the ground and in flight: each reads as drawn (`A350Probe displays` and `sdtext`); keep one fixture per page and place.
- [ ] **Step 3:** Each misread fixed test-first (Steps 1-5 per page, test named for the page); commit `fix(a350): the SD's <page> page reads <what>`.

## Handoff to plan 21

- **Pilot-facing:** "A350 E/WD call-outs: memos are said once, in the color the cockpit holds; a memo back after an alert is not repeated; 'Reverse green' is called on landing (its own Ctrl+M row)." Guide: the A350's ECAM section and the Ctrl+M list; changelog `fix` and `feature`.
- **Rule proposal:** a new A350 rule if Task 1's (a) is a recording trap (the fill a text takes), with Task 1's capture as its full text.
- **Docs:** the flight phase table (Task 5) into "The E/WD" or, if it serves window titles, the owner's ruling.

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
