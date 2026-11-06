# BlueBird 757 EFB: One Page Grammar, Presses That Scroll, No OCR Left (plan 20, added 2026-11-06)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** Every tablet page the memory reader already reaches reads by one set of rules: its section in the title, every value with its label, every control named by the tablet's words and pressable wherever it sits on the page; the OCR leftovers ("Read the tablet again", the scroll buttons, PaddleOCR and its model release) are gone.

**Architecture:** `Bbs757EfbModelReader` becomes a page grammar over BlueBird's control names (R1-R13 below) with one small exceptions table, `Bbs757EfbPageRules`; `Bbs757EfbElements` lays the page out content first, a "Sections" heading and Home last, names in sentence case; `Bbs757EfbPressRunner` scrolls a control into view (and closes BlueBird's keyboard over it) before it taps, sets two-choice switches and sliders from the slider's own rectangle. Every page's live memory tree becomes a test fixture with a golden listing of the window's elements.

**Tech Stack:** C# 13 / .NET 10 Windows Forms (WebView2 through `FbwEfbForm`), xUnit, the ES5 EFB agent (version 8 adds `drag`, Task 8b), the private repo's layout toolkit (Task 8b), jsdom for the shell (only if Task 13 needs the focus opt-in).

**Spec:** the owner's design of 2026-11-06, approved section by section, kept locally (`docs/superpowers/specs/2026-11-06-b757-efb-accessibility-design.md`, git-ignored by the owner's choice); this plan is its phase 1 and copies every decision and value a task needs. Phases 2 and 3 are plans 21 and 22. Evidence: `docs/b757/evidence/efb-memory-pages.md` (Task 1 writes it). Background: `docs/b757.md` "The 757 EFB window"; `docs/wasm-memory.md`; rules B757-17 to B757-21, WASM-1, WASM-2, CORE-7.

## Global Constraints

