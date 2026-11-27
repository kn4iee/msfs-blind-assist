# A330 panel sweep

A live sweep that drives every cockpit control of the iniBuilds A330 once and reads it back, to prove
each position's name (plan 04, Task 4). The runner is not written yet; what exists is:

- `denylist.txt`: the controls the sweep never writes, one `key TAB reason` a line, with the reason
  saying what writing it would do. A test fails on a key that is not a control of the map or one of the
  definition's own controls, so a rename cannot silently drop a control's protection.
- the planner, in the app (`MSFSBlindAssist/Aircraft/A330/Sweep/`): pure code that turns the map's
  controls, each control's starting read, the deny list and the ECAM page keys into a plan. It is in the
  app so that its tests reach it; the runner will link the same files.

## What the plan says

Every control of the map appears once, in the map's order, either with its steps or with the reason it is
skipped.

- A selector visits every other position in the map's order and returns to the position it started in.
- A button that reports a state (`state` in the map) is pressed once; a multi-press key is then pressed
  `pressesToCycle - 1` more times to bring it back. A button with no readable state is skipped.
- A continuous control goes to its minimum, middle and maximum, then back to its start.
- A guard row is never swept on its own. Its switch opens it first and closes it last, unless the guard
  was already open at the start, which it leaves open.
- A control whose proof is on a lower ECAM page presses that page's key before anything else.
- A status row, a denied control, a control whose guard is denied, and a control whose start was not read
  are skipped with their reason; the start is never guessed.

Controls the aircraft does not offer yet (gravity gear extension, passenger oxygen, the evacuation
command, the cargo and cabin doors) are listed as comments at the end of the deny list, to be denied the
day they appear.
