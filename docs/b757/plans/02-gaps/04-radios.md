# BlueBird 757 Radios Implementation Plan (plan 04, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fix first: output N reads the 757's own VOR tuning and courses instead of the simulator's NAV radios, which BlueBird does not use. Then the essentials: a COM radios panel with standby entry and transfer, every COM change announced as on the other Boeings ("COM1 active 124.850"), and a 757 nav radios window on input Ctrl+N for what the FMC cannot set: the ILS course, the ILS frequency once a flight shows how, and the VOR courses only if the CDU cannot set them. VOR frequencies are tuned in the CDU window (Shift+M), as on the aircraft.

**Architecture:** COM is the simulator's own: BlueBird's VHF L, C and R store their frequencies in `COM ACTIVE/STANDBY FREQUENCY:1..3` (measured), so the panel writes the stock standby events and watches the six variables. NAV is BlueBird's own. The VORs are normally tuned by the FMC (AUTO); a pilot who wants a particular station tunes it on the CDU's NAV RADIO page, which the CDU window already does, so N reads that page through plan 01's `Bbs757CduPages` (the first officer's CDU) and the app adds no VOR frequency entry of its own. The ILS is tuned by hand on the ILS control panel on the aft pedestal: the FMC does not tune it, the CDU has no line for it, and no control-map panel holds it, so the 757 Ctrl+N window (replacing the shared one, which writes stock NAV events BlueBird ignores) carries the ILS course and frequency, and shows the FMC's runway ILS from APPROACH REF so the pilot knows what to set.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#1, #3), 3.1, 3.2, 4 (R52-R55, R53 per the owner), 8.

**Scope change (owner, 2026-10-26, after plan 01):** Ctrl+N carries only what the FMC cannot set. The VOR frequency boxes and the VOR auto buttons are dropped: the CDU window tunes a VOR (NAV RADIO, the frequency into 1L or 1R; `DELETE` into it returns to auto) and the VOR/DME panel's buttons select AUTO or MAN. This supersedes the VOR part of spec section 1 #1 and of R55 (the spec is frozen); R55's rule that Enter applies only the field it is pressed in still holds for the fields that remain. Sources: BlueBird's 757 Complete Flight Guide v1.01 (12 October 2026, the owner's copy), p. 43 ("ILS: set or parked as required. Tune the required ILS, or park it when not required."), p. 94 ("VOR/DME selection: as required. Normally AUTO." Auto tuning with the EFIS in MAP or PLAN; manual tuning for a particular station), p. 192 (parking the ILS while approach guidance is selected "can trigger an RW/ILS CRS ERROR or RW/ILS FREQ ERROR scratchpad alert"; after landing "ILS controls: park as appropriate"); its User Procedures guide, ILS approach profile FP-1 ("ILS tuned and identified" before intercept); and session 8's measurements (`docs/b757/evidence/live-findings.md`: NAV RADIO has no ILS line; APPROACH REF shows the FMC's runway ILS).

## Global Constraints

