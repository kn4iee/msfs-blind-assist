# iniBuilds A350 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/inibuilds-a350.md`, which Claude Code loads when it reads matching code. Background: [inibuilds-a350.md](../inibuilds-a350.md).

## A350-1

The A350's WASM displays are read from a read-only copy of its linear memory (`OpenProcess` with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` only), run through the A350's own `Draw()` in a WASM runtime. They are operated only through the sim's own inputs: L:var writes, input events, and `Coherent.call("WASM_MOUSE_MOVE" | "WASM_MOUSE_DOWN" | "WASM_MOUSE_UP" | "WASM_CLICK", guid, x, y, button)` evaluated in the gauge's Coherent view. The 1.2.6 investigation moved the MFD pointer by writing the aircraft's `RequestMouseChange` slots into the sim's memory; on 1.2.7 the Coherent mouse route moves it exactly (measured 26 October 2026), so no write into another process's memory is needed, and a wrong address there could crash the sim.

## A350-2

Measured 26 October 2026: walking all of the sim's ~25,000 memory regions with `VirtualQueryEx` while reading the A350's (0.5 s) produced torn copies: about half the Warning Display draws came back empty or garbled, and one trapped inside `dlmalloc`. Listing only the A350 allocation's regions first (walk from its base while `AllocationBase` matches, about 10 regions) and then reading them into one preallocated buffer took 8 ms for 37.7 MB and gave 8 of 8 consistent draws. A trap during a draw still means a torn copy: take another, never report it.

## A350-3

The ECP keys (`INI_ECAM_CHECKLIST_LEFT`, `PUSH_ECAM_STS`, `INI_ECAM_ABN`, the `INI_ECAM_WHEEL_*_COMMAND` pair, `INI_MASTER_CAUTION_COMMAND`, …) are consumed and reset to 0 by the aircraft. One write of 1 is a press: a lone calculator write `1 (>L:X)`, as `A350MfdInput` types the KCCU keys, or the key's own input event Set, as the ECP rows press theirs (the rule first said a native SimConnect write; review 2026-11-04: the code never uses one, and both of these work). Two MobiFlight calculator writes (`1 (>L:X)` then `0 (>L:X)`) executed before the aircraft's next update, so the aircraft saw 0 and nothing happened (measured with STS and ABN). KCCU keys that the aircraft does not reset (`INI_MCDU1_ND`, `INI_MCDU1_MFD`) take 1, a hold of about 180 ms, then 0.

## A350-4

`ND::MouseEvent` returns unless `INI_CPT_CURSOR_SCREEN` (first officer: `INI_FO_CURSOR_SCREEN`) is 1; the MFD follows the mouse while it is 2. The 1.2.6 investigation concluded the Coherent mouse calls "do not reach the A350 gauges" because the cursor was on another screen. Measured 26 October 2026: with the cursor on the ND, `WASM_MOUSE_MOVE` set the captain EFIS gauge's mouse position, and a click on the aircraft symbol opened the ND pop-up.

## A350-5

In `en-US.locPak`, a control's position names are `INI.TOOLTIPS.<CONTROL>.<key>` and the key a value selects is written in that control's tooltip expression (`tt_value` in the behaviour file), not by a fixed rule. Measured 26 October 2026: `SIGNS_SEAT_BELTS` and `LIGHTS_INT_ANN_LT` count from `.1` (value 0 is `.1` ON / `.1` TEST), `MAINT_ELEC_BAT_VOLTAGE_SELECTOR` from `.0` (value 0 is `.0` EMER 1, 2 is `.2` OFF, 3 is `.3` BAT 1). Two-position push buttons use `.OFF` / `.ON`; the placard name is `.TITLE`. Read the expression per control; an assumed offset names every position of half the controls wrong.

## A350-6

