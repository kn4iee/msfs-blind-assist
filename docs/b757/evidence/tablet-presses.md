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
- **Nothing (9), each explained:** two were the view already shown (Catalog, Fires); five tiles were gray, unavailable at
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
- **A button read "UNAVAIL" acted.** The nose gear door's gray UNAVAIL button opened the nose gear door when tapped.
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

The tap point comes from the parser's pixel work (a box's center, a switch's pill, a slider's knob, a menu entry's
row), not from the OCR's word boxes: the model names a control, it does not place it. So a bigger model buys nothing
for where a tap lands, and on a dim tablet the brightening gets small to where medium is, at no cost in time.

## Captures for plan 18 (Task 1)

Taken 2026-10-29 afternoon, same aircraft, MSFS Blind Assist closed; saved as test pages in
`tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb/` (the picture and the reader's lines, `reread` with `PROBE_SAVE`).

| Page | Shows |
|---|---|
| `bright/12-aircraft-fuel-boarding` | The top bar during an automatic boarding: "DEP -00:46" (red), "BOARDING… 10 MIN" in the green box, the notification count |
| `bright/18-aircraft-ground-services-notification` | BlueBird's notification over the page right after a GPU tap: a panel at about x 1040-1390, y 40-160, "Notifications", the time ("14:17Z"), "GPU CONNECTED." |
| `bright/19-notification-list` | The list, opened by the arrow at the top bar's right end (1373,15; the count pill disappears while it is open): "Clear all", one entry per notification (an icon, the time, the text, an x), scrolling; it covers the page's right side (to about y 515) |
| `bright/20-fuel-start-boarding` | After FETCH SIMBRIEF OFP with AUTOMATIC BOARDING off: "» START BOARDING" in the top bar's green box, "DEP -03:19", and IMPORT FROM SIMBRIEF on the Fuel page at about (517,951). The reader drops that button today: the Fuel page's template ignores the whole content area |
| `bright/21-defuel-question` | After IMPORT FROM SIMBRIEF with 20,100 lb aboard and a 15,543 lb plan: a box over a dimmed page (about x 320-1075, y 415-665): "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?", YES (green, about 424,619), NO (dark blue, about 971,619). The reader today loses the page's section under the dimming and finds no NO |
| `bright/22-fuel-adjustment-complete` | About 40 s after YES: "FUEL ADJUSTMENT IS COMPLETE!" in a box with OK (about 823,662); IMPORT FROM SIMBRIEF gone. The reader drops both today (the Fuel template again) |
| `bright/23-units-question` | REFRESH SIMBRIEF OFP with the aircraft in kilograms: "Mismatch between flight plan and aircraft weight units! The loaded flight plan uses LBS, but the aircraft is currently set to KG. Do you want to switch the aircraft unit system?", YES (about 393,633), NO (about 1001,633), and the "SIMBRIEF OFP RECEIVED." notification. The flight plan behind it is painted over (the page's color), so no route, callsign or plan data is in the repository |
| `bright/41-checklist-header-ticked`, `42-checklist-header-unticked` | The checklist with its header box ticked (every line ticked) and unticked; on the unticked one, and live after unticking, the reader still reads the header as ticked |
| `dim/15-aircraft-maintenance` | 0 percent brightness: the gear doors' faint pink OPEN, which the small model misses on the raw picture |

- REMOVE FP removes the plan at once, with no question.
- Put back afterwards: AUTOMATIC BOARDING on, the aircraft in pounds (the units question's YES), the GPU off, the
  checklist unticked, the plan removed; `efb_settings.json` and `G-BBS75-opt.json` identical to before (credentials
  compared by hash). The fuel stays at the plan's 15,543 lb (defueled from 20,100 lb by the captures).

## After plan 18 (Task 12)

Measured 2026-10-30 on the owner's PC: the same aircraft at Frankfurt stand C6, daylight, AUTO brightness, BBS Nest
running, MSFS Blind Assist closed. The probe's `presstest` was run as on 2026-10-29, with the same pages, navigation and
skips. It now links plan 18's reader: the brightening before OCR (Task 2) and the parser with the top bar, the notices
and the question (Task 3).

Two more controls were skipped than on 2026-10-29: Automatic boarding (the owner keeps it on) and Hoppie ACARS
(switched on, it may log on to Hoppie with the owner's code).

| Page | Offered | Tapped | Landed | Nothing | Wrong |
|---|---|---|---|---|---|
| Aircraft, Ground services | 11 | 11 | 8 | 3 | 0 |
| Aircraft, Cabin & doors | 14 | 14 | 14 | 0 | 0 |
| Aircraft, Maintenance | 8 | 8 | 7 | 1 | 0 |
| Aircraft, Failures | 17 | 17 | 15 | 2 | 0 |
| Checklist | 15 | 15 | 15 | 0 | 0 |
| Settings, General | 7 | 7 | 7 | 0 | 0 |
| Settings, Aircraft | 15 | 13 | 13 | 0 | 0 |
| Settings, Livery options | 3 | 3 | 3 | 0 | 0 |
| Settings, Callouts | 14 | 14 | 14 | 0 | 0 |
| Settings, Cabin audio | 5 | 5 | 5 | 0 | 0 |
| **All** | **109** | **107** | **101** | **6** | **0** |

- **No wrong tap.** Every tap landed on the control the reader named, or did nothing.
- **Every page offers as many controls as on 2026-10-29, or more.** Callouts gains Rotate and Positive rate, which the
  retired UPDATE guard box had covered (Task 10). Cabin audio gains its Flight attendant calls switch.
- **"Check" verdicts, read by hand from the log:**
  - the Failures tabs and categories (the view shown);
  - the checklist header and Interactive (they tick the lines);
  - Radio altimeter (2500), which switches the 2500 call-out off;
  - Units, which writes both keys;
  - the colors theme, whose new theme re-reads the page;
  - the Maintenance brake reset and nose door, which changed only another line's reading wobble ("81F"/"B1F",
    "Recall"/"Recal") and the door's own percentage.
- **Nothing (6):**
  - the two air start tiles, which read gray "JETWAY CONNECTED" while the jetway was attached;
  - STAIRS DISABLED;
  - the brake temperature reset, already reset;
  - the Failures Catalog and Fires tabs, already shown.
- **Left behind by the probe's put-back and restored by hand:**
  - the 2500 call-out (the known coupling);
  - the cockpit door: CLOSE ALL closes it, and ALL CLOSED cannot reopen it;
  - BlueBird's own re-save of its EFB brightness level (1.0 written as 0.999), which AUTO does not use.

  After the run, both settings files were identical to before.
- **The jetway.** At this stand the tapped JETWAY REMOVED went to REQUESTING… and the jetway attached; the put-back
  tap did not cancel it. Pressed to leave, it stayed at JETWAY LEAVING for more than 15 minutes, and a later request
  stayed at REQUESTING… These are BlueBird's and MSFS's own states at this stand. A sim jetway toggle sent during the
  test may have put the two out of step. A fresh flight load resets the airport's jetways.
- **Read only:**
  - Navigation, ACARS dashboard reads FETCH SIMBRIEF OFP and REMOVE FP as buttons, with no caption.
  - On the darkest tablet (manual brightness 0.5 %, white text at 114), Aircraft, Maintenance offers the three gear
    doors with the same words as the bright tablet.
- **Found:**
  - Settings, AIRAC data read UPDATE and both LOGOUTs as a row's value, so the window could not press them. This was
    fixed in Task 12's fix round, with the page as a test fixture (`bright/55-settings-airac-data`, the SimBrief ID
    painted out before the read).
  - Under a light colors theme (Light blue, measured on Settings, General), the reader found no switch, slider or
    choice on the page: every setting read as a plain row. The owner heard the same in the window at the in-sim checks
    (2026-10-30), after his press set Light blue. The other pages and the Light grey and Dark grey themes were not
    measured. By the owner's ruling the theme is offered in neither window as a choice, only as a note, and the settings
    window offers one "Set to Dark blue" while BlueBird's file holds another theme (B757-21).

**Small against medium, plan 18's reader** (`compare` on the Task 1 captures):
- **Bright, 18 pages (183 controls):** 3 differences, all the checklist's "Fuel: OB/PLAN" label.
- **Dim, 4 pages (32 controls):** 1 difference: the left gear door's "0%" read as "05" by small.
- **Both:** the same tap points and states everywhere. Medium read a page in 1.2-1.5 s against small's 0.4-0.7 s.
