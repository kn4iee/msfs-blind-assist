# BlueBird 757 EFB Rework Implementation Plan (plan 11, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** As much of BlueBird's tablet as can be operated and verified, in a reworked accessible EFB window at parity with the PMDG, MD-11 and flyPad EFB windows: flight setup, fuel and load, every ground service, doors, pushback, cockpit presets and quick starts, settings, maintenance resets, status.

**Architecture:** BlueBird's tablet is drawn by its WASM: no text can be read from it. The app taps it at measured points through the existing EFB client and agent (`Coherent.call("WASM_CLICK" â€¦)` on the tablet's own canvas handlers, proven) and judges each result from something it can read: a BlueBird L:var (`BBS757_vis_*`, `BBS_757_GPU_*`, doors, tanks and stations, APU and buses), BlueBird's settings file (`efb_settings.json`, which changes at once), or the CDU. Every toggle is read before it is tapped ([B757-2]'s pattern). The window shows the app's own model of that state, never BlueBird's screen (R73).

**Tech Stack:** C# 13 / .NET 10 Windows Forms (WebView2 through the shared EFB window, or WinForms tabs as the fallback), xUnit, ES5 agent.

**Spec:** `docs/b757/spec-gaps.md` sections 4 (R70, R73), 7; `docs/b757.md` "The 757 EFB window"; rules B757-2, B757-4; `docs/b757/evidence/live-findings.md` sessions 5-8 (measured points, the ND mirror technique); plan 07 (ground power, which this plan's window keeps).

## Global Constraints

- Owned files (from plan 07): `SimConnect/BBS757/Bbs757EfbLayout.cs`, `CoherentBbs757EfbClient.cs`, `Bbs757WorkFolder.cs`, `Bbs757Loadsheet.cs`, `Bbs757LoadWatch.cs`, `Bbs757GroundPower.cs`, `Forms/BBS757/BBS757EfbForm*.cs`, `Resources/coherent-bbs757-efb-agent.js`, `Aircraft/BBS757Definition.Efb.cs`, `Aircraft/BBS757Definition.LoadWatch.cs`, `MainForm.BBS757.cs`; new `SimConnect/BBS757/Bbs757GroundServices.cs`, `Bbs757EfbSettings.cs`, `Bbs757EfbBridge.cs`; additive changes only to `Forms/FBWA380/FbwEfbForm.cs` (the shared EFB window; the PMDG, MD-11 and flyPad EFB tests must stay green); tests `Bbs757EfbTests.cs`, `Bbs757LoadWatchTests.cs`, `Bbs757LoadsheetTests.cs`, `Bbs757GroundPowerTests.cs`, `Bbs757GroundServicesTests.cs`, `Bbs757EfbSettingsTests.cs`, `Bbs757EfbBridgeTests.cs` (new); `docs/b757/evidence/flight-11.md`.
- Measured points (EFB-local): Home (19,62), Aircraft (320,190), Navigation (503,190), Settings (1073,190); Aircraft menu COCKPIT PRESETS (127,178), FUEL (67,237), W&B (124,307), GROUND SERVICES (128,368); tiles GPU (1005,231), CHOCKS (404,556); Settings > AIRCRAFT (98,244), NOSEWHEEL STEERING (1154,249). Estimated from session 8's mirror (each proven by a reversible tap in Task 1 before use): REMOVE ALL (818,159), JETWAY (638,231), FWD STAIR (612,294), GROUND A/C (404,393), CARGO SVC (1003,393), LEFT AIR START (612,456), RIGHT AIR START (1027,454), FUEL TRUCK (1218,616), AFT STAIRS (637,765), CATERING (996,763), GND SRVC mode (400,927), REQ PB TUG (716,932), REMOVE TUG (911,932), arrows (736,999), (811,999), (886,999); CABIN & DOORS menu item about (119,427).
- State sources: GPU `BBS_757_GPU_AC_PWR_CONNECTED`; chocks `BBS757_vis_chocks`; services `BBS757_vis_stairsfwd`, `_stairsaft`, `_fueltruck`, `_catertruck`, `_aircon`, `_baggage1` (cargo), `_airstart` (one for both air-start tiles); doors `B757_DOORS_PAX_L1..L3/R1..R3` (0-100), `B757_DOORS_CARGO_FWD/AFT` (0-200); settings keys in `efb_settings.json` (`docs/b757/evidence/wasm-findings.md` Â§7). Never use `BBS757_vis_cabin`, `_type1_exit`, `_type2_exit` (cabin layout, not services).
- Not operable (measured or by nature): the sliders (IRS ALIGNMENT TIME and BOARDING & LOAD RATE did not move), AUTOMATIC BOARDING (ignored taps), charts, maps, live diagrams, OFP and weather documents, performance outputs, notification text, failure catalogue paging. Where the CDU or the app's own SimBrief fetch gives the content, the window offers that.
- Never tap inside the UPDATE/REMOVE guard box (x 1191-1391, y 297-377 on every page; the agent refuses); pick hit points left of x 1191. Never arm custom SAVE/LOAD (it stays armed invisibly for the next tile).
- Speech: the screen reader reads the button; the window speaks once, from the value read back; "already â€¦" taps nothing; unreadable taps nothing ("The â€¦ cannot be read, so nothing was tapped."). Background changes are announced from the variables.
- Credentials (Hoppie, SayIntentions, Navigraph tokens, the SimBrief ID number) are never shown, spoken or logged.
- Variables added: up to 9 (the proven services' rows).

## Review Focus

- A tile whose state cannot be read taps nothing (Task 2 test).
- REMOVE ALL while external power is on line is refused before any tap (Task 2 test).
- A BlueBird update that moves buttons without changing the canvas size: every run judges its result from a variable, so a moved button reads back "BlueBird did not â€¦", never a false success (Task 2 test with an unchanged read-back).
- The settings switches near the guard box (DISPLAYS BOOTUP y 310, SYNC BAROMETERS y 371) are tapped at x 1154, outside it (Task 3 test).
- The doors are announced by plan 06; this plan only reads them for its Doors page (one owner per spoken row).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/11-efb-rework.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 11`, with the `Plan: 11` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [owner present]: Measure the rest of the tablet

**Files:** `docs/b757/evidence/flight-11.md`; `Bbs757EfbLayout.cs` (points moved from "est." to measured).

- [ ] With the aircraft at a gate, engines off, APU on, MSFSBA closed: using the ND mirror and window captures (session 8's method) and the L:var snapshot tool, for each estimated tile tap once, record which variable changed, tap again to restore: FWD STAIR, AFT STAIRS, GROUND A/C, CARGO SVC, FUEL TRUCK, CATERING, LEFT and RIGHT AIR START (the values `_airstart` takes), JETWAY (find its variable, if any), GND SRVC mode (the settings key), REQ PB TUG and REMOVE TUG against stock `PUSHBACK ATTACHED` / `PUSHBACK STATE`, one arrow (tap or hold?), REMOVE ALL (then restore each).
- [ ] The CABIN & DOORS page: capture it, measure its menu item and one door tile (open, then close), which variable moves.
- [ ] Settings: the switch point of every Settings > Aircraft row (rows at y 128, 192, 249, 310, 371, 434, 492, 553, 614, 674, 735, 793, 853, 914, 975; switches near x 1154) and the General page; for each, tap twice and confirm the settings-file key flips and returns.
- [ ] Cockpit presets Cold & Dark, Turnaround and the four flight presets (433/818, 214-372 est.); Maintenance and Failures locations; the brake temperature and fire bottle reset buttons.
- [ ] Record everything in `flight-11.md`; move each proven point to measured in `Bbs757EfbLayout.cs`; commit `docs(b757): the 757 tablet measured`.

### Task 2: Ground services and doors, pure

**Files:** Create `SimConnect/BBS757/Bbs757GroundServices.cs`; test `Bbs757GroundServicesTests.cs`.

**Interfaces:**
  ```csharp
  public sealed record Bbs757GroundService(string Key, string Name, EfbPoint Tile, string StateVar, string OnWord, string OffWord);
  public static class Bbs757GroundServices
  {
      public static IReadOnlyList<Bbs757GroundService> All { get; }   // Task 1's proven tiles only
      public static string? Refusal(Bbs757GroundService s, bool wantOn, double? state);
      public static string Result(Bbs757GroundService s, bool wantOn, double? before, double? after);
      public static string? ChangePhrase(Bbs757GroundService s, double v);   // "Fuel truck called" / "Fuel truck removed"
      public static string? RemoveAllRefusal(double? extPwrOn);
      public static string DoorPhrase(string door, double position, double max);   // "Door L1 open" at â‰¥ 90 %, "closed" at â‰¤ 5 %, null between
  }
  ```
  Words per service from Task 1 (e.g. "Forward stairs attached / removed", "Fuel truck called / removed", "Catering called / removed", "Ground air conditioning connected / disconnected", "Cargo service called / removed", "Air start cart connected / disconnected").

- [ ] **Steps 1-5** (tests per service: refusals, already-there, results, change phrases; door ends; REMOVE ALL refused with EXT PWR on line); commit `feat(b757): ground services and doors, pure`.

### Task 3: Settings, pure

**Files:** Create `SimConnect/BBS757/Bbs757EfbSettings.cs`; modify `Bbs757WorkFolder.cs` (read the listed keys only, never write; never log or return credentials); test `Bbs757EfbSettingsTests.cs`.

**Interfaces:** `record Bbs757EfbSetting(string Key, string Label, EfbPoint Switch, string OnWord, string OffWord)`; `Bbs757EfbSettings.All` (Task 1's proven switches: NOSEWHEEL STEERING, DISPLAYS BOOTUP, SYNC BAROMETERS, INITIAL COCKPIT DOOR, PARKING BRAKE MODE, AUTOMATIC GPU/JETWAY, INITIAL CHOCKS IN PLACE, DIRTY PANEL STATES, HOPPIE ACARS, and the General page's); `Refusal`, `Result` (from the file read before and after, as steering is judged today). The sliders and AUTOMATIC BOARDING are listed as read-only values ("Boarding rate: Realistic (change it in the sim)").

- [ ] **Steps 1-5** (tests: every switch point is outside the guard box; refusals; results; credentials never surface); commit `feat(b757): EFB settings, pure`.

### Task 4: The reworked EFB window

**Files:** Create `SimConnect/BBS757/Bbs757EfbBridge.cs`; modify `MainForm.BBS757.cs` (which window Shift+T opens), `Forms/FBWA380/FbwEfbForm.cs` (additive only, if the spike needs it), `Forms/BBS757/BBS757EfbForm*.cs` (kept as the fallback); test `Bbs757EfbBridgeTests.cs`.

- [ ] **Step 1 (spike):** read how the PMDG, MD-11 and flyPad EFBs feed the shared WebView2 window (`FbwEfbForm` and its bridge interface; PMDG's synthetic preferences `A.SYN_PREFS` show settings the tablet has no readable control for). Confirm a 757 bridge can present a synthetic element list (headings, buttons with state, "Name: value" rows, alerts) and route each button to a C# action, with `FbwEfbForm` unchanged or changed additively. Write the finding at the top of this task. If it cannot, build the same tabs as WinForms (`TabControl`, "Label: State" buttons, read-only status `TextBox`es, [VAT-13]) in `BBS757EfbForm` instead, and say so in the handoff.
- **The window's pages** (each element's state from the variables and the settings file; each action one guarded run as plan 07's ground power buttons run, with this plan's Tasks 2 and 3):
  1. **Flight setup:** Navigraph (signed in or not, Sign in), SimBrief ID (stored or not set; the field and Set), Fetch SimBrief flight, Accept units, Keep units, Nav data (cycle, current or not; Update), a read-only plan summary from the app's own fetch (origin, destination, route, block fuel, passengers, ZFW).
  2. **Fuel and load:** tanks and total in the aircraft's units, cabin and holds, total weight, the load watch's progress; Load fuel and payload from SimBrief; boarding state; boarding rate read-only.
  3. **Ground services:** one button per service ("Fuel truck: Called"), Ground power, Chocks, Remove all, the ground service mode.
  4. **Doors:** each door's state ("Door L1: Closed / Open / Moving") and, if Task 1 proved them, open/close actions.
  5. **Pushback:** Request tug, Remove tug, Left, Straight, Right, with `PUSHBACK STATE`, only if Task 1 showed the stock pushback variables follow BlueBird's tug; else a line pointing to the app's existing pushback and GSX support.
  6. **Presets:** the preset list and Load (the ground / flight gate kept), APU quick start, Engine quick start, IRS rapid align, each read back from a signature variable ("Before start loaded: APU running, battery on"; "IRS aligned").
  7. **Settings:** each proven switch as a two-state control, changed only when it differs; read-only rows for the rest.
  8. **Maintenance:** brake temperatures and Reset, fire bottle Reset, random failures (read-only, with "Turn random failures off" if proven).
  9. **Status:** EFB link state and the last result.
- [ ] **Steps 2-6:** tests over the bridge's element model (pure: given variable and settings values, the elements and their texts; an action maps to the right guarded run; credentials never appear); run, implement, full suite (the other EFBs' tests green); commit `feat(b757): the reworked 757 EFB window`.

### Task 5: Services and doors announced

**Files:** `Aircraft/BBS757Definition.Efb.cs`; test `Bbs757GroundServicesTests.cs`.

- Rows for each proven service's state var (Continuous, `IsAnnounced`, Ctrl+M rows "Fuel Truck" â€¦), spoken through `_changes.Observe` with `ChangePhrase`; a window run's echo silenced as plan 07's ground power is (its `LastWindowRunUtc` stamp). The doors are announced by plan 06: this plan reads them for the Doors page only (one owner per spoken row).
- [ ] **Steps 1-5**; commit `feat(b757): ground services announced`.

## In-sim checks (owner)

1. Open Shift+T; call and remove the fuel truck and the forward stairs; set and remove chocks; load the Before start preset and hear its read-back; switch PARKING BRAKE MODE and back; read the Doors page.

## Handoff to plan 16

- **Pilot-facing:** "Input Shift+T still opens the 757 EFB window, which does everything that changes the aircraft, each press checked against a variable or BlueBird's settings file. A new **Read the tablet screen** button in it opens BlueBird's tablet as a page you can read with your screen reader, page by page: Home, the tablet's menus and Scroll move between pages, nothing else on it can be pressed, and it says so (*"This window only reads the tablet. Use the classic 757 EFB window to change it."*). F5 reads the tablet again, F6 says whether it is connected. When a page cannot be reached it says *"The tablet did not show <page>. Try again, or use Read the tablet again."*; when reads disagree, *"The tablet read differently each time, so this page may not be right. Use Read the tablet again."* The first time it opens, the simulator comes to the front for a moment while BA pops out the captain's ND (parked off-screen; the camera is put back)." Changelog `feature`. Check the quoted strings against `Bbs757EfbElements` and `Bbs757EfbBridge` when writing the guide; the window's title is "BlueBird 757 tablet screen" (`MainForm.BBS757.cs`).
- **Rule change:** extend B757-2 with each proven service tile on its `BBS757_vis_*` and each settings switch on its file key, and add: `- [B757-10] Never tap BlueBird's EFB without a variable or settings key that judges the result: the tablet is drawn by its WASM, so a moved button can only be caught by reading back, and a tap with no read-back (JETWAY, custom SAVE/LOAD) is not offered. The tablet screen reader (`Bbs757EfbBridge.ReadOnly`) taps page navigation only (Home, menus, scroll), judged by the page name it lands on (`Bbs757EfbBridge.PageIs`): a tap placed where OCR found a control can hit a different one, and a before-and-after variable cannot see that. Full: docs/invariants/b757.md#b757-10` (full text: docs/b757/evidence/flight-11.md section 8, the team review of 2026-10-27).
- **docs/b757.md:** the EFB section (the measured table, the classic window, the read-only tablet screen reader and why it is read-only, what is not operable and why). The reader's reliability rules, from flight-11.md section 8: a page is read only when its picture really changed (`Bbs757EfbFrameCompare`, a noise tolerance, never an exact hash), after the picture settles, from two agreeing reads when it never settles; a navigation tap is judged by the page name; reading order is `Bbs757EfbReadingOrder`, never fixed pixel bands.
- **Build output folder (frozen csproj):** `MSFSBlindAssist.csproj` now sets `AppendTargetFrameworkToOutputPath=false` and `OutputPath` `bind\$(Configuration)
et10.0-windows\`, because the OCR TFM had moved the app's output away from the folder the updater, `release.yml`, `preview.yml`, `scripts/build-release.ps1` and `tools/user-guide-publish.sh` use (a release would have zipped the updater without the app). Keep both lines while the TFM names a Windows SDK; verify with a clean Release build that the exe and `MSFSBlindAssistUpdater.exe` share that folder.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** claimed
- **Claimed by:** Declan Webster, 2026-10-26 (with Robin Kipp's agreement)
- **Worked by:** Declan Webster, with Claude (no subagents)
- **Started:** 2026-10-26, from `B757` 14944413
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** full suite passed 2026-10-27 (17,752 tests; the one failure, a test written for the old page heading, was updated and the 1,865 BlueBird tests re-run green); again 2026-10-27 after the read-only rework and the rebase onto `B757` 7e0a42ed: 17,954 passed, 0 failed
- **Tasks:** 0 done. 1 done for what screen reading needs (flight-11.md sections 5-6: services, sliders, switches,
  scrolling; presets, pushback arrows, REMOVE ALL and the Cabin & Doors door tiles were read, not tapped). 2 done
  (`Bbs757GroundServices`). 3 replaced: settings are read from the tablet's own page, see "Changed". 4 done as the
  screen-reading bridge (`Bbs757EfbBridge`, Shift+T opens the shared `FbwEfbForm`; the old window stays as "Classic
  757 EFB window" inside it). 5 done for the four proven services (CHOCKS stays the map's Status row).
- **Waiting on the owner:** the in-sim checks below at a gate with the APU on (this work ran on a runway, cold and
  dark then tablet-only); FWD STAIR (its tile did not move on a runway); JETWAY (no jetway on a runway); GND SRVC
  mode, tug and arrows; a door open/close from the Cabin & Doors page. After 2026-10-27: Shift+T opening the classic
  window, its Read the tablet screen button, and a few pages read and navigated in the read-only window in the app
  itself (the reliability numbers below are the probe's, which runs the same reader files).
- **In-sim results:** 2026-10-26/27, MSFS 2024, BlueBird 757-200 PW 0.1.0, through the app's own Shift+T window (test
  build, UI Automation): pages read and navigated (Home, Aircraft, Settings General/Aircraft/Callouts, Checklist);
  a settings switch (Radio altimeter 2500) on and off; EFB brightness mode Manual then Auto; a slider (Notifications
  fade-out timer) 10 -> 6 -> 10 SEC from its choice list, each confirmed on the tablet. The probe proved every slider,
  the four services and scrolling (flight-11.md). Not run yet: the plan's in-sim check 1 as written (at a gate).
  2026-10-27, after the team review: the probe's `tour` read 22 tablet pages 3 times each with the reworked reader;
  21 landed on the page tapped and 21 read the same every time (flight-11.md section 8, with the misreads that remain).
- **Changed from the plan:** R73 ("the window never shows BlueBird's screen") is replaced: the tablet IS read, from
  the ND pop-out's pixels with Windows OCR (flight-11.md sections 1-4), and every tablet control is offered as the
  tablet draws it, each result judged by reading the page again, or a variable where one is proven (services, GPU,
  steering per B757-2). So Task 3's settings-file reader was not needed: a switch is judged on the page it was tapped
  on. The sliders ARE operable (the plan listed them as not): tapped on their measured tracks, closed-loop. The
  app's TFM became `net10.0-windows10.0.19041.0` (main and test projects) for `Windows.Media.Ocr`: a frozen-file
  change, recorded here as a handoff. Not done: notification toasts as alerts; typing into the tablet's number
  fields (read-only for now); Navigation's dense data pages read poorly (the app's own SimBrief fetch carries that).
  **Changed again 2026-10-27 (team review):** the classic window is the 757 EFB window again (Input Shift+T), and the
  screen-reading window is a read-only "Read the tablet screen" button in it: it taps only page navigation, shows every
  other control as text with its state ("Chocks: Off"), and refuses a press out loud. The press code stays in
  `Bbs757EfbBridge`/`Bbs757EfbElements` behind `ReadOnly` (default true) for a later decision. The reader was made
  steadier from live measurements (flight-11.md section 8): no re-read of an unchanged picture, a settle before a read,
  two agreeing reads when the picture never settles, navigation judged by the page name, stable reading order. The
  build output folder was put back (Handoff, "Build output folder").
- **Rulings made while working:** "Read the tablet's pixels, overriding R73?" Yes: Declan Webster with Robin Kipp,
  2026-10-26 ("claim plan 11 and do this in full"). "Must sliders work?" Yes, proven (Robin Kipp asked).
  "Should the screen reader press things?" No, page navigation only: the team's review of the first in-app test
  (2026-10-27: Gus's reads that needed repeating and pages that changed on refresh; the reviewer's case that OCR is
  for what no variable gives us, never for deciding a press), agreed by Declan Webster.
- **Handoff to plan 16 (added):** [B757-10]'s wording should become "never tap BlueBird's EFB without reading the
  result back (the page again, or a variable)"; chocks announcing stays the map's (plan 16); docs/b757.md's EFB
  section should describe the pop-out (Shift+T parks the captain's ND pop-out off-screen while the window is open).
