# BlueBird 757 Radios Implementation Plan (plan 04, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fix first: output N reads the 757's own VOR tuning and courses instead of the simulator's NAV radios, which BlueBird does not use. Then the essentials: a COM radios panel with standby entry and transfer, every COM change announced as on the other Boeings ("COM1 active 124.850"), the 757 nav radios window on input Ctrl+N, and the ILS frequency once a flight shows how.

**Architecture:** COM is the simulator's own: BlueBird's VHF L, C and R store their frequencies in `COM ACTIVE/STANDBY FREQUENCY:1..3` (measured), so the panel writes the stock standby events and watches the six variables. NAV is BlueBird's own: VOR frequencies are read and tuned on the CDU's NAV RAD page through plan 01's `Bbs757CduPages` (the first officer's CDU), courses through BlueBird's course variables. A 757 Ctrl+N window replaces the shared one (which writes stock NAV events BlueBird ignores).

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#1, #3), 3.1, 3.2, 4 (R52-R55, R53 per the owner), 8.

## Global Constraints

- Owned files: `Aircraft/BBS757Definition.Radios.cs` (new), `Aircraft/BBS757/Bbs757Radios.cs` (new), `Aircraft/BBS757/Bbs757NavRad.cs` (new), `Forms/BBS757/BBS757NavRadiosForm.cs` (new), tests `Bbs757RadiosTests.cs`, `Bbs757NavRadTests.cs`, `docs/b757/evidence/flight-04.md`.
- Radios: COM 1 = VHF L, COM 2 = VHF C, COM 3 = VHF R (measured). Stock events: `COM_STBY_RADIO_SET_HZ`, `COM2_STBY_RADIO_SET_HZ`, `COM3_STBY_RADIO_SET_HZ`; `COM_STBY_RADIO_SWAP`, `COM2_RADIO_SWAP`, `COM3_RADIO_SWAP`. Tuning-panel transfer buttons `B757_PED_COMHEAD_1/2/3_SWAP_BUTTON` (held 1 then 0); panel 1's selected radio is `BBS_RTP_L_LIGHTS` (measured: bit 2 VHF L); panels 2 and 3 are believed to be `BBS_RTP_R_LIGHTS` and `BBS_RTP_C_LIGHTS` (the knob request vars say so; Task 4 step 1 measures it). Bits 2 VHF L, 4 VHF C, 8 VHF R.
- COM range 118.000-136.990 MHz; valid channels have a kHz part whose value mod 25 is 0, 5, 10 or 15.
- Words (owner, Q1): "COM1 active 124.850", "COM2 standby 121.500"; three decimals, invariant; Ctrl+M rows "COM1 Active", "COM1 Standby", … as on the PMDG 737.
- Status rows show frequencies with three decimals through plan 01's `FormatRadioDisplay` seam (units "MHz" alone would round to "125").
- VOR: 108.00-117.95 in 0.05 steps; the NAV RAD page (measured on the captain's CDU): line 1 `VOR L … VOR R`, line 2 the tunings (`116.90AGSU`, `113.90R`, `------`), line 3 `CRS RADIAL CRS`, line 4 the courses. Entry into 1L / 1R tunes; `KEYDEL` + 1L / 1R returns to auto. Courses: `L:BBS_VOR_L_COURSE`, `L:BBS_VOR_R_COURSE`, `L:BBS_ILS_COURSE`, integer degrees, read and write.
- Never read stock NAV variables on the 757 (proposed rule B757-5). Background CDU work on CDU 2 only (R52); the FO CDU is left on NAV RADIO.
- Variables added: 15.

## Review Focus

- "121,5" typed under de-DE is 121.500 and the event's Hz is invariant; "121.57" is 121.575; "121.520" is refused (Task 2 test).
- A message on the FO CDU after a VOR entry ("VOR 113.90 INVALID", "INVALID ENTRY"): the app speaks it and wipes it, never claims the frequency took (Task 5 test).
- A stock swap BlueBird ignores falls back to the panel button whose panel selects that radio; no such panel → refused aloud (Task 4 test).
- The app's own standby set is said once (the echo window, Task 3 test).
- N with the FO CDU busy or unreachable still reads the courses and says the frequencies are unavailable (Task 1 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/04-radios.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 04`, with the `Plan: 04` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: N reads the 757's own nav radios

**Files:** Create `MSFSBlindAssist/Aircraft/BBS757/Bbs757NavRad.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.Radios.cs` (implements plan 01's `AddRadioVariables`, `HandleRadioHotkey` for `ReadNavRadioInfo`); test `Bbs757NavRadTests.cs`.

**Interfaces:**
  ```csharp
  public sealed record Bbs757VorTuning(double? Mhz, char? Mode, string? Ident);   // 'A' auto, 'R' remote, other manual
  public sealed record Bbs757NavRadPage(Bbs757VorTuning Left, Bbs757VorTuning Right);
  public static class Bbs757NavRad
  {
      public const string PageKey = "NAVRAD";
      public static bool IsNavRadio(Bbs757CduScreen s);          // Bbs757CduTitle.PageName(line 0) == "NAV RADIO"
      public static Bbs757NavRadPage? Parse(Bbs757CduScreen s);
      public static string TuningPhrase(string side, Bbs757VorTuning t);   // "VOR L 116.90 auto, GSU" / "VOR R not tuned"
      public static string Readout(Bbs757NavRadPage? page, int? vorL, int? vorR, int? ils);
  }
  ```
  `Readout`: "VOR L 116.90 auto, GSU, course 095. VOR R not tuned, course 000. ILS course 095."; without the page: "VOR L course 095, VOR R course 000, ILS course 095. Frequencies unavailable." (three-digit courses).
- Variables: `BBS757_VOR_L_COURSE` (`BBS_VOR_L_COURSE`), `BBS757_VOR_R_COURSE`, `BBS757_ILS_COURSE`: LVar, OnRequest, `ExcludeFromMonitorManager`.
- `HandleRadioHotkey`, `ReadNavRadioInfo` (output N): read the courses fresh; `CduPages.ShowPageAsync(2, PageKey, IsNavRadio, ct)`; speak `Readout`; `handled = true`. (Until now N fell to MainForm's shared readout of the stock NAV radios.)

- [ ] **Step 1: Write the failing tests** from session 8's lines: `Parses_auto_and_remote` (`116.90AGSU        ------` → Left 116.90 'A' "GSU", Right null; `113.90R           ------` → 113.90 'R'); `Not_the_page_is_null`; `Readout_sentences` (both forms); `N_is_handled_by_the_757` (the action is handled even when the CDU service returns null).
- [ ] **Step 2: Run** with `--filter Bbs757NavRadTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the full suite.** Expected: PASS.
- [ ] **Step 5: Commit** `fix(b757): output N reads the 757's own nav radios`.

### Task 2: COM logic, pure

**Files:** Create `MSFSBlindAssist/Aircraft/BBS757/Bbs757Radios.cs`; test `Bbs757RadiosTests.cs`.

**Interfaces:**
  ```csharp
  public static class Bbs757Radios
  {
      public static string ActiveKey(int com);     // "BBS757_COM1_ACTIVE"
      public static string StandbyKey(int com);    // "BBS757_COM1_STANDBY"
      public static string SetKey(int com);        // "BBS757_COM1_SET" (typed field)
      public static string TransferKey(int com);   // "BBS757_COM1_TRANSFER" (button)
      public static bool TryParseCom(string? text, out double mhz, out string error);
      public static string StandbySetCode(int com, double mhz);   // "121500000 (>K:COM_STBY_RADIO_SET_HZ)" / COM2_… / COM3_…
      public static string SwapCode(int com);                     // "(>K:COM_STBY_RADIO_SWAP)" / "(>K:COM2_RADIO_SWAP)" / "(>K:COM3_RADIO_SWAP)"
      public static int? TransferPanelFor(int com, double? rtpPanel1, double? rtpPanel2, double? rtpPanel3);
      public static string Frequency(double mhz);                 // "124.850"
      public static string ActivePhrase(int com, double mhz);     // "COM1 active 124.850"
      public static string StandbyPhrase(int com, double mhz);    // "COM1 standby 121.500"
  }
  ```
  `TryParseCom`: two decimals ending 2 or 7 get a trailing 5; round to 1 kHz; range and channel rule. Errors (exact): empty → "Type a frequency first."; otherwise → "Frequency must be 118.000 to 136.990, on a 25 or 8.33 kilohertz channel." `TransferPanelFor`: the radio's bit (COM1 2, COM2 4, COM3 8) in panel 1, 2, 3 order; null when none or unread.

- [ ] **Steps 1-5** (tests `Parses_channels`, `Refuses_off_channel_and_range`, `De_DE`, `Transfer_panel`, `Phrases`); commit `feat(b757): COM entry and phrases, pure`.

### Task 3: The COM radios panel and COM announcements

**Files:** `BBS757Definition.Radios.cs` (`AddRadioVariables`, `AddRadioPanels`, `HandleRadioTypedText`, `ProcessRadioUpdate`, `FormatRadioDisplay`); test `Bbs757RadiosTests.cs`.

**Interfaces:**
- For COM 1-3: `ActiveKey`, `StandbyKey` (stock `COM ACTIVE FREQUENCY:n` / `COM STANDBY FREQUENCY:n`, MHz, Continuous, `IsAnnounced`, read-only status rows "COM1 Active", "COM1 Standby"); `SetKey` (the squawk field's pattern, label "COM1 Standby Frequency"); `TransferKey` (button "COM1 Transfer"). The three `BBS_RTP_*_LIGHTS`: OnRequest, `ExcludeFromMonitorManager`.
- Panel "COM radios" in the Pedestal right after "TCAS and transponder": COM1 field, COM1 transfer, COM2 field, COM2 transfer, COM3 field, COM3 transfer; its status display the six rows, shown by `FormatRadioDisplay` as `Frequency(value)`.
- `ProcessRadioUpdate`: the six rows through `_changes.Observe(key, phrase, NowMs())`; `handled = true` for them.
- `HandleRadioTypedText` for a `SetKey`: parse (refusal → the error); `CanWrite` false → "COM1 unavailable"; arm the standby row's echo (`_changes.ArmEcho`); write; after `ReadBackDelayMs` read the row fresh; speak `StandbyPhrase` of what came back; rebaseline the row.

- [ ] **Steps 1-5** (tests `Com_panel_follows_tcas`, `Com_change_is_announced_once_after_a_silent_first_reading`, `Own_standby_set_is_not_said_twice`, `Six_ctrl_m_rows`, `Frequencies_display_with_three_decimals_under_de_DE`); commit `feat(b757): COM radios panel and COM announcements`.

### Task 4: Transfer

**Files:** `BBS757Definition.Radios.cs` (`HandleRadioSet`); test `Bbs757RadiosTests.cs`; `docs/b757/evidence/flight-04.md`.

- [ ] **Step 1 (owner-in-sim, on the ground):** hold `B757_PED_COMHEAD_2_SWAP_BUTTON` and `_3_` (1, 200 ms, 0) and record which COM swapped, with the three `BBS_RTP_*_LIGHTS`; try `K:COM_STBY_RADIO_SWAP`, `COM2_RADIO_SWAP`, `COM3_RADIO_SWAP` and record whether each swaps; swap everything back. Record in `flight-04.md` and correct the panel-to-lights mapping in Global Constraints if it differs.
- **Interfaces:** `HandleRadioSet` for a `TransferKey`: `CanWrite` false → "COM1 unavailable"; read active and standby fresh; arm both echoes; write `SwapCode`; after `ReadBackDelayMs` read both; swapped → "COM1 active 121.500, standby 124.850"; not swapped → `TransferPanelFor` (null → "No tuning panel selects COM1") → press that panel's button (1, 200 ms, 0), read back, speak the sentence or "COM1 did not transfer".
- [ ] **Steps 2-6:** tests `Stock_swap_that_works_is_used`, `Panel_fallback_when_stock_swap_is_ignored`, `No_panel_refuses`; run, implement, full suite; commit `feat(b757): COM transfer, stock swap first, the panel's button as fallback`.

### Task 5: The Ctrl+N nav radios window

**Files:** Create `MSFSBlindAssist/Forms/BBS757/BBS757NavRadiosForm.cs`; modify `Bbs757NavRad.cs`, `BBS757Definition.Radios.cs` (`HandleRadioHotkey` for `SetNavRadios`, `CloseRadioWindows`); test `Bbs757NavRadTests.cs`.

- [ ] **Step 1 (owner-in-sim, on the ground):** on the first officer's CDU, NAV RAD, type 113.90 into 1L: does VOR L tune (the captain's NAV RAD and the VOR/DME panel show 113.90)? Then `DELETE` + 1L. The same for 1R and VOR R. Record in `flight-04.md`. If the FO CDU tunes only its own side, the window tunes VOR L by the VOR/DME panel instead (`B757_MCP_VORDME_1_BUTTON` to MAN when `B757_MCP_VOR1_MODE_AUTO` reads 1, then `L:B757_VOR_L_FREQ_WHOLE_INC` / `FRAC_INC` steps from the frequency NAV RAD shows, one request per call, 150 ms apart; read back on NAV RAD), and auto by the same button; never the captain's CDU.
- **Interfaces:** in `Bbs757NavRad`: `TryParseVor(string? text, out double mhz, out string error)` (108.00-117.95, 0.05 steps), `VorEntry(double mhz)` ("113.90"), `TryParseCourse(string? text, out int degrees, out string error)` (0-360, 360 → 0), `CourseCode(string lvar, int degrees)` ("95 (>L:BBS_ILS_COURSE)").
- `SetNavRadios` (input Ctrl+N): `hotkeyManager.ExitInputHotkeyMode()`; show the one window (created on first use); `handled = true`.
- `BBS757NavRadiosForm` (modeless, owned by the definition, closed by `CloseRadioWindows`): a read-only status `TextBox` first (N's `Readout` on open), then "VOR L frequency", "VOR L course", "VOR R frequency", "VOR R course", "ILS course" boxes with labels, buttons "VOR L auto", "VOR R auto". Enter applies only the focused box (R55); Escape closes and returns focus as `ValueInputForm` does. Actions refuse aloud when `CanWrite` is false or the CDU service is null:
  - VOR frequency: per step 1's result; read back from NAV RAD → "VOR L 113.90"; a CDU message → speak it ("CDU: VOR 113.90 INVALID") and wipe; null → "The first officer's CDU did not answer."
  - VOR auto: per step 1's result; read back → `TuningPhrase`.
  - Course: write `CourseCode`, read back after `ReadBackDelayMs` → "VOR L course 095" / "ILS course 095".
  - Every result also goes into the status box.
- [ ] **Steps 2-6:** tests `Vor_and_course_parsing` (de-DE "113,90"), `Course_code_is_invariant`, `Message_after_entry_is_spoken_not_claimed` (a fake pages service returning an INVALID ENTRY screen); run, implement, full suite; commit `feat(b757): Ctrl+N nav radios window`.

### Task 6 [in flight]: ILS frequency

**Files:** `Bbs757NavRad.cs`, `BBS757Definition.Radios.cs`, `BBS757NavRadiosForm.cs`, tests; `flight-04.md`.

- [ ] **Step 1 (owner, in flight and on the ground):** with a view of the aft pedestal's ILS panel (a custom or drone camera, window capture), step `L:B757_ILS_FREQ_WHOLE_INC` -1 twenty times, one per call: does it stop at 108 or wrap, and is PARK a position? The same for `FRAC_INC`. On an ILS approach with the panel in PARK, does the localizer capture (FMC autotuning)? Record the APPROACH REF ILS line. Restore. Record in `flight-04.md`.
- Then build the branch it supports:
  - **A. PARK lets the FMC tune the arrival ILS:** an "ILS park" button (the steps that reach PARK) and N saying "ILS parked, FMC tunes 110.50 IHZK" from APPROACH REF (`ShowPageAsync(2, "INIT", …)`, then L6, L5; parse its ILS line, measured "ILS 09L 110.50 IHZK"). Preferred: nothing to read back.
  - **B. The knobs stop at the ends of 108.10-111.95:** an "ILS frequency" box that homes (whole steps down past 108, fraction steps down past .10) and steps up, one request per call, 150 ms apart; it cannot read back: "ILS tuning sent: 110.50. The 757 cannot report the ILS frequency; check the localizer with Alt+P."
  - **C. Neither:** no ILS frequency entry; the handoff says so.
- [ ] **Steps 2-6:** tests for the chosen branch; commit `feat(b757): ILS frequency per the flight measurement`.

## In-sim checks (owner)

1. The fix: output N at the gate reads VOR L as the VOR/DME panel shows it.
2. The essentials: type 121.5 in COM2 Standby → "COM2 standby 121.500"; COM2 Transfer → "COM2 active 121.500, standby …"; a VHF knob turned in the cockpit or the Surroundings window's Enter → "COM1 standby …" once. Ctrl+N: VOR L 113.90, VOR L auto, ILS course 095; N reads all.

## Handoff to plan 16

- **Pilot-facing:** the fix "757: output N reads the 757's own VOR tuning and the VOR and ILS courses (it used to read the simulator's nav radio, which the 757 does not use)" (`fix`); the essentials "a COM radios panel on the Pedestal (COM1 is VHF L, COM2 VHF C, COM3 VHF R) with standby boxes and transfer buttons; every COM change is announced ("COM1 active 124.850"); input Ctrl+N opens the 757 nav radios window: VOR frequencies (through the first officer's CDU, left on its NAV RAD page), VOR and ILS courses, VOR auto" (`feature`).
- **Proposed rule line:** `- [B757-8] The 757's VHF L, C and R are the stock COM 1, 2 and 3 (BlueBird stores its frequencies there, measured): read them from `COM ACTIVE/STANDBY FREQUENCY:n` and set standby with the stock `COMn_STBY_RADIO_SET_HZ`; never assume VHF C is COM 3. Full: docs/invariants/b757.md#b757-8`

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started
- **Claimed by:** none
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** (date; the `B757` commit you branched from)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
