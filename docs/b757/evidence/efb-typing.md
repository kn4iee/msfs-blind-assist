# Typing into the 757 tablet (plan 22, Task 1)

Measured 2026-11-07, 16:58 to 17:42 local, MSFS 2024, BlueBird 757 parked at EDDF, MSFSBA closed. Memory was read only.
Taps were made with the owner's go-ahead ("the 757 stays loaded in the sim for you to test against"): navigation, PERF
calc's Airport box and the Charts request box with BlueBird's keyboard while typing, Enter, the keyboard's down key, and the
fuel and Weight & balance value dialogs typed into and left with CANCEL; each change was put back. The tablet was seen
through the agent's mirror on the left ND (`efb-and-manual.md`, "Seeing it"). Offsets are from the object's start;
addresses are this session's only.

## The keyboard app says whether it is up

`kb_grid` reads visible on every page and never changes (plan 20, `efb-memory-pages.md`). The state is in the keyboard
app: BBOS app map key 2, class `app_keyboard` (vptr 0x26f18).

| Field | Type | Down | Up |
|---|---|---|---|
| +0x1ac | byte | 0 | 1 from the field's tap until `btn_keyboard_down` |
| +0x1ec | pointer | 0 | the text box being typed into |
| +0x1a8 | float | 1.0 | eases to 0 (the slide), about ×0.6 per 62 ms tick |
| +0x1ad (= +0x1f9) | byte | 0 | 1 once settled, 1.12 to 1.16 s after the tap; 0 at once on down |
| +0x201 | byte | 1 once fully down, about 1.1 s after down | 0 |
| +0x1ae | byte | | 1: the numeric keypad instead of the QWERTY keyboard |
| +0x1d8, +0x1dc, +0x1e0, +0x1e4 | floats | | the keypad's x, y, w, h |
| +0x1e8, +0x1e9, +0x1ea | bytes | | numeric layer, shift, caps lock |

- Proven by two up and down cycles each way (a diff stable within each state), a 10 ms poll, and the mirror showing the
  keyboard up and then gone.
- While the keyboard is up, the top bar's `keyboard_locked_label` reads "KEYBOARD LOCKED" (LOCK KEYBOARD TO EFB was on)
  and the top bar app's byte +0x199 is 1.
- **Wait for +0x1ad.** A key tapped 100, 300 or 700 ms after the field's tap landed on the page beneath (PERF calc's
  runway drawing): the box lost focus and the keyboard slid back down. At 1,300 ms the key typed.

## Text boxes

| Offset | Type | Meaning |
|---|---|---|
| +0x181 | byte | 1 while the keyboard types into it |
| +0x1ac | int | the caret (+0x1b0 and +0x1b4 always equal it) |
| +0x1b8 | float | the font size |
| +0x1bc | int | the maximum length |
| +0x1cc | int | 1: it opens the numeric keypad; 0: the QWERTY keyboard |

+0x184 is the caret the last tap chose and +0x18c the last tap's x.

- **A tap puts the caret at the nearest character boundary**, the text centered in the box. On "EDDF" in
  [289.7,156 150x34]: x 295 gives 0, 350 gives 1, 360 gives 2, 370 gives 3, 380 and over 4. So a tap neither selects the
  text nor always appends.
- **A typed character goes in at the caret, then the caret goes to the end.** "EDF" with the caret at 2, then `d`:
  "EDDF", caret 4.
- **A full box drops the key.** Backspace deletes before the caret; `btn_delete` deletes after it. So the exact way to
  clear: read the caret c, then c backspaces and (length − c) deletes.
- **Boxes filter what they take:** Airport takes letters only and upper-cases them (`k` types "K"; `1` is dropped); the
  Charts request box keeps case, takes digits and drops `-`, `@` and `?`; the fuel dialog's box takes digits only. A
  read-back that differs from the typed text is normal.
- Maximum lengths: Airport 4, runway length 5, Charts 4, the fuel dialog 8, TOD 3, SimBrief 8, Hoppie 24,
  SayIntentions 64.

## Layers

- Each character key's int +0x150 is the layer it shows: 0 lower, 1 upper, 2 third. The keys' texts ("q|Q|1") never
  change.
- `btn_shift` and `btn_shift2` are a one-shot shift: the next character key or backspace clears it, a second tap turns it
  off. `btn_double_shift` is caps lock (it sets +0x1e9 and +0x1ea) and holds until tapped again. `btn_numeric_switch` and
  `btn_numeric_switch2` toggle the third layer, which holds; numeric and shift cancel each other.
