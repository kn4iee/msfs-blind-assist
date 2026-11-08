# BlueBird 757 Displays from Memory: the Shared Monitor and the FMA (plan 23, added 2026-11-08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** The 757's flight mode annunciator, read from `BBS752.wasm` memory, is spoken as it changes and listed at the top of the Ctrl+P window; and the shared groundwork every later display reader (plans 24-28) builds on is in place.

**Architecture:** One state machine, `Bbs757DisplayMonitor<TLayout, TReading>`, ported from `Pmdg737FmaMonitor` with its states and timings and made generic over a small reader interface (`IBbs757DisplayReader<TLayout, TReading>`: layouts, samplers, gate, changes, rows). The FMA is its first reader. The 757's module session moves from the tablet window to the definition (`BBS757Definition.Displays.cs`), which starts, restarts and stops every monitor through a registry with one seam per later reader. The FMA layout gains the AFDS status column, the two F/D switch bytes and the MCP's selected altitude and heading for the gate, derived by the private recipe (generator version 4).

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, the private repo's toolkit (`MSFSBlindAssist.WasmToolkit`), the b757-memory probe scripts (read-only).

**Spec:** the owner's design of 2026-11-08 (`docs/superpowers/specs/2026-11-08-b757-memory-display-readers-design.md`, local); its decisions S1-S17 are repeated in "The series" below, so this file stands alone. Evidence: `docs/b757/evidence/displays-fma.md` (Task 1 writes it); the 2026-11-06 probe (`findings.md` on the private orphan branch `b757-memory`, local only).

## The series (plans 23-28)

The owner's decisions for every display plan (2026-11-08). Plans 24-28 cite them by number.

