# A350 Manual Audit: FCU, MFD and KCCU, Electronic Checklist, EFIS (plan 14, audit)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to work this plan task by task (it is live work, not code). Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, comes first.

**Goal:** Every item on PDF pages 52-63 of "Airbus A350 Manual 1.5" has a ledger line saying where a blind pilot reaches it in MSFSBA and the live evidence that it works, or that it is a gap (handed to the plan owning the code) or not modelled by iniBuilds (with evidence from the aircraft's own files).

**Architecture:** The handoff's cover-to-cover method (its section 3.1), for one page range: the pages' text and their image-only parts read, each item classified, each "built but not proven" item proven live on the ground and in the air, whole procedures rather than single controls. The audit changes no code: it writes its own ledger and new fixtures, and gives each gap to the plan owning its files.

**Tech Stack:** Git Bash `pdftotext`, Windows PowerShell 5.1 with `Windows.Data.Pdf` for image pages; MSFSBA itself; `tools/A350Probe`.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 3.1), 4, 6.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `docs/a350/ledger/14-fcu-mfd-efis.md` (new), fixtures `tests/MSFSBlindAssist.Tests/A350/Fixtures/p14_*.json` (new, for the plans that fix gaps), `docs/a350/evidence/14-manual-fcu-mfd-efis.md` (new). No code, test or other doc changes.
- Pages: PDF 52-63 (the manual's own numbers are one lower): Aircraft Systems: the FCU (guidance modes, the FPV, A/THR), the MFD and KCCU (the menu map is an image: render it), the interactive flight plan and its revisions, the KCCU controls, the Electronic Checklist, the EFIS and the EFIS Control Panel.
- "Not modelled" needs evidence from the module or its files (behavior XML, WASM names, the OIS script), never "it did not move" ([DBG-7]); the handoff's "Do not chase" list is re-checked on 1.2.8, not assumed.
- A gap in a plan that is open goes to its claimant (or into its plan file's handoff request, by them); in a plan that has landed, it is listed for the owner to make into a new plan.

## Review Focus

- The MFD menu map (an image page): every page it names has its own line, reached through the MFD window.
- A managed and a selected mode of one FCU window: both proven (the dot, the dashes, the preset a turn makes).
- An F-PLN revision offered only for one kind of waypoint (a hold, an airway entry): proven on that kind.
- An item proven only on the ground that also works in the air (or the reverse): proven in both, as the handoff asks.
- An image-only page: rendered and read, never skipped because the text layer is empty.

---

### Task 0: Claim this plan

A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/14-manual-fcu-mfd-efis.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 14`, with the `Plan: 14` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step.

### Task 1: The pages read

- [ ] **Step 1:** Extract the pages' text (`pdftotext -layout -f 52 -l 63`) and render every page whose figures carry items the text layer drops (the MFD menu map, the KCCU and EFIS pictures); keep both in your scratch folder, never in the repo.
- [ ] **Step 2:** List every item: a control, a display, a value, a behavior, a procedure step.

### Task 2: The ledger

**Files:** create `docs/a350/ledger/14-fcu-mfd-efis.md`.

- [ ] **Step 1:** One line per item: `| PDF page | Item (the manual's words) | Where in MSFSBA (panel and row, window, hotkey, call-out) | Class | Evidence | Owning plan |`. Class is one of: proven live (date and build), built but not proven, gap, not modelled (with the file or symbol that shows it). Evidence first from `docs/inibuilds-a350.md`, `git log --grep` and the handoff's "already proven" list.
- [ ] **Step 2:** Commit and push the ledger as it grows (`docs(a350): plan 14 ledger, pages N-M`).

### Task 3 [sim, in flight]: Prove what is built

- [ ] **Step 1:** Each "built but not proven" line proven on the ground and in the air through MSFSBA as a pilot would use it (whole procedures where the pages describe one); a capture kept as `p14_<what>.json` where a later fix will need it.
- [ ] **Step 2:** Update the line's class and evidence; record anything surprising in `docs/a350/evidence/14-manual-fcu-mfd-efis.md`.

### Task 4: Gaps handed on

- [ ] **Step 1:** Each gap line names its owning plan (the index's table): told to that plan's claimant if open, or listed under "Gaps for the owner" below if that plan has landed or none owns the file.
- [ ] **Step 2:** Done when every page in the range has its lines and none reads "built but not proven"; set **Status** to landed (or waiting on owner, naming the items that wait).

## Gaps for the owner

(Task 4 fills this in: each gap whose plan has landed, with the page, the item and the file.)

## Handoff to plan 21

- **Docs:** this range's summary for "Manual coverage" in `docs/inibuilds-a350.md` (pages, items, how many proven, gaps, not modelled).

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
