# Pressing tablet controls where the reader finds them: measurements (plan 18)

Measured 2026-10-29 on the owner's PC: MSFS 2024, BlueBird 757-200 RR at Frankfurt, on the ground, engines off, BBS
Nest running, MSFS Blind Assist closed. The reader as landed on `B757` at da7e39c8e (PaddleOCR PP-OCRv6 small, plan
17), run by the probe's new `presstest` command (`tools/Bbs757EfbProbe/Program.PressTest.cs`), which compiles the app's
own reader files.

## Why

The team's review of 2026-10-27 made the tablet screen reader read-only: a control placed by OCR must never be pressed,
because a misread could become a press on the wrong control (flight-11.md section 8). That ruling rested on Windows
OCR's reading. Plan 17 replaced it with PaddleOCR (tablet-ocr.md: 91.5% of items read exactly, against 61.5%). Before
the unified EFB window is designed, the owner asked for the question to be measured again: does a tap at the point the
reader found land on that control, and change nothing else? And if the small model falls short, does the medium one do
better?

## Method

For every control on a page that an operable window would offer (the reader's buttons and tiles, switches, check
boxes, sliders and tabs; the side menus and Home are page navigation, which the tours measured), outside the list
below:

1. Read the page as the app reads it: the mirror on, a settled capture, one PaddleOCR read, the parser.
2. Record all 2,173 BlueBird L:vars (one Coherent call, `docs/b757/tools/live/snap.js`) and BlueBird's settings files
   (`efb_settings.json`, `liveries\*-opt.json`, credentials compared by hash and never shown), twice, a second apart:
   what moves between those two is noise, as are the 65 names of `docs/b757/tools/live/noise.txt` and what moved
   over four idle seconds at the start.
3. Tap where the reader placed the control. A slider is tapped where the reader puts the next option (or 30 percent
   further along when it knows no options).
4. Wait for the picture to change and the variables to settle (two snapshots 0.8 s apart that agree), read the page
   again and record again.
5. Put it back (tap again; a slider at its old place; a tab by the tab that was shown) and check that every variable
   and setting is back.

A control **landed** when it, its variables or its settings changed and no other existing control did (a notification,
or REMOVE ALL appearing once a service runs, are its consequences); **nothing** when nothing changed; **wrong** when a
different control, variable or setting changed. Every case the script could not settle was read by hand from its log.

Not tapped (big or irreversible effects, each with its own check in the app): the cockpit presets page (panel
states, quick starts, SAVE and LOAD USER CONFIG, PUSH ALL CBs), REMOVE ALL, the pushback tug, FAIL NOW and SET UP,
REPAIR, FETCH and REFRESH SIMBRIEF OFP and REMOVE FP, the AIRAC page (UPDATE, REMOVE, LOGOUT), the checklist's
RESET and RESET ALL, the GND SRVC mode, the Systems diagrams, Charts and Map, and the PERF calc inputs.

## Results: bright tablet (AUTO brightness, daylight)

| Page | Tapped | Landed | Nothing | Wrong |
|---|---|---|---|---|
| Aircraft, Ground services | 11 | 5 | 6 | 0 |
| Aircraft, Cabin & doors | 14 | 13 | 1 | 0 |
| Aircraft, Maintenance | 8 | 8 | 0 | 0 |
| Aircraft, Failures | 17 | 15 | 2 | 0 |
| Checklist | 15 | 15 | 0 | 0 |
| Settings, General | 7 | 7 | 0 | 0 |
| Settings, Aircraft | 15 | 15 | 0 | 0 |
| Settings, Livery options | 3 | 3 | 0 | 0 |
| Settings, Callouts | 12 | 12 | 0 | 0 |
| Settings, Cabin audio | 4 | 4 | 0 | 0 |
| **All** | **106** | **97** | **9** | **0** |

- **Every tap landed on the control the reader named, or did nothing.** The 41 Settings switches and sliders each
  changed only their own key in BlueBird's settings files (Units changes the unit in both files; Radio altimeter (2500)
  also switches the 2500 call-out off, below); the services, doors and gear doors moved exactly their own BlueBird
  variables; the failure categories and tabs each showed their own list.
- **Nothing (9), each explained:** two were the view already shown (Catalog, Fires); five tiles were grey, unavailable at
  this stand or in this state (GROUND A/C, both air starts, STAIRS DISABLED, and JETWAY, which read red); FWD STAIR and
  the cabin lighting mode did not respond in this state (as plan 11 found for FWD STAIR).

## Results: dim tablet (EFB brightness MANUAL, 0 percent)

The darkest the tablet goes: its white text at 80 of 255, as dim as the 2026-10-26 night captures (82). The same
pages but Failures, Livery options and Callouts; EFB brightness and the colors theme not tapped. Put back afterwards:
AUTO, the manual level at 1.0 (it read 0.999 before; AUTO does not use it).

