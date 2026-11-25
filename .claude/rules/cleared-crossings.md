---
paths:
  - "MSFSBlindAssist/Navigation/ClearedCrossing*.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
  - "MSFSBlindAssist/Database/Models/TaxiRoute.cs"
  - "tests/MSFSBlindAssist.Tests/ClearedCrossing*.cs"
---
# Cleared crossing rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/cleared-crossings.md.

- [CLR-1] Only `ClearedCrossingHolds` lifts a runway stop: ONE crossing at ONE place, for a crossing the pilot entered or a Continue, never by runway name, the SayIntentions fill, a default or a recalculation, nor a stop shared with the pilot's own hold; its label is kept (`PlacedHoldLabel`, `PlacedStartHoldLabel`) to count down, say and restore. Full: docs/invariants/cleared-crossings.md#clr-1
- [CLR-2] A cleared crossing is recorded by its stop node, used once, and re-cleared after a recalculation only at that node (`ClearedCrossingHolds.Apply`); a route crossing the runway elsewhere or again stops there and says so, and one that drops a cleared stop still ahead is adopted even when [RTE-22] would skip it as unchanged. Full: docs/invariants/cleared-crossings.md#clr-2
- [CLR-3] `HoldShortOfNextClearedRunway` only puts a cleared stop back (never on runway pavement) or reports; no key but Continue clears a runway, and Continue before the line needs the countdown to have named that runway (`CanClearEarly`), never applies on the route's last segment and never pairs a stop shared with the pilot's own hold. Full: docs/invariants/cleared-crossings.md#clr-3
