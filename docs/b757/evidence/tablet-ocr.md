# The 757 tablet reader on PaddleOCR: measurements (plan 17)

Measured 2026-10-29 on the owner's PC (32 logical processors, so the engine runs 4 threads) from saved captures, no
simulator: the reader as plan 17 Task 4 left it (commit c0d978947): PaddleOCR PP-OCRv6 small through RapidOcrNet
4.2.0, `RapidOcrOptions.PPOCRv6` with word boxes on and the orientation step off, one read per page at the tablet's
own size.

## Method

- **Ground truth:** the hand-checked text of 23 tablet pages from the 2026-10-27 engine benchmark, 909 items with the
  status bars: the reader's 11 test pages (`tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb/`) and 12 live captures
  (9 Aircraft, Navigation, Settings and Home pages, and 3 pages of a SimBrief OFP). The truth and the live captures stay
  outside the repository: the OFP pages hold a flight plan.
- **Items read exactly:** each truth item against the engine's words inside its box, case and spaces ignored (the
  benchmark's scoring script).
- **Page elements right:** the engine's lines and the truth's lines both through the reader's current parser
  (`Bbs757EfbScreen.Parse`), compared element by element: kind, words, value and state, as the pilot gets them.
- **Before:** Windows OCR as the app read on 2026-10-27 (its whole reader: two passes and the close-up re-reads),
  recorded then; at the page level parsed with the current parser, so both engines meet the same parser.
- **After:** the probe's `reread` (`tools/Bbs757EfbProbe`) with `PROBE_SAVE`. Re-reading the 11 test pages reproduced
  the committed `.lines.json` byte for byte: the engine is deterministic on a given picture.

## Results

**Items read exactly:**

| Pages | PaddleOCR, as shipped | Windows OCR, before |
|---|---|---|
| All 23 pages (909 items) | 91.5% | 61.5% |
| The 11 test pages (353 items) | 98.6% | 80.7% |
| The 12 live pages (556 items) | 87.1% | 49.3% |
| Small text, 24 px or less, all 23 pages (270 items) | 83.7% | 34.8% |

**Page elements right, through the current parser:**

| Pages | PaddleOCR, as shipped | Windows OCR, before |
|---|---|---|
| The 11 test pages (256 elements) | 98.8%, 9 of 11 pages entirely right | 92.6%, 5 of 11 |
| The 12 live pages (293 elements) | 78.2%, 5 of 12 | 60.8%, 3 of 12 |

**Time and memory:**

- Loading the engine: 141 ms (125-223 ms over Task 4's runs). The process working set went from 30 MB before loading
  to 97 MB after. The engine is loaded only while the tablet screen window is shown.
- One page: 334-503 ms on the 11 test pages, 236-687 ms on the live pages (the OFP pages slowest); median 382 ms over
  the 23 pages. Windows OCR's whole reader took a median 546 ms (185-1,401 ms) on 2026-10-27.

## What PaddleOCR still gets wrong on the test pages

- Checklist: "Recall" reads "Recal", and "OB / __ PLAN" reads "OB/-PLAN": misread letters the parser cannot know, left
  as they are.
- Failures: the counts come as a heading "Failures" and a line "Active 0 armed 0" rather than one line "Failures,
  active 0, armed 0". The pilot hears both counts. The live Failures page gives the one line.

On the live pages most misses are on the dense Navigation pages (the OFP, ACARS dashboard, PERF calc).

## Parser changes made for PaddleOCR (plan 17 Task 4)

PaddleOCR's line boxes reach about 5-6 px above and below the letters, where Windows OCR's were tight; some of the
parser's pixel distances were tuned on the tight boxes.

- A label over a box with more boxes further down its column names none of them: the presets page's ON GROUND, IN
  FLIGHT and CUSTOM are column headings again, and the Failures title no longer names CATALOG.
- A read holding a figure is matched only against known words holding a figure, so "ACTIVE 0 ARMED 0" keeps its counts.
- A side-menu icon read as a word is no menu entry (the fuel page's COCKPIT PRESETS icon read "tlt").
- "SET UP" names its failure row as FAIL NOW does: "Set UP, L engine fire" (the button says SET UP; Windows OCR read
  "SET").
- A line whose start is a known label ending in ":" keeps the rest as that label's value: the Failures page now says
  "Random failures: off" and "Notifications: off" (Windows OCR lost both states).
- Accepted as read: the presets page's CIRCUITS STATE now names its one button ("Circuits state: PUSH ALL CBs").

## On the live tablet (plan 17 Task 8)

Measured 2026-10-29 with the probe's `tour 3` on the deployed models: MSFS 2024, BlueBird 757-200 RR at Frankfurt, on
the ground in daylight, MSFS Blind Assist closed. The tour opens every app and section it reaches and reads each page
three times, on a settled picture, once it has landed.

- **From Home:** 25 pages; all 25 read the same all three times; 24 landed on the page tapped. The Navigation tap was
  lost while Home was still sliding in (the tour taps as soon as the first change shows), as the Checklist tap was in
  Windows OCR's tour (flight-11.md section 8). The app cannot race like that: it offers the next tap only after it has
  read the settled page.
- **From Settings:** 30 pages, Navigation's six sections included; all 30 landed; 29 read the same all three times. The
  exception, the Navigation map, was still loading during the first read: "Loading navigraph map" and the map layer's
  status line were on that read only.
- **Before:** Windows OCR's tour 3 on 2026-10-27: 22 pages, 21 landed, 21 read the same (Aircraft Failures differed
  in small text while a build loaded the CPU).
- **Time:** the engine loaded in 310 ms in the first run and 148 ms in the second (working set 31 MB to 99 MB); a page
  read in 293-1,344 ms, the Charts list the slowest; a page showed 161-773 ms after its tap.
- **Credential rows** (Settings > AIRAC data), read three times, the reader's output searched by script for the SimBrief
  ID's digits and never shown: "SimBrief ID number: set", "Hoppie logon code: not set", "SayIntentions ACARS key: not
  set", as BlueBird's settings file holds them; no digit of the ID was in the output.
- One misread seen in passing: the boarding rate's hover note "Faster service turnaround" read "tumaround".

## The owner's checks by ear (plan 17's in-sim checks 1-4)

2026-10-29, in the app as deployed (5ff4d2906), the same flight:

- **Check 1, reading:** passed. The app's log: the pop-out opened on the first click; Home, four Aircraft pages and
  PERF calc each landed on their first read, and every re-read matched the page shown; 268-502 ms a read.
- **Check 2, the models moved aside:** passed. Picking the 757 opened the offer, Not now closed it, and Read the tablet
  screen opened the offer instead of the reader. Whether "Switched to …" was still heard before the window's own
  speech was not reported.
- **Check 3, the offer box unticked:** passed: picking the 757 opened no offer.
- **Check 4, the app started on the 757 with the models aside:** passed: the offer came after the other startup
  windows.

## Not measured here

- Check 5, the first-run download through the app: it waits for the release (plan 17 Task 9).