| Page | Tapped | Landed | Nothing | Wrong |
|---|---|---|---|---|
| Aircraft, Ground services | 11 | 5 | 6 | 0 |
| Aircraft, Cabin & doors | 14 | 13 | 1 | 0 |
| Aircraft, Maintenance | 6 | 5 | 1 | 0 |
| Checklist | 15 | 15 | 0 | 0 |
| Settings, General | 5 | 5 | 0 | 0 |
| Settings, Aircraft | 15 | 15 | 0 | 0 |
| Settings, Cabin audio | 4 | 4 | 0 | 0 |
| **All** | **70** | **62** | **8** | **0** |

- The same controls landed and the same did nothing as on the bright tablet (the Maintenance RESET did nothing this
  time: the brake temperatures were already reset), at the same points within 1 px.
- **Two controls were not offered:** the nose and right gear doors' OPEN, pink on pink, too faint for OCR at 0
  percent, so the reader found no words in their boxes. Not a wrong tap: a control the pilot would not have been given.
- The checklist header's state read right this time (it misread on the bright tablet).

**Both passes: 176 taps, 159 landed on the control the reader named, 17 did nothing, none changed anything else.**

## What went wrong: reading, not tapping

- **A slider's options in the wrong order.** EFB COLORS THEME: the reader's vocabulary lists DARK BLUE last, the tablet
  has it first (`ui_theme` 0, the knob at the left end). The tap hit the slider and set another theme than intended,
  and the revert a third. Any slider set by a computed option place must be checked against the value read back
  (the settings file or the page) and corrected.
- **A check box read wrong.** The checklist's header box ("Before start, whole checklist") read as ticked while none
  of its lines were; tapping it ticked them all.
- **A button read "UNAVAIL" acted.** The nose gear door's grey UNAVAIL button opened the nose gear door when tapped.
- **BlueBird couples some controls.** Switching "Radio altimeter (2500)" on switches the "2500" call-out off, and
  switching it off again does not bring "2500" back (put back by hand). Interactive on ticks the checklist lines whose
  conditions hold.
- **Small text wobbles between reads of a changed page:** "OB/PLAN", "OB/-PLAN", "OB/_"; "Recall", "Recal"; "79F",
  "79 F". The controls' places and states did not.

## Seen on the way (for the design)

- **Notifications cover controls.** After many taps BlueBird shows a notification at the top right (about x 1040-1390,
  y 40-160) for 10 seconds (`notification_popback_seconds`); it covered the Livery options page's first switch right
  after a tap. A tap placed from an earlier read could land on the notification: the picture must be compared with the
  one the page was read from right before any tap.
- **The top bar is state.** After FETCH SIMBRIEF OFP it showed a departure countdown ("DEP -00:42"), the boarding
  control ("BOARDING… 13 MIN" where START BOARDING sits) and a notification count; the reader drops everything above
  y 32 today.
- **With AUTOMATIC BOARDING on** (`automatic_loading` true, the owner's setting), FETCH SIMBRIEF OFP alone fueled the
  aircraft to the plan and started boarding; the Fuel page showed no IMPORT FROM SIMBRIEF button. The classic window's
  "Load fuel and payload from SimBrief" taps that place blind and then START BOARDING's place mid-boarding.
- **Not offered by the reader:** the pushback arrows (icons without words), the PERF calc tabs (TAKE OFF, LANDING) and
  its inputs, the Fuel page's sliders and fields.
- **Speed:** the picture changed 145-185 ms after each tap.

## Small against medium model

The probe's `compare` reads saved captures with both PP-OCRv6 models (medium: the largest v6, 139 MB of models against
small's 31 MB) and compares the controls the press test would pick: kind, words, state and tap point.

- **Bright, 25 pages (209 controls):** the same controls, the same tap points within 3 px and the same states on all
  25; one label differs ("Fuel: OB/PLAN" against "Fuel: _OB/__PLAN"). Medium read a page in 0.8-2.2 s against small's
  0.3-0.7 s (about 2.7 times as long).
- **Dim, 8 pages (88 controls):** medium found the two faint gear doors small missed; otherwise the same.
- **Dim, brightened before reading:** each dim capture scaled so its brightest pixel is white (as `Bbs757EfbPixels`
  already does for the parser, but not for the OCR, which reads the raw frame): small then found every control,
  the two gear doors included, and matched medium on all four pages tried (one label differs).

The tap point comes from the parser's pixel work (a box's centre, a switch's pill, a slider's knob, a menu entry's
row), not from the OCR's word boxes: the model names a control, it does not place it. So a bigger model buys nothing
for where a tap lands, and on a dim tablet the brightening gets small to where medium is, at no cost in time.
