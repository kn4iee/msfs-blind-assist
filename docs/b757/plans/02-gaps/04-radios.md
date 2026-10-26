# BlueBird 757 Radios Implementation Plan (plan 04, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fix first: output N reads the 757's own VOR tuning and courses instead of the simulator's NAV radios, which BlueBird does not use. Then the essentials: a COM radios panel with standby entry and transfer, every COM change announced as on the other Boeings ("COM1 active 124.850"), and a 757 nav radios window on input Ctrl+N for what the FMC cannot set: the ILS course, the ILS frequency once a flight shows how, and the VOR courses only if the CDU cannot set them. VOR frequencies are tuned in the CDU window (Shift+M), as on the aircraft.

**Architecture:** COM is the simulator's own: BlueBird's VHF L, C and R store their frequencies in `COM ACTIVE/STANDBY FREQUENCY:1..3` (measured), so the panel writes the stock standby events and watches the six variables. NAV is BlueBird's own. The VORs are normally tuned by the FMC (AUTO); a pilot who wants a particular station tunes it on the CDU's NAV RADIO page, which the CDU window already does, so N reads that page through plan 01's `Bbs757CduPages` (the first officer's CDU) and the app adds no VOR frequency entry of its own. The ILS is tuned by hand on the ILS control panel on the aft pedestal: the FMC does not tune it, the CDU has no line for it, and no control-map panel holds it, so the 757 Ctrl+N window (replacing the shared one, which writes stock NAV events BlueBird ignores) carries the ILS course and frequency, and shows the FMC's runway ILS from APPROACH REF so the pilot knows what to set.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#1, #3), 3.1, 3.2, 4 (R52-R55, R53 per the owner), 8.

**Scope change (owner, 2026-10-26, after plan 01):** Ctrl+N carries only what the FMC cannot set. The VOR frequency boxes and the VOR auto buttons are dropped: the CDU window tunes a VOR (NAV RADIO, the frequency into 1L or 1R; `DELETE` into it returns to auto) and the VOR/DME panel's buttons select AUTO or MAN. This supersedes the VOR part of spec section 1 #1 and of R55 (the spec is frozen); R55's rule that Enter applies only the field it is pressed in still holds for the fields that remain. Sources: BlueBird's 757 Complete Flight Guide v1.01 (12 October 2026, the owner's copy), p. 43 ("ILS: set or parked as required. Tune the required ILS, or park it when not required."), p. 94 ("VOR/DME selection: as required. Normally AUTO." Auto tuning with the EFIS in MAP or PLAN; manual tuning for a particular station), p. 192 (parking the ILS while approach guidance is selected "can trigger an RW/ILS CRS ERROR or RW/ILS FREQ ERROR scratchpad alert"; after landing "ILS controls: park as appropriate"); its User Procedures guide, ILS approach profile FP-1 ("ILS tuned and identified" before intercept); and session 8's measurements (`docs/b757/evidence/live-findings.md`: NAV RADIO has no ILS line; APPROACH REF shows the FMC's runway ILS).

## Global Constraints

- Owned files: `Aircraft/BBS757Definition.Radios.cs` (new), `Aircraft/BBS757/Bbs757Radios.cs` (new), `Aircraft/BBS757/Bbs757NavRad.cs` (new), `Forms/BBS757/BBS757NavRadiosForm.cs` (new), tests `Bbs757RadiosTests.cs`, `Bbs757NavRadTests.cs`, `docs/b757/evidence/flight-04.md`.
- Radios: COM 1 = VHF L, COM 2 = VHF C, COM 3 = VHF R (measured). Stock events: `COM_STBY_RADIO_SET_HZ`, `COM2_STBY_RADIO_SET_HZ`, `COM3_STBY_RADIO_SET_HZ`; `COM_STBY_RADIO_SWAP`, `COM2_RADIO_SWAP`, `COM3_RADIO_SWAP`. Tuning-panel transfer buttons `B757_PED_COMHEAD_1/2/3_SWAP_BUTTON` (held 1 then 0); panel 1's selected radio is `BBS_RTP_L_LIGHTS` (measured: bit 2 VHF L); panels 2 and 3 are believed to be `BBS_RTP_R_LIGHTS` and `BBS_RTP_C_LIGHTS` (the knob request vars say so; Task 4 step 1 measures it). Bits 2 VHF L, 4 VHF C, 8 VHF R.
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

