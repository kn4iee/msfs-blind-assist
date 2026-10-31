# A350 Reader Traps Implementation Plan (plan 03, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every trap in the A350's draw code is logged as what it was and what it cost, a trap provably changes nothing a pilot hears, the draws that call `nvgTransform` read in the right place, and the reader's addresses are recorded for 1.2.8.

**Architecture:** The draw engine already retries a trapped draw once on a new copy (`A350DisplayService.DrawAsync`) and starts a fresh instance after any trap ([A350-9]). This plan adds a pure `A350TrapReport` that reads Wasmtime's message (the faulting function, the address against the copy's memory, the kind), puts its one-line summary in the log and the retry's outcome in the minute line, pins the "costs nothing" behavior with tests, and passes `nvgTransform`'s six numbers through the patch so the engine applies it as it applies `nvgTranslate`, `nvgScale` and `nvgRotate`.

**Tech Stack:** C# 13 / .NET 10, Wasmtime, xUnit (`WasmTestModule` for module-level tests); `tools/A350Probe`.

**Spec:** `docs/a350/spec.md` sections 2.2 and 4; `docs/a350/evidence/flight-2026-10-31.md` "Memory and the reader"; rules [A350-2], [A350-9], [A350-10], [A350-11].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/Display/A350DrawEngine.cs`, `A350DisplayService.cs`, `A350MemoryReader.cs`, `A350MemoryCopy.cs`, `A350HotPages.cs`, `A350ReaderStats.cs`, `A350ReadHealth.cs`, `A350ModuleCache.cs`, `A350ModulePatcher.cs`, `A350ModuleFacts.cs`, `A350WasmModule.cs`, `A350DrawCapture.cs`, `A350DrawState.cs`, `A350DrawLayout.cs`, `A350ShapeReader.cs`, `A350AVars.cs`, `A350ShownCapture.cs`, `A350StableValue.cs`, `A350TrapReport.cs` (new), `Aircraft/IniBuildsA350Definition.Displays.cs`, `tools/A350Probe/**` (then plan 20's); their tests (the index's "Tests" list) and `A350TrapReportTests.cs`, `A350TrapRetryTests.cs`, `A350TransformTests.cs` (new); `docs/inibuilds-a350.md` sections "The reader", "The patch", "Questions the draw code asks the sim", "Gauges and where they draw", the address table, "Operating the displays", "The draw code's own sim reads", "Logs"; `docs/a350/evidence/03-reader-traps.md` (new).
- The two traps of 2026-10-31, verbatim, are the test inputs (their backtraces hold no pilot data): EFIS#0 `ND_Logic::drawModernConnections`, "memory fault at wasm address 0x405d46e6 in linear memory of size 0x19d40000"; MFD#0 `FlightPlanButtonNew::draw`, "memory fault at wasm address 0x44a8575c in linear memory of size 0x19d40000".
- A trap is never spoken and never reported as a failure of the aircraft ([A350-2]: take another copy, never report it).
- A patch change bumps `A350ModulePatcher.Version` and its pinned hash together ([A350-11]).
- A saved memory copy (`A350Probe snapshot`) holds the pilot's A350 memory: it stays on that machine, is never committed, attached or shared (the repo ignores `tools/a350-probe/*.bin`).

## Review Focus

- A trap message without a memory-fault line (the draw deadline, an unreachable, a stack overflow) still yields its function and a kind, never an exception (Task 1 test).
- A message Wasmtime words differently (a version change): `Parse` returns null and the log keeps today's line (Task 1 test).
- The retry traps too: the minute line says "1 retried, 0 drew", the display's health counts it, and the monitors get no capture for it (Task 2 test).
- `nvgTransform` inside `nvgSave`/`nvgRestore`: positions after the restore are untransformed again (Task 4 test).
- An old compiled module in the cache after the patch change: it is never loaded (the existing [A350-11] tests stay green with the bump).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/03-reader-traps.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 03`, with the `Plan: 03` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: A trap read as what it was

**Files:** create `Aircraft/A350/Display/A350TrapReport.cs`; modify `A350DisplayService.cs` (`NoteDraw`); test `A350TrapReportTests.cs`.

**Interfaces:**
```csharp
public enum A350TrapKind { PastTheMemory, InsideTheMemory, Deadline, Other }
public sealed record A350TrapReport(string? Function, IReadOnlyList<string> Frames, ulong? Address, ulong? MemorySize,
    A350TrapKind Kind)
{
    /// Null when the message is not Wasmtime's backtrace format.
    public static A350TrapReport? Parse(string message);
    /// "EFIS#0 trap in ND_Logic::drawModernConnections: a pointer past the copy (0x405d46e6, memory 0x19d40000), retried"
    public string Summary(string gauge, int index);
}
```
- `Function` is frame 0's name without its argument list; `PastTheMemory` when `Address >= MemorySize` (a torn pointer: the copy changed under the draw), `InsideTheMemory` below it, `Deadline` for the engine's own deadline message (`A350DrawEngine.DrawDeadline`), `Other` otherwise. Kind words in the summary: "a pointer past the copy", "a fault inside the copy", "the draw ran past its deadline", "a trap".
- `NoteDraw` logs the summary line first, then the backtrace as today.
- [ ] **Steps 1-5** (tests `The_nd_trap_of_31_october_is_a_pointer_past_the_copy`, `The_mfd_trap_of_31_october_is_a_pointer_past_the_copy`, `A_deadline_trap_is_named_so`, `A_message_without_a_fault_line_still_names_its_function`, `An_unknown_format_parses_to_null`); commit `feat(a350): each draw-engine trap is logged as what it was`.

### Task 2: The retry's outcome and the fresh instance's cost, counted

**Files:** `A350ReaderStats.cs`, `A350DisplayService.cs`, `A350DrawEngine.cs`; tests `A350ReaderStatsTests.cs`.

**Interfaces:**
- `A350ReaderStats`: `RetryDrew()` beside `Retried()`; `FreshInstance(double milliseconds)`; the minute line's retry part reads "1 retried, 1 drew" (and "0 drew" when the retry failed), and a fresh instance adds "1 fresh instance, {ms} ms" (worst of the minute); a minute without them reads as today.
- `A350DrawEngine` times its `Instantiate()` and exposes `LastInstantiateMs`; `DrawAsync` calls `RetryDrew()` when the retried draw is `Ok`, and `FreshInstance` when the draw started a fresh instance.
- [ ] **Steps 1-5** (tests `A_retry_that_draws_is_counted`, `A_retry_that_traps_again_is_counted_as_not_drawn`, `A_fresh_instance_adds_its_time`, `A_quiet_minute_reads_as_before`, `The_minute_line_reads_the_same_under_de_DE`); commit `feat(a350): the reader's minute line says whether a retry drew and what a fresh instance cost`.

### Task 3: A trap changes nothing a pilot hears

**Files:** `A350DisplayService.cs` (an internal constructor taking a reader and an engine, if `A350DisplayServiceTests` has no seam for them); test `A350TrapRetryTests.cs`.

- [ ] **Steps 1-5** (tests `A_trapped_draw_is_retried_once_on_a_new_copy`, `Only_the_retry_reaches_the_monitor`, `A_second_trap_in_one_request_is_not_retried`, `A_trap_between_two_agreeing_ecam_reads_says_nothing_new`, the last feeding `A350EcamMonitor` good, good, trapped, good captures of one E/WD); commit `test(a350): a trapped draw costs the pilot nothing`.

### Task 4: `nvgTransform` applied

**Files:** `A350ModulePatcher.cs` (the hook carries the six numbers: a second hook call for `e` and `f`, or a hook of six values, as the patch's hook signature allows), `A350DrawEngine.cs` (the transform multiplied into the current one, as `nvgTranslate`, `nvgScale` and `nvgRotate` are; the warning names the gauge drawn, once per gauge), `A350ModulePatcher.Version` and its pinned hash; tests `A350TransformTests.cs` (on `WasmTestModule`), the patcher's pinned-hash test.

- [ ] **Steps 1-5** (tests `A_text_after_nvgTransform_is_placed_by_its_matrix`, `nvgRestore_undoes_a_transform`, `nvgResetTransform_clears_it`, `The_patch_version_moves_with_the_transform_hook`); commit `fix(a350): the reader applies nvgTransform`.
- [ ] **Step 6 [sim]:** Find what draws with it: click the MFD's menu bar at (308,77) on 1.2.8 (`A350Probe mfd "click:0:308:77"`), then `A350Probe mfdlines live 0` with `A350_SAVE` set; record the page and whether its lines now read in place; keep the capture as `p03_mfd_transform.json` with a test reading it.

### Task 5 [sim]: The reader's addresses on 1.2.8

**Files:** `docs/inibuilds-a350.md` (the address table, renamed for 1.2.8), `tools/A350Probe/**` (the `sdmap`/`sdsweep` layout check), `docs/a350/evidence/03-reader-traps.md`.

- [ ] **Step 1:** On 1.2.8, re-derive each row of "Addresses for 1.2.7" by its "How to find it" column (`A350Probe speeds`, `memfind`, `memread`, the module facts' log); record each value beside 1.2.7's.
- [ ] **Step 2:** Make the probe's `sdmap`/`sdsweep` check follow the re-derived layout (or the module facts) instead of 1.2.7's entry number; run each once.
- [ ] **Step 3:** Rename the table to the version it holds, keep 1.2.7's column; commit `docs(a350): the reader's addresses on 1.2.8`.

### Task 6 [in flight]: One flight read for traps

- [ ] **Step 1:** From the next full flight on a build with Tasks 1-2 (anyone's), read `a350_display.log`: each trap named, each retry's outcome, the fresh instance times, traps per hour. Record them in the evidence file. A trap that is not `PastTheMemory`, or a retry that did not draw, is a finding: put it to the owner.

## Handoff to plan 21

- **Pilot-facing:** nothing heard changes; "A350 MFD pages drawn with a transform read in the right place" if Task 4's page shows a difference (changelog `fix`).
- **Rule proposal:** extend [A350-9]'s full text with Task 1's kinds and the 2026-10-31 rate; if the transform needs a guardrail (the hook's argument count), a new A350 rule for it.
- **Docs:** `docs/tooling.md`'s A350Probe line for the layout check (plan 20's file).

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
