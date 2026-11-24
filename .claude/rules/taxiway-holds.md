---
paths:
  - "MSFSBlindAssist/Navigation/TaxiwayHoldPlacement.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiwayHoldPlacement*.cs"
---
# Taxiway hold placement rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxiway-holds.md.

- [TWH-1] A hold short of a TAXIWAY stops `TaxiwayHoldPlacement.SetbackMetres` before the junction, at a node or (on a pilot's Calculate only) one inserted on its edge, with the route on to the junction clear of every runway's margin (`SampleStepM`), else nearer it; no stop for a junction inside one; the tick-box "end of taxiway" stop stays put. Full: docs/invariants/taxiway-holds.md#twh-1
- [TWH-2] A taxiway stop is placed after the Turn picks (`TaxiwayHoldPlacement.Choose`): it never splits an off-chart leg, never moves back before the first segment on a picked row's taxiway, every split shifts the Turn and deferred runway picks' segment indices (`ShiftPicksAfterSplit`), and a recalculation never cuts a kept tail again. Full: docs/invariants/taxiway-holds.md#twh-2
