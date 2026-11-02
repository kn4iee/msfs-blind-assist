# A350 Reader Traps Implementation Plan (plan 03, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every trap in the A350's draw code is logged as what it was and what it cost, a trap provably changes nothing a pilot hears, the draws that call `nvgTransform` read in the right place, what the reader takes from memory is checked against the A350's own L:vars at every start, and the reader's addresses are recorded for 1.2.8.

**Architecture:** The draw engine already retries a trapped draw once on a new copy (`A350DisplayService.DrawAsync`) and starts a fresh instance after any trap ([A350-9]). This plan adds a pure `A350TrapReport` that reads Wasmtime's message (the faulting function, the address against the copy's memory, the kind), puts its one-line summary in the log and the retry's outcome in the minute line, pins the "costs nothing" behavior with tests, and passes `nvgTransform`'s six numbers through the patch so the engine applies it as it applies `nvgTranslate`, `nvgScale` and `nvgRotate`. A pure `A350FactsCheck` compares, on each copy until it decides, the variable table's slots and the draws' L:var answers with MSFSBA's cached L:vars; a disagreement withholds what it disproves and says so once.

**Tech Stack:** C# 13 / .NET 10, Wasmtime, xUnit (`WasmTestModule` for module-level tests); `tools/A350Probe`.