- **The owner's decisions (2026-11-06):** D1 content first, then a "Sections" level 2 heading with the side menu (current entry "(current page)") and Home last. D2 the SimBrief ID, Hoppie logon code and SayIntentions ACARS key show as the tablet shows them, never logged. D3 every label, heading and control name in sentence case, abbreviations kept (`Bbs757EfbVocabulary.Pretty`); values and data exactly as the tablet writes them. D4 a two-choice switch is a choice list of its two catalog words; picking the other word taps the switch once. D5 one page grammar from BlueBird's names, a short exceptions table, no per-page builders, no layout-only grouping. D7 PaddleOCR goes entirely and the `text-recognition-1` release and its tag are deleted (only the B757 testers downloaded it; nothing on main points at it, checked 2026-11-06).
- **Rulings kept:** nothing refuses a press (B757-19); every press re-reads memory and relocates (B757-18); Settings presses judged by BlueBird's file (B757-20); the colors theme stays a note (B757-21); the gear door buttons keep BlueBird's words with their label first ("Nose door: Open", 2026-10-30); a two-choice setting says its new choice once (2026-10-30); checklist lines are not read (2026-11-06).
- **Owned files:** `SimConnect/BBS757/Bbs757Efb*.cs` (new: `Bbs757EfbPageRules.cs`), `MainForm.BBS757.cs`, the text recognition lines of `MainForm.cs`, `MainForm.MenuHandlers.cs`, `Forms/Settings/SettingsForm.cs`, `Settings/UserSettings.cs` (one property), `MSFSBlindAssist.csproj` (one package), `Services/TextRecognition/**`, `Forms/TextRecognitionOfferDialog.cs`, `Forms/Settings/TextRecognitionPanel.cs`, additive changes to `Forms/FBWA380/FbwEfbForm.cs` and `tools/flypad-shell-test/` (Task 13 only), `tools/Bbs757EfbProbe/**`, `SimConnect/BBS757/CoherentBbs757EfbClient.cs`, `Resources/coherent-bbs757-efb-agent.js`, `MSFSBlindAssist.DisplayLayouts/Bbs757/**` and the private repo's `Bbs757EfbRecipe` with its tests (Task 8b), the tests of these files, `.claude/rules/b757.md`, `.claude/rules/text-recognition.md`, `docs/invariants/b757.md`, `docs/invariants/text-recognition.md`, `docs/text-recognition.md` and its CLAUDE.md row (the code they guard is deleted or changed here, and `ClaudeContextBudgetTests` fails otherwise; plans 17, 18 and the memory reader edited them the same way). Handed to plan 16: `docs/b757.md`, `guide/**`, `HotkeyGuides/BBS757_Hotkeys.txt`, `changelog.d/**`.
- **House rules:** CORE-7; CORE-16 (Read a file before changing it); A380C-6 (`SafeBeginInvoke`); CORE-14/15 (`Log.*` only, never a field's value); US spelling; every spoken or formatted text tested under de-DE and sv-SE (`TestCulture.Run`); test names keep `Bbs757`; no test reads `docs/b757/`; no SimConnect variable added; one new memory field, the scrollbox's offset, and only through the layout file the generator writes (Task 8b, WASM-2).
- **Fixtures:** a tree fixture is written only through `Bbs757EfbTreeScrub` (Task 1): the three credentials and the flight plan data (flight number, callsign, route, dates, times, OFP values) replaced with the made-up values it defines. A fixture holding a real one is never committed.
- **The simulator:** navigation taps (Home, home icons, side-menu entries, tabs) and wheel steps change nothing on the aircraft and are the controller's, as in the 2026-11-06 survey. Any other tap needs the owner's go-ahead in chat first; the step says so.
- **Workflow:** `00-index.md`'s workflow and shared conventions; every commit ends with the trailers `Plan: 20` and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; build `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (a fresh worktree builds `plugins/MSFSBlindAssist.VPilotPlugin` once first); tests `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --artifacts-path <worktree>/bin/b757-p20 --filter "FullyQualifiedName~<name>"`, the full suite without the filter.

## Review Focus

- **A control below the visible part of the page** (Settings > Aircraft's last row while the page sits at its top): dragged into view, read again, tapped where it is drawn now (its rectangle less the scroll offset); never a tap at a point outside the viewport (Task 9 test `A_control_below_the_viewport_is_scrolled_into_view_before_its_tap`).
- **A control a drag never brings into view** (the scroll offset stops changing at the page's end): no tap, "<name> could not be brought into view, not pressed." (Task 9 test `A_control_that_never_comes_into_view_is_not_tapped`).
- **BlueBird's keyboard up over the control:** its down key first, never a key tap (Task 9 test `A_control_under_the_keyboard_closes_it_first`).
- **Settings > Livery options lives in `aircraft_stackbox`:** it reads "Settings, Livery options", never "Settings, Aircraft" (Task 4 test `Livery_options_is_not_read_as_aircraft`).
- **A word-less button two labels touch:** no guess; it keeps its own words, or is left out when it has none (Task 6 test `A_button_two_labels_touch_keeps_its_own_words`).

---

### Task 0 [controller]: Claim plans 20 to 22 and this plan

- [ ] **Step 1: Check the plans are free.** `git fetch kn4iee`; `git ls-tree kn4iee/B757 docs/b757/plans/02-gaps/` lists no `20-*`, `21-*` or `22-*`. If one exists, stop and ask the owner.
- [ ] **Step 2: Branch and claim.** In this session's worktree (at `kn4iee/B757` e37a642c6 or later), `git branch -m B757-p20-efb-page-grammar`, rebase onto the head fetched in step 1; delete `tests/MSFSBlindAssist.Tests/ScratchEfbCrawlTests.cs` (the survey's throwaway, never committed); in this file's Execution record set Status to claimed, Claimed by, Started (today, the base commit); commit this file with `21-efb-memory-classes.md` and `22-efb-typing.md` (both unclaimed): `docs(b757): plans 20 to 22, claim plan 20`.
- [ ] **Step 3: Push the claim.** `git fetch kn4iee`, `git rebase kn4iee/B757`, `git push kn4iee HEAD:B757`; repeat on a rejected push; confirm with `git log kn4iee/B757 -1`.

### Task 1 [controller, simulator up, MSFSBA closed]: Every page's tree as a fixture

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/Bbs757EfbTreeJson.cs`, `tests/MSFSBlindAssist.Tests/Bbs757EfbTreeScrub.cs`, `tests/MSFSBlindAssist.Tests/Bbs757EfbTreeScrubTests.cs`, `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb-memory/*.tree.json`, `docs/b757/evidence/efb-memory-pages.md`
- Modify: `tests/MSFSBlindAssist.Tests/Bbs757EfbMemoryLiveTests.cs`

**Interfaces:**
- Produces: `internal static class Bbs757EfbTreeJson { string Write(IReadOnlyList<Bbs757EfbMemory.App> apps); IReadOnlyList<Bbs757EfbMemory.App> Read(string json); IReadOnlyList<Bbs757EfbMemory.App> Load(string name) }` (Load reads `Fixtures/bbs757-efb-memory/<name>.tree.json`; nodes keep kind, name, text, rectangle and children; the address is not written and reads back as 0). `internal static class Bbs757EfbTreeScrub { IReadOnlyList<Bbs757EfbMemory.App> Scrub(IReadOnlyList<Bbs757EfbMemory.App> apps) }`.

- [ ] **Step 1: Scrub tests** (`Bbs757EfbTreeScrubTests`): `The_saved_simbrief_id_becomes_the_made_up_one` (`simbrief_id_saved_label` text "123456"); `The_acars_codes_become_made_up_ones` (`hoppie_logon_code_value_label`, `sayintentions_acars_key_value_label`, and any `*_textbox` text: "------" stays "------", anything else becomes "MADEUP"); `The_flight_plan_becomes_the_made_up_flight` (every `planning_acars_*_value_*`, `planning_acars_route_value` and `planning_operating_fp_value` text replaced by the made-up flight `Bbs757EfbTreeScrub.MadeUpFlight`: flight number "MSF1", callsign "MSF1", airports "KSEA / SEA" and "KPDX / PDX", route "KSEA/16L DCT BTG DCT KPDX/28R", dates "01 Jan 26", times "12:00 UTC", every other value "0"); `Every_other_text_is_kept`.
- [ ] **Step 2:** Run them: FAIL (no scrub). Implement `Bbs757EfbTreeJson` and `Bbs757EfbTreeScrub` (by node name only, never by text). Run: PASS.
- [ ] **Step 3: The capture.** In `Bbs757EfbMemoryLiveTests` add `Captures_the_tablet_as_a_tree_fixture`: skipped unless `BBS_EFB_CAPTURE` names a file; it reads `ReadFrontApps()` as `Prints_the_live_tablet_controls` does and writes `Bbs757EfbTreeJson.Write(Bbs757EfbTreeScrub.Scrub(apps))` there.
- [ ] **Step 4: Capture** with `docs/b757/tools/live/efb-tap.sh` for the navigation and the capture test for each page, as `01-home`, `10-aircraft-cockpit-presets`, `11-aircraft-fuel`, `12-aircraft-weight-balance`, `13-aircraft-ground-services`, `14-aircraft-cabin-doors`, `15-aircraft-maintenance`, `15b-aircraft-maintenance-hydraulic` (its HYDRAULIC tab), `16-aircraft-failures`, `20-navigation-acars-dashboard`, `21-navigation-operating-fp`, `22-navigation-weather`, `23-navigation-charts`, `24-navigation-map`, `25-navigation-perf-calc`, `30-systems`, `40-checklist`, `50-settings-general`, `51-settings-aircraft`, `51b-settings-aircraft-scrolled` (after one `wheel 800 600 300`), `52-settings-livery-options`, `53-settings-callouts`, `54-settings-cabin-audio`, `55-settings-airac-data`. Side-menu entries are at x 125, y 172 + 60 n; home icons at (327,182), (513,182), (698,182), (883,182), (1069,182); Home at (25,57). Check each file holds the page it is named for (the Aircraft app reopens on its last section: tap the entry). Grep each file for a real credential or OFP word before keeping it.
- [ ] **Step 5 [owner go-ahead]: the keyboard and the chocks.** With the owner's word: tap PERF calc's Airport box (type nothing), capture `60-keyboard-up`, tap `btn_keyboard_down`; tap CHOCKS, capture `13b-aircraft-ground-services-chocks-removed` (the chocks picture's other file name), tap CHOCKS again and read it back attached.
- [ ] **Step 6: Measure the wheel.** On Settings > Aircraft: the `*_stackbox` Y before, after one, two and three `wheel 800 600 300` steps, and after three `-300` steps. Record pixels per step (or that one step moves nothing and how many do); scroll back to the top.
- [ ] **Step 7: Evidence.** `efb-memory-pages.md`: the method, each fixture's page, every content container and distinctive name per section (Task 4's table comes from it), the tab rows and their names, the wheel's pixels per step, the keyboard's rectangle and keys, the chocks file names, the BBOS class list (25 classes, spec section 2) and which the layout maps. Commit `test(b757): the tablet's memory trees as fixtures, and the scrub` with the evidence.

### Task 2: Golden listings of the window

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/Bbs757EfbListing.cs`, `tests/MSFSBlindAssist.Tests/Bbs757EfbPageListingTests.cs`, `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb-memory/*.listing.txt`

**Interfaces:**
- Consumes: `Bbs757EfbTreeJson.Load` (Task 1).
- Produces: `internal static class Bbs757EfbListing { string Of(Bbs757EfbElements.Built built) }`: line 1 `title: <page>`, then one line per element, `<role>  <words>` with role one of `alert`, `heading 2`, `heading 3`, `button`, `checkbox checked`, `checkbox not checked`, `combo [<value>] {a|b|c}`, `slider <value>`, `edit [<value>]`, `pre`, `text`; a `pre` element's lines follow indented four spaces. `Bbs757EfbPageListingTests.Every_page_reads_as_its_listing` (a Theory over every `*.tree.json`: `Bbs757EfbListing.Of(Bbs757EfbElements.Build(Bbs757EfbModelReader.Read(Load(name)), null, extras: none))` equals `<name>.listing.txt`). With `BBS_EFB_UPDATE_LISTINGS=1` the test rewrites the listings instead and passes.

- [ ] **Step 1:** Write `Bbs757EfbListing`, the Theory, and a unit test `The_listing_names_each_role` over a hand-built `Built` with one element of each role.
- [ ] **Step 2:** Run with `BBS_EFB_UPDATE_LISTINGS=1`, then without: PASS. The listings are today's output (the survey's problems included): characterization. Every later task that changes the reading reruns with the variable set and commits the listings' diff with its code; the reviewer reads the diff against that task's rules.
- [ ] **Step 3:** Commit `test(b757): every tablet page's window listing as a golden file`.

### Task 3: The model gains what the grammar needs

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbScreen.cs`, `Bbs757EfbPress.cs` (`Describe`/`Line` only), `tests/MSFSBlindAssist.Tests/Bbs757EfbModelJson.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbModelReaderTests.cs`, `Bbs757EfbPressTests.cs`

**Interfaces:**
- Produces: `Bbs757EfbKind` gains, after `Row`, `Field` (a value box: Text = its name, Value = the value), `Tab` (Selected = current), `LongText` (Text = the whole text, lines by `\n`). `Bbs757EfbItem` gains `int Level = 0` (a heading's 2 or 3), `string Label = ""` (the name the grammar gave a control whose own words are a state or action: "NOSE DOOR" for the button "OPEN"), `string Name = ""` (BlueBird's control name). `Bbs757EfbScreenModel` gains `Rectangle Viewport { get; init; } = Bbs757EfbScreen.ContentArea` (new constant `new Rectangle(250, 82, 1147, 966)`) and `Bbs757EfbKeyboard? Keyboard { get; init; }`. New records in the same file: `Bbs757EfbKeyboard(Rectangle Box, IReadOnlyList<Bbs757EfbKey> Keys)`, `Bbs757EfbKey(string Name, string Lower, string Upper, string Third, Point Hit)` (`char_btn_q|Q|1` is Lower "q", Upper "Q", Third "1"; a command key such as `btn_enter` has its name and empty layers).

- [ ] **Step 1: Tests.** `Bbs757EfbModelJson` round-trips the new fields (`A_model_with_every_new_field_reads_back_equal`); `Bbs757EfbPress.Fingerprint` differs when only an item's `Label` or `Level` differs (`A_label_change_is_a_new_page_to_push`); `PageUnchanged` ignores `Keyboard` (`The_keyboard_coming_up_is_no_page_change`).
- [ ] **Step 2:** FAIL; implement; PASS; full suite green (no listing changes).
- [ ] **Step 3:** Commit `feat(b757): the tablet model's field, tab, long text, label, level, viewport and keyboard`.

### Task 4: R1 the section, R2 the current tab

**Files:**
- Create: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbPageRules.cs`
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbModelReader.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbModelReaderTests.cs`, the listings

**Interfaces:**
- Produces: `internal static class Bbs757EfbPageRules` with `string? SectionFromTable(string app, IEnumerable<string> names)` (the table: for each app, a section's distinctive name or prefix, from Task 1's evidence; at least AIRCRAFT `fuel_` FUEL, `wb_` WEIGHT & BALANCE, `gnd_service_` GROUND SERVICES, `cabin_lighting_section` CABIN & DOORS, `maintenance_` MAINTENANCE, `failures_canvas` FAILURES, the presets page's name COCKPIT PRESETS; SETTINGS `weight_units_setting_grid` LIVERY OPTIONS; the rest as the evidence shows). The reader's section: the table's answer when it has one, else the first menu word whose folded letters equal, or start, the folded name of a content container (`*_canvas`, `*_card`, `*_grid`, `*_scrollbox`, `*_panel`, with `planning_` and the suffix removed) in tree order, else "". Tab rows: two or more sibling buttons whose names end in `_tab_button_N`, `_tab_N`, `_source_button_N`, `_type_button_N` or `_unit_button_N` become `Bbs757EfbKind.Tab` items keyed `tab:<their parent's name>|<words>`; the current tab is the one tab of the row whose folded words appear in a folded name or text elsewhere in the content (`maintenance_landing_gear_panel`, the card title "DEPARTURE WEATHER"), none when zero or two match.

- [ ] **Step 1: Tests** (synthetic trees shaped like the fixtures): `Every_aircraft_section_is_named_by_its_table_entry` (Theory over the seven), `Navigation_sections_are_named_by_their_canvas` (Theory: `planning_acars_dashboard_canvas` ACARS DASHBOARD, `planning_operating_fp_canvas` OPERATING FP, `planning_charts_canvas` CHARTS, `planning_perf_calc_canvas` PERF CALC, and the weather and map names from the evidence), `Airac_data_is_named_by_its_grid`, `Livery_options_is_not_read_as_aircraft`, `The_section_marks_its_menu_entry_current`, `A_page_matching_no_section_reads_its_app_alone` ("Systems", nothing marked), `A_tab_row_marks_the_tab_its_content_names`, `A_tab_row_with_no_match_marks_none`. Replace `A_short_section_does_not_scroll_and_livery_matches_by_prefix` (its `livery_scrollbox` is no tree BlueBird draws) with the real shape.
- [ ] **Step 2:** FAIL; implement; PASS. Rerun the listings with `BBS_EFB_UPDATE_LISTINGS=1`: every surveyed page but Systems and Checklist now names its section; check the diff.
- [ ] **Step 3:** `IsAiracPage` and `Bbs757EfbSettingsMatch.SectionFor` now see "Settings, AIRAC data" and "Settings, Livery options": add `Bbs757EfbBridgeTests.The_airac_page_from_memory_offers_the_sign_in` (the `55-settings-airac-data` tree, signed out: the sign-in extra is pushed). Full suite green.
- [ ] **Step 4:** Commit `feat(b757): every tablet page names its section, and tab rows their current tab`.

### Task 5: R3 headings, R4 value lines, R10 pictures and separators

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbModelReader.cs`, `Bbs757EfbPageRules.cs` (the picture states)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbModelReaderTests.cs`, the listings

**Interfaces:**
- Produces: name parts: a control name split on `_` and spaces, lower-case invariant. Role words, label side {label, title, name, lbl, len}; value side {value, val, saved} (and `value_label` as `value` + `label` on the value side). A label and a value pair when their parts, role words removed, are equal as sets: one `Bbs757EfbKind.Row` item, Text = the label's words, Value = the value's words, Name = the label's name. A `*_unit` partner (parts equal once `unit` is removed, or once `unit` and `qty` are) appends " " + its words to the Value. A label named `*_title` (or `*_title_label`) with no value partner is a `Heading`: Level 2 unless it sits inside a container already titled by a heading, then 3. `IsHeading`'s capitals-and-width test goes. `IsNoise` keeps only `page_title_label`, image paths and labels whose letters and digits are none ("|"). `Bbs757EfbPageRules.PictureState(string fileName) -> string?`: the chocks' two file names to "IN PLACE" and "REMOVED" (Task 1's evidence); a picture with a state joins the button in its box (`chocks_service_box`): that button's Text becomes its words plus the state ("CHOCKS IN PLACE"), its key unchanged.

- [ ] **Step 1: Tests:** `A_label_and_value_named_alike_are_one_line` (`planning_acars_flight_info_label_0` "Flight Number" + `_value_0` "4RK" → Row "Flight Number" / "4RK"); `A_title_with_a_value_is_a_line_not_a_heading` (`wb_empty_weight_title` "EMPTY" + `wb_empty_weight_value` "136940 Lbs"); `Lbl_title_and_lbl_val_pair` (`lbl_title_V1` "V1" + `lbl_val_V1` "---"); `A_name_and_value_pair` (`cabin_ceiling_name` + `cabin_ceiling_value`); `A_unit_joins_its_value` (`fuel_left_qty` "1539" + `fuel_left_qty_unit` "Gal" → Value "1539 Gal"); `A_card_title_is_a_level_2_heading` ("FLIGHT PLAN") and `A_title_inside_a_titled_card_is_level_3`; `The_route_is_no_heading` (`planning_acars_route_value` is long text, Task 6); `A_title_label_that_is_not_the_page_title_is_kept` (`gnd_service_mode_title_label` "GND SRVC Mode:"; `latest_cycle_title_label` "LATEST CYCLE:" + `latest_cycle_value_label` "2611" → one Row); `Separators_are_dropped`; `The_chocks_picture_gives_its_tile_a_state` (both file names). Values keep the tablet's case in the model.
- [ ] **Step 2:** FAIL; implement; PASS; update the listings and check the diff (ACARS dashboard, W&B, PERF calc results, AIRAC data, Ground services, Cabin lighting).
- [ ] **Step 3:** Commit `feat(b757): tablet headings, label-value lines and picture states from BlueBird's names`.

### Task 6: R5 names, R6 index rows, R7 value boxes, R8 sliders, R11 icon buttons, long text

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbModelReader.cs`, `Bbs757EfbPageRules.cs` (field names)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbModelReaderTests.cs`, the listings

**Interfaces:**
- Produces: a control whose words are none or only state or action words (`Bbs757EfbScreen.StateWords` and the new `Bbs757EfbScreen.ActionWords` = OPEN, CLOSE, CLOSED, LOGOUT, UPDATE, REMOVE, RESET, FAIL NOW, SET UP, REPAIR, REPAIR ALTN) gets `Label` from, in order: (a) the one label whose parts equal the control's once role words and the control words {btn, button, slider, toggle, box, action, setup} are removed; (b) its index row's name (R6); (c) the one label whose rectangle touches the control's (a gap of at most 12 px below, above or left, overlapping in the other axis); else no Label. R6: siblings whose names end in the same `_N` with the same stem before their last part form a row; its `*_name_N` label (else its first label) is the Label of every control and the Text of a Row whose Value is each other label ("L ENGINE FIRE" / "READY"). R7: a word-less button holding exactly one label is a `Field` (Value = that label, unit by R4, Text = its Label; Fuel's and W&B's from `Bbs757EfbPageRules.FieldName(string name) -> string?`: `fuel_*_qty_box` "QUANTITY", `fuel_*_weight_box` and `total_fuel_weight_box` "WEIGHT", `total_fuel_qty_box` "QUANTITY", `wb_*_weight_box` and `wb_*_value_box` "WEIGHT"). R8: every slider is an item (also outside settings rows): Text by R5, Value by its R4 partner or the table (`fuel_slider_<side>` ← `fuel_<side>_tank_label`, `fuel_slider_total` ← `total_fuel_perc_label`), `Slider = new(TrackLeft: X, TrackRight: X + W, KnobX: X + W × p / 100)` when Value reads as a percent p, else `KnobX = X`; settings-row sliders get the same `Slider`, which `ChoiceAsync` and `PercentAsync` need and today never get. R11: a button with no words and no Label is named from its own name's parts without `btn`/`button` when two or more remain and all are letters ("PUSHBACK LEFT"); otherwise left out. Long text: a label or `other` node with text taller than 60 px or holding a newline is `LongText` (`planning_acars_route_value`, the VNAV note).

- [ ] **Step 1: Tests:** `A_state_word_button_takes_the_label_sharing_its_name` (`maintenance_gear_door_nose_button` "OPEN" ← `maintenance_gear_door_nose_label` "NOSE DOOR"); `A_door_button_takes_the_label_touching_it` (`btn_l1_door` "CLOSED" [674,187 100x40], `l1` "LEFT FWD" [692,227 64x16]); `A_button_two_labels_touch_keeps_its_own_words`; `An_index_row_names_its_controls_and_state` (`failure_name_0` "L ENGINE FIRE", `failure_state_0` "READY", `failure_action_0` "FAIL NOW", `failure_setup_0` "SET UP"); `A_value_box_is_a_field` (`fuel_left_qty_box` > `fuel_left_qty` "1539", unit "Gal" → Field "QUANTITY" / "1539 Gal"); `A_slider_outside_a_settings_row_is_kept_with_its_value` (`cabin_ceiling_slider` "" → Slider Label "CEILING", Value "OFF"; `fuel_slider_left` → "LEFT", "70%", KnobX at 70 %); `A_settings_slider_carries_its_track` (`Slider` from its rectangle); `An_icon_button_is_named_from_its_control_name` (`btn_pushback_left` → "PUSHBACK LEFT"; `btn_x1` left out); `The_route_is_long_text`.
- [ ] **Step 2:** FAIL; implement; PASS; update the listings and check the diff (Cabin & doors, Maintenance, Failures, Fuel, W&B, Ground services' pushback, Charts' zoom, Systems).
- [ ] **Step 3: Pressing still finds them.** `Bbs757EfbPressTests`: `A_labelled_door_button_is_relocated_by_its_key` and `A_settings_choice_slider_taps_its_choice_from_the_track` (the `51-settings-aircraft` tree's BOARDING & LOAD RATE slider asked for QUICK taps `Bbs757EfbPress.ChoiceX(track, 2, 4)`; check against the 2026-10-27 measurement, x 1095 QUICK and x 945 REALISTIC at y 591, within the choice's width). Full suite green.
- [ ] **Step 4:** Commit `feat(b757): tablet controls named by their labels, rows, value boxes, every slider`.

### Task 7: R9 order, R12 the keyboard and the viewport

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbModelReader.cs`, `Bbs757EfbPageRules.cs` (orders)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbModelReaderTests.cs`, the listings

**Interfaces:**
- Produces: `Bbs757EfbPageRules.Reorder(string app, string section, IReadOnlyList<Bbs757EfbItem> items) -> IReadOnlyList<Bbs757EfbItem>`: WEIGHT & BALANCE puts the `wb_*_title_box` lines (EMPTY, ZFW, ZFW CG, BLK FUEL, TAKE-OFF, LANDING) before the side panel; FUEL reads tank by tank under level 2 headings LEFT, CENTER, RIGHT, TOTAL FUEL (from the slider titles and `total_fuel_label`), each with its slider, quantity field and weight field; every other page keeps tree order. The model's `Keyboard`: from the front app of class "app" holding a visible `kb_grid` (its rectangle; each `char_btn_*` key split on `|` into its three layers, `btn_*` command keys by name, `,|,|!` and `.|.|?` as keys); null when none is visible. The model's `Viewport`: the content `*_scrollbox`'s rectangle when there is one, else `ContentArea`; the keyboard is never an item.

- [ ] **Step 1: Tests:** `Weight_and_balance_reads_its_top_row_first`; `Fuel_reads_tank_by_tank` (the `11-aircraft-fuel` tree: the order of headings and items); `The_keyboard_is_read_when_up_and_never_an_item` (the `60-keyboard-up` tree: 30 character keys, `btn_enter`, `btn_keyboard_down`, Box y 648-1048; no item from the shell app); `No_keyboard_reads_as_null`; `The_viewport_is_the_content_scrollbox`.
- [ ] **Step 2:** FAIL; implement; PASS; update the listings; commit `feat(b757): tablet reading order, the keyboard and the viewport`.

### Task 8: The window: content first, Sections and Home last, one casing

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbElements.cs`, `Bbs757EfbPressRunner.cs` (`NotShownText` only)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbElementsTests.cs`, `Bbs757EfbBridgeTests.cs`, the listings

**Interfaces:**
- Produces: `Bbs757EfbElements.Build` order: alert; question and answers; content items; the app's extras; when `TopBar` or `Notices` hold anything, "Status bar" (level 2, key `h:status`) with the top bar, then "Notifications" (level 2) with each notice; "Sections" (level 2, key `h:sections`) with the menu entries; Home (key `btn:HOME`, a button, unless on the home screen). Gone: `ReadAgainText`, the "More" heading, `ActionKind.Refresh`, `ActionKind.ScrollDown`, `ActionKind.ScrollUp`. Rendering: Heading at its `Level` (2 when 0); Row "<label>: <value>" with one colon (a label ending in ":" is not given another); Button `Label.Length > 0 ? Pretty(Label) + ": " + Pretty(Text) : Pretty(Text)`; Tab as a button, "(current)" after the current one's words; Toggle: when `Bbs757EfbSettingsMatch.SettingFor` gives a setting whose `ChoicesFor` is two words other than Off and On, a `select` of those words valued by `ChoiceIndex(setting, Value)` (an unread word: a check box as today); otherwise a check box named `Pretty(Text)`; Slider as today, named `Pretty(Label or Text)`; Field as a Row; LongText as `controlType "pre"` with its text. `Pretty` on names and headings only; Value, Field values, LongText and the extras never. `NotShownText(page)` = "The tablet did not show " + page + ". Try again, or press F5 to read the tablet again."

- [ ] **Step 1: Tests:** `Content_comes_first_then_sections_then_home`; `Home_is_last_and_absent_on_the_home_screen`; `No_read_again_more_or_scroll_elements`; `Names_are_sentence_case_and_values_as_written` (a Row "SEATTLE-TACOMA INTL", a Toggle "LOCK KEYBOARD TO EFB" → "Lock keyboard to EFB", a Tile "GPU CONNECTED" → "GPU connected"); `A_labelled_button_reads_label_then_words` ("Nose door: Open", "Left fwd: Closed"); `A_two_choice_switch_is_a_choice_list` ("Nosewheel steering" with Realistic and Rudder, value Rudder); `The_current_tab_says_current`; `A_label_ending_in_a_colon_gets_no_second`; `Long_text_is_a_pre_block`; every spoken text under sv-SE and de-DE. Update the bridge tests that pressed `btn:refresh` or the scroll buttons (the refresh is F5's `get_display_elements`).
- [ ] **Step 2:** FAIL; implement; PASS; update the listings and read the whole diff (every page changes); full suite green.
- [ ] **Step 3:** Commit `feat(b757): the tablet window reads content first, names in sentence case, no OCR-era buttons`.

### Task 8b (added 2026-11-06 from Task 1's measurement): BlueBird's scroll offset and drag scrolling

Task 1 found that a control's rectangle never moves when its page scrolls: BlueBird keeps the scroll as a float at the scrollbox's +0x158 (0 at the top), draws every control inside the scrollbox at its rectangle's y less that offset, and in this build scrolls on a drag but not reliably on the wheel (`docs/b757/evidence/efb-memory-pages.md`, "Scrolling"). A tap from a rectangle is right only at offset 0, so the offset is read, and the page is scrolled by dragging.

**Files:**
- Modify: `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757EfbMemLayout.cs`, `Bbs757LayoutDescriptions.cs`, `Bbs757Module.cs`; `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbMemory.cs`; `MSFSBlindAssist/Resources/coherent-bbs757-efb-agent.js`; `MSFSBlindAssist/SimConnect/BBS757/CoherentBbs757EfbClient.cs`; `tools/Bbs757EfbProbe/Program.cs` (its `wheel` command becomes `drag x y0 y1`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757LayoutFileTests.cs`, `Bbs757EfbMemoryTests.cs`, `Bbs757EfbMemoryLiveTests.cs` (its `Layout` gains the two fields), the agent's own checks if any
- Private repo (worktree `C:\Claude\MSFSBA-wasm-displays\.claude\worktrees\b757-displays`, branch `feature/b757-displays`, local commits only): `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757EfbRecipe.cs`, `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757EfbRecipeTests.cs`, `Bbs757EfbFixture.cs`

**Interfaces:**
- Produces: `Bbs757EfbMemLayout` gains `int ScrollboxOffset` (0x158 in this build) after `CtrlButtonText`, and `ClassVptrs` the kind `scrollbox` (`N3bbs22bbos_control_scrollboxE`'s vptr); `Equals`/`GetHashCode` and the layout file carry them; a file without them reads as missing ([WASM-26]). The walk (`Bbs757EfbMemory.ReadFrontApps`) reports DRAWN positions: a node of kind `scrollbox` reads its offset (a float, 0 when unreadable or not finite, clamped to 0..100,000) into a new `Node` field `float Scroll = 0`, and every node under it has `Y` less the sum of its scrollbox ancestors' offsets. The agent (version 8) gains `drag(x, y0, y1)`: mouse move and down at (x, y0), 20 moves 50 ms apart to (x, y1), held 400 ms, up; refused (`'busy'`) while a click run is going; returns `'started'`; `wheel` is removed. `CoherentBbs757EfbClient.DragAsync(int x, int y0, int y1, CancellationToken ct) -> Task<bool>` calls it and waits 1,500 ms before returning true; `WheelAsync` is removed.

- [ ] **Step 1: App tests:** `The_efb_layout_with_the_scroll_offset_round_trips`; `A_layout_without_the_scroll_offset_reads_as_missing`; `A_scrolled_scrollbox_moves_its_controls_up_by_its_offset` (fake memory: a scrollbox with offset 175.19 over a row at y 1202: the row reads y 1026.81; a control outside the scrollbox is unmoved); `A_nested_scrollbox_adds_its_offset`; `An_unreadable_offset_reads_as_zero`.
- [ ] **Step 2:** FAIL; implement the layout record, the file format and the walk; PASS. Update `Bbs757EfbMemoryLiveTests.Layout` with the scrollbox vptr (from Step 3) and 0x158.
- [ ] **Step 3: The recipe** (private repo; merge the app branch in first: `git fetch C:/Claude/msfs-blind-assist B757-p20-efb-page-grammar` then `git merge -X theirs FETCH_HEAD`): `TryGetClassVptr("N3bbs22bbos_control_scrollboxE")` gives the `scrollbox` kind; `ScrollboxOffset = 0x158` a validated constant beside `CtrlButtonText`; tests `The_recipe_finds_the_scrollbox_vptr` and `The_recipe_carries_the_scrollbox_offset`; the toolkit suite green; regenerate the bbs757 layout from the installed BBS752.wasm and copy it to `%APPDATA%\MSFSBlindAssist\layouts\bbs757\` with the PowerShell tool. Commit there, locally: `feat(bbs757): the EFB scrollbox and its scroll offset` (trailers as here).
- [ ] **Step 4: The agent and client.** `drag` and `DragAsync` as above; `wheel`, `WheelAsync` and their callers go (the bridge's `Wheel` seam becomes `Drag` in Task 9; until then the bridge's scroll buttons call nothing that is removed: Task 8 already removed them). Version comment line `// 8: drag(x, y0, y1) replaces wheel: this build scrolls on a drag (plan 20, Task 1)`.
- [ ] **Step 5 [controller, simulator up]:** with the regenerated layout, `51b-settings-aircraft-scrolled` captured after `Bbs757EfbProbe drag 500 600 500` (offset about 98): its rows read about 98 px higher than in `51-settings-aircraft`; a drag back to the top. Commit the app side: `feat(b757): read BlueBird's scroll offset and scroll the tablet by dragging`.

### Task 9: Presses that scroll, the keyboard, two-choice switches, sliders

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbPressRunner.cs`, `Bbs757EfbBridge.cs`, `Bbs757EfbPress.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbPressTests.cs`, `Bbs757EfbBridgeTests.cs`

**Interfaces:**
- Consumes: the model's `Viewport`, `Keyboard` (Task 3/7), `Slider` (Task 6); drawn positions and `CoherentBbs757EfbClient.DragAsync` (Task 8b).
- Produces: `public const int MaxScrollSteps = 8` in `Bbs757EfbPressRunner`; `Bbs757EfbPress.TapPoint(Bbs757EfbScreenModel m, Bbs757EfbItem item) -> Point?`: the middle of the part of the item's drawn `Box` inside `Viewport` and above `Keyboard.Box.Top` when there is a keyboard, or null when that part is under 12 px tall (the last row of Settings, Aircraft at the end of its scroll shows 21 px: tapped there); `Bbs757EfbPress.DragFor(Bbs757EfbScreenModel m, Bbs757EfbItem item) -> (int X, int Y0, int Y1)?`: at x = `Viewport.X + 250` (the rows' label area, measured inert), from y0 to y1 inside the viewport (and above the keyboard) so that y0 − y1 moves the item's drawn middle to the viewport's middle, at most `Viewport.Height − 200` in one drag (a longer way takes more drags), null when the item is in view; `Bbs757EfbPress.NotInViewText(string name) => name + " could not be brought into view, not pressed."`. `Bbs757EfbPressIo.Wheel` becomes `required Func<int, int, int, CancellationToken, Task<bool>> Drag` (the bridge's, over `DragAsync`). `LocateAsync` gains the step between finding the control and returning it: while `TapPoint` is null and steps < 8 (keyboard first: when the keyboard covers the item and the press is no typing, tap its `btn_keyboard_down` key and read again), drag `DragFor`, read, relocate; a drag that left the item where it was ends the loop (the page's end); still null: `NotInViewText`, no tap. Every tap goes to `TapPoint`, not `Hit`. The bridge's `SetAsync` for a Toggle with a `select` value: the wanted word's index among `ChoicesFor`; equal to the read one (`ChoiceIndex(setting, item.Value)`): nothing; else `SwitchAsync(pressed, wantOn: true)`: a two-choice row's `On` is null, so `NeedsTap` taps it once, and its file check (the choice not set before the tap) and its one-time new-choice read-back apply unchanged. `PercentAsync`'s check: the relocated slider's Value as a percent within `PercentSlack`, or, when its Value is no percent, a Value different from before; else `CouldNotSetText`. A Tab press is a `ClickAsync` whose outcome the existing `ButtonOutcome` says ("Hydraulic (current)" once). The scroll elements' handling and `ScrollAsync` go.

- [ ] **Step 1: Tests** (fakes as the file's): `A_control_below_the_viewport_is_scrolled_into_view_before_its_tap`; `A_control_above_the_viewport_scrolls_up`; `A_control_that_never_comes_into_view_is_not_tapped`; `A_control_under_the_keyboard_closes_it_first`; `A_control_in_view_taps_without_a_drag`; `A_partly_shown_last_row_is_tapped_in_its_shown_part`; `A_long_way_takes_several_drags`; `A_two_choice_switch_set_to_its_other_word_taps_once`; `A_two_choice_switch_set_to_its_shown_word_taps_nothing`; `A_slider_percent_is_judged_by_its_shown_value`; `A_tab_press_says_its_new_words_once`; texts under sv-SE and de-DE.
- [ ] **Step 2:** FAIL; implement; PASS; full suite green.
- [ ] **Step 3:** Commit `feat(b757): tablet presses scroll their control into view and close the keyboard over it`.

### Task 10: PaddleOCR, entirely

**Files:**
- Delete: `MSFSBlindAssist/Services/TextRecognition/` (all), `MSFSBlindAssist/Forms/TextRecognitionOfferDialog.cs`, `MSFSBlindAssist/Forms/Settings/TextRecognitionPanel.cs`, `tests/MSFSBlindAssist.Tests/TextRecognition/` (all), `.claude/rules/text-recognition.md`, `docs/text-recognition.md`
- Modify: `MSFSBlindAssist/MainForm.cs` (the downloader field, its Speak wiring, the startup offer, the exit check's text recognition part, its shutdown), `MainForm.BBS757.cs` (`OfferTextRecognition`, `ShowTextRecognitionOffer`, the menu-pick offer, the using), `MainForm.MenuHandlers.cs` (the argument), `Forms/Settings/SettingsForm.cs` (the panel and parameter), `Settings/UserSettings.cs` (`OfferTextRecognitionFor757`), `MSFSBlindAssist.csproj` (the `RapidOcrNet` reference and its comment), `tools/Bbs757EfbProbe/Bbs757EfbProbe.csproj` (any compile of `Services/TextRecognition`), `.claude/rules/b757.md` (the mirrored OCR block), `docs/invariants/text-recognition.md` (every `## OCR-n` heading renamed `## OCR-n (retired: PaddleOCR removed, plan 20)`, bodies kept), `CLAUDE.md` (the text-recognition row), `tests/MSFSBlindAssist.Tests/Walkthroughs/DocRegions.cs` (its text recognition region, if any)

- [ ] **Step 1:** Confirm nothing outside these files uses them: `git grep -n -i -E "TextRecognition|PaddleOcr|RapidOcr|OfferTextRecognition"` lists only these files. Confirm `SettingsManager` reads a settings file that still holds `OfferTextRecognitionFor757` (unknown keys ignored): add `UserSettingsTests.A_settings_file_with_a_retired_key_still_loads` (in the existing settings test file) if no test covers it.
- [ ] **Step 2:** Delete and edit as listed; build; full suite green, `ClaudeContextBudgetTests` and `ClaudeRulesHookTests` included (follow a budget failure's own instruction).
- [ ] **Step 3:** `git grep -n -i -E "TextRecognition|PaddleOcr|RapidOcr|text-recognition|\[OCR-[0-9]"` lists only `docs/invariants/text-recognition.md` and `docs/b757/` history. Commit `feat(b757): remove PaddleOCR, its download, offer and settings tab (nothing reads with it)`.

### Task 11: The other OCR-era leftovers

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbVocabulary.cs`, `Bbs757EfbElements.cs` and `Bbs757EfbBridge.cs` (comments), `docs/b757/tools/live/efb-tap.sh` (its stale header line)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbTests.cs` (the vocabulary tests), the `.model.json` fixtures

- [ ] **Step 1:** Delete `CredentialLabels`, `CredentialLabel`, `IsCredential`, their private lists and tests (D2). Delete `Snap`, `Nearest`, `Distance`, `IsKnown`, `Labels`, `Notes`, `Values` and their tests only if `git grep` finds no production caller (the scrub of Task 1 must not depend on them); keep `Pretty`, `Apps`, `Menus`, the acronyms.
- [ ] **Step 2:** Remove the OCR wording from comments ("a label OCR misread", "the OCR reader keyed it", the bridge's summary's "No capture, no OCR, no mirror"). Delete each `Fixtures/bbs757-efb/**/*.model.json` (and its `.png`) no test loads any more; keep those `Bbs757EfbExtrasTests`, `Bbs757EfbSettingsMatchTests`, `Bbs757EfbTopBarTests` still use.
- [ ] **Step 3:** Full suite green; commit `chore(b757): drop the OCR-era vocabulary, credential filter, comments and fixtures`.

### Task 12: Rules

**Files:**
- Modify: `.claude/rules/b757.md`, `docs/invariants/b757.md`

- [ ] **Step 1:** Rewrite B757-18's line and full text: the press re-reads memory, relocates, brings the control into view (`Bbs757EfbPress.InView`, the keyboard down first) and taps it, or nothing; never a tap from a stale read or at a point outside the viewport. Add, each under 400 characters with its full text: **B757-22** "Name a tablet element from BlueBird's structure only (`Bbs757EfbModelReader`'s pairing by name parts, index rows, the one touching label) or `Bbs757EfbPageRules`; never from capitals, width or words guessed; a control two labels touch keeps its own words." **B757-23** "BlueBird's on-screen keyboard (`Bbs757EfbScreenModel.Keyboard`) is never a window element; only the press runner taps its keys." **B757-24** "A tablet tree fixture is written only through `Bbs757EfbTreeScrub`: the three credentials and every flight plan value made up; never commit a real one."
- [ ] **Step 2:** `ClaudeContextBudgetTests` and `ClaudeRulesHookTests` green; commit `docs(b757): the tablet's press, naming, keyboard and fixture rules`.

### Task 13 [controller, simulator up]: Live checks, the deploy, the owner's ears

- [ ] **Step 1: Listings live.** Recapture every Task 1 page (scrubbed) and compare its listing with the committed one: only live values may differ. Fix and commit what differs.
- [ ] **Step 2: Presses the controller may make.** Navigation through every app and section (each lands, the title names it); a tab on Maintenance (HYDRAULIC, then LANDING GEAR: "(current)" moves, said once).
- [ ] **Step 3 [owner go-ahead]:** with the owner's word: Settings > Aircraft's last row (Dirty panel states) pressed from the top of the page and back (scrolled into view, tapped, the file follows); a two-choice switch (EHSI up reference HDG → TRK → HDG); BOARDING & LOAD RATE to Quick and back; the cabin ceiling light slider to 50 and back to 0. Restore and compare `efb_settings.json` with its copy from before (credentials by hash).
- [ ] **Step 4: Deploy.** `Get-Process MSFSBlindAssist` shows none; read `(Get-Item C:\MSFSBA-B757\MSFSBlindAssist.dll).VersionInfo.ProductVersion`; build Release; `robocopy` the Release output to `C:\MSFSBA-B757`.
- [ ] **Step 5 [owner]:** the in-sim checks below, one per message.
- [ ] **Step 6 (only if check 2 fails): the title focus opt-in.** `Bbs757EfbElements.Build` sets `Data["focusTitleOnPageChange"] = "true"`; `FbwEfbForm` passes it in the render payload; the shell, on a page change, focuses `#pgttl` only when the payload says so; a jsdom test in `tools/flypad-shell-test/efb-shell.test.js` (`npm test` there) for both cases; the other EFBs send nothing and render as before. Redeploy; check 2 again.

## In-sim checks (owner, by ear)

The listings prove the words; these are only what needs the owner's ears.

1. Fuel and Cabin & doors read through: the title, then the content; H moves through the headings; "Sections" lists the side menu with the current page marked, Home last. How it sounds, and whether anything is in the way.
2. A side-menu entry pressed: NVDA lands on the new page's title (else Task 13 step 6).
3. Settings, Aircraft: a two-choice switch's choice list, and the last row pressed from the top of the page (after Task 13 step 3 proved it lands).

## Handoff to plan 16

- **Pilot-facing:** "The 757 tablet window reads every page with its section and every value with its label, names every button by the tablet's words, scrolls by itself to the control you press, and offers two-choice settings as a choice list. The Read the tablet again and scroll buttons are gone: F5 reads the tablet again. The text recognition download and its settings tab are gone." Changelog `improvement`; `HotkeyGuides/BBS757_Hotkeys.txt` and the guide's 757 EFB part (no "Read the tablet again", no scroll buttons, no text recognition download).
- **docs/b757.md:** "The 757 EFB window" rewritten: the memory reader, the page grammar (R1-R13 and `Bbs757EfbPageRules`), presses that scroll, the keyboard; the text recognition paragraphs removed.
- **CLAUDE.md, CORE-7's list of today's cases:** a tab's new words once; "<name> could not be brought into view, not pressed."
- **Testers:** `%APPDATA%\MSFSBlindAssist\text-recognition` (about 32 MB) is no longer used and can be deleted.

## After landing [controller, at the owner's word]

- Delete the model release and its tag (decision D7): `gh release delete text-recognition-1 --repo oasis1701/msfs-blind-assist --cleanup-tag --yes`; `gh release view text-recognition-1 --repo oasis1701/msfs-blind-assist` then fails. A refusal (permission) goes to an admin, said in the Execution record.

## Execution record

- **Status:** claimed
- **Claimed by:** Robin Kipp (robin24), 2026-11-06
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-11-06, from `B757` e37a642c6
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
