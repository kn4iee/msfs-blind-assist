# Plan 11 evidence: BlueBird's tablet read from its pixels, and the tablet measured

Measured 2026-10-26 on Declan's PC: MSFS 2024, BlueBird 757-200 PW (0.1.0), BBS Nest running, on a runway at
night, cold and dark then with the tablet only. Tools: `tools/Bbs757EfbProbe` (the app's own reader files,
linked), the EFB agent (`Resources/coherent-bbs757-efb-agent.js`), the simconnect MCP for the camera.

## 1. Why the tablet can be read now (the ruling R73 rested on "unreadable")

- **No pixels through the debugger.** In the tablet's Coherent view, `canvas.getContext('2d').getImageData`
  throws `NotSupportedError (DOM Exception 9)` on ANY canvas (a plain 4 x 4 red one too), `toDataURL()` returns
  an empty string, and there is no WebGL. Reading the tablet in the page is impossible.
- **`BBSJ2WI` is not a command channel.** The tablet JS (deobfuscated in the live view through its own string
  table) sends only `{"u": "<gamer name>"}` on it, at start and on Navigraph sign-in. The other Comm Bus names are
  Navigraph sign-in and map ones (wasm-findings.md section 2).
- **The tablet's live image is an `<img>`.** In the `BBSNavigraphMap` view each WASM gauge is a
  `<wasm-instrument url="…wasm_gauge=NAME">` holding an `<img src="WasmGaugeLiveView_N">`; the tablet is
  `efb_l` (1397 x 1048), the captain's ND `nd_l` (704 x 859 at page 21,735). Robin's session-8 mirror laid
  that image over the ND.
- **A pop-out of the ND shows the mirrored tablet at full brightness, cold and dark.** Right-Alt + click on the
  ND in instrument view 0 (ADI/EHSI) makes a `WASMINSTRUMENT` window; PrintWindow (PW_CLIENTONLY |
  PW_RENDERFULLCONTENT) captures it parked off the desktop, with the camera back on the pilot's view
  (measured: the pop-out keeps rendering whatever the camera shows). A new pop-out opens at the size the sim
  last used (1350 x 1138 measured) with the ND letterboxed between black bars; parked at the ND's aspect
  (client 1397 x 1705) there are no bars and the tablet is the top 1048 rows at its own width.
- **Calibration: capture pixels ARE agent click coordinates.** Magenta markers drawn over the mirror at tablet
  points (100,100), (1297,948), (698,524), (40,1000) were found at (99,99), (1295,946), (696,523.5), (40,998):
  within 2 px, at the corners and the middle. Repeated after a sim restart: identical.
- **The mirror shows ~0.5 s after it is laid** (three runs: 0.75-0.85 s after the eval returned, the eval itself
  ~0.3 s through a fresh socket). The app keeps the mirror on while the EFB window is shown.

## 2. Text: Windows.Media.Ocr

