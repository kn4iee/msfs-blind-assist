---
paths:
  - "MSFSBlindAssist/Services/BBS757/Bbs757Ehsi*.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Ehsi.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Bbs757Ehsi*.cs"
---
# BlueBird 757 EHSI reader rules

Loaded when Claude reads matching code. Background: docs/b757.md. Full text: docs/invariants/b757-ehsi.md. The shared 757 rules (B757-30 and on) are in b757.md.

- [B757-36] `Bbs757EhsiReader` reads the flown legs, legs, pending modification and points all or nothing: a torn vector, a leg or label outside the module, a count over the cap or a non-finite or out-of-range position makes the reading `Corrupt`, no partial route; a discontinuity is its own leg with no row, never a leg across it. Full: docs/invariants/b757-ehsi.md#b757-36
- [B757-37] `Bbs757EhsiText` lists a map item only where the EHSI draws it: projected by `Bbs757EhsiGeometry.Project` from the nd's center and rotation and inside `OnScreen`'s clip (circle at 88.7 % of range, PLAN too, plus the screen's edges), never by distance against the range alone; constants from the layout. Full: docs/invariants/b757-ehsi.md#b757-37
- [B757-38] `HotkeyAction.ReadNDWaypoint` says `Bbs757EhsiDisplay.WaypointLine`: the active leg's name (a pending modification's leg 0, else the route's; the top line draws none), then the top line's distance and ETA as drawn ("no ETA" under 100 kt); interrupting, window open or closed, never muted by the Ctrl+M row. Full: docs/invariants/b757-ehsi.md#b757-38
- [B757-39] `Bbs757EhsiDisplay.SimInputs` takes `PLANE LATITUDE` and `PLANE LONGITUDE` because BlueBird's top-line draw reads them (an input, never compared with memory, [B757-30]); the map rows use the nd's own center. Never swap one for the other. Full: docs/invariants/b757-ehsi.md#b757-39