The OIS (EFB) is MSFS SDK components with its own keyboard handling (`_onKeyDown` in `ini-efb-a350.js`). Measured 26 October 2026 on T.O PERF: setting WIND, OAT, QNH and TOW with the native value setter plus `input` and `change` events showed the values in the fields and kept them across a page change, but COMPUTE produced nothing. Typing the same values as `keydown`, `keypress`, value append with `input`, and `keyup` per character, then Enter and `change`, made COMPUTE return V1 142, VR 143, V2 148, FLEX 67, flaps 2, THS 26.4 %. The airport field reacted to the plain value set, so a field that seems to work that way is no proof the others do.

## A350-7

Measured 27 October 2026 (1.2.7, HUD fitted and deployed): `INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID_Set` writes only the key's `INI_FCU_HUD_DECLUTTER_LEFT_ANIM`, whatever its parameter; the model's update puts the ANIM back to 1 once `INI_FCU_HUD_DECLUTTER_LEFT_TIMER` runs out. Four writes of 1 and four of 0 left `INI_FCU_HUD_DECLUTTER_LEFT` at 0. The mouse rect's LeftSingle code is what declutters: ANIM 0, TIMER 2, and `INI_FCU_HUD_DECLUTTER_LEFT` flipped between 0 and 1. The generator maps such a key as a two-position selector on that variable (`CLICK_SWITCHES`). `A350WritePlan.ClickWrites` writes the same three L:vars, which switched both sides' declutter on and off through MSFSBA's own path. The map first called it a button pressed with 1, which did nothing. A key's `_Set` that moves only an animation is no proof of a working press: read the mouse code.

## A350-8

Measured 27 October 2026 (1.2.7): the two OIS views are `VCockpit10 - iniEfbA350` and `VCockpit11 - iniEfbA350`, and each loads its side's page, `ini-efb-a350-cpt.html` or `ini-efb-a350-fo.html` (the `url` of its `ini-efb-a350` element). `CoherentPmdgEfbClient.ForA350Ois` picks the view by evaluating that page name over each candidate socket (its side probe), as the PMDG tablets do with `getTabletSide()`. A view's number is the order the sim created the instruments in, not a side: picking by number would put the first officer's tablet behind the captain's key on any load that creates them the other way round.

## A350-9

Measured 27 October 2026 (Wasmtime 48.0.2, .NET 10, Windows 11): any trap inside the A350 draw code (a torn memory copy makes the EFIS and MFD draws read out of bounds on about 40% of fresh copies; a deliberate `emu_CTR_Draw(0)` traps the same way) killed the whole process with 0xC0000409 and no managed exception, in a WinForms exe and in a console exe alike, while the same trap in the xUnit test host came back as a `TrapException`. Not the cause: the host callbacks (all of them answering zero still crashed), the thread's stack (a 256 MB stack still crashed), the guard pages (explicit bounds checks still crashed), optimisation, unwind or debug info. The cause: .NET 9 and later mark an x64 app CET-compatible by default (hardware-enforced shadow stack), and Wasmtime leaves a trap with a non-local jump that the shadow stack refuses with a fast-fail. With `<CETCompat>false</CETCompat>` both traps came back as `TrapException`. MSFSBlindAssist.csproj sets it (`Every_exe_hosting_the_engine_is_not_cet_compatible_so_a_trap_cannot_kill_it`), and so does tools/A350Probe. Any other exe that hosts the A350 draw engine needs it too.

