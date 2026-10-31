# A350 Tools Made Portable Implementation Plan (plan 20, later)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Anyone on the team can run the A350 tools on their own machine: `tools/A350Probe` and the prototype scripts in `tools/a350-probe` hold no hard-coded paths, user names or machine-specific values, are safe by default, and are documented in `docs/tooling.md`.

**Architecture:** Each tool's machine-specific values (paths, the sim's package folder, a user name, a scratch folder, a port) come from arguments, the environment or the same lookups MSFSBA uses (`MsfsPackagesLocator`, `AppLogs.PathFor`); a script the probe has replaced is removed rather than kept stale. The README and `docs/tooling.md` say how to run each.

**Tech Stack:** C# 13 / .NET 10 (`tools/A350Probe`, built on its own: `dotnet build tools/A350Probe -c Debug -p:Platform=x64`), Python 3 and Node scripts (`tools/a350-probe`).

**Spec:** `docs/a350/spec.md` sections 3 ("Last: tools portable"), 4; `tools/A350Probe/README.md`, `tools/a350-probe/README.md`, `docs/tooling.md`.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply; [A350-1] and [A350-9] for every tool hosting the draw engine (`<CETCompat>false</CETCompat>`).
- After: plan 03 has landed (it owns `tools/A350Probe/**` until then). The OIS harness (`tools/a350-ois-test`) is plan 06's (its Task 5).
- Owned files: `tools/A350Probe/**`, `tools/a350-probe/**`, the A350 part of `docs/tooling.md`; `docs/a350/evidence/20-tools-portable.md` (new).
- Never commit what a tool derives from a pilot's A350 (`tools/a350-probe/*.wasm`, `*.cwasm`, `*.bin`, `*.json`, `*.txt` stay ignored); a saved memory copy never leaves its machine.

## Review Focus

- A tool run with no argument where it used a fixed path: it says what it needs and exits, never writes into an unexpected folder.
- A second user name or drive (`D:\MSFS`): the package lookup finds the A350 through the sim's own package locations.
- A probe mode that writes to the sim: still says so in the README, and still never in a `live` pass for what a restore cannot undo.
- `A350_DRAW_JSON` and the other output folders: never overwrite a fixture a test pins.

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/20-tools-portable.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch from the branch's head once plan 03 has landed. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and that commit. Commit only that change: `docs(a350): claim plan 20`, with the `Plan: 20` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step.

### Task 1: What is machine-specific

**Files:** create `docs/a350/evidence/20-tools-portable.md`.

- [ ] **Step 1:** List every hard-coded path, user name, drive, port and scratch folder in `tools/A350Probe/**` and `tools/a350-probe/**` (search for `C:\`, `Users`, `AppData`, drive letters, `19999`), and every script in `tools/a350-probe` an `A350Probe` mode now does; record each with what replaces it.

### Task 2: `tools/A350Probe` portable

**Files:** `tools/A350Probe/**`.

- [ ] **Step 1:** Each value from Task 1 from an argument, the environment, or MSFSBA's own lookups; the README's environment table updated; the probe builds and `A350Probe list` runs offline; commit `chore(a350): the probe takes its paths from the machine it runs on`.

### Task 3: `tools/a350-probe` portable or retired

**Files:** `tools/a350-probe/**`.

- [ ] **Step 1:** Each script either takes its inputs as arguments (with a usage line) or, when an `A350Probe` mode replaced it, is deleted with the mode named in the README; the README says which remain and why; commit `chore(a350): the prototype scripts take their inputs as arguments`.

### Task 4: `docs/tooling.md`

- [ ] **Step 1:** The A350 tools' section: what each is for, how to build and run it, what it never does ([A350-1]); commit `docs(a350): the A350 tools in tooling.md`.

## Handoff to plan 21

- **Pilot-facing:** none (developer tools).
- **Docs:** CLAUDE.md's build paragraph names `tools/A350Probe` among the standalone tools already; nothing to change unless a tool is added or removed.

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