**Spec:** `docs/a350/spec.md` sections 2.2 and 4; `docs/a350/evidence/flight-2026-10-31.md` "Memory and the reader"; rules [A350-2], [A350-9], [A350-10], [A350-11].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/Display/A350DrawEngine.cs`, `A350DisplayService.cs`, `A350MemoryReader.cs`, `A350MemoryCopy.cs`, `A350HotPages.cs`, `A350ReaderStats.cs`, `A350ReadHealth.cs`, `A350ModuleCache.cs`, `A350ModulePatcher.cs`, `A350ModuleFacts.cs`, `A350WasmModule.cs`, `A350DrawCapture.cs`, `A350DrawState.cs`, `A350DrawLayout.cs`, `A350ShapeReader.cs`, `A350AVars.cs`, `A350ShownCapture.cs`, `A350StableValue.cs`, `A350TrapReport.cs` (new), `A350FactsCheck.cs` (new), `Aircraft/IniBuildsA350Definition.Displays.cs`, `tools/A350Probe/**` (then plan 20's); their tests (the index's "Tests" list) and `A350TrapReportTests.cs`, `A350TrapRetryTests.cs`, `A350TransformTests.cs`, `A350FactsCheckTests.cs` (new); `docs/inibuilds-a350.md` sections "The reader", "The patch", "Questions the draw code asks the sim", "Gauges and where they draw", the address table, "Operating the displays", "The draw code's own sim reads", "Logs"; `docs/a350/evidence/03-reader-traps.md` (new).
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
- A check part with no pair (an L:var MSFSBA streams that the copy lacks, or draws that ask for none MSFSBA streams): it stays undecided, the minute line says so, and nothing is withheld (Task 5 test).

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

### Task 5: What the reader takes from memory, checked against the A350's own L:vars

Added 2026-11-01 at the owner's word, after plan 01 landed (the spec is frozen). Nothing compares a value the reader takes from the copy with the same value the A350 publishes as an L:var, so after an update a layout that looks right but reads shifted values would be spoken with confidence: the probe's sweeps once named every value 24 entries off while the variable table's own check passed (fixed 2026-10-30). The check runs at every start, so a new A350 version is checked the first time it flies, and nothing of it is kept between sessions.

**Files:** create `Aircraft/A350/Display/A350FactsCheck.cs`; modify `A350ModuleFacts.cs` (the variable table's slot by name: the derivation `PauseOf` makes, exposed, and `PauseOf` built on it), `A350DrawEngine.cs` (the CustomVariables this copy's draws asked for, each with the value answered), `A350DisplayService.cs` (the check on each copy until both parts decide; a new check whenever a module is prepared), `A350StableValue.cs` (the sentence, in `A350DisplayStatus`), `A350ReaderStats.cs` (the minute line's verdicts), `Aircraft/IniBuildsA350Definition.Displays.cs` (the `Displays` setter hands the service the L:vars MSFSBA streams and their cached values, as MainForm hands it the A: vars; MainForm is not touched), `docs/inibuilds-a350.md` ("The reader": the check; "Logs": its lines); test `A350FactsCheckTests.cs`.

**Interfaces:**
```csharp
public enum A350FactsPart { Table, Variables }
public enum A350FactsVerdict { Undecided, Agrees, Disagrees }
/// One name read both ways in one copy: from the copy (its variable-table slot, or the engine's answer to a draw's ask)
/// and from MSFSBA's cached L:var.
public sealed record A350FactsPair(A350FactsPart Part, string Name, double FromCopy, double FromSim);
public sealed class A350FactsCheck
{
    public const double Tolerance = 0.5;
    public const int CopiesToDecide = 3;
    public static readonly TimeSpan Apart = TimeSpan.FromSeconds(2);
    /// One copy's pairs; returns the parts this copy decided (each part decides once).
    public IReadOnlyList<A350FactsPart> Add(IReadOnlyList<A350FactsPair> pairs, DateTime utc);
    public A350FactsVerdict Verdict(A350FactsPart part);
    /// "facts check, table: agrees on 5 pairs (INI_VLS_SPEED 166 in the copy, 166 in the sim; …)", invariant numbers.
    public string LogLine(A350FactsPart part);
}
// A350ModuleFacts: public A350VariableTable? Table { get; } (a record with uint? SlotOf(string name))
// A350DrawEngine: public IReadOnlyList<(string Name, double Answer)> AskedVariables { get; }
// A350DisplayService: public Func<string, double?>? LVarCached { get; set; } (by name, without "L:"; null when not
//     streamed or not yet read) and public IReadOnlyCollection<string> StreamedLVars { get; set; }
// A350DisplayStatus: public static string FactsMismatch(bool table, bool variables)
```
- **Pairs:** the variable table's names MSFSBA streams (the read-outs' `INI_VLS_SPEED`, `INI_VFE_SPEED`, `INI_VMAX_SPEED` and the alpha speeds among them; VLS and VREF matched their slots live on 2026-10-30), each slot read from the copy the way the pause is; and the CustomVariables the draws asked for that MSFSBA streams. A pair counts only when one of its values is non-zero and its sim value is the one it had at the previous copy, so a value moving between the copy and the sim read (VLS as the weight changes, a knob being turned) never decides.
- **Verdicts:** for each part, a copy agrees when every counted pair is within `Tolerance`, disagrees when one is not, and is no evidence without a counted pair. A part decides after `CopiesToDecide` copies in a row of one kind, each at least `Apart` after the last one counted; a copy of the other kind starts the run again. Until a part decides, everything reads as today, and the minute line ends with both verdicts ("facts: table agrees, variables undecided").
- **A disagreement** withholds what its part disproves. The table's: the service raises `PauseRead` no more this session (the pause alerts go without, as when the module does not say). The variables': the displays keep reading (Step 7). It is said once through `Spoken`, as the reader's status, with no Ctrl+M row ("getting ready" has none): "MSFSBA does not match this A350 version yet: pause alerts off", "…: display values may be wrong", or both, joined by a comma. Every verdict is logged once (`A350Display`; a disagreement at Warn, with each disagreeing pair's two values). Nothing is written to the sim or the copy ([A350-1]).
- [ ] **Steps 1-5** (tests `Three_agreeing_copies_two_seconds_apart_agree`, `Three_disagreeing_copies_disagree_and_keep_their_pairs`, `A_pair_whose_sim_value_moved_since_the_last_copy_does_not_count`, `A_pair_of_two_zeros_does_not_count`, `A_copy_of_the_other_kind_starts_the_run_again`, `Copies_closer_than_two_seconds_count_once`, `The_table_and_the_variables_decide_apart`, `With_no_pair_a_part_stays_undecided`, `A_names_table_slot_is_where_PauseOf_finds_the_pause` (on `WasmTestModule`), `The_engine_keeps_what_it_answered_each_asked_variable`, `A_table_disagreement_stops_the_pause_reads` and `A_disagreement_is_said_once_and_an_agreement_never` (on Task 3's seam), `The_check_lines_read_the_same_under_de_DE_and_sv_SE`); commit `feat(a350): the reader checks what it takes from memory against the A350's own L:vars`.
- [ ] **Step 6 [sim]:** On 1.2.8, at the gate once the displays read and again in a climb: `debug.log`'s facts check lines say both parts agree; record each part's names and values in the evidence file, beside Task 6's addresses. A disagreement cannot be made in the sim without a write into its memory ([A350-1]); the tests cover it.
- [ ] **Step 7 [owner]:** Settle the sentence by ear, and confirm or reverse the ruling that a variables disagreement keeps the displays reading (the other way: they stop, said as "cannot be read", until MSFSBA is fixed).

### Task 6 [sim]: The reader's addresses on 1.2.8

**Files:** `docs/inibuilds-a350.md` (the address table, renamed for 1.2.8), `tools/A350Probe/**` (the `sdmap`/`sdsweep` layout check), `docs/a350/evidence/03-reader-traps.md`.

- [ ] **Step 1:** On 1.2.8, re-derive each row of "Addresses for 1.2.7" by its "How to find it" column (`A350Probe speeds`, `memfind`, `memread`, the module facts' log); record each value beside 1.2.7's.
- [ ] **Step 2:** Make the probe's `sdmap`/`sdsweep` check follow the re-derived layout (or the module facts) instead of 1.2.7's entry number; run each once.
- [ ] **Step 3:** Rename the table to the version it holds, keep 1.2.7's column; commit `docs(a350): the reader's addresses on 1.2.8`.

### Task 7 [in flight]: One flight read for traps

- [ ] **Step 1:** From the next full flight on a build with Tasks 1-2 (anyone's), read `a350_display.log`: each trap named, each retry's outcome, the fresh instance times, traps per hour. Record them in the evidence file. A trap that is not `PastTheMemory`, or a retry that did not draw, is a finding: put it to the owner.

## Handoff to plan 21

- **Pilot-facing:** a trap still changes nothing heard; "A350 MFD pages drawn with a transform read in the right place" if Task 4's page shows a difference (changelog `fix`); Task 5's one sentence when what the reader takes from memory disagrees with the A350's L:vars (guide: the A350 chapter, beside "A350 displays getting ready"; changelog `improvement`).
- **Rule proposal:** extend [A350-9]'s full text with Task 1's kinds and the 2026-10-31 rate; if the transform needs a guardrail (the hook's argument count), a new A350 rule for it; and a new A350 rule for Task 5: a value the reader takes from the copy's raw memory is withheld for the session once `A350FactsCheck` finds it disagreeing with the A350's own L:vars, and the pilot is told once; the disagreement is never made silent.
- **Docs:** `docs/tooling.md`'s A350Probe line for the layout check (plan 20's file).
- **Gaps in other plans' files:** the take-off speeds, the minimums and BTV have no L:var twin in 1.2.7 (INI_V1, INI_VR and INI_V2 are never written), so Task 5 cannot check them; checking them against what draws them (the MFD's PERF TAKE OFF page, the PFD's minimums, the ND's BTV exit) is for plans 07, 08 and 09 to weigh.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** claimed
- **Claimed by:** Francesco Tissera (francescotissera1211), 2026-11-02
- **Worked by:**
- **Started:** 2026-11-02, from 548ad6175
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