- Proven in text on Charts: backspace twice, SHIFT, d, f gave "EDDf"; backspace twice, CAPS, d, f, CAPS gave "EDDF";
  backspace, NUM, 1 gave "EDD1".
- The third layer, from the key names: row 1 the digits 1 to 0; row 2 `@ # $ & * ( ) ' "`; row 3 `% - + = / ; :`; `!` on
  `,` and `?` on `.`.

## Pace

Tapping is not paced: the agent's click runs the tablet's handlers as it is made. Keyboard: runs of 10 taps, each built so a
lost or doubled tap shows in the result ("EDDF", backspace four times, k, s, e, a, backspace, t: "KSET"; two others),
three runs each at 300, 200, 150, 100, 50, 20 and 0 ms: all 21 exact. Keypad: runs of 11 taps at 150, 50 and 0 ms: all
exact.

## Enter and leaving

- `btn_enter` on the Airport box takes the tap but types nothing and commits nothing that shows; the keyboard stays up
  (with the box full and with 3 characters).
- PERF calc looks the airport up on every change: the runway length box empties at "EDD" and fills again at "EDDF";
  "EDDM" fetches EDDM's METAR.
- `btn_keyboard_down` keeps the edited text.
- The Charts request box does not react while it is typed into.

## The numeric keypad

- Drawn by the keyboard app with no controls in any tree; its labels are static strings ("7 8 9 4 5 6 1 2 3 - 0 . BKSP
  DEL OK"). Shown at once, with no slide, for a box whose +0x1cc is 1.
- Layout: a 50 px strip, then the rows `7 8 9`, `4 5 6`, `1 2 3`, `- 0 .`, `BKSP DEL OK`. Key centers: x = x0 + 60 +
  120 × column, y = y0 + 87.5 + 75 × row, from its rectangle (keyboard app +0x1d8): under the fuel dialog [595.5,348
  360x430].
- BKSP deletes one character; DEL clears the whole text (from the caret at 0 and at 3); a tap on the box moves the caret;
  there is no cancel key.

## Value boxes

- `fuel_left_qty_box` and `wb_pax_business_weight_box` open `fuel_input_dialog` ("Insert desired fuel level (0-2176
  Gal)", "Insert desired business cabin weight (0-2960 Lbs)"). Its `fuel_input_textbox` is pre-filled with the current
  value and focused, with the keypad up at once.
- CANCEL closes the dialog and the keypad in one tap, with no change (fuel 2176, 5784, 2176, 10136 Gal and the Weight &
  balance listing read as before).

## The account rows (Settings, AIRAC data; read only, no text read)

- **SimBrief:** `simbrief_id_setting_grid` [250,82 1147x70]: `simbrief_id_input_cell` holds `simbrief_id_textbox`
  (hidden while an ID is saved; [763.5,95 170x44], keypad, max 8) and `simbrief_id_saved_label` [763.5,104 170x26];
  `simbrief_id_action_cell` holds `simbrief_id_status_label` (hidden) and `simbrief_id_logout_button` [1197,87 200x60].
  No save button.
- **Hoppie:** `hoppie_logon_code_textbox` [897,165 300x44] (QWERTY, max 24) and `hoppie_logon_code_value_label`; no save
  button.
- **SayIntentions:** `sayintentions_acars_key_textbox` [737,235 300x44] (QWERTY, max 64),
  `sayintentions_fetch_key_button` [1047,235 140x44] and `sayintentions_acars_key_value_label`.
- The ACARS dashboard holds a hidden `planning_acars_simbrief_id_textbox` (keypad, max 8).

## Side effects of the session

- BlueBird rewrote `efb_settings.json` three times on its own (17:25, 17:29, 17:37): `charts_manual_icao` became EDDF,
  the value its memory already held before the session (the file had KIGM from before), and `display_brightness` follows
  the automatic brightness. The account values are unchanged (compared by hash). What triggers a save is not settled.
- PERF calc's wind, temperature and pressure boxes changed (000/00, 15, 29.92 and "ERROR FETCHING METAR" at 17:00;
  080/05, 6, 30.24 and "Manual!" by 17:20): the pace runs passed through valid codes (KSEA, KSET, LOWI, LOWG, EDDM), each
  looked up. A controlled EDDF, EDDM, EDDF edit left those boxes alone, so the cause is not isolated. Left as they are.

## Follow-up (17:44 to 17:56 local)

