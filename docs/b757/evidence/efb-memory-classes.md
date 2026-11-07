# The 757 tablet's text boxes, drop-downs, selection, status bar, notifications and questions in memory (plan 21, Task 1)

Measured 2026-11-07, about 02:20Z to 03:20Z, MSFS 2024, BlueBird 757 build of 2026-10-31 (BBS752.wasm 29,765,919 bytes,
SHA-256 357292c9…), parked at the gate, MSFSBA closed. Read-only memory reads with a scratch memory probe; taps at
rectangles read from memory: navigation, side menus, tabs and
the notifications arrow. With the owner's go-ahead, each undone: Fuel's left quantity box (the fuel input dialog, then
CANCEL), PERF calc's runway drop-down (opened, then its current entry), the AFT cargo door (opened, closed), Automatic
boarding off and on again. `efb_settings.json` read back equal to its copy from before the first tap. No snapshot was
kept and no credential read: the AIRAC page's boxes were read as lengths only.

Offsets are from the object's start (its vptr). Addresses are wasm, hex. "std::string" is the 12-byte Emscripten
libc++ string `ModuleEfbMemory.ReadStdString` reads. A heap object's address (BBOS, an app, a control) is that session's
only: the reader reaches each through the layout's roots every reading, never by an address.

## Classes

| Kind | Type name | Runtime vptr | How found |
|---|---|---|---|
| text box | `N3bbs20bbos_control_textboxE` | 0x30dec | live; RTTI in the file |
| multi-line text box | `N3bbs30bbos_control_multiline_textboxE` | 0x305e4 | live; RTTI |
| drop-down | `N3bbs24bbos_control_pulldownboxE` | 0x307a0 | live; RTTI |
| image | `N3bbs18bbos_control_imageE` | 0x303ac | live; RTTI |
| top bar app | `N3bbs11app_overlayE` | 0x28c84 | live (app map key 1); RTTI |
| dialog app | `N3bbs15app_modaldialogE` | 0x27df0 | live (app map key 3); RTTI |
| list box | `N3bbs20bbos_control_listboxE` | 0x304c4 | RTTI only: no live instance |

Also seen live, already read as other kinds or "other": stack 0x30d60, canvas 0x30fa0, colorbox 0x3102c, gradientbox
0x301f4, grid 0x30320; an image's pan indicator (`N3bbs26bbos_control_pan_indicatorE`, vptr not taken). Dialog pages
(base `bbos_page`): `fuel_change_confirm_dialog` 0x28338, `add_fm_preset_dialog` 0x27e80, `add_fm_preset_input_dialog`
0x28044, `app_modal_error_dialog` 0x2821c, `reposition_status_dialog` 0x28500, `fuel_input_dialog` 0x28628,
`navigation_unit_mismatch_dialog` 0x28820, `power_off_confirm_dialog` 0x28abc.

## Text box: +0x128 points at its text label

- **+0x128 is a pointer** to a `bbos_control_label` named `text_label`; the shown text is that label's text (label
  +0x128, as any label's). The label's parent (+0xe8) is 0 and it is in no children vector, and the box's own children
  vector (+0xf4) is empty, so a tree walk never reaches it. Its visible byte is 1; its rectangle is the box's inset 1 px.
- The label is often allocated right after the box (4 of 6 PERF calc boxes) but not always: read the pointer.
- The box itself holds a name (+0x1c) and a GUID (+0x28), no text.
- 10 of 10 boxes had one: PERF calc's airport "KPHX" (agreeing with "METAR: KPHX"), runway length "10300", weight
  "207322", wind "260/07", temperature "23", pressure "29.92"; Charts' request "KIGM" (agreeing with "KIGM109"); on AIRAC
  data `simbrief_id_textbox` (hidden), `hoppie_logon_code_textbox` and `sayintentions_acars_key_textbox`, empty. On AIRAC
  data the boxes' labels are not laid out ([0,0 170x44], [0,0 100x44]); the saved values show in separate labels
  (`simbrief_id_saved_label`, `hoppie_logon_code_value_label`, `sayintentions_acars_key_value_label`), not read.

