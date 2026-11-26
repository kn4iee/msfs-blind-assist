---
paths:
  - "MSFSBlindAssist/Navigation/TaxiwayHoldPlacement.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiwayHoldPlacement*.cs"
---
# Taxiway hold placement rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxiway-holds.md.

- [TWH-1] A hold short of a TAXIWAY stops `TaxiwayHoldPlacement.SetbackMetres` before the junction, at a node or (Calculate only) one inserted on its edge, the route from its 25 m capture before it to the junction clear of every runway's margin (`SampleStepM`), else nearer it; none if the junction's is not; the tick-box "end of taxiway" stop stays put. Full: docs/invariants/taxiway-holds.md#twh-1
- [TWH-2] A taxiway stop goes after the Turn picks, placed as with none (`ChooseAroundTurn`: B under the setback past a turn is held before it; a linked turn's join moves onto its taxiway), never splits an off-chart leg, a split shifts the Turn and deferred runway picks' indices (`ShiftPicksAfterSplit`), and a recalculation never cuts a kept tail again. Full: docs/invariants/taxiway-holds.md#twh-2