- Owned files: `Aircraft/BBS757Definition.Radios.cs` (new), `Aircraft/BBS757/Bbs757Radios.cs` (new), `Aircraft/BBS757/Bbs757NavRad.cs` (new), `Forms/BBS757/BBS757NavRadiosForm.cs` (new), tests `Bbs757RadiosTests.cs`, `Bbs757NavRadTests.cs`, `docs/b757/evidence/flight-04.md`.
- Radios: COM 1 = VHF L, COM 2 = VHF C, COM 3 = VHF R (measured). Stock events: `COM_STBY_RADIO_SET_HZ`, `COM2_STBY_RADIO_SET_HZ`, `COM3_STBY_RADIO_SET_HZ`; `COM_STBY_RADIO_SWAP`, `COM2_RADIO_SWAP`, `COM3_RADIO_SWAP`. Tuning-panel transfer buttons `B757_PED_COMHEAD_1/2/3_SWAP_BUTTON` (held 1 then 0); panel 1's selected radio is `BBS_RTP_L_LIGHTS` (measured: bit 2 VHF L); panel 2's is `BBS_RTP_C_LIGHTS` and panel 3's `BBS_RTP_R_LIGHTS` (measured 2026-10-26, `docs/b757/evidence/flight-04.md`: panel 2's button swapped COM 2 and panel 3's COM 3), and all three stock swaps (`COM_STBY_RADIO_SWAP`, `COM2_RADIO_SWAP`, `COM3_RADIO_SWAP`) work and hold on BlueBird. Bits 2 VHF L, 4 VHF C, 8 VHF R.
- COM range 118.000-136.990 MHz; valid channels have a kHz part whose value mod 25 is 0, 5, 10 or 15.
- Words (owner, Q1): "COM1 active 124.850", "COM2 standby 121.500"; three decimals, invariant; Ctrl+M rows "COM1 Active", "COM1 Standby", … as on the PMDG 737.
- Status rows show frequencies with three decimals through plan 01's `FormatRadioDisplay` seam (units "MHz" alone would round to "125").
- VOR: normally tuned by the FMC (AUTO on the VOR/DME panel, "A" on NAV RADIO; the manual's "normally AUTO"). The NAV RAD page (measured on the captain's CDU): line 1 `VOR L … VOR R`, line 2 the tunings (`116.90AGSU`, `113.90R`, `------`), line 3 `CRS RADIAL CRS`, line 4 the courses. Entry into 1L / 1R tunes (the pilot does it in the CDU window); `KEYDEL` + 1L / 1R returns to auto. Whether the CRS line takes a course entry is unmeasured (Task 5 step 1). Courses: `L:BBS_VOR_L_COURSE`, `L:BBS_VOR_R_COURSE`, `L:BBS_ILS_COURSE`, integer degrees, read and write (BlueBird's list; the ILS course measured: a direct write of 95 held).
- ILS: tuned by hand on the ILS control panel (aft pedestal), parked when not needed (the manual, p. 43 and p. 192). The FMC does not tune it: APPROACH REF shows the selected runway's ILS from the database (measured on the FO CDU: `ILS 09L 110.50 IHZK`, `FRONT CRS 095°`), and the FMC posts RW/ILS FREQ ERROR or RW/ILS CRS ERROR when what is tuned differs (when, and on which CDU, is unmeasured: Task 6 step 1). NAV RADIO has no ILS line. No variable holds the tuned ILS frequency; its knobs are step requests (`L:B757_ILS_FREQ_WHOLE_INC`, `L:B757_ILS_FREQ_FRAC_INC`, ±1, consumed). The panel is in no instrument view and on no control-map panel: today the pilot cannot tune the ILS at all.
- Never read stock NAV variables on the 757 (proposed rule B757-5). Background CDU work on CDU 2 only (R52); the FO CDU is left on NAV RADIO.
- Variables added: 15.

## Review Focus

- "121,5" typed under de-DE is 121.500 and the event's Hz is invariant; "121.57" is 121.575; "121.520" is refused (Task 2 test).
- The ILS frequency cannot be read back: the app says what it sent and never claims the ILS is tuned; an RW/ILS FREQ ERROR the FMC posts after a set is spoken, if Task 6 step 1 shows where it appears (Task 6 test).
- APPROACH REF unreadable (no approach selected, the FO CDU busy): the Ctrl+N window opens anyway, its status saying the FMC's runway ILS is unavailable (Task 5 test).
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

- [x] **Step 1 (owner-in-sim, on the ground):** hold `B757_PED_COMHEAD_2_SWAP_BUTTON` and `_3_` (1, 200 ms, 0) and record which COM swapped, with the three `BBS_RTP_*_LIGHTS`; try `K:COM_STBY_RADIO_SWAP`, `COM2_RADIO_SWAP`, `COM3_RADIO_SWAP` and record whether each swaps; swap everything back. Record in `flight-04.md` and correct the panel-to-lights mapping in Global Constraints if it differs.
- **Interfaces:** `HandleRadioSet` for a `TransferKey`: `CanWrite` false → "COM1 unavailable"; read active and standby fresh; arm both echoes; write `SwapCode`; after `ReadBackDelayMs` read both; swapped → "COM1 active 121.500, standby 124.850"; not swapped → `TransferPanelFor` (null → "No tuning panel selects COM1") → press that panel's button (1, 200 ms, 0), read back, speak the sentence or "COM1 did not transfer".
- [ ] **Steps 2-6:** tests `Stock_swap_that_works_is_used`, `Panel_fallback_when_stock_swap_is_ignored`, `No_panel_refuses`; run, implement, full suite; commit `feat(b757): COM transfer, stock swap first, the panel's button as fallback`.

### Task 5: The Ctrl+N nav radios window

**Files:** Create `MSFSBlindAssist/Forms/BBS757/BBS757NavRadiosForm.cs`; modify `Bbs757NavRad.cs`, `BBS757Definition.Radios.cs` (`HandleRadioHotkey` for `SetNavRadios`, `CloseRadioWindows`); test `Bbs757NavRadTests.cs`.

VOR frequencies are not in this window (the scope change above): the pilot tunes a VOR in the CDU window, and N reads it back.

- [x] **Step 1 (owner-in-sim, on the ground):** on the first officer's CDU, NAV RADIO: type `095` into the CRS line under VOR L (2L), then under VOR R (2R): does `L:BBS_VOR_L_COURSE` / `L:BBS_VOR_R_COURSE` follow, and does the captain's NAV RADIO show it? Restore the old courses. Then find the keys from any page to APPROACH REF on the FO CDU (INIT REF, its INDEX, then APPROACH) with an arrival runway selected, and record them and its ILS lines. Record both in `flight-04.md`.
- **Interfaces:** in `Bbs757NavRad`: `TryParseCourse(string? text, out int degrees, out string error)` (0-360, 360 → 0), `CourseCode(string lvar, int degrees)` ("95 (>L:BBS_ILS_COURSE)"), and
  ```csharp
  public sealed record Bbs757RunwayIls(string Runway, double Mhz, string Ident, int? FrontCourse);
  public static bool IsApproachRef(Bbs757CduScreen s);                    // Bbs757CduTitle.Identity(line 0) == "APPROACH REF"
  public static Bbs757RunwayIls? ParseApproachRefIls(Bbs757CduScreen s);  // "ILS 09L 110.50 IHZK" and "FRONT CRS 095°" (measured)
  public static string RunwayIlsPhrase(Bbs757RunwayIls? ils);             // "FMC runway ILS 09L, 110.50, IHZK, front course 095" / "FMC runway ILS unavailable"
  ```
- `SetNavRadios` (input Ctrl+N): `hotkeyManager.ExitInputHotkeyMode()`; show the one window (created on first use); `handled = true`.
- `BBS757NavRadiosForm` (modeless, owned by the definition, closed by `CloseRadioWindows`): a read-only status `TextBox` first: on open, N's `Readout`, then `RunwayIlsPhrase` of APPROACH REF read on the FO CDU through `Bbs757CduPages` with step 1's keys (the FO CDU is then put back on NAV RADIO, where N leaves it). Then the "ILS course" box with its label, and "VOR L course" and "VOR R course" boxes only if step 1 shows the CRS line does not set them. Task 6 adds the ILS frequency. Enter applies only the focused box (R55); Escape closes and returns focus as `ValueInputForm` does. Actions refuse aloud when `CanWrite` is false:
  - Course: write `CourseCode`, read back after `ReadBackDelayMs` → "ILS course 095" / "VOR L course 095".
  - Every result also goes into the status box.
- [ ] **Steps 2-6:** tests `Course_parsing`, `Course_code_is_invariant` (de-DE), `Approach_ref_ils_parsed` (the measured lines; a page with no ILS line → null), `Window_opens_without_approach_ref` (a fake pages service answering null: the status says "FMC runway ILS unavailable"); run, implement, full suite; commit `feat(b757): Ctrl+N nav radios window for the ILS`.

### Task 6 [in flight]: ILS frequency

**Being worked (owner, 2026-10-26):** the controller is measuring and building the localizer-benchmark approach; the ground parts of step 1 were measured (`docs/b757/evidence/flight-04.md`, "The ILS frequency: findings and ideas"); branch B as written cannot work (no end stop to home against, no readable state).

**Files:** `Bbs757NavRad.cs`, `BBS757Definition.Radios.cs`, `BBS757NavRadiosForm.cs`, tests; `flight-04.md`.

BlueBird's procedures tune the ILS by hand on the ILS control panel and park it when it is not needed; the FMC does not tune it (Global Constraints). Expected outcome: branch B, with an "ILS park" button.

- [ ] **Step 1 (owner, on the ground, then in flight):** with a view of the aft pedestal's ILS panel (a custom or drone camera, window capture):
  - Step `L:B757_ILS_FREQ_WHOLE_INC` -1 twenty times, one per call: does it stop at 108, wrap, or reach PARK (where on the knob is PARK, and how is it left)? The same for `FRAC_INC`. Restore.
  - With an ILS approach selected on the CDU, tune a different ILS frequency, then a different course: does RW/ILS FREQ ERROR or RW/ILS CRS ERROR appear, on which CDU, and when (on the ground, in the climb, only in descent)? Restore.
  - With the EHSI in its ILS (APP) mode, does an Alt+N read give the tuned ILS frequency? (The real 757 shows it there; BlueBird's is unseen.)
  - On an ILS approach with the panel in PARK, does the localizer capture? (Expected no: the manual tunes the ILS by hand.) Record the APPROACH REF ILS line.
  - Record everything in `flight-04.md`.
- Then build the branch it supports:
  - **B (expected). The knobs stop at the ends of 108.10-111.95, or PARK is a known knob position:** in the Ctrl+N window, an "ILS frequency" box that homes (whole steps down past 108, fraction steps down past .10, or into PARK and out of it, as measured) and steps up, one request per call, 150 ms apart; and an "ILS park" button (the steps that reach PARK), since the procedures park the ILS before taxi when it is not needed and after landing. It cannot read the frequency back, so it says what it sent and how to confirm it: "ILS tuning sent: 110.50.", then, as step 1 allows, the FMC's RW/ILS FREQ ERROR if it appears (read on the CDU step 1 names, through `Bbs757CduPages`: spoken, never taken as proof the ILS is tuned), and "Check it with Alt+N" or "Check the localizer with Alt+P". The status box keeps the FMC's runway ILS beside it (Task 5).
  - **A. PARK lets the FMC tune the arrival ILS (not expected):** an "ILS park" button and N saying "ILS parked, FMC tunes 110.50 IHZK" from APPROACH REF.
  - **C. Neither:** no ILS frequency entry; the handoff says so.
- [ ] **Steps 2-6:** tests for the chosen branch (B: `Ils_entry_parsing` with de-DE "110,50", `Ils_steps_from_home` against a fake panel, `Park_steps`, `Freq_error_is_spoken_not_claimed`); run, implement, full suite; commit `feat(b757): ILS frequency per the flight measurement`.

## In-sim checks (owner)

1. The fix: output N at the gate reads VOR L as the VOR/DME panel shows it.
2. The essentials: type 121.5 in COM2 Standby → "COM2 standby 121.500"; COM2 Transfer → "COM2 active 121.500, standby …"; a VHF knob turned in the cockpit or the Surroundings window's Enter → "COM1 standby …" once. VOR L in the CDU window (Shift+M, NAV RADIO, 113.90 into 1L): N reads it; `DELETE` into 1L: N reads it as auto again. Ctrl+N: the status names the FMC's runway ILS (with an arrival selected); ILS course 095; N reads all.

## Handoff to plan 16

- **Pilot-facing:** the fix "757: output N reads VOR L and VOR R as the CDU shows them (auto, tuned from the CDU, or manual), each hand-tuned VOR's course and the ILS course (it used to read the simulator's nav radio, which the 757 does not use)" (`fix`); the essentials "a COM radios panel on the Pedestal (COM1 is VHF L, COM2 VHF C, COM3 VHF R) with standby boxes and transfer buttons; every COM change is announced ("COM1 active 124.850"); a transfer says "COM1 active 121.500, standby 124.850"; input Ctrl+N opens the "757 Nav Radios" window: the FMC's runway ILS for reference ("FMC runway ILS KATL 09L, 110.50, IHZK, front course 095"; it names the airport because, on the ground, APPROACH REF shows the departure runway's ILS) and a box for the ILS course; the VORs are normally tuned by the FMC, and a particular station, with its course, is tuned in the CDU window on the NAV RADIO page, as on the aircraft" (`feature`). **The ILS frequency is not in this landing: Task 6 is in progress** (the localizer-benchmark approach, flight-04.md); add its line here when it lands, and until then the Ctrl+N window carries the ILS course only. Guide: say that the ILS is a panel on the aft pedestal (the app sets its course; the frequency per Task 6), that VOR tuning and a VOR's course are the CDU's (N speaks a VOR's course only when that VOR is hand-tuned, because the CDU's CRS line is blank in auto), and that Ctrl+N pages the first officer's CDU (CDU 2) through INIT REF to APPROACH REF and leaves it on NAV RADIO.
- **Proposed rule lines** (B757-5 is also in plan 01's handoff; plan 16 writes it once):
  - `- [B757-5] Never read the stock NAV variables (frequency, OBS, CDI, glideslope) on the 757: BlueBird's VOR and ILS receivers are its own and leave them untouched (measured). Frequencies come from the CDU's NAV RADIO page through `Bbs757CduPages`, courses from `BBS_VOR_L_COURSE`, `BBS_VOR_R_COURSE`, `BBS_ILS_COURSE`. Full: docs/invariants/b757.md#b757-5`
  - `- [B757-8] The 757's VHF L, C and R are the stock COM 1, 2 and 3 (BlueBird stores its frequencies there, measured): read them from `COM ACTIVE/STANDBY FREQUENCY:n` and set standby with the stock `COMn_STBY_RADIO_SET_HZ`; never assume VHF C is COM 3. Full: docs/invariants/b757.md#b757-8`
- **Frozen-file request:** `MSFSBlindAssist/SimConnect/BBS757/Bbs757CduPages.cs` (plan 01, frozen): a call that presses a key only while a given page shows, inside one gate, so a multi-page walk (Ctrl+N's APPROACH REF read: INIT, `L6`, `L5`) cannot interleave with another caller. Today the walk is three gated calls, not one sequence, and its fill keeps running after the window closes: pressing N, or Escape then Ctrl+N, mid-fill interleaves keys, and the window can then say "FMC runway ILS unavailable" wrongly (the Task 5 review's Minor 1). The review also called a fix in plan 04's own files worth making before the whole-branch review (cancel the fill when the window closes, or start no new fill while one runs); the call above is the full fix.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** Tasks 0-5 landed; Task 6 in progress
- **Claimed by:** kn4iee (the owner), 2026-10-26
- **Worked by:** kn4iee (the owner) with Claude, with subagents (superpowers:subagent-driven-development): the controller did Task 0 and made the rulings; an implementer and a reviewer did each of Tasks 1-5; a measuring agent ran the ground measurements (rounds 1-3) with the owner's go-ahead; a read-only agent did the localizer geometry check; a records agent wrote `flight-04.md`'s rounds 2 and 3 and these records; the owner programmed the KATL-KMIA route for round 3
- **Started:** 2026-10-26, `B757` at b64fa844b (plan 01's base cf1dfce94 plus its record, 9bce44b54 and the claims of plans 02, 03 and 05)
- **Landed:** (filled at the push)
- **Tests at landing:** (filled at the push)
- **Tasks:** Tasks 0-5 done; Task 6 in progress. Each of Tasks 1-5 had an implementer and a reviewer, and every review came back clean; the counts are the full suite at that commit, all with 0 failures.
  - Task 0, the claim: ecc077271 (pushed to `kn4iee/B757`, 2026-10-26).
  - Task 1, output N reads the 757's own nav radios: 47146f095 (16,977 passed). Its parser read VOR R like VOR L; measurement showed the layout, and Task 5 fixed it.
  - Task 2, COM logic, pure: 0d8845c06 (17,064).
  - Task 3, the COM radios panel and COM announcements: ead5486c9 (17,092).
  - Task 4, transfer: e0c56e1a3 (17,138). Step 1 was measured (`flight-04.md`, A), which created that file.
  - Task 5, the Ctrl+N nav radios window: af472e90c (17,202). Step 1 was measured (`flight-04.md`, B and E2).
  - Task 6, the ILS frequency (in progress, [in flight]; steps unticked): step 1's ground parts are measured (rounds 1-3, the geometry check, and the findings in `flight-04.md`, "The ILS frequency: findings and ideas"). Branch B as written cannot work: the knobs are rings with no end stop and nothing reads the frequency back. The controller is measuring and building the localizer-benchmark approach (owner, 2026-10-26); the in-flight parts wait on the owner.
  - Not in this plan: the pilot's guide (CORE-19). Plan 16 writes it from the handoff above (the index), so no guide edit was made here.
  - Review minors left unfixed (for the whole-branch review and plan 16 to weigh):
    - Task 1: the de-DE check in `Readout_sentences` is a separate theory (coverage equivalent); cancellation after the page read is untested; the three course reads are sequential (up to about 4.5 s of silence on a silent sim). Fixed in Task 5: the VOR R layout and the per-side parse (measured, with positive tests), and "Pure." dropped from `Parse`'s doc since it logs.
    - Task 2: the pre-filter refuses sign, exponent, units and full-width digits (kept by ruling); "121." is accepted as 121.000, unpinned; the de-DE refusal lines prove little about culture; four-decimal ties are unpinned; `TransferPanelFor` skips negative, NaN and huge lights; `StandbySetCode` does not validate its MHz (callers pass parsed values).
    - Task 3: two quick Enters on one field can speak twice (no in-flight guard); a switch-away race logs `ObjectDisposedException` at Error level (the squawk field does too); `FormatRadioDisplay` shows "NaN" for a non-finite value; the status rows show "121.900" with no unit (an owner's call, below). The duplicated event names were fixed in Task 4.
    - Task 4: the pure helpers' RED is not shown separately; a flight load during a panel-button fallback is not cancelled (`ResetRadios` is unimplemented); whether a panel's button follows its selection or is hard-wired to its own number is not told apart, so "measured" in `PanelPressCode`'s comment and in the Global Constraints bullet overstates; the panel button is written with the control map's `,bool` code, unexercised in the sim (the stock swaps work, so the fallback is not expected to run); the quiet settle in the no-panel and link-lost paths and the null-after-panel-press branch are untested; `CanWrite` is not re-checked just before the stock swap write; a standby set and a transfer on one radio are not coordinated (possible double speech); the panel press and release strings are a third copy of the control map's codes.
    - Task 5: the APPROACH REF walk is three gated calls, not one sequence, and the fill keeps running after its window closes (the review's Minor 1, which it called worth fixing before the whole-branch review; the frozen-file request in the handoff is the full fix); a second Ctrl+N only re-activates the window, and an ILS course set replaces the whole status, so the FMC runway ILS line is lost until Escape and Ctrl+N (owner's call); a late fill overwrites a set result and speaks courses read before the set; the "both tuned" test row is 28 columns and the no-ILS fixture uses the console's `???.?` rather than the scraped `___.`; the Enter key handler is untested; the catch after the write says "unavailable" instead of "sent, not read back"; focus is set in `Load` where `ActiveControl` would state the intent.
- **Waiting on the owner:**
  - The plan's two "In-sim checks (owner)": not run.
  - Task 6 [in flight]: step 1's in-flight parts, which the controller does not run. The open questions are in `flight-04.md` ("The ILS frequency: findings and ideas", "Open in-flight questions"): whether the FMC posts RW/ILS FREQ ERROR or RW/ILS CRS ERROR in descent (which CDU, how long after a mismatch); whether the localizer captures with the ILS parked or wrongly tuned; whether APPROACH REF switches to the destination's runway in flight, and whether `INIT` alone opens it in descent; whether the EHSI shows the frequency or ident once a localizer is received; why 27L and 27R were not received at 0.8 NM; whether `BBS_LOC_DEV_DEG` and `B757_SADI_LOC` follow a received localizer in flight and clear when the ILS is parked.
  - Owner's calls: a second Ctrl+N only re-activates the window (its status can be stale, and an ILS course set drops the FMC runway ILS line until Escape and Ctrl+N); whether the COM status rows should show a unit (they show "121.900"; the A380 shows "MHz", [A380-27]).
  - To put back: the camera is on instrument view 1 after round 3 (the writes that restore it were ignored); the owner's cockpit-view key brings the default view back.
- **In-sim results:** the plan's "In-sim checks (owner)": none run. The steps that decided the branches (Task 4 step 1, Task 5 step 1, Task 6 step 1's ground parts) were measured on 2026-10-26 by a Claude agent through the simconnect MCP server and the Coherent debugger, with the owner's go-ahead, on the ground at KATL gate B1 with MSFSBA closed, in three rounds (11:26-11:51, 12:07-12:19, 12:24-12:39); everything is in `docs/b757/evidence/flight-04.md`.
  - Round 1: all three stock COM swaps work and hold; the tuning panels' lights are 1 L, 2 C, 3 R; the CDU sets a VOR's course only while that VOR is hand-tuned (in auto its CRS field is blank and an entry is ignored; a direct write of `BBS_VOR_R_COURSE` showed on the CDU's CRS line); INIT, `L6`, `L5` reach APPROACH REF on the ground; the ILS knobs are rings through PARK with no readable frequency.
  - Round 2: `BBS_LOC_DEV_DEG` takes a station-specific value for a received localizer but latches; `B757_SADI_LOC` is live both ways with the SADI ILS selector on. The ILS debug page was not reached.
  - Round 3: APPROACH REF with a route shows the origin's runway ILS in two rows; the FMC posts no RW/ILS FREQ ERROR or CRS ERROR on the ground (wrong frequency, wrong course or PARK); the ILS panel's course window shows `BBS_ILS_COURSE`; the EFB's Systems app has no ILS debug page and no logs. The geometry check matches the four received localizers to 0.15-0.26 degrees.
  - Put back: every value changed was restored and proved, except the camera (left on instrument view 1 after round 3). Seen, not caused by the agent: `B757_FC_SPOILER_LEVER` 0 to 0.815 and `B757_PSU_STATE` 1 to 0 between rounds 1 and 2 (left as found); the IRS finished aligning during round 3 from the owner's NAV selection; one unexplained change in round 1 (`B757_MCP_VOR1_MODE_AUTO` 1 to 0 after the ND mode was written back), put back with the VOR/DME 1 button (its warning is in `flight-04.md`).
- **Changed from the plan:**
  - The panel-to-lights mapping: panel 1 is `BBS_RTP_L_LIGHTS`, panel 2 `BBS_RTP_C_LIGHTS`, panel 3 `BBS_RTP_R_LIGHTS`. Measured: panel 2's button swapped COM 2 and panel 3's COM 3, which reverses the plan's belief (panel 2 = R, panel 3 = C). Global Constraints corrected in Task 4; the mapping lives in one place (`Bbs757Radios.RtpLights`). All three stock swaps also work and hold, so the panel fallback is not expected to run on this build. Whether a panel's button follows its selection or is hard-wired to its own number was not separated (every panel selects its own number's radio).
  - The mirrored VOR R and the per-side parse: Task 1 read VOR R like VOR L. Measured: the right side is `[ident][mode]freq` flush right (`GSUR116.90`), and a hand-tuned VOR L on the panel reads `117.40M` (mode `M` is manual). `Bbs757NavRad.Parse` reads each side on its own, so `Bbs757NavRadPage`'s sides are nullable (an unreadable side says "VOR R frequency unavailable" and the other side still reads); the page is null only when the screen is not NAV RADIO.
  - Courses only for hand-tuned VORs: the CDU blanks a VOR's CRS field in auto and ignores an entry, so `Readout` speaks a VOR's course only when that VOR is hand-tuned (a frequency and a mode other than `A`). The ILS course is always spoken; without the page every course is still read.
  - No VOR course boxes in Ctrl+N: the CDU's CRS line sets them whenever they apply (after hand tuning in the CDU window), so the window has the ILS course box only.
  - APPROACH REF is two rows, with the airport: the ILS label row (` ILS 09L       FRONT CRS`) and the value row (`110.50 IHZK         095°`), with the runway row above them (` KATL09L`). `Bbs757RunwayIls` gained `Airport` (default null, so the four-field form still works), and the phrase names it ("FMC runway ILS KATL 09L, 110.50, IHZK, front course 095"). The keys are `INIT`, `L6`, `L5` with a page check after each, then the first officer's CDU is put back on NAV RADIO; the path also takes a CDU whose `INIT` already shows APPROACH REF or the index (whether `INIT` does that in descent is unmeasured).
  - The origin's runway on the ground: APPROACH REF shows the ORIGIN's runway ILS (KATL 09L) even with an arrival selected, which is why the phrase names the airport. Whether it switches to the destination's runway in flight is unmeasured.
  - 18 variables, not 15: Task 1's three course L:vars plus Task 3's fifteen (6 batch rows, 3 standby fields, 3 transfer buttons, 3 tuning-panel lights). "Variables added: 15" in the header was an estimate; the cap (900) is what binds, and `Bbs757DefinitionTests` passes.
  - The event names exposed once: `Bbs757Radios.StandbySetEvent(com)` and `SwapEvent(com)` are the only place the stock event names live; the registrations and the RPN codes share them (the duplicate `SetEventName` / `SwapEventName` in the definition were deleted).
  - The transfer: a second press of a radio's Transfer while its transfer runs says "COM1 transfer in progress" (other radios run at once); "swapped" is a cross-equal at 1 kHz (an equal pair counts as swapped); a row unread before the swap still swaps and speaks, and a row unread after says "COM1 transfer sent, the radio did not report back" (no panel fallback).
  - The window: titled "757 Nav Radios"; it speaks its status once when it fills; the ILS course errors are "Type a course first." and "Course must be 0 to 360."
  - Task 6 is not built in this landing: its branch B (home against an end stop) cannot work and the frequency cannot be read back; the localizer-benchmark approach is in progress.
- **Rulings made while working:** (question; answer; who decided. The controller is Claude; its rulings are the owner's to reverse. Owner statements are from the chat, 2026-10-26.)
  - Is the header's "Variables added: 15" binding? No, it is an estimate; the real count (18 before Task 6) is recorded here, and the cap (900, about 500 used) is what binds, with a test that catches a breach; the controller.
  - The controller never dispatches an owner-in-sim step, so who runs Task 4 step 1, Task 5 step 1 and Task 6 step 1's ground parts? The owner's request to use the simconnect MCP server and the Coherent debugger to execute plan 04 is the go-ahead for the reversible steps: a measuring agent runs them and restores every change. Task 6's in-flight parts stay with the owner. Cost if wrong: COM, CDU and ILS state that the agent changed and has to put back. The controller, on the owner's request.
  - Which branch name? The worktree's own (`claude/plan-4-subagent-tdd-52aca3`) instead of `B757-p04-radios`; pushes go `HEAD:B757`. Cost if wrong: a branch name only. The controller.
  - Task 2: how is the "two decimals ending 2 or 7 get a 5" rule applied, what does `TransferPanelFor` do with an unread panel, and what does a radio outside 1-3 do? The rule reads the typed text, not the parsed number; an unread panel is skipped; a radio outside 1-3 throws. The controller.
  - Task 2: does the pre-filter refuse "121.5 MHz" (sign, exponent, units, full-width digits)? Yes, it is kept: a frequency box takes digits and one separator, and a refusal is spoken with the range message. Cost if wrong: a pilot typing "121.5 MHz" hears the range message. The controller.
  - Task 3: where do the tuning-panel lights live, where does the COM panel go if the control map has no TCAS panel, what does a null read-back after a standby set say, and when is the own write noted? The three lights are `Bbs757Radios.RtpLights`; the panel is appended to the Pedestal when its TCAS panel is absent; a null read-back says the standby was not confirmed and does not rebaseline the row; `OnOwnWrite(SetKey)` runs before the write. The controller.
  - Which panel's button is which radio? Panel 1 L, panel 2 C, panel 3 R (measured), reversing the plan's belief; sent to the Task 3 implementer mid-task, and Task 4 corrected the Global Constraints line. Cost if wrong: the transfer fallback presses the wrong panel's button (unused while the stock swaps work). The controller.
  - Does `TransferPanelFor` follow the panel's number or its selection? It stays lights-based (the selected radio), though today every panel selects its own number's radio, so "panel n always swaps COM n" is not ruled out. Cost if wrong: the fallback picks a panel by selection when the button is hard-wired (only if a stock swap ever stops working). The controller.
  - Task 4: how is a transfer judged and guarded? Swapped means cross-equal at 1 kHz (an equal pair counts as swapped); a null read before still swaps; a null read after says "sent, did not report back"; a second press in flight says "COM1 transfer in progress"; the event names are exposed once from `Bbs757Radios`. The controller.
  - Task 4's review warned about `flight-04.md`'s header facts (the owner's go-ahead, the starting panel state, BBS Nest running): are they true? Yes, they come from the owner and the brief. The controller.
  - What state does the ILS panel load in? PARK with fraction .10: the owner said the flight was in its starting panel state when round 1 began (only the batteries on and the APU started), and stepping proved the .10. Unproven for other start types and an MSFSBA restart. The owner.
  - Who programs the KATL-KMIA flight for round 3? The owner first said a KATL-KMIA SimBrief flight exists and the agent could load it into the EFB and program the FMC if testing needed it, then offered to program it himself: the measuring agent was told to stop round 2 at the next safe point, restore and free the tablet; the owner programmed the departure and arrival and closed MSFSBA; round 3 then ran E2 first, E1 as a read-only record, E3, and round 2's leftover D2 steps. The owner.
  - How does `Bbs757NavRad.Parse` read VOR R? As `[ident][mode]freq` flush right (`GSUR116.90`), each side on its own so one unreadable side does not drop the other, mode `M` as manual. This was a Task 1 defect found by measurement, carried into Task 5 (load-bearing). Cost if wrong: N says "Frequencies unavailable" whenever VOR R is tuned. The controller.
  - Does N speak a course for a VOR in auto? No, only for a hand-tuned VOR (the CDU blanks the CRS field in auto), and without the page every course is still read. Cost if wrong: the pilot does not hear BlueBird's stored course for an auto-tuned VOR. The controller.
  - Does Ctrl+N get VOR course boxes? No: the CDU's CRS line sets a VOR's course whenever it applies, after hand tuning in the CDU window. Cost if wrong: a pilot sets a VOR course in the CDU window instead. The controller.
  - Does the Ctrl+N window speak its status? Once, when it fills (the reader cannot know it changed; CORE-7 allows what the reader cannot say). Cost if wrong: one extra announcement on Ctrl+N. The controller.
  - Does the ILS phrase name the airport? Yes (`Bbs757RunwayIls.Airport`, from the runway row), because on the ground APPROACH REF shows the departure runway's ILS: "FMC runway ILS KATL 09L, 110.50, IHZK, front course 095". Sent to the Task 5 implementer mid-task. Cost if wrong: a longer phrase. The controller.
  - Task 5: how does the window reach APPROACH REF, and what does it parse? `INIT`, then `L6` and `L5`, with a page check after each and the first officer's CDU put back on NAV RADIO; the ILS parser scans the rows for the measured two-row layout; course errors read "Type a course first." and "Course must be 0 to 360."; the window is titled "757 Nav Radios". The controller.
  - The Task 5 review's Minor 1 (the APPROACH REF walk is not one gated sequence and its fill outlives the window): fix now? Deferred and recorded; the full fix is the frozen-file request in the handoff, and the reviewer called a fix in plan 04's own files worth making before the whole-branch review. The controller.
  - Can the ILS frequency be homed against an end stop (Task 6 branch B)? No: the whole knob is a ring through PARK and nothing reads the state back, so Task 6 is not built as written; the options were a picture read to seed an app-tracked state, or branch C. The controller.
  - Could a received localizer anchor the dial? The owner asked: "Could we not try setting the ILS of the departure runway to get a benchmark?" That became idea 6; the controller ordered a read-only geometry check, which found the four received localizers within 0.15-0.26 degrees of the prediction and at least 2.8 degrees apart. Proposed by the owner, checked by the controller.
  - Who takes Task 6? The owner first said: "Please record findings and ideas. Finish all tasks in plan and push, but will hand the plan off to robin24 to investigate this further". He then changed that: "Continue to look into this idea, try to implement if possible. Let's hold off on handing off to robin". So the findings and ideas are recorded in `flight-04.md`, Tasks 0-5 finish, and the controller measures and builds the localizer-benchmark approach for Task 6. The owner.
  - Does this plan edit the pilot's guide (CORE-19)? No: plan 16 writes it, per the index; the handoff carries the pilot-facing text. The controller.