- **S1** One 757 monitor, built here; later plans add readers only. The PMDG 737's monitors stay untouched.
- **S2** The definition owns the 757's module session and every monitor; `StartBbs757Displays()` runs beside `StartPmdg737Displays()` at startup, after a switch and when the simulator changes; a flight reload invalidates the session and restarts the monitors; a switch away stops them and disposes the session. The tablet window reads the EFB through the same session.
- **S3** One gate per reader: structural (the object found by its class vptr from the gauge manager, every read inside the module, every value in range) and at least one value that changes in flight compared with the simulator's own reading against 3 s of memory history. A reader whose measuring finds no such value does not ship.
- **S4** Nothing compared or spoken while the display is unpowered, nor for 10 s after it powers up; the first good reading after a start or reload is a silent baseline; a corrupt reading is never spoken.
- **S5** No reader suppresses or replaces the MCP-light lines, plan 13's lights, the autoland status row or BlueBird's aurals and callouts; everything a reader says by itself is the non-interrupting announce.
- **S6** The FMA rows go first in the Ctrl+P window; Alt+P stays the AI read until plan 27.
- **S7** FMA wording is the PMDG 737's, word for word: "FMA: " then the changed parts in the order A/T, roll, roll armed, pitch, pitch armed, AFDS; an armed mode turning active spoken once, as active; blank "none"; a value with no word "unrecognised" (logged).
- **S8** EICAS: "EICAS warning: <text>" (red), "EICAS caution: <text>" (amber), "EICAS: <text>" (advisory, communication), "EICAS gone: <text>"; one level's messages in one settle window share a line; BlueBird's words exactly (its leading indent not spoken).
- **S9** A message the pilot's CANC hides is not spoken as gone; the CANC or RECALL press is read back once.
- **S10** An item is read from memory only where BlueBird's draw reads memory; where the draw takes a simulator variable, the reader takes the same variable.
- **S11** Status messages are spoken only while the lower EICAS shows the STATUS page.
- **S12** EHSI: plan 26 MAP and PLAN; plan 28 VOR and ILS, TCAS, the map options.
- **S13** Output Ctrl+W on the 757 (`HotkeyAction.ReadNDWaypoint`, unhandled on the 757 before plan 26) says the EHSI's top line as drawn, interrupting.
- **S14** One line for all 757 readers when no layout file loads, once per module session: "BlueBird display files could not be loaded; the display keys fall back to AI reading."
- **S15** Each window opens on the key that reads its display by AI today (Alt+E, Alt+S, Alt+N; Alt+P in plan 27); the AI read is the fallback when the reader cannot run. Alt+I stays the AI read.
- **S16** Plan 23 adds one seam per later reader; plans own separate files. Three shared lines (the module's reader list, the private recipe list, `GeneratorVersion.Current`) follow the landing protocol in `00-index.md`'s "Display readers" section.
- **S17** Recipes live on the private `feature/b757-displays`, the app branch merged in for `DisplayLayouts`; memory snapshots only in the scratchpad, deleted after use (they can hold credentials).

## Global Constraints

- **Memory:** read only ([WASM-1]); no address, offset or table base in app code ([WASM-2]); the module found only by its anchor ([WASM-3]); every read bounds-checked, re-walked ([WASM-4]); one session for every reader ([WASM-11]). A layout file lacking a member or a reader reads as missing ([WASM-26]): this plan's new FMA fields make the published generator-3 file unreadable, the EFB's reader included, until the generator-4 file is installed (Task 11 deploys them together).
- **Two repositories:** `MSFSBlindAssist.DisplayLayouts` is app-repo code, authored here (Task 4), then the app branch merged into the private worktree (`git fetch <app> +B757-p23-displays-fma` then `merge -X theirs`) so the recipe compiles (Task 5). Nothing is pushed in either repository, and no layout published, without the owner's word.
- **Snapshots:** module memory snapshots stay in the scratchpad and are deleted after use; never committed.
- **The simulator:** reads are the controller's. A write (an F/D switch, an MCP knob, CMD, arming LNAV) needs the owner's go-ahead in chat first and is put back after; the step says so. BBS Nest must run, or the 757 ignores writes.
- **House rules:** `00-index.md`'s workflow and shared conventions; CORE-7; CORE-16; A380C-6 (`SafeBeginInvoke`); CORE-14/15 (`Log.*` and `Log.Channel` only); VAT-13; US spelling; spoken and formatted text tested under de-DE and sv-SE (`TestCulture.Run`); test names keep `Bbs757`; no test reads `docs/b757/`; at most 4 SimConnect variables added (Task 7 counts them).
- **Workflow:** every commit ends with the trailers `Plan: 23` and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; build `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (a fresh worktree builds `plugins/MSFSBlindAssist.VPilotPlugin` once first); tests `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --artifacts-path <worktree>/bin/b757-p23 --filter "FullyQualifiedName~<name>"`, the full suite without the filter; toolkit tests `dotnet test toolkit/MSFSBlindAssist.WasmToolkit.Tests` in the private worktree.
- **Owned files:** `MSFSBlindAssist/Services/BBS757/**` (new files listed per task, and `Bbs757FmaReader.cs`), `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757FmaLayout.cs` and `Bbs757LayoutDescriptions.cs`, `Aircraft/BBS757Definition.Displays.cs` (new), the registry and lifecycle calls in `Aircraft/BBS757Definition.cs` (`MonitorRows`) and `Aircraft/BBS757Definition.Interaction.cs` (`OnSwitchedAway`, `OnSimContextReset`, `HandleHotkeyAction`), `Aircraft/BBS757Definition.Autopilot.cs`, `Forms/BBS757/BBS757AutopilotWindow.cs`, `Forms/BBS757/BBS757DisplayForm.cs` (new), `MainForm.BBS757.cs`, one line each in `MainForm.cs` and `MainForm.AircraftSwitch.cs` (beside the 737's calls), the tests of these files, `.claude/rules/b757.md`, `docs/invariants/b757.md`, `docs/b757/evidence/displays-fma.md`; in the private repo `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757FmaRecipe.cs`, `GeneratorVersion.cs`, new `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757FmaRecipeTests.cs` and `Bbs757FmaFixture.cs`.

## Review Focus

- **A flight reload while the FMA reads:** the new flight's first reading is a silent baseline; a stop line the pilot heard before the reload is answered by "FMA announcements resumed" alone (Task 3 test `A_stop_heard_before_a_reload_is_answered_by_resumed_alone`).
- **The 757 profile chosen but another aircraft loaded, or the sim in its menus:** the monitor waits silently, never "unavailable: aircraft memory not found" (Task 3 test `Another_aircraft_loaded_waits_silently`).
- **A mode word BlueBird adds in an update** (a value with no word): spoken "unrecognised", logged once per column and value, never dropped and never fatal (Task 6 test `A_value_with_no_word_is_spoken_unrecognised_and_logged_once`).
- **The F/D switched while an MCP knob turns:** the gate passes on a match within 3 s, each field against any sample of the window, so the simulator's lag never stops the reader (Task 6 test `Each_gate_field_may_match_a_different_sample_within_3_s`).
- **A published generator-3 layout with this build:** the file reads as missing for every 757 reader, the tablet window says "BlueBird file not recognised", nothing guessed (Task 4 test `A_layout_without_the_fma_gate_fields_reads_as_missing`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** `git fetch kn4iee`; in this session's worktree, `git switch -c B757-p23-displays-fma kn4iee/B757`; this file's Execution record: claimed, Claimed by, Started; commit only this file, `docs(b757): claim plan 23`; `git push kn4iee HEAD:B757` (fetch and rebase on a rejection). In the private worktree (`C:\Claude\MSFSBA-wasm-displays\.claude\worktrees\b757-displays`), `git fetch origin` and confirm `feature/b757-displays` is at its remote head.

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/displays-fma.md`

- [ ] **Step 1: The build.** Record the installed `BBS752.wasm` size, date and SHA-256, and whether the published generator-3 layout still fits it (`LayoutFit` through a toolkit harness). A new BlueBird build: regenerate and re-check every number below against it.
- [ ] **Step 2: The AFDS status column.** Offline (`prepare.sh`, then `switches.py`/`ifchain.py` on the FMA draw, the one function referencing UTF-32 "THR HLD"): every value the status slot (`afds+11948` in the probed build) is drawn for and its word. Live: read it cold and dark, with the Captain's F/D on, with CMD L engaged (owner's go-ahead: F/D and CMD L, put back after).
- [ ] **Step 3: The F/D bytes.** Live: `afds+11957` and `+11958` against `L:BBS_FD1_SWITCH` and `L:BBS_FD2_SWITCH` with each switch on and off (owner's go-ahead). Record which byte follows which switch, and the draw's two `i32.load8_u` getters that load them (Task 5 derives them).
- [ ] **Step 4: The MCP's selected altitude and heading.** Find BlueBird's `mcp` object (static slot 0xf1048 in the probed build) and how a recipe reaches it without a name: a pointer from `pfd` or `afds`, or a one-line accessor returning the slot's address (the probe's method). Diff the object (`diffobj.py`) with `L:BBS_AP_TARGET_ALTITUDE` and `L:BBS_AP_TARGET_HEADING` changed once each (owner's go-ahead; put back): each field's offset, type (i32 or f64), units and rounding against the L:var, and the getter the MCP's own draw or `afds` reads it through.
- [ ] **Step 5: The armed columns.** Which field is pitch armed and which roll armed (the layout says pitch armed 11944, roll armed 11940; the probe's table had them the other way): arm LNAV on the ground with a route (owner's go-ahead) and read both.
- [ ] **Step 6: The waiting signal.** `SimConnectManager.CurrentAircraftTitle` with each 757 variant loaded and with another aircraft loaded; record the titles. The monitor waits while the title lacks "757" (case-insensitive) unless this shows a better test.
- [ ] **Step 7:** Write the evidence (no snapshot, no credential); commit `docs(b757): plan 23's FMA measurements`.

### Task 2: The shared monitor, the running path

**Files:**
- Create: `MSFSBlindAssist/Services/BBS757/IBbs757DisplayReader.cs`, `MSFSBlindAssist/Services/BBS757/Bbs757DisplayMonitor.cs`, `MSFSBlindAssist/Services/BBS757/Bbs757DisplaySpeech.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757DisplayMonitorTests.cs` (with a fake reader, environment and sampler in the same file)

**Interfaces:**
- Produces (every later plan uses these exact names):
  - `public interface IBbs757Reading { bool Corrupt { get; } }`
  - `public interface IBbs757DisplaySampler<TReading> : IDisposable { bool TryRead(out TReading reading); }`
  - `public readonly record struct Bbs757SimValues(IReadOnlyDictionary<string, double> Values)` with `double? this[string key]`.
  - `public interface IBbs757DisplayReader<TLayout, TReading> where TLayout : class where TReading : IBbs757Reading` with: `string Name` ("FMA"; status lines read "<Name> announcements …"), `string MuteKey`, `string LogChannel`, `int TickMs`, `IReadOnlyList<string> GateKeys`, `IReadOnlyList<TLayout> LayoutsIn(ModuleAnalysis analysis)`, `IBbs757DisplaySampler<TReading>? SamplerFor(LocatedModule module, TLayout layout)` (null when this module's build has another layout; takes its own lease), `bool Passes(IReadOnlyList<(long AtMs, TReading Reading)> history, Bbs757SimValues sim)`, `bool? Lit(TReading reading, Bbs757SimValues sim)` (null: no power state), `IReadOnlyList<Bbs757SpokenLine> Changes(TReading heard, TReading now, bool withPrefix)`, `IReadOnlyList<string> Rows(TReading reading)`, `string DescribeAll(TReading reading)`.
  - `public readonly record struct Bbs757SpokenLine(string MuteKey, string Text)`: each change line names the Ctrl+M row that mutes it, so a reader with several rows (the upper EICAS's messages and thrust reference) is muted row by row; the status lines ("unavailable", "stopped", "resumed") belong to the reader's `MuteKey`.
  - `public interface IBbs757DisplayEnvironment` with: `long NowMs`, `Bbs757SimValues? ReadSimulator(IReadOnlyList<string> keys)` (null while disconnected or another aircraft is loaded), `bool IsMuted(string muteKey)`, `Bbs757DisplayFilesNotice FilesNotice`, `void Speak(string text)`, `void Log(string channel, string line)`, `Task<ModuleAnalysis> AnalyseAsync()`, `Task<ModuleLocation> LocateAsync()`.
  - `public interface IBbs757DisplayMonitor : IDisposable` with `void Start(IBbs757DisplayEnvironment env)`, `void Restart()`, `void Stop()`, `bool KeyFallsBackToAi()`, `IReadOnlyList<string> View()`, `TReading`-free so the registry holds every monitor.
  - `public sealed class Bbs757DisplayMonitor<TLayout, TReading> : IBbs757DisplayMonitor` with `public Bbs757DisplayMonitor(IBbs757DisplayReader<TLayout, TReading> reader)`, `public void Start(IBbs757DisplayEnvironment env, bool startTimer)`, `internal void Tick()`, `internal string StateName`, `public TReading? Latest` (the latest passing reading while Running, else default), `public IReadOnlyList<string>? RowsWhileRunning()`.
  - `Bbs757DisplaySpeech` (pure): `Unavailable(string name, string reason)` = "<name> announcements unavailable: <reason>", `Stopped(name, reason)`, `Resumed(name)`, `Starting(name)` = "<name> reader starting", `Waiting(name)` = "<name> reader waiting for the simulator".
- Consumes: `ModuleAnalysis`, `ModuleLocation`, `LocatedModule` (Services/WasmMemory/ModuleSession.cs); `Bbs757DisplayReasons` (DisplayLayouts/Bbs757/Bbs757FmaLayout.cs).

- [ ] **Step 1: Write the failing tests** (the fake reader's `Changes` returns `[new Bbs757SpokenLine("MUTE_X", "X: " + now)]` and `Rows` `["X: " + now]`):
  - `Waits_silently_while_the_simulator_reads_null`: `ReadSimulator` null for 30 s of ticks → `StateName == "WaitingForSimulator"`, nothing spoken.
  - `The_first_passing_reading_is_a_silent_baseline`: one candidate, passing readings → `Running`, nothing spoken, `View()` the reader's rows.
  - `A_change_is_spoken_once_after_300_ms_of_settling`: reading A then B held 299 ms → nothing; at 300 ms → `["X: B"]` once.
  - `A_change_that_returns_before_settling_is_not_spoken`: A, B for 200 ms, A again → nothing.
  - `A_failing_candidate_gives_way_to_the_next`: two candidates, the first never passes within 2 s → the second runs.
  - `Unlit_readings_are_not_compared_and_lighting_up_waits_10_s`: `Lit` false → no change spoken; true at t → changes before t+10 s silent, the reading at t+10 s the baseline.
  - `View_while_starting_is_one_starting_row`: before Running → `["X reader starting"]`.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Bbs757DisplayMonitorTests"`: FAIL (types missing).
- [ ] **Step 3:** Implement by porting `Pmdg737FmaMonitor`'s state machine (states Stopped, WaitingForSimulator, Analyzing, Locating, Verifying, Running, Mismatched, Unavailable; constants `SettleMs` 300, `LocateRetryMs` 5,000, `LocateGiveUpMs` 120,000, `SlowRetryMs` 30,000, `LostStopMs` 10,000, `MismatchToleranceMs` 2,000, `CorruptTickLimit` 10, `HistoryMs` 3,000, `LitQuietMs` 10,000) onto the reader interface. Its timer interval is `reader.TickMs`; its log lines go to `env.Log(reader.LogChannel, …)`. A reading counts for the gate only while `Lit` is not false and 10 s have passed since it turned true.
- [ ] **Step 4:** Run the filter: PASS. **Step 5:** Commit `feat(b757): one display monitor for the 757's memory readers`.

### Task 3: The shared monitor, stops and recovery

**Files:**
- Modify: `MSFSBlindAssist/Services/BBS757/Bbs757DisplayMonitor.cs`
- Create: `MSFSBlindAssist/Services/BBS757/Bbs757DisplayFilesNotice.cs` (`public void Say(bool muted, Action<string> speak)`, once per instance, the S14 line as `public const string Line`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757DisplayMonitorTests.cs`

**Interfaces:**
- Produces: `KeyFallsBackToAi()` true while Unavailable or stopped for at least 10 s; `View()` while stopped repeats the stop line the pilot heard, after a mismatch with ". Last known: " + `DescribeAll(latest)`.

- [ ] **Step 1: Write the failing tests:**
  - `No_passing_reading_for_2_s_stops_with_the_mismatch_line`: → spoken "X announcements stopped: values do not match the simulator" once.
  - `Ten_corrupt_readings_stop_with_unexpected_values`.
  - `A_lost_reading_searches_silently_and_says_memory_not_found_after_10_s`.
  - `The_search_window_running_out_says_unavailable_once_then_retries_every_30_s`; found later → "X announcements resumed".
  - `Resumed_carries_what_changed_meanwhile`: stopped at A, resumed at B → "X announcements resumed, " + `Changes(A, B, withPrefix: false)` joined with ", ".
  - `A_stop_heard_before_a_reload_is_answered_by_resumed_alone` (Review Focus).
  - `Another_aircraft_loaded_waits_silently` (Review Focus): `ReadSimulator` null after Restart for 10 min → nothing spoken.
  - `Final_reasons_hold_until_restart`: files not found, file not recognised, no access, internal error → "X announcements unavailable: <reason>" once, no retry until `Restart`.
  - `No_layout_file_says_the_one_files_line_once_per_notice`: two monitors on one `Bbs757DisplayFilesNotice`, `ModuleAnalysis` with no builds → the S14 line once, no reader's own line.
  - `A_muted_row_says_nothing`: `IsMuted(MuteKey)` true → no change, status or resumed line spoken; `View()` unchanged.
  - `Each_change_line_is_muted_by_its_own_row`: two lines with keys A and B, A muted → only B's text spoken; the resumed line joins only the unmuted parts.
  - `Key_falls_back_to_ai_only_when_unavailable_or_stopped_10_s`.
  - `An_exception_in_a_tick_ends_in_internal_error_and_restart_recovers`.
  - `Stop_releases_every_sampler_once`: fake samplers count `Dispose`.
  - `Session_failures_are_worded_for_the_757`: an analysis failing with the session's `FmaReasons.FileNotRecognised` and `FilesNotFound` → "X announcements unavailable: BlueBird file not recognised" and "… BlueBird 757 files not found", never "PMDG".
- [ ] **Step 2:** Run the filter: FAIL. **Step 3:** Implement as `Pmdg737FmaMonitor` does (its `_resumeOwed`, `_stopReason`, `HeardStopLine`), with `Bbs757DisplayReasons` (`Mismatch` is "values do not match the simulator"); every failure from the session passes through `Bbs757DisplayReasons.FromSession` first. **Step 4:** PASS. **Step 5:** Commit `feat(b757): the display monitor's stops, recovery and status lines`.

### Task 4: The FMA layout's new fields

**Files:**
- Modify: `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757FmaLayout.cs`, `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757LayoutDescriptions.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757LayoutFileTests.cs`, `tests/MSFSBlindAssist.Tests/Bbs757FmaReaderTests.cs` (constructor update only)

**Interfaces:**
- Produces: `Bbs757FmaLayout` gains, after `RollArmedWords`: `int Status`, `Bbs757WordTable StatusWords`, `int FdCaptain`, `int FdFirstOfficer` (byte offsets into afds), and the MCP route Task 1 Step 4 chose, as `McpRoute` fields named by it (for a pointer from afds: `int AfdsToMcp`; for a static slot: `uint McpSlot`), plus `int McpAltitude`, `int McpHeading`, `Bbs757McpValueType McpValueType` (`I32` or `F64`). `Equals`/`GetHashCode` include every new member; `Describe` logs them.
- Consumes: Task 1's evidence.

- [ ] **Step 1: Write the failing tests:** `A_layout_with_the_gate_fields_round_trips` (write, read, equal, byte-identical under de-DE and sv-SE); `A_layout_without_the_fma_gate_fields_reads_as_missing` (Review Focus: the generator-3 text of today's sample → `TryRead` false).
- [ ] **Step 2:** FAIL. **Step 3:** Add the members. **Step 4:** PASS, and the full suite (other tests construct the record). **Step 5:** Commit `feat(b757): the FMA layout carries the AFDS column and the gate's fields`.

### Task 5 [private repo]: The FMA recipe derives them

**Files:**
- Modify: `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757FmaRecipe.cs`, `toolkit/MSFSBlindAssist.WasmToolkit/GeneratorVersion.cs` (`Current = 4`, with its dated comment)
- Create: `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757FmaFixture.cs`, `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757FmaRecipeTests.cs`

**Interfaces:**
- Consumes: Task 4's record (merge the app branch in first: `git fetch <app worktree> +B757-p23-displays-fma`, `git merge -X theirs FETCH_HEAD`).
- Produces: a generated generator-4 757 layout whose `fma` reader carries every new field.

- [ ] **Step 1: Write the failing tests** on a synthetic module built by `Bbs757FmaFixture` (shaped like `Bbs757EfbFixture`: the RTTI anchor, the gauge callbacks' common accessor returning the manager, a pfd vtable, an FMA draw referencing UTF-32 "THR HLD" with the five columns, the status slot, the two `i32.load8_u` F/D getters and the MCP route): `Derives_every_column_and_its_words`, `Derives_the_status_column_and_the_fd_bytes`, `Derives_the_mcp_route_and_fields`, `A_second_THR_HLD_reference_fails_with_its_step`, `Pitch_and_roll_are_told_apart_by_their_words_not_their_order`.
- [ ] **Step 2:** FAIL. **Step 3:** Derive the status column like the armed columns (its slot's getter and its `br_table` or `i32.eq` words), the F/D bytes from the draw's two `i32.load8_u` afds getters (Captain first, as Task 1 Step 3 found), the MCP route and fields by Task 1 Step 4's route; every step matches exactly once or fails naming the step. **Step 4:** PASS, and the whole toolkit suite.
- [ ] **Step 5:** Generate from the installed `BBS752.wasm` (a harness calling `LayoutGeneration.Generate(Bbs757Module.Description, <file>, …)`); every new field equals Task 1's measurement, the existing fields and the `efb` reader unchanged; copy the file to `%APPDATA%\MSFSBlindAssist\layouts\bbs757\`. Commit `feat(bbs757): the FMA recipe derives the AFDS column and the gate's fields` (private, not pushed).

### Task 6: The FMA reader, gate and words

**Files:**
- Modify: `MSFSBlindAssist/Services/BBS757/Bbs757FmaReader.cs` (the sampler: implements `IBbs757DisplaySampler<Bbs757FmaReading>`)
- Create: `MSFSBlindAssist/Services/BBS757/Bbs757FmaGate.cs`, `MSFSBlindAssist/Services/BBS757/Bbs757FmaSpeech.cs`, `MSFSBlindAssist/Services/BBS757/Bbs757FmaDisplay.cs` (the reader: `IBbs757DisplayReader<Bbs757FmaLayout, Bbs757FmaReading>`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757FmaReaderTests.cs`, `Bbs757FmaGateTests.cs`, `Bbs757FmaSpeechTests.cs`, `Bbs757FmaDisplayTests.cs`

**Interfaces:**
- Produces: `Bbs757FmaReading` gains `int AtValue, PitchValue, PitchArmedValue, RollValue, RollArmedValue, StatusValue` (raw, for change detection and "unrecognised"), `bool FdCaptain, FdFirstOfficer`, `int McpAltitude, McpHeading`, `string? Status`; implements `IBbs757Reading`. `Bbs757FmaDisplay.MuteKey = "MSFSBA_BBS757_FMA"`, `Name = "FMA"`, `LogChannel = "bbs757-fma"`, `TickMs = 100`, `GateKeys = ["BBS_FD1_SWITCH", "BBS_FD2_SWITCH", "BBS_AP_TARGET_ALTITUDE", "BBS_AP_TARGET_HEADING"]`, `MonitorRowLabel = "Flight Mode Annunciator"`. `Bbs757FmaSpeech.Rows(reading)` gives four rows ("A/T: SPD", "Roll: LNAV, armed LOC", "Pitch: VNAV PTH, armed G/S", "AFDS: CMD").
- Consumes: Tasks 2 and 4.

- [ ] **Step 1: Write the failing tests:**
  - Reader: `Reads_the_status_column_and_the_fd_bytes`, `Reads_the_mcp_altitude_and_heading_through_the_layout_route`, `A_read_outside_the_module_is_lost`.
  - Gate: `Passes_when_fd_altitude_and_heading_match_within_3_s`, `Each_gate_field_may_match_a_different_sample_within_3_s` (Review Focus), `Heading_0_equals_360`, `An_fd_byte_that_disagrees_for_3_s_fails`.
  - Speech: `A_change_names_only_the_changed_parts_in_737_order` ("FMA: Roll: LOC, Pitch: G/S"), `An_armed_mode_turning_active_is_said_once_as_active`, `A_part_going_blank_is_none`, `A_value_with_no_word_is_spoken_unrecognised_and_logged_once` (Review Focus), `The_status_column_is_AFDS` ("FMA: AFDS: CMD"), `Rows_read_each_axis_with_its_armed_mode`, `Resumed_parts_have_no_prefix`, and each of these under de-DE and sv-SE.
- [ ] **Step 2:** FAIL. **Step 3:** Implement; the words are the layout's tables, verbatim; the change order A/T, roll, roll armed, pitch, pitch armed, AFDS (S7, as `Pmdg737FmaSpeech.DescribeChange`). **Step 4:** PASS. **Step 5:** Commit `feat(b757): the FMA reader's gate and words`.

### Task 7: The app side: the definition owns the session and the monitors

**Files:**
- Create: `MSFSBlindAssist/Services/BBS757/Bbs757DisplayEnvironment.cs`, `MSFSBlindAssist/Services/BBS757/Bbs757DisplayRegistry.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.Displays.cs`
- Modify: `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (`MonitorRows`: the registry's rows last), `MSFSBlindAssist/Aircraft/BBS757Definition.Interaction.cs` (`OnSwitchedAway`: `StopDisplays()` before the tablet client goes; `OnSimContextReset`: `ResetDisplays()`; `HandleHotkeyAction`: the registry's handlers first, after `Sim = simConnect`), `MSFSBlindAssist/MainForm.BBS757.cs`, `MSFSBlindAssist/MainForm.cs` (one call beside `StartPmdg737Displays()`), `MSFSBlindAssist/MainForm.AircraftSwitch.cs` (one call beside each of `StartPmdg737Displays()` and `RestartPmdg737DisplaysIfSimulatorChanged()`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757DisplaysWiringTests.cs`, `Bbs757DisplayRegistryTests.cs`

**Interfaces:**
- Produces (plans 24-28 implement the seams):
  - `internal sealed class Bbs757DisplayRegistry` with `void Add(IBbs757DisplayMonitor monitor)`, `void OnKey(HotkeyAction action, Func<Bbs757KeyContext, bool> handler)` (one handler per action; a second throws), `void AddMonitorRow(MonitorRow row)`, `IReadOnlyList<IBbs757DisplayMonitor> Monitors`, `bool TryHandle(HotkeyAction action, Bbs757KeyContext context)`, `IReadOnlyList<MonitorRow> Rows`.
  - `internal readonly record struct Bbs757KeyContext(SimConnectManager Sim, ScreenReaderAnnouncer Announcer, Form Parent, HotkeyManager Hotkeys, Func<bool> AiRead)`; `AiRead` runs the base's AI read of the same action.
  - In `BBS757Definition.Displays.cs`: `internal ModuleSession? DisplaySession`, `internal Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> Fma`, `internal void StartDisplays(IModuleHost host, ILayoutSource layouts, SimConnectManager sim, ScreenReaderAnnouncer announcer)`, `private void ResetDisplays()`, `private void StopDisplays()`, and the seams `partial void RegisterUpperEicas(Bbs757DisplayRegistry r);` (plan 24), `partial void RegisterLowerEicas(Bbs757DisplayRegistry r);` (plan 25), `partial void RegisterEhsi(Bbs757DisplayRegistry r);` (plan 26, then 28), `partial void RegisterAdi(Bbs757DisplayRegistry r);` (plan 27), called once, in that order, after the FMA registers itself. The registry is filled on its first use (a lazy `Displays` property), so `MonitorRows` and the keys see every reader before `StartDisplays` runs; a monitor not started yet answers `KeyFallsBackToAi()` true.
  - In `MainForm.BBS757.cs`: `private void StartBbs757Displays()` (a no-op unless the 757 is current; builds `ModuleHost` and `LayoutStore` as `StartPmdg737Displays` does), `private void RestartBbs757DisplaysIfSimulatorChanged()`; `ReadBbs757EfbModelAsync` reads through `DisplaySession` ("aircraft memory not found" while null); `EnsureBbs757ModuleSession` and the session's dispose in `DisposeBBS757Forms` are removed.
  - `Bbs757DisplayEnvironment.ReadSimulator`: null while `SimConnectManager.IsConnected` is false ([WASM-7]'s order) or the title test of Task 1 Step 6 fails, or a key has no cached value; else the cached values.
- Consumes: Tasks 2, 3, 6.

- [ ] **Step 1:** Check each `GateKeys` entry's registration: a key not registered `Continuous` gets a hidden `Continuous` copy with `ExcludeFromMonitorManager = true` (never a second `Continuous` batched key of the same name: [VAR-7], `VarNameCollisionTests`); record the count (at most 4) in the execution record.
- [ ] **Step 2: Write the failing tests:** `Switching_away_stops_every_monitor_and_disposes_the_session`, `A_flight_reload_invalidates_the_session_and_restarts_every_monitor`, `Monitor_rows_end_with_the_flight_mode_annunciator`, `A_registered_key_runs_before_the_base_and_can_fall_back_to_ai`, `A_second_handler_for_one_key_throws`, `The_tablet_reads_through_the_definitions_session`, `The_environment_reads_null_while_disconnected_first`, `The_environment_speaks_without_interrupting` (S5: `Announce`, never `AnnounceImmediate`), `Monitor_rows_list_every_reader_before_the_readers_start`.
- [ ] **Step 3:** FAIL. **Step 4:** Implement. **Step 5:** PASS, and the full suite. **Step 6:** Commit `feat(b757): the 757 definition owns its display readers' session`.

### Task 8: The Ctrl+P window's FMA list

**Files:**
- Modify: `MSFSBlindAssist/Forms/BBS757/BBS757AutopilotWindow.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.Autopilot.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757AutopilotWindowFmaTests.cs`

**Interfaces:**
- Produces: `BBS757AutopilotWindow(IReadOnlyList<Bbs757AutopilotButton> items, Action? requestStates, Func<IReadOnlyList<string>> fmaRows)`; a `DisplayListBox` named and `AccessibleName`d "Flight mode annunciator", `TabIndex` 0, the buttons after it; its lines set with `SetLines` only when they differ, on the window's existing refresh.
- Consumes: `Fma.View()` (Task 2).

- [ ] **Step 1: Write the failing tests:** `The_fma_list_is_first_in_the_tab_order`, `The_list_shows_the_monitors_rows`, `Unchanged_rows_are_not_set_again`, `While_not_running_the_list_shows_one_status_row`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit `feat(b757): the autopilot window lists the FMA first`.

### Task 9: The shared display window

**Files:**
- Create: `MSFSBlindAssist/Forms/BBS757/BBS757DisplayForm.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757DisplayFormTests.cs`

**Interfaces:**
- Produces (plans 24-27 open their windows with it): `internal sealed class BBS757DisplayForm : Form` with `public BBS757DisplayForm(string title, string listName, Func<IReadOnlyList<string>> rows, Control? controls = null)`, `public void ShowForm()` (shows, focuses the list, refreshes at once), a 1 s refresh of the list in place while shown (`SetLines` only on change), Escape hides it and returns to the previous window (as `PMDG737EngineDisplaysForm`); the controls area below the list in the tab order.

- [ ] **Step 1: Write the failing tests:** `The_list_is_named_and_first`, `Rows_refresh_in_place`, `Escape_hides_the_window`, `The_controls_follow_the_list_in_the_tab_order`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit `feat(b757): one display window for the 757's readers`.

### Task 10: Rules

**Files:** `.claude/rules/b757.md`, `docs/invariants/b757.md`

- [ ] **Step 1:** Add, each one line of at most 400 characters with its full text under `## B757-n`: **B757-30** the gate (S3, S4: only readings passing the reader's `Passes` with `Lit` not false for 10 s are spoken, shown or a baseline; corrupt never spoken; `Bbs757DisplayMonitor`); **B757-31** never in the way (S5: no 757 reader suppresses the MCP-light, plan 13 or autoland lines; all a reader says by itself is the non-interrupting announce); **B757-32** one session, the definition's (S2: `BBS757Definition.DisplaySession`; a reader never opens the simulator or reads the file itself; the tablet reads through it).
- [ ] **Step 2:** The generic memory rules the 757 readers follow ([WASM-1], [WASM-2], [WASM-4]) do not load on `Services/BBS757/**` (`wasm-memory.md`'s globs): mirror them word for word in a "Mirrored from wasm-memory.md" block of `b757.md`, and confirm no file loads both (`ClaudeContextBudgetTests`).
- [ ] **Step 3:** `ClaudeContextBudgetTests` and `ClaudeRulesHookTests` green; commit `docs(b757): the display readers' rules`.

### Task 11 [controller, simulator up]: Live checks, deploy, publish

- [ ] **Step 1:** With the generator-4 layout in `%APPDATA%` and the app built from this branch, load the 757: `bbs757-module.log` shows the file used ("saved", generator version 4) and both readers; `bbs757-fma.log` reaches Running; the tablet window still reads (the EFB through the definition's session).
- [ ] **Step 2:** Read-only: the Ctrl+P list against the probe's `state.py` on the ground (READY FOR TAXI: "A/T: none", "Roll: TO", "Pitch: TO", "AFDS: FD").
- [ ] **Step 3:** Deploy to `C:\MSFSBA-B757` (never while it runs) with the layout file; record the build and layout name.
- [ ] **Step 4:** Ask the owner: push `B757` (after the rebase and full suite), push the private branch, publish the generator-4 757 layout (`toolkit/scripts/publish-layouts.ps1 -Source` that file only).

## In-sim checks (owner, by ear)

1. One flight: takeoff (TO / TO, then THR HLD at 80 kt), climb (VNAV SPD, LNAV), level-off (ALT CAP, VNAV PTH), and an autoland (LOC and G/S armed, then captured, FLARE, ROLLOUT): each heard once, after "FMA: ", never cutting off an MCP-light line.
2. Ctrl+P during the flight: the "Flight mode annunciator" list first, matching what was heard.
3. Ctrl+M "Flight Mode Annunciator" off: no FMA line; on again: the next change heard.
4. A flight reload: nothing said for the new flight's first modes.

## Handoff to plan 16

- **Pilot-facing:** "The 757's flight mode annunciations are spoken as they change and listed at the top of the autopilot window (Ctrl+P)." Changelog `feature`; the guide's 757 autopilot window part and its Ctrl+M rows.
- **docs/b757.md:** the display readers (the shared monitor, the session, the registry and seams); `docs/wasm-memory.md`: the 757 module and its readers beside the 737's.
- **CLAUDE.md:** none.

## Execution record

- **Status:** landed (2026-11-08) at the owner's word ("Yes, push and publish"), with the generator-4 757 layout published and the older 757 files removed from the `layouts` branch; Task 11 Step 1 passed; the FMA heard right through a whole flight to an autoland, and the Ctrl+P list and the Ctrl+M mute passed; waiting on the owner for the reload check by ear, Task 11 Step 2 and the cold-and-dark gate read
- **Claimed by:** Robin Kipp (robin24), 2026-11-08
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-11-08, from `B757` e95b4c9e3 (plan 22 landed; plans 23-28 written; claim c9989d1c9)
- **Landed:** 2026-11-08, rebased without a conflict onto `B757` 7cc82a4d3 (plan 29, the First Officer's parity, pushed meanwhile; no shared file), then a fast-forward of `B757`; the last code commit `fix(b757): the FMA's VNAV comment says what BlueBird's tables key` (29d5a4e4f, 124d578a1 before the rebase); `git log --grep "^Plan: 23"` lists the plan's commits. Private: `feature/b757-displays` pushed at 0c8b8f72 (merge 278d0c11, recipe c753d9af, hardening 0c8b8f72); the generator-4 757 layout `bbs757/39462c4abb2f46d2af51c4c2290c4e33a72e25c25bdf378de5d5c81a34bb10b4-561f3062da22ab51.json` (SHA-256 f0cc0565…, the file in `%APPDATA%`) published to the `layouts` branch beside the older files
- **Tests at landing:** 21,817 passed, 0 failed (app, on the rebased code with plan 29); 506 passed, 0 failed (toolkit, private 0c8b8f72); before the rebase, at 124d578a1: 21,770; the solution builds in Debug and Release with 0 errors
- **Tasks:**
  - 0 done: the claim (c9989d1c9), pushed to `kn4iee/B757` before any work.
  - 1 done by the controller (0904c3d81, `docs/b757/evidence/displays-fma.md`): no simulator write was needed; the F/D bytes, the AFDS column, the armed columns and the MCP route came from BlueBird's draw, its cockpit model and the 2026-11-06 probe, checked live read-only in cruise (MSFSBA left running; the L:vars read through the `VCockpit15` view).
  - 2 done (a6f2cae78, fix round 1 054864bec: a dark candidate never holds the search), 3 done (225285c5c), 4 done (00608ad86), 5 done (private 278d0c11, c753d9af), 6 done (f84a0f516, fix round 1 ed5895f6a: an armed mode turning active as the 737 says it), 7 done (57581d610, fix round 1 07ba82ba5: the menus and the gate keys), 8 done (23939d009), 9 done (9cc5e9d58), 10 done (ba25862b9: B757-30, B757-31, B757-32 and the WASM-1/2/4 mirror in `b757.md`). Each was reviewed (spec and quality) and its fixes re-reviewed.
  - The whole-branch review (opus): no Critical, four Important for the files this plan freezes; its fix wave 432de3a62..124d578a1 (gate variables may be stock simulator variables, a sampler may take the simulator's values, the registry owns later readers' windows, a dark display reads "<Name> display not powered", every wait ends in a silent baseline, the Review Focus layout test, the VNAV comment) and private 0c8b8f72 (the status step fails on any other status comparison; the regenerated layout byte-identical), re-reviewed: all addressed.
  - Task 7 Step 1: **2 SimConnect variables added**, hidden batched copies of `L:BBS_FD1_SWITCH` and `L:BBS_FD2_SWITCH` (`ExcludeFromMonitorManager`); the MCP altitude and heading ride the existing `BBS757_MCP_ALTITUDE_WATCH` and `BBS757_MCP_HEADING_WATCH` rows.
  - 11: the layout generated and installed (above); Step 3 done: the Release build 124d578a1 deployed to `C:\MSFSBA-B757` at 2026-11-08 21:32 (after the owner had closed the copy running plan 22's dbb81c30c and the simulator), robocopy rc 3, ProductVersion `0.0.0+124d578a1…` and the DLL's hash confirmed, with the generator-4 layout above in `%APPDATA%`; after the rebase, the landed head (with plan 29) redeployed there in Release the same way (068a60541, 21:41). Step 1 passed on the owner's flight with that build (read from the logs, 2026-11-08): at 21:43:29 `bbs757-module.log` used the generator-4 file ("saved; generator version 4", the three older local files skipped as not loading) and described the FMA's and the EFB's layouts; `bbs757-fma.log` reached Running at 21:43:30, fell to WaitingForSimulator at 21:43:44 ("the simulator's values are not available") and was Running again at 21:43:45, with no line after it through 22:06; the tablet's reads located the build every time, with no failure line. They search memory again about every 5.5 s (`ModuleSession.ReuseMs`, 5 s), at the same rate on 2026-11-06 and 11-07: older than this plan.
- **Waiting on the owner:** Task 11 Step 2 (the Ctrl+P list against `state.py` on the ground, read-only); a cold-and-dark load, for a read-only check that the gate's MCP L:vars and F/D bytes agree with memory while unpowered (not measurable in cruise); check 4 below (a flight reload) by ear. A tester's copy of this build reads no 757 display, the tablet included, until it has the published generator-4 file.
- **In-sim results:** 2026-11-08, the owner's flight on `C:\MSFSBA-B757` at 068a60541: check 1's takeoff, climb and cruise passed ("spot on"); check 2 (Ctrl+P in flight: "perfectly as expected") and check 3 (the Ctrl+M mute) passed; check 1's approach and autoland passed ("Landed, everything sounded as expected"), at KPVD runway 23, the rollout engaging at 2026-11-09 00:08:19 (`debug.log`, `LandingFlareAssist`), so check 1 passed whole. `bbs757-fma.log` has no line from 21:43:45 (Running) to 00:31:10 (stopped when the owner closed the app): no value without a word and no stop through the whole flight. Check 4 not yet run.
- **Changed from the plan:**
  - **The AFDS column:** BlueBird draws "CMD" for status 1, otherwise "FD" when this side's F/D is on, else nothing; no other status value has a word. The layout gained `FdWord` beside `Status`/`StatusWords`; a status value not in the table falls to the F/D test, never "unrecognised".
  - **The strip's gate:** BlueBird draws pitch, pitch armed, roll and roll armed only while the Captain's F/D is on or CMD is engaged (A/T always); the reading blanks them the same way (S10).
  - **The F/D bytes** come from one getter with two loads (side 0 first, the Captain's), not two getters; `L:BBS_FD1_SWITCH` is the Captain's switch.
  - **The MCP route** is the pointer at afds+11848 (`AfdsToMcp`), the fields f64; the recipe tells altitude from heading by the variable each is written to.
  - **Armed columns:** pitch armed 11944 and roll armed 11940, as the layout had them (the probe's table was swapped); no LNAV arming was needed.
  - **The menus:** `SimConnectManager.FlightRunning` (the "Sim" event the app already tracked) makes the readers wait while no flight runs; the title test alone never ends.
  - **For plans 24-28 (the frozen files):** gate variables are typed (`Bbs757GateVariable`: name, type, units; `GateKeys` stays the L:var default), taken from each monitor by the registry; `IBbs757DisplaySampler.TryRead(Bbs757SimValues, out …)`; `Bbs757DisplayRegistry.OwnWindow(form)`, closed for good on a switch away; a dark display's window row "<Name> display not powered".
  - **No status line is UK-respelled:** "BlueBird file not recognised" and "unrecognised" stay as the 737 and plans 20-22 say them.
- **Open at landing (later work, none blocking):** plans 24-27 must call `r.OwnWindow(form)` for their `BBS757DisplayForm`; VNAV engaging is said "FMA: Pitch: VNAV PTH, Pitch armed: none" (BlueBird's armed VNAV and active VNAV PTH/SPD have different values; the 737's value test kept); a final "unavailable" (no access, internal error, file not recognised) gets no "resumed" after a reload (as the 737); the window says "unavailable: unexpected values" where the pilot heard "stopped: unexpected values" (as the 737); Ctrl+P now opens on the FMA list (the plan 16 guide note); the recipe would miss a second status switch reached through arithmetic; the side effects of the measuring: none. **For the release (as plan 29 noted for the v2 file):** under this plan's format the store's generator-3 757 file (`…-69911a5bb8c57b24.json`) no longer loaded either, and `LayoutBundler` is all-or-nothing, so `LayoutBundle` failed on the `layouts` branch at 7a6d8a21 on both older 757 files. At the owner's word both left the branch in `layouts` af2d7f71 (2026-11-08); `LayoutBundle` from this branch then passed on it (one 757 file, two PMDG 737 files), and the three older 757 files in `%APPDATA%` were deleted too.
- **Rulings made while working:** in the SDD ledger (git-ignored, `.superpowers/sdd/23-displays-fma/progress.md`) and the commit messages.
