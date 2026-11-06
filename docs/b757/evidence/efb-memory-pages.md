# The 757 tablet's pages in memory (plan 20, Task 1)

Measured 2026-11-06, MSFS 2024, BlueBird 757 build of 2026-10-31 (BBS752.wasm 29,765,919 bytes, SHA-256 357292c9…),
BBS Nest running, MSFSBA closed. Read-only memory reads through `Bbs757EfbMemory` with the layout of
`Bbs757EfbMemoryLiveTests`; navigation taps (Home, home icons, side-menu entries, the Maintenance HYDRAULIC tab) through
`docs/b757/tools/live/efb-tap.sh`; nothing else tapped.

## Fixtures

`tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb-memory/<name>.tree.json`, written by
`Bbs757EfbMemoryLiveTests.Captures_the_tablet_as_a_tree_fixture` through `Bbs757EfbTreeScrub` (the SimBrief ID, the
two ACARS codes and every ACARS dashboard value made up; checked by grep for the real flight's words before commit).

| Fixture | Page | Content containers and distinctive names |
|---|---|---|
| 01-home | Home | `main_canvas`, `main_grid_7x3`, `aircraft_btn` … `settings_btn` |
| 10-aircraft-cockpit-presets | Aircraft, Cockpit presets | `canvas_aircraft`, `presets_label`, `quick_start_label` |
| 11-aircraft-fuel | Aircraft, Fuel | `canvas_aircraft`, `fuel_*` (`fuel_left_tank`, `fuel_slider_left`, `fuel_left_qty_box`, `total_fuel_*`) |
| 12-aircraft-weight-balance | Aircraft, Weight & balance | `aircraft_weight_balance_card`, `wb_*` |
| 13-aircraft-ground-services | Aircraft, Ground services | `canvas_aircraft`, `gnd_service_mode_box`, `btn_*_toggle`, `chocks_service_box` |
| 14-aircraft-cabin-doors | Aircraft, Cabin & doors | `canvas_aircraft`, `btn_l1_door` …, `l1` … `aft`, `cabin_lighting_section` |
| 15-aircraft-maintenance | Aircraft, Maintenance (LANDING GEAR tab) | `maintenance_layout_grid`, `maintenance_landing_gear_panel` |
| 15b-aircraft-maintenance-hydraulic | the same, HYDRAULIC tab | `maintenance_layout_grid`, the hydraulic panel |
| 16-aircraft-failures | Aircraft, Failures | `failures_canvas`, `failure_tab_N`, `failure_group_N`, `failure_name_N` / `_state_N` / `_action_N` / `_setup_N` |
| 20-navigation-acars-dashboard | Navigation, ACARS dashboard | `planning_acars_dashboard_canvas`, `planning_acars_*_title`, `*_label_N` / `*_value_N`, `planning_acars_route_scrollbox` |
| 21-navigation-operating-fp | Navigation, Operating FP | `planning_operating_fp_canvas`, `planning_operating_fp_scrollbox` > `planning_operating_fp_value` (25,761 px tall, a class the layout does not map) |
| 22-navigation-weather | Navigation, Weather & NOTAM | `planning_weather_notam_canvas`, `planning_weather_primary_tab_button_0..3`, `planning_weather_primary_scrollbox` |
| 23-navigation-charts | Navigation, Charts | `planning_charts_canvas`, `planning_charts_source_button_0..2`, `planning_charts_type_button_0..4`, `planning_charts_dropdown` |
| 24-navigation-map | Navigation, Map | `map_canvas`, `map_provider_canvas`, `map_provider_dropdown` |
| 25-navigation-perf-calc | Navigation, PERF calc | `planning_perf_calc_canvas`, `planning_perf_calc_tab_button_0..1`, `planning_perf_calc_unit_button_0..2`, `*_input`, `*_dropdown` |
| 30-systems | Systems | `header_layer`, `selected_page_title_label`, `pb_menu1` … |
| 40-checklist | Checklist | `checklist_main_grid`, `checklist_scrollbox` |
| 50-settings-general | Settings, General | `general_scrollbox`, `general_stackbox`, `*_setting_grid` |
| 51-settings-aircraft | Settings, Aircraft | `aircraft_scrollbox` (966 tall) > `aircraft_stackbox` (1,180 tall) |
| 52-settings-livery-options | Settings, Livery options | `aircraft_stackbox` (no scrollbox: the name is not the section's), `weight_units_setting_grid` |
| 53-settings-callouts | Settings, Callouts | `callouts_scrollbox` |
| 54-settings-cabin-audio | Settings, Cabin audio | `cabin_audio_scrollbox` |
| 55-settings-airac-data | Settings, AIRAC data | `airac_data_grid`, `simbrief_id_setting_grid`, `latest_cycle_row`, `waiting_panel_box` |

`13b-aircraft-ground-services-chocks-removed` (the owner's go-ahead, 2026-11-06): CHOCKS tapped twice. In place,
`chocks_status_image` reads `./data/efb/images/bbs_ldg_gr_chocks.png`; removed, `./data/efb/images/bbs_ldg_gr.png`; the
page read back identical after the second tap.

**The QWERTY keyboard's state is not readable from its tree.** The shell app (map key 2, class "app") holds `kb_grid`
[0,648 1397x400] with its own visible byte set on every page captured, before and after a tap on PERF calc's Airport
box (the owner's go-ahead) and before and after `btn_keyboard_down` [1228,968 164x75]; neither the grid's nor its app's
0x400 bytes changed, but for a clock double at app +0x20. The OCR reader of plans 17 and 18 read the page beneath
(Ground services' tiles at y 744 to 917), so the keyboard is normally hidden while its tree reads visible. Its keys:
`char_btn_<lower>|<upper>|<third>` 100x75 in rows of 10, 9 and 7, `btn_delete`, `btn_backspace`, `btn_tab`,
`btn_enter`, `btn_double_shift`, `btn_shift2`, `btn_shift`, `,|,|!`, `.|.|?`, `btn_home`, `btn_numeric_switch2`,
`btn_space`, `btn_numeric_switch`, `btn_keyboard_down`. A tap on a key while the keyboard is hidden would land on the
page under it, so nothing taps it until its shown state is found (plan 22). Number fields (the SimBrief ID, the TOD
pause distance) open a separate numeric keypad with an OK button (`efb-and-manual.md`, `efb-settings.md`).

## Scrolling: rectangles never move; the offset is the scrollbox's own

Settings, Aircraft (`aircraft_scrollbox` [250,82 1147x966] over `aircraft_stackbox` 1,180 tall):

- **The wheel does not scroll this build reliably.** The agent's `wheel(800,600,±300)` six times and `+2000` three
  times: no control's rectangle and no byte of the scrollbox or stackbox (0x400 each) changed between the last reads;
  one earlier run had left the offset at 98.24.
- **A drag scrolls**, about 1:1: mouse down at (500,600), 20 moves 50 ms apart to (500,500), 400 ms held, up: the
  offset went 0 → 97.75; another 50 px → 145.63; another 50 px → 175.19 and no further (the end). Down drags return it
  to 0. Started on the rows' label area (x 500), the drags changed no setting (the tree before and after identical).
- **The offset:** a float at scrollbox +0x158, 0 at the top, 175.19 at the end of this page; +0x188 is 82 plus it.
  +0x134 held 214 (the stackbox's 1,180 less the scrollbox's 966) while scrolled and 0 at the top. Every control's
  rectangle stays in content coordinates: on the tablet a control inside the scrollbox is drawn at its rectangle's y
  less the offset. At the end of this page the last row (Dirty panel states, y 1202-1262) is drawn at 1027-1087: its
  top 21 px show above the viewport's bottom (1048), its center does not.
- **So a tap from a rectangle is right only at offset 0.** A page the pilot or BlueBird scrolled moves every control
  inside the scrollbox up by the offset; plan 20 reads the offset and subtracts it (Task 8b).

## BBOS classes

BBS752.wasm's RTTI names 25 classes: `bbos_control`, `bbos_container`, `bbos_control_grid`, `_graph`, `_image`,
`_label`, `_layer`, `_stack`, `_button`, `_canvas`, `_slider`, `_toggle`, `_listbox`, `_textbox`, `_viewbox`, `_colorbox`,
`_progress`, `_scrollbox`, `_gradientbox`, `_pulldownbox`, `_range_graph`, `_pan_indicator`, `_multiline_textbox`,
`bbos_app`, `bbos_page`. The layout maps `button`, `label`, `toggle`, `slider`, `grid` and the seven app classes;
everything else reads as kind "other", with a label's or button's text when it has one.
