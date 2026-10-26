# BlueBird 757 EFB Rework Implementation Plan (plan 11, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** As much of BlueBird's tablet as can be operated and verified, in a reworked accessible EFB window at parity with the PMDG, MD-11 and flyPad EFB windows: flight setup, fuel and load, every ground service, doors, pushback, cockpit presets and quick starts, settings, maintenance resets, status.

**Architecture:** BlueBird's tablet is drawn by its WASM: no text can be read from it. The app taps it at measured points through the existing EFB client and agent (`Coherent.call("WASM_CLICK" …)` on the tablet's own canvas handlers, proven) and judges each result from something it can read: a BlueBird L:var (`BBS757_vis_*`, `BBS_757_GPU_*`, doors, tanks and stations, APU and buses), BlueBird's settings file (`efb_settings.json`, which changes at once), or the CDU. Every toggle is read before it is tapped ([B757-2]'s pattern). The window shows the app's own model of that state, never BlueBird's screen (R73).

**Tech Stack:** C# 13 / .NET 10 Windows Forms (WebView2 through the shared EFB window, or WinForms tabs as the fallback), xUnit, ES5 agent.

**Spec:** `docs/b757/spec-gaps.md` sections 4 (R70, R73), 7; `docs/b757.md` "The 757 EFB window"; rules B757-2, B757-4; `docs/b757/evidence/live-findings.md` sessions 5-8 (measured points, the ND mirror technique); plan 07 (ground power, which this plan's window keeps).

## Global Constraints

- Owned files (from plan 07): `SimConnect/BBS757/Bbs757EfbLayout.cs`, `CoherentBbs757EfbClient.cs`, `Bbs757WorkFolder.cs`, `Bbs757Loadsheet.cs`, `Bbs757LoadWatch.cs`, `Bbs757GroundPower.cs`, `Forms/BBS757/BBS757EfbForm*.cs`, `Resources/coherent-bbs757-efb-agent.js`, `Aircraft/BBS757Definition.Efb.cs`, `Aircraft/BBS757Definition.LoadWatch.cs`, `MainForm.BBS757.cs`; new `SimConnect/BBS757/Bbs757GroundServices.cs`, `Bbs757EfbSettings.cs`, `Bbs757EfbBridge.cs`; additive changes only to `Forms/FBWA380/FbwEfbForm.cs` (the shared EFB window; the PMDG, MD-11 and flyPad EFB tests must stay green); tests `Bbs757EfbTests.cs`, `Bbs757LoadWatchTests.cs`, `Bbs757LoadsheetTests.cs`, `Bbs757GroundPowerTests.cs`, `Bbs757GroundServicesTests.cs`, `Bbs757EfbSettingsTests.cs`, `Bbs757EfbBridgeTests.cs` (new); `docs/b757/evidence/flight-11.md`.
- Measured points (EFB-local): Home (19,62), Aircraft (320,190), Navigation (503,190), Settings (1073,190); Aircraft menu COCKPIT PRESETS (127,178), FUEL (67,237), W&B (124,307), GROUND SERVICES (128,368); tiles GPU (1005,231), CHOCKS (404,556); Settings > AIRCRAFT (98,244), NOSEWHEEL STEERING (1154,249). Estimated from session 8's mirror (each proven by a reversible tap in Task 1 before use): REMOVE ALL (818,159), JETWAY (638,231), FWD STAIR (612,294), GROUND A/C (404,393), CARGO SVC (1003,393), LEFT AIR START (612,456), RIGHT AIR START (1027,454), FUEL TRUCK (1218,616), AFT STAIRS (637,765), CATERING (996,763), GND SRVC mode (400,927), REQ PB TUG (716,932), REMOVE TUG (911,932), arrows (736,999), (811,999), (886,999); CABIN & DOORS menu item about (119,427).
- State sources: GPU `BBS_757_GPU_AC_PWR_CONNECTED`; chocks `BBS757_vis_chocks`; services `BBS757_vis_stairsfwd`, `_stairsaft`, `_fueltruck`, `_catertruck`, `_aircon`, `_baggage1` (cargo), `_airstart` (one for both air-start tiles); doors `B757_DOORS_PAX_L1..L3/R1..R3` (0-100), `B757_DOORS_CARGO_FWD/AFT` (0-200); settings keys in `efb_settings.json` (`docs/b757/evidence/wasm-findings.md` §7). Never use `BBS757_vis_cabin`, `_type1_exit`, `_type2_exit` (cabin layout, not services).
- Not operable (measured or by nature): the sliders (IRS ALIGNMENT TIME and BOARDING & LOAD RATE did not move), AUTOMATIC BOARDING (ignored taps), charts, maps, live diagrams, OFP and weather documents, performance outputs, notification text, failure catalogue paging. Where the CDU or the app's own SimBrief fetch gives the content, the window offers that.
- Never tap inside the UPDATE/REMOVE guard box (x 1191-1391, y 297-377 on every page; the agent refuses); pick hit points left of x 1191. Never arm custom SAVE/LOAD (it stays armed invisibly for the next tile).
- Speech: the screen reader reads the button; the window speaks once, from the value read back; "already …" taps nothing; unreadable taps nothing ("The … cannot be read, so nothing was tapped."). Background changes are announced from the variables.
- Credentials (Hoppie, SayIntentions, Navigraph tokens, the SimBrief ID number) are never shown, spoken or logged.
- Variables added: up to 9 (the proven services' rows).

## Review Focus

- A tile whose state cannot be read taps nothing (Task 2 test).
- REMOVE ALL while external power is on line is refused before any tap (Task 2 test).
- A BlueBird update that moves buttons without changing the canvas size: every run judges its result from a variable, so a moved button reads back "BlueBird did not …", never a false success (Task 2 test with an unchanged read-back).
- The settings switches near the guard box (DISPLAYS BOOTUP y 310, SYNC BAROMETERS y 371) are tapped at x 1154, outside it (Task 3 test).
- The doors are announced by plan 06; this plan only reads them for its Doors page (one owner per spoken row).

---

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
      public static string DoorPhrase(string door, double position, double max);   // "Door L1 open" at ≥ 90 %, "closed" at ≤ 5 %, null between
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

- Rows for each proven service's state var (Continuous, `IsAnnounced`, Ctrl+M rows "Fuel Truck" …), spoken through `_changes.Observe` with `ChangePhrase`; a window run's echo silenced as plan 07's ground power is (its `LastWindowRunUtc` stamp). The doors are announced by plan 06: this plan reads them for the Doors page only (one owner per spoken row).
- [ ] **Steps 1-5**; commit `feat(b757): ground services announced`.

## In-sim checks (owner)

1. Open Shift+T; call and remove the fuel truck and the forward stairs; set and remove chocks; load the Before start preset and hear its read-back; switch PARKING BRAKE MODE and back; read the Doors page.

## Handoff to plan 16

- **Pilot-facing:** "The 757 EFB window is rebuilt like the other aircraft's EFB windows: flight setup, fuel and load, every ground service with its state, doors, pushback (if proven), presets and quick starts with their result, BlueBird's settings, maintenance resets." Changelog `feature`.
- **Rule change:** extend B757-2 with each proven service tile on its `BBS757_vis_*` and each settings switch on its file key, and add: `- [B757-10] Never tap BlueBird's EFB without a variable or settings key that judges the result: the tablet is drawn by its WASM, so a moved button can only be caught by reading back, and a tap with no read-back (JETWAY, custom SAVE/LOAD) is not offered. Full: docs/invariants/b757.md#b757-10`
- **docs/b757.md:** the EFB section (the measured table, the bridge or the tabs, what is not operable and why).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Claim the plan by filling in **Claimed by** and
setting **Status** to claimed, and push that one change before starting; if the push stops on a conflict in these
lines, someone claimed it first. Fill in the rest as you go and when the plan lands (index, "Records").

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