- Built into Windows, offline. The app's TFM becomes `net10.0-windows10.0.19041.0` (and the test project's).
- Greyscale by LUMINANCE, stretched to full range, 2x: reads every page's labels. Greyscale by the brightest
  channel lost every red and green tile (a red tile's red channel is as bright as its white text).
- A second pass with a square-root gamma rescues dim text (a dim green OFF); merged only where pass A read
  nothing.
- Small (10-14 px) condensed text is unreliable at any scale, filter or brightness: the door names under the
  door tiles, the "Choose SAVE or LOAD…" note, the brake temperatures' labels. Those come from page templates
  (Bbs757EfbScreen.Templates) or variables.
- A control re-read tries luminance and darkest-channel at 2x, 3x and 4x: a white OPEN on a pink button read
  only with the darkest channel at 4x.
- Times on this PC: a page 1-3.5 s (OCR plus re-reads), a region 10-90 ms.

## 3. Brightness and themes

- The tablet dims itself (EFB BRIGHTNESS MODE AUTO, at night): white text read 82/255. Every colour scales
  with it: red tile (74,0,0) dim, (235,0,0) at 100 %; grey (38,38,38) / (136,136,136); chocks green
  (21,66,12) / (91,223,61). The page background is near black dim, slate (22,29,37) at 100 %.
- The reader normalises each frame to its own white and measures its own background, so one set of rules
  holds at both. All 22 pages were captured both ways; the tests run on both sets.
- The tablet was set to MANUAL 100 % for the bright set and set back to AUTO afterwards (Settings, General).

## 4. Pages and what the reader makes of them (both brightness sets)

Home (5 apps; labels are not clickable, the icon 70 px above is); Aircraft: Cockpit presets, Fuel, Weight &
balance, Ground services, Cabin & doors, Maintenance, Failures; Navigation: ACARS dashboard, Operating FP,
Weather & NOTAM, Charts, Map, Perf calc; Systems (diagrams: labels too small, SELECT only); Checklist; Settings:
General, Aircraft, Livery options, Callouts, Cabin audio, AIRAC data. Selected menu entries, tabs, tile states
(colour), settings switches (pill and knob) and sliders (knob), checklist boxes, labelled values (templates).
Navigation's data pages are dense small text and read poorly; the app's own SimBrief fetch carries that content.

## 5. Controls proven by a tap and a read-back

| Control | Tap | Read back |
|---|---|---|
| Settings switch (Live autobrake info) | (1168, 351) | ON -> OFF -> ON on screen |
| Slider: Notifications fade-out timer | track 895-1190 (taps outside ignored) | 4, 6, 8, 11 … 28 SEC, NEVER at the right end; knob follows within a few px; put back to 10 SEC |
| Slider: Boarding & load rate (plan 11 listed it "not operable") | 945 / 970-1045 / 1070-1120 / 1145+ at y 591 | REALISTIC / STANDARD / QUICK / INSTANT; put back to REALISTIC |
| Slider: IRS alignment time ("not operable") | 1045, 531 | FULL (7 MIN+) -> 2 MIN -> FULL |
| Slider: EFB brightness (MANUAL) | track 795-1100 | 79 %, 89 %, 100 % |
| Scroll (agent wheel, deltaY 300) at (800, 600) | Settings, Callouts | the list moves ~130 px; -300 moves it back |

## 6. Ground services (Task 1): which variable each tile moves

Read through the agent's `readVars` (SimVar.GetSimVarValue in the tablet's page). Each tile tapped once, read,
tapped again, read: every one went back to where it was.

| Tile | Variable | Result |
|---|---|---|
| AFT STAIRS (645, 744) | `BBS757_vis_stairsaft` | 0 -> 1 -> 0 |
| FUEL TRUCK (1230, 597) | `BBS757_vis_fueltruck` | 0 -> 1 -> 0 |
| CATERING (1009, 744) | `BBS757_vis_catertruck` | 0 -> 1 -> 0 |
| CARGO SVC (1009, 371) | `BBS757_vis_baggage1` | 0 -> 1 -> 0 |
| CHOCKS (410, 535) | `BBS757_vis_chocks` | measured 2026-10-25 (plan 07 era) |
| FWD STAIR (620, 275) | `BBS757_vis_stairsfwd` | no change; the tile stayed FWD STAIR OFF on screen (a BlueBird precondition, not found) |
| GROUND A/C, LEFT AIR START, RIGHT AIR START | `_aircon`, `_airstart` | no change; the tiles are grey (unavailable) |
| JETWAY (645, 214) | none found | no change, on a runway (no jetway) |

A variable that does not exist reads 0 through `readVars` too (`NOT_A_REAL_VAR_123`), so only a 0 -> 1 -> 0
proves one. Brake temperatures: `B757_BRAKE_TEMP_SENS_0` 16.8 (degrees C) while the tablet showed 61 F.

## 7. Incidents

- The sim crashed once during the session (after the bright crawl, before any wheel event reached the
  tablet). After the restart BBS Nest, still running, had to be restarted before the aircraft ran its systems
  (the tablet showed "BBS|NEST is not running").

## 8. Reliability, and the window made read-only (2026-10-27)

