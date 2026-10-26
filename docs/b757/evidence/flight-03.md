# Plan 03 measurements: guarded writes, the fire handle relock, the landing altitude

2026-10-26, on the ground at KATL, BlueBird 757-200 RR cold and dark, BBS Nest running, MSFSBA closed; the owner asked
for plan 03 to be worked with the simconnect MCP and the Coherent debugger. Run by Claude: each write through the
MCP's calculator path (MobiFlight, as the app writes), read back through the MCP, and the emergency lights through the
L:var snapshot tool (`docs/b757/tools/lvar-snapshot/`, 2,173 names, noise list from two idle snapshots). These replay
the RPN plan 03's code sends; the app-level checks (Task 4 and "In-sim checks (owner)") still need MSFSBA.

## Found before

Emergency lights 0 (OFF) with their guard open (1), as BlueBird's cold-and-dark state has them (spec-gaps 3.3); stab
trim C 0 (NORMAL), guard closed; landing altitude 1000 ft; no fire handle unlocked.

## Emergency lights (`L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK`, guard `L:B757_OVHD_SWITCH_GAURD`)

- `2 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)` with the guard open: holds (2 s and 7 s later) and reaches the
  systems: `BBS757_CABIN_EMER_LIGHTING` 0 → 1, `B757_PSU_STATE` 0 → 2, the emergency battery `B757_M730_BATT_PERCENT`
  starts to drain (100 → 97 % over the test). A direct write of the state var is a real write, not a dead mirror.
- The guard's own press (the map's click code) while open: guard 1 → 0, switch 2 → 1 (ARMED),
  `B757_OVHD_PASS_OXY_UNARMED_DECAL` 1 → 0, `BBS757_CABIN_EMER_LIGHTING` 1 → 0, `B757_PSU_STATE` 2 → 1. So closing
  writes ARMED, as the XML says.
- Put back: guard press (open; opening writes nothing to the switch), then `0 (>L:…_CLICK)`. Read back: switch 0,
  guard 1. Not back on its own: `B757_PSU_STATE` stayed 1 (BlueBird's WASM owns it, so it was left alone) and the
  emergency battery read 97 %.
- The UNARMED light is readable (`B757_OVHD_PASS_OXY_UNARMED_DECAL`, 1 while not ARMED), for plan 13.

## Stab trim C cutout (`L:B757_Stab_trim_C_toggle_switch_CLICK`, guard `L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH`)

- Guard toggled open, then `1 (>L:B757_Stab_trim_C_toggle_switch_CLICK)`: holds. Cold and dark, no other variable in
  the snapshot moved (the guard variables are not in the snapshot's name list; they were read directly).
- The guard's own press while open: guard 1 → 0, switch 1 → 0 (NORMAL). Back as found.
- The MCP refused one combined call of 220 characters ("negative count") and nothing ran; split in two, both landed.
  An MCP quirk, not the sim's: the app's calls go through `ExecuteCalculatorCodeUnique`.

## Fire handle 1 unlock (`L:BBS757_FIREUNLOCK1`)

- The map's unlock press through the calculator (`1 (>L:BBS757_FIREUNLOCK1,bool) 1 (>O:…) …`): still 1 four seconds
  later. The cockpit relocks 1.5 s after a click through the component's `O:` timer (XML 15535-15553), which a
  calculator write does not start, so an app unlock stays unlocked: the defect spec-gaps 3.7 #6 names.
- `0 (>L:BBS757_FIREUNLOCK1)`: relocked (read 0); the handle stayed in (`L:B757_ENGFIRE_1_SWITCH` 0).

## Landing altitude (`L:B757_OVHD_LDG_ELEV`, step `L:B757_OVHD_LDG_ELEV_CHG`)

- `1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)`: reads 1030, and the step var reads 0 again.
- `-70 … 1 (>L:B757_OVHD_LDG_ELEV_CHG)`: reads -60.
- The range edges `Bbs757LandingAltitude.Write` sends (simconnect MCP only): `-1080 … 1 …` reads -1070, `14090 … 1 …`
  reads 14100, and one more `1 (>L:B757_OVHD_LDG_ELEV_CHG)` at 14100 still reads 14100 (BlueBird clamps at the top).
- Put back: `990 … 1 …`: reads 1000.

## Checked in the app (owner, 2026-10-26)

Debug build of `B757` at `0bb05ff55` (plan 03 and plan 05's Mach entry): the landing altitude typed on the Pressurization
panel as 1030 and as 645 (heard 650), and a fire handle unlock that relocks after 1.5 s: all passed. The emergency
lights and stab trim checks need plan 02's guarded rows and moved to plan 16's in-sim pass.

## Auto rate (read only, simconnect MCP)

The cockpit sets `L:B757_OVHD_AUTORATE_SWITCH` to 3.5, the index, once on load (XML 10153-10156: range 0 to 13 in
steps of 0.1); after the owner's flight reload it read 3.5. Right after that reload every MCP calculator read returned
0, even `(A:PLANE ALTITUDE, feet)`, until the MCP was reconnected: an MCP quirk, not the aircraft's.
