# BlueBird 757 Gaps Integration Plan (plan 16, last)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to work this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, comes before any other task.

**Goal:** Fold plans 01-15 into one releasable branch: apply their handoff requests, write the pilot-facing docs once (hotkey guide, the pilot's guide, `docs/b757.md`), add the new rules, run the owner's short in-sim pass, write the changelog, open the one pull request to `main`, and delete `docs/b757/` before it merges.

**Architecture:** Plans 01-15 never touched shared docs; each left a "Handoff to plan 16" section. This plan is the only writer of the files the index reserves for it. If the owner wants the fixes and essentials released before the new features, run Tasks 1-6 once after plans 01-08 (a first pull request) and again after plans 09-15.

**Tech Stack:** Markdown, C# (only for handoff code requests), `gh`.

**Spec:** `docs/b757/spec-gaps.md`; `docs/b757/plans/02-gaps/00-index.md`; CLAUDE.md "Git workflow and release notes", CORE-19; `changelog.d/README.md`; `docs/user-guide.md`.

## Global Constraints

- Owned files: `MSFSBlindAssist/HotkeyGuides/BBS757_Hotkeys.txt`, `docs/b757.md`, `guide/**`, `docs/invariants/b757.md`, `.claude/rules/b757.md` (rule lines), `changelog.d/**`, `CLAUDE.md`, `docs/b757/evidence/live-findings.md`, plus whatever a handoff request names (frozen files included, now that every plan has landed).
- Rule lines at most 400 characters, one mechanism each, next free numbers, full text under `## B757-n` (CLAUDE.md "Adding or changing a rule"); `ClaudeContextBudgetTests` must pass.
- Changelog: one fragment per category earned, written for a pilot; the PR number is read from `gh pr create`, never guessed.
- The guide changes in the same PR (CORE-19); the in-sim pass is the owner's, a few scenarios only.

## Task 0: Claim this plan

Before any other task. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] Check the plan is free on the remote: `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/16-integration.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 16`, with the `Plan: 16` trailer.
- [ ] Push the claim: `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

## Task 1: Merge state and handoffs

- [ ] Every plan's execution record says landed (15 may say not built) and every handoff section is filled; list what still waits on the owner across the plans (it joins Task 5's pass).
- [ ] Apply each handoff code request (list them here as they arrive: e.g. plan 05's optional read-only reason for `ValueInputForm`, plan 09's settings key), with tests; build and full suite green.
- [ ] Commit `chore(b757): handoff requests from the gap plans` (skip if none).

## Task 2: Rules

- [ ] Add to `.claude/rules/b757.md` and `docs/invariants/b757.md` the proposed lines from the handoffs, numbered in landing order: B757-5 stock NAV never read (01); B757-6 guarded controls carry no click code (02); B757-7 landing altitude write (03); B757-8 VHF L/C/R are COM 1/2/3 (04); B757-9 flap speeds from the measured VREF table (06); B757-10 no EFB tap without a read-back (11); B757-11 cockpit-wide announcements through the agent's watch list (13); and extend B757-2 with the GPU tile (07) and every proven EFB toggle (11). Each full text cites its session 8 or flight-NN measurement.
- [ ] Fold the plans' `docs/b757/evidence/flight-NN.md` findings into `live-findings.md` as session 9 onward (the facts the docs and rules cite).
- [ ] Run the full suite (`ClaudeContextBudgetTests`). Commit `docs(b757): rules for the gap work`.

## Task 3: Developer doc and hotkey guide

- [ ] `docs/b757.md`: from each handoff (seams and `Bbs757CduPages`; the control map's new keys, replay test and sweep; guarded writes, landing altitude, spring buttons; radios; MCP windows, Mach, intervene, autoflight announcements; readouts and announcements; EFB ground power and the reworked window; the CDU window; the First Officer; cockpit-wide announcements; thrust levers if built); "Not here yet" rewritten (EICAS and FMA text unless plan 13 found a source, ADF, V-speed announcements, anything left open).
- [ ] `BBS757_Hotkeys.txt`: every pilot-facing line from the handoffs, in the file's own voice and order (COM radios panel, Ctrl+N, N, Shift+1-6, D, Shift+D, the MCP windows' keys and Mach, Ctrl+P, the EFB, the Overhead and Pressurization fields, the trims, the CDU keys, Shift+C, the Spoken-on-change list, Ctrl+M's groups, Alt+I, thrust levers if built). Remove "Mach entry comes later" and, if plan 15 is built, "a hardware throttle moves them". Add the shared keys the PMDG 737 guide lists that the 757 handles through MainForm, checked one by one.
- [ ] Commit `docs(b757): developer doc and hotkey guide for the gap work`.

## Task 4: The pilot's guide

- [ ] Read `docs/user-guide.md`; update the 757 section of the Boeing chapter, the First Officer chapter (the 757 row) and the hotkey reference from the handoffs; build the one-page guide as `docs/user-guide.md` says and read the 757 part through.
- [ ] Commit `docs(guide): BlueBird 757 fixes, essentials and new features`.

## Task 5: The owner's in-sim pass (owner)

1. **Gate (fixes and essentials):** panels read real names; Emergency Lights ARMED and back; Landing Altitude typed; ground power disconnect and connect; EXT PWR reads back; COM standby typed and transferred; Ctrl+N; N, D, Shift+D, Shift+3; the CDU cursor stays on its line.
2. **Climb and cruise:** Speed Intervene in VNAV, a Mach set, Shift+S, an MCP altitude change heard once.
3. **Approach:** LAND 3 (or LAND 2), the A/P disconnect announced.
4. **New features:** Shift+C's checklist ticking; the reworked EFB (a service, a preset, a setting); a preset's summary line; the CDU rows on braille.
- [ ] Record the results for the PR description; fix what fails (back to the owning plan's tests).

## Task 6: Changelog, pull request, cleanup

- [ ] Push `B757`; open the pull request to `main` (`gh pr create`, its number printed); add `changelog.d/<pr>-b757-gaps.feature.md` and `.fix.md` (and `.improvement.md` if earned), written for a pilot; commit and push.
- [ ] PR description: "What changes for a pilot" first, then the owner decisions (spec section 4 and 8), a short "How the work went" from the plans' execution records (who worked each plan, what changed from the plan, the rulings made while working, anything left open), the in-sim results, and `User guide: updated: <chapters>`.
- [ ] Before the merge, remove the working documents as the Fenix v3 branch did (`9b87b53a9`, which left no reference to them on `main`):
  1. Fold every fact that code, tests, `docs/b757.md`, `docs/invariants/b757.md`, `tools/b757-gen/curation.json` or a test fixture cites from `docs/b757/` into `docs/b757.md` or the rule's full text (each full text stands alone: what was measured, when, how), then point those comments there or drop the path. On 2026-10-25 about 25 places cited `docs/b757/` (`git grep -n "docs/b757/"`).
  2. `git rm -r docs/b757` (the spec and investigation, both plan sets with their execution records, the evidence and flight notes, `docs/b757/tools/` with the snapshot tool and the live scripts).
  3. Check: `git grep -n "docs/b757/"` prints nothing; build and full suite green after the removal (no test reads the folder); the generator tests pass.
  4. Commit `docs(b757): remove the investigation folder before merge`, its body naming the commit before it ("the history keeps the folder: `git show <sha>:docs/b757/...`"), as `9b87b53a9` did; name that commit in the PR description and in this plan's execution record; push.
- [ ] What stays on `main` on purpose: `tools/b757-gen/` (the generator, its curation and tests; CI runs them and the shipped control map is made by it, as `tools/md11-gen/` is), and plan 12's `tools/b757-gen/PanelSweep/` with the other sweeps and probes in `tools/` (CORE-4) unless the owner says to drop it (then `git rm -r` it in the commit above).
- [ ] Merge with `gh pr merge --merge` (the repository's way, so the removed folder stays in the history). After the merge: delete `B757` locally and on kn4iee, and any `B757-pNN-*` branch left; `git worktree list` shows no `.claude/worktrees/b757-*`; remove leftover `bin/b757-*` test output (`dotnet build-server shutdown` first).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started
- **Claimed by:** none
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** (date; the `B757` commit you branched from)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Pull request:** (number, link; the commit that last holds `docs/b757/` before Task 6 deletes it)
- **Waiting on the owner:** (the in-sim pass scenarios not run yet)
- **In-sim results:** (Task 5's four scenarios: passed or failed, date)
- **Changed from the plan:** (what changed, and why)
- **Rulings made while working:** (question, answer, who decided)