- **The Charts request box acts at the first valid code**, without Enter: typing "EDDM" (backspace four times, SHIFT e,
  CAPS d d m) showed MUNICH's labels and its 62 charts, and BlueBird wrote `charts_manual_icao` EDDM, before any Enter
  ("EDD", "EDD1", "EDDf" changed nothing). Typing EDDF back requested Frankfurt again and reset the page's view: the type
  tab went to SID and the chart list to its first entry.
- **Enter depends on the box:** on the Charts box `btn_enter` closed the keyboard (shown, ready and the target cleared,
  the box's focus byte 0, the layers reset) and did nothing else; on PERF calc's Airport box it does nothing. So a typed
  field is finished with `btn_keyboard_down`, which keeps the text on both.
- **A space is dropped** by the Charts box ("EDD" stayed "EDD"), as it drops `-`, `@` and `?`.
- **The keypad's OK in `fuel_input_dialog` closes only the keypad:** the target cleared at once, the box kept "2176", the
  dialog stayed up (BBOS +0x1d8 still 1) and nothing was inserted (no new notification); INSERT is still the dialog's
  commit, and one CANCEL closed it with no change.
- **The TOD pause keypad** (Settings, Aircraft): with `tod_pause_toggle` on, `tod_pause_textbox` [1202,210 100x44] shows
  "40" (BlueBird wrote `tod_pause_distance` 40); its tap brings the keypad up with no dialog, at [1037,266 360x430]: 12 px
  below the box and pushed against the tablet's right edge, so **the keypad moves with its box** (read +0x1d8 every time).
  Its OK closed the keypad and BlueBird wrote the value (40, unchanged): OK commits a keypad box with no dialog. The
  toggle turned off again: the box hidden, `tod_pause_distance` -1, Settings, Aircraft's listing as before.
- **A toggle's state is its byte +0x188** (1 on, 0 off; the only word that changed in a stable diff of `tod_pause_toggle`;
  across the page's 16 toggles it matches every state the listing shows; +0x189 read 1 on all). The window reads a
  settings row's state from the row's value label, and the TOD row has none, so it said "not checked" while TOD pause was
  on.
- **The layers reset whenever the keyboard closes** (by its down key or by Enter): it always comes up on the lower layer
  (numeric, shift and caps 0, polled through each come-up, three cycles). The keypad byte +0x1ae keeps its 1 after the
  keypad closes and clears when the QWERTY keyboard next opens: read it only while +0x1ac is 1.
- **The settle time varies:** three come-ups settled 1.095, 1.163 and 1.112 s after the tap; one took more than 1.94 s.
  Poll the ready byte with a timeout of a few seconds, never a fixed wait.
- Side effects: the Charts page shows its first TAXI chart (EDDF101P) instead of the one shown before (entry 23; putting it
  back is a drop-down choice), and `chart_view_states` gained that chart's view; `display_brightness` follows the
  automatic brightness again. Everything else in `efb_settings.json` equals its copy from before, the credentials by hash.

## What the layout carries (plan 22, Task 1b)

| Field | Value | What it holds |
|---|---|---|
| `KeyboardShown`, `KeyboardReady`, `KeyboardKeypad` | 0x1ac, 0x1ad, 0x1ae | the keyboard app's bytes: up, settled, the numeric keypad instead of the QWERTY keyboard |
| `KeyboardTarget` | 0x1ec | the keyboard app's pointer to the text box being typed into, 0 when down |
| `KeypadRect` | 0x1d8 | the keyboard app's four floats: the keypad's x, y, w, h |
| `KeyboardNumeric`, `KeyboardShift`, `KeyboardCaps` | 0x1e8, 0x1e9, 0x1ea | the keyboard app's bytes: the third layer, the one-shot shift, caps lock |
| `TextboxCaret`, `TextboxMaxLength`, `TextboxKeypad` | 0x1ac, 0x1bc, 0x1cc | a text box's ints: the caret, its maximum length, 1 when it opens the keypad |

The keyboard app (`N3bbs12app_keyboardE`, vptr 0x26f18) joins the class list as `keyboard`, derived by RTTI; before, it read as
the fallback class `app`.

## Not settled

- Enter on the Hoppie and SayIntentions boxes (never needed: the down key keeps the text); how the empty SimBrief row
  saves (the keypad's OK, presumably, as on TOD: the owner's in-sim check); `btn_tab` and `btn_home`; whether KEYBOARD
  LOCKED depends on LOCK KEYBOARD TO EFB; where a tap puts the caret in text wider than its box.