## Multi-line text box: +0x128 is the whole text

- **+0x128 is a std::string**, the whole text with '\n' between lines (the offset of a label's text). It can exceed
  `ReadStdString`'s 4,096-character cap: the Operating FP is 91,869 characters and reads null today, so its reader needs a
  larger cap for this field.
- +0x134 is a std::vector of 16-byte line records {int start, int end, int next, float width px}; the character at
  `end` is '\n' (a hard break) or ' ' (a wrap); next is end + 1, but for the last line (next = end = the length).
- Proven on the Operating FP (`planning_operating_fp_value`): 91,869 characters, 2,225 newlines, 2,226 records, none
  inconsistent, the widest 663.158 px; and Weather (`planning_weather_primary_content`): the METAR tab 138 characters in
  4 lines (line 2 wraps at 120, on a space), the TAF tab 187 characters in 6 lines.
- Beside them: +0x1c8, +0x1cc and +0x1d0 the length again (int); +0x144 float 18.0 (the font size?); +0x104 to +0x110
  four padding floats (25 on the OFP, 18 on Weather); +0x58 an inner rectangle; the rectangle's height (+0x44) is the
  content's. Line pitch: 18 on Weather, 17.93 on the OFP (not settled; reading needs no pitch).
- Other instances: `planning_acars_route_value` (ACARS dashboard), `planning_charts_viewer_text` (Charts, hidden).

## Drop-down: a face, a list and the entries

- **+0x128 points at `main_button`**, the face: a button whose text (+0x144) is the current choice and whose rectangle
  is the drop-down's.
- **+0x130 points at `item_scrollbox`** (a scrollbox) > `item_stack` (a stack) > one `item_btn_<GUID>` button per
  entry (text at +0x144). None of them is in a children vector (the drop-down's +0xf4 is empty).
- **+0x138 is a std::vector of entry records** in 8-byte slots (pointer, padding), as a children vector. A record is
  0x28 bytes: +0x00 std::string GUID (the suffix of its `item_btn_<GUID>`), **+0x0c std::string, the entry's text**,
  +0x18 a typed data object (`perf_calc_runway_item_data`, `perf_calc_enum_item_data<…>`, `perf_calc_length_mode_item_data`,
  `chart_dropdown_data`, `map_provider_data`, `systems_pulldownbox_data`), +0x20 a pointer back to the drop-down. The
  vector's order is the stack's drawn order (6 checked), which is BlueBird's insertion order (Charts lists "AP …" before
  "AA …").
- **No index field.** On 12 drop-downs no word from +0x14c to +0x19c equals the current index, and the records carry no
  flag. The current entry is the one whose text equals the face's: runway RW07L, 0 of 6; tora FULL, 1 (ASDA, FULL, LDA,
  Shorten Length, TODA, TORA); flap Optimum, 3 (15, 20, 5, Optimum); thrust Optimum, 1 of 2; bleed Auto, 0 of 2; antiice
  Auto, 0 of 3; surface Dry, 0 of 6; flex Enabled, 1 of 2; impclimb Disabled, 0 of 2; charts "AP  AIRPORT, AIRPORT
  INFO  KIGM109", 0 of 2; map IFR Low, 0 (IFR Low, IFR High, VFR, World). The exception: Systems' `pb_menu1` always shows
  "SELECT", matching no entry (24 - ELECTRICAL, 28 - FUEL, 29 - HYDRAULICS, 36 - PNEUMATICS); the current Systems page
  is in `selected_page_title_label` ("ELECTRICAL").