After the first test in the app (Declan, 2026-10-27: reads that had to be repeated, a page that came out
differently on each refresh) and the team's review (a misread must never become a press), the reader was
measured live with the probe's new `stability`, `noise` and `tour` commands, which run the app's own reader
files. MSFS 2024, BlueBird 757-200 PW 0.1.0, on the ground, the tablet dimmed (AUTO).

- **The same picture always reads the same.** One capture read twice gave the identical page every time:
  Windows OCR is deterministic. What varies is the picture.
- **Two captures of an unchanged page are never pixel-identical.** Four pairs 250 ms apart differed in 15,
  73,723, 20 and 17 of 1,464,056 pixels, every difference at most 4 levels (of 255) in any channel: the ND
  pop-out's rendering noise. Text, a tile changing colour or a page sliding in moves pixels by tens to hundreds
  of levels. So "unchanged" is now judged with a tolerance (`Bbs757EfbFrameCompare`: at most 30 pixels may
  move by more than 12 levels), never by an exact hash.
- **That noise changed the words.** Home read 8 times without a change: 7 said "BBS752-PW2037", one
  "BBS752.PW2037"; later runs also gave "BBS752PW2037". The old bridge re-read every page every 20 s, so a still
  page could change under the pilot. Now a page whose picture has not really changed is never read again on its
  own, and a read that says what is already shown is not pushed.
- **A tap was read before its page arrived.** Tour 1 (each app tapped from Home, read 300 ms later, then on
  a still picture): 4 of 5 apps read as Home on the first read, the right page on the next two. An unchanged
  old page looks perfectly still, so waiting for stillness cannot catch it. Waiting for the picture to change
  first (tour 2) still landed on the wrong page for 3 of 12: the first change is often the icon's press
  highlight, and the page itself followed 406-2,392 ms after the tap (median 434). A navigation tap is now
  judged by the page it lands on (`Bbs757EfbBridge.PageIs`: the page name is the one tapped), read again for
  up to 4 s, and "The tablet did not show …" is said when it never is.
- **Tour 3, with all of the above:** 22 pages (Home, every app and every section of Aircraft, Navigation and
  Settings), each read 3 times on a settled picture after landing. 21 of 22 landed on the page tapped (the
  Checklist tap was lost while Home was still sliding in: the window now says so) and 21 of 22 read the same
  all 3 times. The exception, Aircraft Failures, differed in small text ("Set i.p" / "Set ip",
  "Failure, status, actions" / "FAILURE, srAtus, ACTIONS"), read while a solution build was competing for the
  CPU (OCR up to 22 s that run, against 2-7 s otherwise). Earlier tours also showed one-off misreads on still
  pages: a brake temperature "55 F" / "not read", the Maintenance button "Altn repair" / "RE9A.e".
- **Reading order.** Lines were ordered in fixed bands (`Top / 10`, `/ 12`, `/ 14`, `/ 20`), so two things on
  one line could fall either side of a band edge and swap on a refresh. They are now grouped by vertical
  centre around each row's first line (`Bbs757EfbReadingOrder`); the parser's tests over every captured page
  pass unchanged.

**What this means.** Consistency is fixed: the window no longer changes unless the tablet did. Accuracy has a
floor: one read of a page can carry a misread word, and it then stays until the page changes or the pilot asks
for a fresh read. That is why the window is read-only (plan record, "Rulings made while working"): a press
placed where the OCR found a control can hit a different one if the read is off, and a before-and-after
variable only proves the intended thing did not change, not that nothing else did.

**The build folder.** The TFM change (section 2) moved the app's output to `bin\x64\<cfg>\net10.0-windows10.0.19041.0\`
while the updater, `release.yml`, `preview.yml`, `scripts/build-release.ps1` and `tools/user-guide-publish.sh`
all use `bin\x64\<cfg>\net10.0-windows\`: a release would have zipped the updater without the app (Gus noticed
the split). The csproj now sets `AppendTargetFrameworkToOutputPath=false` and that `OutputPath`; a clean
`dotnet build MSFSBlindAssist.sln -c Release` puts the exe, the updater and the agents in the one folder again.
