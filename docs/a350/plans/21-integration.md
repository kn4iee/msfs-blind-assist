# A350 Integration Plan (plan 21, last)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to work this plan task by task (docs, owner steps and the pull request). Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, comes first.

**Goal:** Every plan's handoff is applied, the pilot's guide, hotkey guide, rules and docs say what the A350 does now, the whole suite is green on the branch merged with `main`, one regression flight follows plan 17's ledger, and, when the owner says, the pull request to `main` opens with the team's credit, the records compiled into it, and `docs/a350/` deleted.

**Architecture:** Docs and owner steps only, plus the frozen files' requested changes: the handoffs (each plan's "Handoff to plan 21") are applied in one pass per file, the proposed rule lines checked against CLAUDE.md's "Adding or changing a rule" and `ClaudeContextBudgetTests`, `main` merged in (never rebased), the full suite run (`ClaudeRulesHookTests` included), then the flight and the pull request.

**Tech Stack:** Markdown, the repository's rule and guide tooling (`ClaudeContextBudgetTests`, `ClaudeRulesHookTests`, `tools/GuideBuilder`, `tools/ChangelogBuilder`), `gh`.

**Spec:** `docs/a350/spec.md` sections 3 ("Last", "Do not chase"), 4 (Git), 5; every plan's handoff; CLAUDE.md's "Git workflow and release notes" and CORE-19.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. The pull request opens only when the owner says, with a disclaimer crediting Gus Pacleb and Tyler Wood; until then the A350 work stays out of anything public.
- After: every other plan landed (or the owner says which may wait, recorded here).
- Owned files: `guide/**`, `HotkeyGuides/iniBuilds_A350_Hotkeys.txt`, `docs/invariants/inibuilds-a350.md`, `.claude/rules/**`, `docs/first-officer.md`, `docs/visual-guidance.md` (on this branch), `docs/user-guide.md`, `changelog.d/**`, `CLAUDE.md`, `README.md`, `docs/inibuilds-a350.md`'s top, "Flying the A350 for a test", "Parity with the other aircraft", "Manual coverage", "Variants" and any new section; the frozen shared files (`MainForm*.cs`, `Services/**`, …) only for a handoff request; `docs/a350/**` (deleted at the end).
- A rule line follows CLAUDE.md's form (one line, at most 400 characters, its full text under its heading); a mirror goes in the call-site files CLAUDE.md lists.
- `main` is merged in (never rebased) and the merge announced to anyone still working on the branch.

## Review Focus

- A handoff that two plans gave for one file: applied once, both intents kept, or put to the owner.
- A guide page that names a key or window a plan changed: changed with it (CORE-19), and the PR's "User guide:" line says so.
- A rule proposed for code that moved since: its backticked names still load on a file declaring them (`ClaudeContextBudgetTests`).
- The 1.2.8 "Do not chase" list: each item re-checked by its area plan, or here if none did, never copied forward unchecked.
- `docs/a350/` deleted: no test, code or CI step reads it (the index's rule), and the last commit holding it is named in the PR.

---

### Task 0: Claim this plan

A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/21-integration.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch from the branch's head once the other plans have landed. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and that commit. Commit only that change: `docs(a350): claim plan 21`, with the `Plan: 21` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step.

### Task 1: The handoffs

- [ ] **Step 1:** Collect every plan's "Handoff to plan 21" and "Gaps for the owner" into one list in this file's execution record, by file.
- [ ] **Step 2:** Apply the frozen files' requests, each its own commit with its test where code changes (`fix(a350): <request>`); a gap no plan fixed goes to the owner, never silently dropped.

### Task 2: The rules and the docs

- [ ] **Step 1:** Each proposed rule line added to `.claude/rules/inibuilds-a350.md` (or the rule file its code loads) with its full text in `docs/invariants/inibuilds-a350.md`, mirrors where CLAUDE.md's list says; `ClaudeContextBudgetTests` green.
- [ ] **Step 2:** `docs/inibuilds-a350.md`: "Manual coverage" from the ledgers (23's and 12-17's), the 1.2.8 "Do not chase" list as each plan re-checked it, "Parity with the other aircraft" and the top updated to 1.2.8; `docs/first-officer.md`, `docs/visual-guidance.md` and `docs/tooling.md` (with plan 20) as the handoffs say.
- [ ] **Step 3:** The guide (`guide/airbus-aircraft.md`'s A350 chapter, `guide/hotkey-reference.md`, the Ctrl+M list) and `HotkeyGuides/iniBuilds_A350_Hotkeys.txt` updated from the pilot-facing lines; `tools/GuideBuilder` builds.

### Task 3: `main` merged and the whole suite

- [ ] **Step 1:** Announce the merge to the claimants still working; `git merge origin/main` (never rebase), resolve, then the whole suite green, `ClaudeRulesHookTests` included, and the solution's Release build; push.

### Task 4 [in flight] [owner]: The regression flight

- [ ] **Step 1:** One full SimBrief flight from cold and dark to securing on the merged build, following plan 17's ledger: every line that plan 17 found working still works; record the results and the logs' findings in `docs/a350/evidence/21-integration.md`.

### Task 5 [owner]: The pull request

- [ ] **Step 1:** Only when the owner says: open the pull request to `oasis1701/msfs-blind-assist` from `feat/inibuilds-a350` with the disclaimer crediting Gus Pacleb and Tyler Wood, the plans' records compiled into its description (what each plan did, who, the in-sim results, what waits), its "User guide:" line (CORE-19) and its in-sim test plan.
- [ ] **Step 2:** Once the number is known: the changelog fragment (`changelog.d/<pr>-inibuilds-a350.aircraft.md`, written for a pilot), committed and pushed.
- [ ] **Step 3:** Name the last commit holding `docs/a350/` in the description, delete the folder (`docs(a350): remove the plan documents before the merge`), push; the pull request merges with its history (`gh pr merge --merge`) when the owner says.

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