- **Open: byte +0x154 is 1** (the owner's go-ahead). Tapping the runway drop-down's face [519.1,156 150x34] set +0x154
  0 → 1 (+0x155 stayed 1); nothing else in the drop-down, its list or BBOS changed (BBOS +0x1d8 stayed 0: a drop-down is
  not a dialog). Open, the entry buttons are laid out: `item_btn_*` [519.1, 189 + 38 × i, 150x38] for i = 0 to 5 (RW07L
  to RW26), in the entries' order, inside `item_scrollbox` [519.1,189 150x228]; closed, they read [0,0 50x38]. Tapping
  RW07L (the current entry) closed it: +0x154 → 0, the face RW07L, V1 149 unchanged.
- **The list's place** (closed, all 12): `item_scrollbox`'s x and width are the face's and its y is the face's bottom
  (Charts 142 + 34 → 176, Map 86 + 34 → 120, Systems 32 + 50 → 82; PERF calc one pixel higher, 156 + 34 → 189); its height
  is min(n × 38, the int at drop-down +0x194) when that int is above 0, else n × 38 (runway 6 / 380 → 228; flap 4 / 150 →
  150; surface 6 / 180 → 180; antiice 3 / 120 → 114; the two-entry ones 96 → 76; tora 6 / 0 → 228; charts 2 / 814 → 76;
  map 4 / 190 → 152; `pb_menu1` 4 / 200 → 152).
- **A list taller than its cap scrolls** inside `item_scrollbox`, whose content offset is +0x134 as any scrollbox's
  (B757-26): surface 6 × 38 = 228 > 180, flap 4 × 38 = 152 > 150. Not exercised: no drag was made in an open list.
- Other words: +0x14c an id per drop-down (0x13ee to 0x14fd); +0x148 on runway points at `tora_dropdown` (unknown);
  +0x144 the page.

## List box: none reachable

No instance on Home, any Aircraft entry (the Maintenance tabs included), any Navigation entry, Systems, Checklist, or
Settings' Aircraft, Livery options, Callouts, Cabin audio and AIRAC data (General not swept). The only users are static
strings: `presets_listbox` and `input_params_listbox` on `app_flight_model`'s `app_fm_tuning_page` ("FM TUNING"), an app
not in BBOS's app map (keys 1 to 10 and 12 present, 11 absent). Nothing to measure for a list box's entries or current one.

## Image: +0x128 is the picture's path

The path is a std::string at +0x128 (a label's text offset: the reader already reads it as the node's text):
`chocks_status_image` "./data/efb/images/bbs_ldg_gr.png" with the chocks removed (agreeing with the notification
"CHOCKS REMOVED."; plan 20 read "bbs_ldg_gr_chocks.png" with them in); `label_bbs_img` "./data/efb/bbs_logo_light.png";
the defuel question's `fuel_icon` "./data/efb/icons/bbs_icon_fuel1.png"; PERF calc's hidden
`planning_perf_calc_metar_status_icon` ".../bbs_icon_warning_round2.png". +0x144 points at `<name>_pan_indicator`.

## The selected entry: button bytes +0x140 == 1 and +0x143 == 0

- **The current side-menu entry or tab is the button whose byte +0x143 is 0, among buttons whose byte +0x140 is 1.**
  Proven by reading the same buttons before and after taps: Navigation's side menu (PERF CALC → OPERATING FP), Aircraft's
  (MAINTENANCE → FUEL; GROUND SERVICES 0 while current), Settings' (AIRCRAFT → GENERAL → AIRCRAFT; AIRAC DATA 0 on its
  page), PERF calc's tabs (TAKE OFF ↔ LANDING), Maintenance's (LANDING GEAR → HYDRAULIC → APU → LANDING GEAR), Weather's
  (METAR → TAF → METAR). In one state each, agreeing with the tablet: Charts' source MANUAL, Charts' type TAXI, Weather's
  selector DEPARTURE.
- The exact diff (`btn_general` current → not, `btn_aircraft` not → current): +0x100 int 1 / 0; +0x124 low byte 1 / 0;
  +0x130 to +0x137 a timestamp double; **+0x143 byte 0 current, 1 not**; +0x18c to +0x198 a color (0.087, 0.11, 0.14, 1)
  / (0, 0, 0, 0).