Since plan 03 (1.2.8, 2026-11-02) a trap is read as what it was (`A350TrapReport.Parse`): `PastTheMemory` (a pointer past the copy's linear memory: the copy was torn), `InsideTheMemory`, `Deadline` (the draw ran past its interrupt), `Other` (an unreachable, a stack overflow); a message Wasmtime words otherwise parses to null and the log keeps its line. The flight of 2026-10-31 trapped twice in about three hours (EFIS#0 `ND_Logic::drawModernConnections`, MFD#0 `FlightPlanButtonNew::draw`), both pointers past a 413 MB copy (0x19d40000), both in flight-plan drawing late in a long flight, each retried and drawn; the flight of 2026-11-02, on 42 to 44 MB copies for 55 minutes, had none.

## A350-10

Measured 28-29 October 2026 (1.2.7). With the A350's memory at 355 MB, a copy took about 650 ms and spanned many frames. On the two copies saved in flight, 7 of 8 draws (both EFIS, CTR, MFD) trapped at the same place: `dlmalloc` <- `malloc` <- `operator new` (live, under `PFD::DrawFMA`), a memory fault at an address made of text bytes. The copy's allocator state (wasi-libc's dlmalloc: bins, free lists, the top chunk) had been copied across frames and no longer agreed, so the draw code's first allocation followed a free-list pointer into text.

`A350ModulePatcher` therefore answers every allocator entry point that reaches the heap state (`malloc`, `dlmalloc`, `calloc`, `realloc`, `internal_memalign`, `posix_memalign`) with a question to the engine, returned at once, and turns `free` and `dlfree` into a bare return. `A350DrawEngine` hands out blocks from the head room above the copy (below the answered strings' page). Each block carries dlmalloc's chunk head (size | in use) for code that asks a block's size, and the head room is used afresh by every call of a Draw. A function is answered in each signature the patch knows (dlmalloc's own `internal_memalign(mstate, alignment, bytes)` as well as wasi-libc's two-argument one). One a version lacks is left unpatched, not refused, and so is one with a signature the patch does not know (an answer reading shifted arguments hands out wrong blocks; a bare return where a result is due does not even compile): it then runs and can reach the copy's heap, so `A350PatchedModule.Allocator` and `AllocatorLeftAlone` say which, and the patch logs both when it compiles. A `calloc` whose count times size passes 32 bits answers NULL, a `posix_memalign` whose pointer lies outside the memory answers ENOMEM (48), and a draw that runs the head room out says so (`A350DrawEngine.LastHeapExhausted`, logged when it starts and ends). On the same two saved copies all 8 draws then read. A third copy still trapped both EFIS in `list_next` under `ND_Logic::drawModernConnections`: the flight plan's own list was torn, and the retry on a fresh copy covers that.

A review on 29 October 2026 checked the 1.2.7 module's whole call graph: `dlmalloc` is reached only from the patched entry points, `dlfree` only from `free` and `realloc`, and `sbrk` (the only `memory.grow`) only through them. `free` sits in the function table, but its body is patched, so indirect calls are covered. The aligned `operator new` path goes through `__libcpp_aligned_alloc` to `posix_memalign`. Tests: `A350DrawEngineAllocatorTests`, on a module whose allocator traps when reached. Every allocation there must also be non-null and above the copy, or the draw traps. Without that guard the tests passed with the allocator answering 0, because address 0 of the restored copy reads back what the draw wrote. With the guard, both mutations fail: the allocator answering 0, and the heap starting inside the copy.

## A350-11

The compiled module is cached by the installed module's hash and `A350ModulePatcher.Version` (`<sha>-p<version>.cwasm`, `A350ModuleCache.CachePath`). Keyed by the hash alone, a module compiled from an older patch would load from the cache and the change would silently never apply. On 29 October 2026 that was the allocator patch [A350-10]: its whole point would have been lost on every machine that already had a cache. A build removes other modules' caches and, of this module's other patch versions, all but the latest written, so the app and a probe built from different patch versions keep each other's instead of compiling on every start (review 2026-10-29); it also deletes a write left over an hour. Tests: `The_cache_is_keyed_by_the_module_hash_and_the_patch_version`, and `The_patch_version_moves_whenever_the_patch_output_does`, which pins the patched module's SHA-256 beside `Version`: a patch change without a bump fails the suite, and the fix is to bump `Version` and the pinned hash together.

## A350-12

The OIS OPTIONS > 3RD PARTY page holds the pilot's SimBrief ID, SayIntentions API key, Hoppie logon code and Navigraph login, and the OIS hides them on screen while OPTIONS > STREAMER MODE is on. MSFSBA must hide them the same way, or a screen reader says them aloud and a log or a captured fixture keeps them. While STREAMER MODE's ON radio is checked, `A.isSecret` marks a field in that options page (`A.streamerScope`) whose label matches `A.SECRET_LABEL` (ID, KEY, LOGON, PASSWORD or TOKEN as a word), and, whatever STREAMER MODE says, any `input[type=password]` anywhere; such a field ships `secret: true` and its name as `A.hiddenName` composes it ("SIMBRIEF USER ID, hidden, 7 characters", "…, hidden, empty"). Its value still travels with it, masked: list mode's TextBox holds the field's value, and an empty one would make Enter wipe the credential. So nothing on that path (the agent's push, `CoherentPmdgEfbClient`, the shell, `FbwEfbForm`) may speak, log or capture a pushed field's value (final review 2026-10-29: the rule had said the value never shipped, which a later change could have trusted). `CoherentPmdgEfbClient` carries `secret` through to the shell (and into its change signature), and `FbwEfbForm` renders it as `type=password` with autocomplete off, a list-mode TextBox with `UseSystemPasswordChar`, and confirms a set with `FieldSetSpoken` ("Set, hidden, N characters"). Typing into a hidden field still works key by key ([A350-6]). With STREAMER MODE off the fields read as the OIS shows them: the pilot's own data on the pilot's own screen. Fixtures: `tools/a350-ois-test/secrets.test.js` fails when a fixture keeps a credential or a login code; the redacted 3RD PARTY fixture keeps only the structure. Tests: `streamer.test.js` (OIS harness), `FbwEfbFormFieldSetSpokenTests`, and the shell's secret tests in `tools/flypad-shell-test/efb-shell.test.js`.

The OIS's LOGIN WITH NAVIGRAPH pop-up shows a sign-in code the pilot types on Navigraph's site: the OIS window speaks it so the pilot can, and `A350SpeechLog.ForTheLog` replaces it in a350_speech.log ("User Code: (not logged)", the rest of its sentence kept), as it keeps a set field's name and drops its value ("Set HOPPIE LOGON to (value not logged)"); no fixture keeps it (plan 06).

## A350-13

The Altimeter window's typed QNH reaches up to three baros (the captain's, the first officer's, the ISIS), and the OIS's OPTIONS >
AIRCRAFT > LINKED INSTR decides which writes move which (plan 02, 1.2.8, every write measured at each setting, evidence 02): at NONE (0)
each baro moves alone; at CPT & FO (1) a side's click or pull copies to the other side; at CPT/FO & ISIS (2) the ISIS goes with them, and
on STD the ISIS's own click takes both sides off STD (the flight of 2026-10-31 hit exactly this). So `A350BaroEntry.Plan` reads every
input once and plans one entry from that read: at 1 and 2 the captain's side alone (its click or pull carries the rest), the ISIS a target
of its own only at 0 and 1, where its write and click leave the sides alone, and at 2 no ISIS click while the sides are on STD. Planning
each baro from its own read, or clicking the ISIS on STD at 2, moves the sides behind the pilot's back. Tests: `A350BaroEntryTests`,
`A350BaroEntryRunTests`.

## A350-14

The ISIS has an STD of its own, toggled only by its STD/CHANGE key and shown only on its display (the SFD's `STD` line against its
`Baro:` line): no variable says it. While it is on, `KOHLSMAN SETTING MB:3` holds 1013.25 and nothing written moves it, not a direct
write, not a click, not MSFSBA's row, at every timing tried from 100 ms to 3 s; the setting underneath survives (the key brought back
1020). At LINKED INSTR 2 a side's push puts the ISIS on STD with them and it stays there when the option goes back to 0 or 1, where only
its own key gets it back (plan 02, evidence 02). So `A350BaroEntry` plans no ISIS target while the SFD shows it on STD and says so ("ISIS
not set: it is on STD."), never a write that would read back unchanged and be reported as a failure.

## A350-15

The reader takes values straight from the copy's memory by addresses re-derived per version (the variable table's slots, the pause, the speeds); after an update a layout that looks right can read shifted values that are then spoken with confidence (the probe's sweeps once named every value 24 entries off while the table's own check passed). So at every start `A350FactsCheck` holds two parts against the A350's own L:vars as MSFSBA streams them: the table's slots (141 pairs agreed on 1.2.8 at the gate) and the variables the draws asked for. A part that disagrees withholds what it disproves for the session (`A350DisplayService.CheckFacts`: the table's pause reading) and the pilot hears it once (`A350DisplayStatus.FactsMismatch`); the variables' part keeps the displays reading. Nothing of it is kept between sessions, so a new version is checked the first time it flies (plan 03, Task 5, evidence 03).

## A350-16

The owner (2026-11-12): the MFD, "the FMS and everything else, but just the MFD", at full parity with the A380's MFD window in how
things are announced, "the colours would have to be removed completely from the MFD". The A380's window says a line as
"<text>, <role>[, state][, dimmed]" (`FBWA380MCDUForm.RoleWord`) and never a colour; its footer message alone. So the A350's MFD
window, its FMS message call-outs and the status display's MFD entries go through `A350MfdWording`: `Speak` turns the reader's
colours into the owner's words where the colour asks something of the pilot (amber on a field: "required", the owner's word for an
entry the FMS needs, said once, as an empty amber box already reads "required"; yellow, the temporary flight plan: "temporary",
never beside the word TMPY; magenta, a constraint: "constraint", also in an F-PLN row's note, "ALT constraint") and drops every
other colour; `A350MfdPage.Shown` adds the A380 window's role words on top; `Message` says an FMS message as the A380 says its
footer ("CHECK DATABASE CYCLE").

The reader writes its colours in three forms, all in lowercase where the page's own texts are capitals (a "Green dot" label is the
reader's, a GREEN waypoint the page's): a segment after a comma ("CI: required, amber"), a value's colour in brackets
("LOCAL ETA AT DEST (white)"), and an F-PLN row's note naming a cell ("FPA white", "ident white", "ALT magenta"). The inventory
of 222 captured pages (plan 25) found amber on fields, keys, texts and messages, yellow on the TMPY title, keys, fields, check
boxes, texts and rows, magenta on notes and one text; `A350MfdWordingTests` sweeps every captured MFD page, both sides, for a
colour word.

`A350MfdPage.Read` keeps its colours: they are the reader's own facts (a grey key is `A350MfdWording.IsDimmed` and not offered,
`A350MfdPages.Offered`; a message read ", black" is caught without its box, `A350MfdPage.NoMessageRead`; the FMS message
observers compare raw reads). Strip them there and those checks go blind. The other displays (PFD, ND, E/WD, SD, RMP, mailbox)
keep their colours: the owner scoped this to the MFD.

## A350-17

The owner (2026-11-13) asked for the Fenix's and the PMDG 737's display windows on the A350: each display key opens a live window of
its display, and under the list the cockpit controls that change it ("the ND in the Fenix is an actual window with all the buttons").
Those controls are the panels' rows already flown live, so a window press must be exactly the panel's press: `A350WindowRows` (the
display windows' `IA350WindowRows`) presses a row through `IniBuildsA350Definition.PressRow` (its write plan, its refusals, its
read-back), a two-position row through `PressToggleFromWindow` (from the commanded state, so two presses flip twice), a typed row
through the row's plan as MainForm's typed field does (NaN for an entry that does not parse), and labels each button from
`TryDescribeControlState`, the bare label while a state is unread, never a guess. A window that wrote a variable of its own would act
unlike its panel and escape the write plans ([A350-1], [A350-5]).

Silence ([CORE-7]): the screen reader says a button's new text, so the press must not be said again. The row is armed as the window's
own echo (`ArmWindowEcho`), and the row's lights follow as the pilot's own press through the write path (`NoteOwnLamps`), which
`A350LampAnnouncementTests.A_window_press_lights_its_lamp_unheard` pins. The window speaks nothing on open, on its one-second
refresh, on F5 or on close; a refresh never moves the pilot off the row they are on (`A350DisplayRows.RowToFollow`).

