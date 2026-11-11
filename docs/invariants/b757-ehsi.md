# BlueBird 757 EHSI reader rules, full text

The rule lines are in `.claude/rules/b757-ehsi.md`; the shared 757 rules are in [b757.md](b757.md). Background: [docs/b757.md](../b757.md).

## B757-36

- `Bbs757EhsiReader` reads the flown legs, the legs, a pending modification and the points all or nothing. The route is built from vectors BlueBird reallocates while the reader walks them, so a torn read could drop a leg or join two across a gap, and the pilot would hear a route that is not flown. A torn vector, a leg or label pointer outside the module, a count over the layout's cap, or a non-finite or out-of-range position makes the whole reading `Corrupt`, with no partial route. A discontinuity (seen live 2026-11-11 in KOLM, discontinuity, OLM, BTG, KPDX: leg type 3, +24 = 0, no label, position 0, 0) is its own leg and gives no row; the reader never draws a leg across it, as BlueBird breaks the line there. Without this a half-read route would be spoken as the route. Evidence: `docs/b757/evidence/displays-ehsi.md`. Plan 26, 2026-11-11.

## B757-37

- `Bbs757EhsiText` lists a map item only where the EHSI draws it. Each item is projected with `Bbs757EhsiGeometry.Project` from the map center and rotation the nd holds, and is listed only inside `OnScreen`'s clip: a circle around the aircraft's screen point (88.7 % of the range in this build, 516 of 582 pixels; in PLAN too, where the map's center is 291 pixels above the circle's) and the screen's own edges (352 pixels either side, 157 below the aircraft). Measured 2026-11-11: KOLM at 37.5 NM, 86.5° right, is not on the screen in MAP at range 40 (beyond 60.5 % of the range sideways) but is at 160; in PLAN at 80 centered on KOLM, KSEA (37.6 NM at 41°) falls 528 pixels from the circle's center and is cut. A test of distance against the range alone would list items a sighted pilot does not see. The constants come from the layout, carried in the reading. Plan 26.

## B757-38

- On the 757, `HotkeyAction.ReadNDWaypoint` (Ctrl+W) says `Bbs757EhsiDisplay.WaypointLine`: the active waypoint's name, then the top line's distance and ETA as drawn, "no ETA" under 100 kt. The name is the route's leg 0, because the EHSI's top line draws no name (measured 2026-11-11: distance and NM at the left, ETA and z at the right, the heading between). It interrupts (`AnnounceImmediate`), the window open or closed, and is never muted by the Ctrl+M row, as the PMDG 737's does the same with its ND line. Without the interrupt a requested line would queue behind the reader's own. Plan 26.

## B757-39

- `Bbs757EhsiDisplay.SimInputs` lists `PLANE LATITUDE` and `PLANE LONGITUDE` because BlueBird's top-line draw reads them fresh on each call (the FMS keeps no copy; measured 2026-11-11), so the spoken distance must come from the same position. They are an input only, never compared with memory ([B757-30]). The map rows use the nd's own center, copied each frame from the selected IRS (`nd+1160`, `nd+1168`), the position the map is drawn from. Parked the two agreed to the sixth decimal; in a turn they can differ. Swapping one for the other would put the top line or the map rows off the screen's own. Plan 26.