- Why +0x143 alone: +0x100 is 1 on most page buttons and does not change on tabs; +0x124 is set only when a tap made the
  entry current (MAINTENANCE, current since its page opened, read 0); the colors follow the theme (tabs: +0x17c, +0x18c,
  +0x19c (0.22, 0.22, 0.22, 1) current, (0.141, 0.271, 0.392, 1) not).
- **Why +0x140 too:** +0x143 is also 0 on two tiles that take no tap, Ground services' `chocks_service_box` and
  `ground_ac_service_box`, whose +0x140 is 0. Every other button read +0x143 = 1 (keyboard keys, YES and NO, IMPORT FROM
  SIMBRIEF, the unit buttons); hidden buttons read +0x140 = 0. The rule holds among a side menu's or a tab row's siblings
  (empirical).
- PERF calc rebuilds its tab buttons on a tab change (`tab_button_0` moved to a new object): read them fresh every time.
- **The recapture of every page (2026-11-07, plan 21 Task 5)**, read through the walk: each page with a side menu marks exactly
  one entry (Aircraft's, Navigation's and Settings' seven, six and six pages); the Checklist marks the checklist shown (BEFORE
  START) and also its RESET ALL, which sits in the side column but is no checklist, and the content's RESET; Home and Systems mark
  none; Maintenance (LANDING GEAR, HYDRAULIC), PERF calc (TAKE OFF), Weather (DEPARTURE, METAR) and Charts (MANUAL, TAXI) mark one
  tab per row, Failures' tab row none; Cockpit presets' Battery and External power buttons read selected (their meaning is not
  measured; the reader uses the state only for side menus and tab rows).

## The top bar: the overlay app, found by its class

- **The top bar is app map key 1, `app_overlay`** (vptr 0x28c84, about 0x2d0 bytes). Its front flag (+0x140) is 0, so
  the reader's front-app walk skips it: it is found by its class, not by its front flag.
- Its roots (+0x17c): `reset_points_button` (hidden) and `overlay_grid` [0,0 1397x32], holding `time_label` (a label)
  [15,0 1382x32] "Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C" (a std::string copy also at app +0x1a8);
  `keyboard_locked_label` [0,0 100x100], visible and empty (its static text is "KEYBOARD LOCKED"); `etd_separator_label`
  [333,0 24x32] and `etd_label` [343,0 120x32], hidden and empty (the departure countdown, presumably; unproven).
- **The boarding control, the notifications button, the list and the pop-up are not controls**: the overlay draws them
  from its own fields (their words are static strings: FUEL, CARGO, PAX, LOADED!, BOARDED!, " MIN", START BOARDING,
  BOARDING..., DEP, " ETA ", Notifications, Clear all, " - LIVE", " - FINAL", X).
- Its fields, opening and closing the list by tapping (1373,15) twice (rectangles are x, y, w, h floats): +0x74/+0x78
  the last mouse position; +0x1b4 and +0x1c8 empty std::strings (stale "ETA 0000Z" and "01:33": the ETA and countdown
  texts? unproven); +0x1e0 a double near 1.79e9 (an epoch? unproven); +0x1f8 float 0 closed, 1.0 open; +0x1fc and +0x1fd
  bytes 0, 0 closed, 1, 1 open; +0x208 int 49, the newest id; +0x20c and +0x210 ints 2 and 1 (unknown); +0x224 a rectangle,
  [1328,3 55x26] with unread notifications (the pill and arrow), [1365,6 18x20] the arrow alone; +0x234 the list panel's
  rectangle [1039.75,38 349.25x0], its height (+0x240) 480 open; +0x244 "Clear all" 0 closed, [1313,46 64x18] open;
  +0x254 the arrow [1365,6 18x20]; +0x264 the scrollbar track 0 closed, [1371,70 8x438] open; +0x274 its thumb 0 closed,
  [1371,70 8x48.69] open (about 47 rows of 84 px); +0x284 a std::vector of 16-byte rectangles, each shown row's X button
  [1334, 77 + 84 × i, 22x22] for i = 0 to 5 (empty closed: the stale ones kept); +0x290 a std::vector of ints, the shown
  rows' ids from the top, 46, 45, 44, 43, 42, 41 (empty closed); +0x2ac int 0 closed, 42 open (unknown); +0x2b5 byte 0
  closed, 1 open; +0x2c4 a std::string `{"active":false,"top":0.0,"bottom":0.0}` closed,
  `{"active":true,"top":0.0,"bottom":521.0}` open.