- [ ] **Step 1 (owner-in-sim, on the ground):** hold `B757_PED_COMHEAD_2_SWAP_BUTTON` and `_3_` (1, 200 ms, 0) and record which COM swapped, with the three `BBS_RTP_*_LIGHTS`; try `K:COM_STBY_RADIO_SWAP`, `COM2_RADIO_SWAP`, `COM3_RADIO_SWAP` and record whether each swaps; swap everything back. Record in `flight-04.md` and correct the panel-to-lights mapping in Global Constraints if it differs.
- **Interfaces:** `HandleRadioSet` for a `TransferKey`: `CanWrite` false → "COM1 unavailable"; read active and standby fresh; arm both echoes; write `SwapCode`; after `ReadBackDelayMs` read both; swapped → "COM1 active 121.500, standby 124.850"; not swapped → `TransferPanelFor` (null → "No tuning panel selects COM1") → press that panel's button (1, 200 ms, 0), read back, speak the sentence or "COM1 did not transfer".
- [ ] **Steps 2-6:** tests `Stock_swap_that_works_is_used`, `Panel_fallback_when_stock_swap_is_ignored`, `No_panel_refuses`; run, implement, full suite; commit `feat(b757): COM transfer, stock swap first, the panel's button as fallback`.

### Task 5: The Ctrl+N nav radios window

**Files:** Create `MSFSBlindAssist/Forms/BBS757/BBS757NavRadiosForm.cs`; modify `Bbs757NavRad.cs`, `BBS757Definition.Radios.cs` (`HandleRadioHotkey` for `SetNavRadios`, `CloseRadioWindows`); test `Bbs757NavRadTests.cs`.

VOR frequencies are not in this window (the scope change above): the pilot tunes a VOR in the CDU window, and N reads it back.

- [ ] **Step 1 (owner-in-sim, on the ground):** on the first officer's CDU, NAV RADIO: type `095` into the CRS line under VOR L (2L), then under VOR R (2R): does `L:BBS_VOR_L_COURSE` / `L:BBS_VOR_R_COURSE` follow, and does the captain's NAV RADIO show it? Restore the old courses. Then find the keys from any page to APPROACH REF on the FO CDU (INIT REF, its INDEX, then APPROACH) with an arrival runway selected, and record them and its ILS lines. Record both in `flight-04.md`.
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

- **Pilot-facing:** the fix "757: output N reads the 757's own VOR tuning and the VOR and ILS courses (it used to read the simulator's nav radio, which the 757 does not use)" (`fix`); the essentials "a COM radios panel on the Pedestal (COM1 is VHF L, COM2 VHF C, COM3 VHF R) with standby boxes and transfer buttons; every COM change is announced ("COM1 active 124.850"); input Ctrl+N opens the 757 nav radios window: the FMC's runway ILS for reference, the ILS course and, per the flight check, the ILS frequency and park (and the VOR courses if the CDU cannot set them); the VORs are normally tuned by the FMC, and a particular station is tuned in the CDU window on the NAV RADIO page, as on the aircraft" (`feature`). Guide: say that the ILS is set in Ctrl+N because it is a panel on the aft pedestal, and that VOR tuning is the CDU's.
- **Proposed rule line:** `- [B757-8] The 757's VHF L, C and R are the stock COM 1, 2 and 3 (BlueBird stores its frequencies there, measured): read them from `COM ACTIVE/STANDBY FREQUENCY:n` and set standby with the stock `COMn_STBY_RADIO_SET_HZ`; never assume VHF C is COM 3. Full: docs/invariants/b757.md#b757-8`

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** claimed
- **Claimed by:** kn4iee (the owner), 2026-10-26
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** 2026-10-26, `B757` at b64fa844b (plan 01's base cf1dfce94 plus its record, 9bce44b54 and the claims of plans 02, 03 and 05)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