## Notifications: a vector in BBOS at +0x9334

- **The records are a std::vector (begin, end, capacity) at BBOS + 0x9334**, 0x3c bytes a record:
  **+0x00 int id** (1 to 49, rising; 13 and 14 absent); **+0x04 std::string title** (empty but for the autobrake ones:
  "AUTOBRAKE 1", "BRAKE THERMAL EFFICIENCY"); **+0x10 std::string text** ("GPU CONNECTED.", "TARGET 4.5 | ACTUAL 4.5
  ft/s2"); **+0x1c std::string time** ("21:37Z"); +0x28 std::string icon path (".../bbs_icon_door.png"); **+0x34 byte
  unread** (1 on ids 41 to 49 before the list was opened, 0 after); +0x36 byte 0x0d, or 0x0b on DEFUELING INITIATED. and
  DEFUELING IS COMPLETE! (unknown); +0x38 int 0. 47 records, capacity 64.
- **Where it is.** The measurement read the vector at 0x29ac24 and took it for a static address: it is not. The file's
  stack pointer starts at 0x240000, the top of its static memory, so 0x29ac24 is heap, and it is that session's BBOS
  object (0x2918f0) + 0x9334. The file says so too (found 2026-11-07, plan 21 Task 3): of the two functions that write
  `bbos`'s vtable, the constructor initializes the vector at this + 0x9334 and the words up to +0x936c and the
  destructor frees it, and a getter returns this + 0x9334 (`i32.const 0x9334` is in 12 functions). To check it from BBS752.wasm
  with the private repo's toolkit (`MSFSBlindAssist.WasmToolkit`): `TryGetClassVptr("N3bbs4bbosE")` gives 0x2fb18, so bbos's
  vtable is 0x2fb10, and `WasmCodeQueries.FunctionsWithI32Const(module, 0x2fb10)` gives functions 65360 (the constructor: it
  holds `i32.const 0x9334` and `i32.const 0x238`, the app map, and stores to +0x1d8 and +0x936c) and 65871 (the destructor:
  0x9334 and 0x238, no such stores); decoded (`WasmDecoder.TryDecode`), function 56508 adds `i32.const 0x9334` (37684) to its
  argument and returns it. Search the code for `i32.const 0x9334` for the other users. The stack top is the module's first
  global, its mutable i32 stack pointer, whose initializer in the global section is `i32.const 0x240000` (the private repo's
  b757-memory notes read the same; the data segments end at 0xec6d0). The words read beside the vector are
  its neighbors in BBOS: +0x9340 46 (the last index), +0x9344 49 (the last id), +0x9348 2 (?), +0x9350 40, then 46 after
  the list was opened (?), +0x9368 2 (?), +0x936c 47 (the count). No other pointer to the records' buffer was found.
  `bbs::notification` (vptr 0x1d1f4) is a different object.
- **A new notification is a new record** (the owner's go-ahead): opening the AFT cargo door (Cabin & doors'
  `btn_aft_cargo_door` [873.5,717 100x40]) added id 50 "AFT CARGO DOOR OPENED." 03:15Z, `bbs_icon_door.png`, unread
  (+0x34 = 1); closing it, id 51 "AFT CARGO DOOR CLOSED." 03:16Z. The overlay's newest id (+0x208) went 49 → 50 → 51.
  The pop-up: overlay +0x1f8 float 0 → 1.0, byte +0x1fc 0 → 1 (+0x1fd, the full list, stayed 0), +0x200 a double (its
  start time?), the panel height (+0x240) 0 → 118 (the list's is 480), +0x224 the unread pill [1328,3 55x26]; +0x2b0
  47 → 48, +0x2c8 39 → 40. The pop-up was gone within about 10 s (BlueBird's `notification_popback_seconds` is 10).
  Speaking new notifications needs only the records: a new id is a new notification.
- **Order:** with the list open at its top, the top rows were ids 46 to 41, not 49; ids 47 to 49 were timed 00:58Z and
  01:16Z, so the list looks sorted by time of day (the newest at the bottom after Zulu midnight). Unproven.
- Opening the list marked 41 to 49 read (BlueBird's own behavior).
- While a door moves, its button reads "MOVING" (BlueBird ignores taps then); the window read that row as
  `text  AFT CARGO`, though a sighted pilot sees MOVING. The cause (plan 21, Task 5, reproduced on the committed Cabin & doors
  tree with the button's text set to MOVING): MOVING was none of `Bbs757EfbScreen.StateWords`, so the button did not wait for a
  name (R5) and kept its bare words, `button  Moving`, in the doors' tree order, while its label AFT CARGO, naming nothing, read
  as a line of its own after the door buttons. MOVING is a state word since: the row reads `button  AFT cargo: Moving`, and the
  button keeps the key it has closed.

## Questions and dialogs: BBOS byte +0x1d8

- **A dialog is up exactly while BBOS's byte +0x1d8 is 1**; then the dialog app's current page (+0x144) is it. Proven
  with the fuel input dialog (the owner's go-ahead): Fuel's left quantity box (`fuel_left_qty_box` [377,780 150x34], a
  button) opens `fuel_input_dialog` in the dialog app: `dialog_grid` [354.5,236 688x192] > `content_grid` >
  `fuel_icon` (an image), `form_grid` > `fuel_input_prompt_label` "Insert desired fuel level (0-2176 Gal)",
  `fuel_input_grid` > `fuel_input_textbox_box` > `fuel_input_textbox` (a text box), `fuel_input_unit_label` "Gal",
  `fuel_input_error_label` (empty, not laid out); `dialog_grid` > `btn_grid` > `cancel_btn` "CANCEL" [354.5,380 140x48],
  `insert_btn` "INSERT" [862.5,380 180x48]. The byte read 1 while it was up and 0 after CANCEL, over two open and
  cancel cycles, and 0 throughout with no dialog (side menus, page changes, a text box tapped). It is not the keyboard:
  with the dialog up and the keyboard app taking no tap (its last mouse stayed 0,0), it stayed 1. While it is 1 a
  side-menu tap is blocked (the page stayed Fuel) and the dialog app records the taps (its last mouse followed them);
  while it is 0, taps reach the page. +0x1d9 read 1 throughout. CANCEL closed it with no change (Fuel read the same).
- **The dialog app's own fields do not tell.** Opening the dialog set the app's front flag (+0x140) 15 → 18 and +0x13c
  0 → 15 (the previous front value), and replaced its page (+0x144) with the new dialog's. Closing it changed nothing in
  the app but its clock and its last mouse (+0x74/+0x78, the CANCEL tap): the front flag stayed 18, the page stayed the
  closed dialog's, whose age kept running. Reopened, BlueBird reused the same page object, whose first 256 bytes read
  the same open and closed, but for its age.
- **A stale dialog stays behind.** At the start the dialog app (key 3, front flag 15) held `fuel_change_confirm_dialog`
  ("Current fuel exceeds the flight plan fuel.\nAre you sure you want to defuel?", YES/NO), every visible byte set, yet
  it had been answered YES about 7 hours before: a page's +0x20 double is its age in seconds (a fresh Navigation page
  read 2.87 s), and this page's read 26,519 s, the keyboard page's (made at BBOS start) 26,780 s: made about 262 s
  after BBOS start, about 19:36Z, with "DEFUELING INITIATED." at 19:41Z and "DEFUELING IS COMPLETE!" at 19:42Z; the
  dialog app's last mouse (424,619) was YES's center, unchanged over about 30 taps while the other apps followed them;
  taps inside its `main_grid` reached the page beneath. BBOS +0x1d8 read 0. So the front flag, the page and its visible
  bytes would speak a question that is not on the screen: only +0x1d8 tells.
- **A question's words and answers** (the stale defuel dialog's layout, read only): `main_grid` [0,32 1397x1016] >
  `colorbox1` [318.5,415 760x250] > `dialog_grid` [334.5,431 728x218] > `message_grid` [334.5,431 728x158] >
  `fuel_icon` (an image) [350.5,478 64x64], `text_label` (a label) [435.5,436 622x148], the words; `dialog_grid` >
  `btn_grid` [334.5,589 728x60] > `yes_btn` [334.5,589 180x60] "YES", `no_btn` [882.5,589 180x60] "NO" (both +0x143 = 1).
  The page's name is a std::string at page +0x64; its rectangle at page +0x28 [0,32 1397x1016]; its children vector
  at page +0x4c; page +0x70 read 15. The app's roots hold only a hidden `reset_points_button`. Static layouts of the
  others: the warning dialog (`warning_icon`, `text_label`; `btn_grid` > `yes_btn`, `no_btn`, `ok_btn` "OK"), the
  power-off one (`dialog_grid` > `text_label` "Power off the EFB?"; `btn_grid` > `yes_btn`, `no_btn`). So the words are
  `text_label`'s and the answers `btn_grid`'s buttons.
- Tapping a page's text box (PERF calc's airport) did not bring the keyboard up (the keyboard app took no tap after
  it); whether the keyboard shows stays plan 22's.

## START BOARDING and DEP: not measured

Automatic boarding off (Settings, Aircraft) changed nothing in the overlay but its clocks: no START BOARDING in this
post-flight state (it likely needs a fresh OFP; an OFP fetch was not among the approved changes). Turned back on. The
departure countdown and the boarding control stay unmeasured: the layout has no field for them.

## What the layout carries (plan 21, Task 2)

| Field | Value | What it holds |
|---|---|---|
| `TextboxLabel` | 0x128 | a text box's pointer to its text label |
| `MultilineText` | 0x128 | a multi-line text box's whole text (std::string) |
| `PulldownFace` | 0x128 | a drop-down's pointer to its face button |
| `PulldownList` | 0x130 | a drop-down's pointer to its list's scrollbox |
| `PulldownEntries` | 0x138 | a drop-down's vector of entry-record pointers (8-byte slots) |
| `PulldownEntryText` | 0x0c | an entry record's text (std::string) |
| `PulldownOpen` | 0x154 | a drop-down's byte, 1 while its list is open |
| `ButtonSelectable`, `ButtonUnselected` | 0x140, 0x143 | a button's bytes: current = 1 and 0 |
| `BbosModalOpen` | 0x1d8 | BBOS's byte, 1 while a dialog is up |
| `BbosNotices` | 0x9334 | BBOS's vector of notification records |
| `NoticeStride` | 0x3c | a notification record's size |
| `NoticeId`, `NoticeTitle`, `NoticeText`, `NoticeTime`, `NoticeUnread` | 0x00, 0x04, 0x10, 0x1c, 0x34 | a record's id (int), title, text, time (std::strings), unread byte |

The class kinds `textbox`, `multiline`, `pulldown`, `image`, `overlay` (the top bar's app) and `modal` (the dialog app)
join the class list, derived by RTTI.

## Not settled

The open list's scrolling (no drag in an open list was made); an open list under a scrolled page (no drop-down read sits
in a scrollbox: the reader takes the page's scroll off the list and its rows as for any control inside a scrollbox, logic
only); the toast, START BOARDING and the departure countdown;
the notification list's order; overlay +0x2ac, +0x20c and +0x210, record +0x36, BBOS +0x9348 and +0x9368; the OFP's
line pitch; list boxes (none reachable); Settings, General's class sweep.
