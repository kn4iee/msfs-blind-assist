# Plan 08: the ND, the ANF, taxi and the other displays on 1.2.8

## Task 4: the other displays (2026-11-02, at VCBI stand A6, engines off, APU on)

Read with `A350Probe displays` (the display service's own capture, the sim's asks answered as MainForm answers them), the reader each
display panel uses.

| Display | Read | Against the cockpit |
| --- | --- | --- |
| SFD | "Speed: 30 knots or less", "Altitude: 69 feet", "Baro: 1012, cyan", "Heading: 220", "Position: N07 10.7 E079 53.3, green", "Position source: GPIR3, white" | stand A6 faces 217; the baro as set |
| SND (the ISIS gauge draws the same) | "Ground speed: 0, green", "Heading source: IR3, white", the same position and source | as drawn |
| BAT, selector EMER 1 / EMER 2 / OFF / BAT 1 / BAT 2 | "Battery voltage: 28.4 V, grey" / "27.0 V" / "Battery voltage display: blank" / "28.1 V" / "28.3 V" | each position its own battery; OFF blank (the selector put back to OFF) |
| Captain HUD, stowed | "HUD: blank" (the panel says it dark: `INI_HUD_LEFT_STATE` 0) | stowed draws nothing |
| Captain HUD, deployed (`INI_HUD_LEFT_STATE` 1) | "Ground speed: 0", "Selected altitude: 1500", "QNH: 1012", "Speed scale: 40, 60", "Altitude scale: 000, 005", "Heading scale: 21, 22, 23", "ILS", "0.7 NM", all green | as drawn; no FMA on the ground; stowed again after |

No misread: nothing to fix. The HUD handle's own tooltip names the click (its value 1 reads "STOW" in the control map), while MSFSBA's
HUD Units panel names the state, 1 "Deployed" (`A350PanelLayout.Names`), which is what drew the HUD here. The battery type
(`INI_BATTERY_TYPE`, 0 here) is plan 23's OIS option; the BAT display was read with it as set.

The SFD's attitude (its horizon and roll scale) is graphics with no text, so the SFD reads no pitch or bank. The PFD reads both from its
horizon shape (`A350Pfd`, `A350ShapeReader.AngleDegrees`); the SFD's horizon can be read the same way, but its pixels per degree and its
zero need captures at several pitches and banks, which the stand cannot give. Left for this plan's flight (Task 1 Step 6): captures of
the SFD climbing, level and turning, then the read added test-first.

## The flight (2026-11-03, 06:03-06:54 local, VCBI circuit, 1.2.8)

Re-entered FMS after the previous day's flight and the plan 22 ATC test (fms_setup: INIT, FUEL&LOAD, RWY 04, ILS Z 04, the OIS's T.O
PERF sent to the FMS and confirmed): CLB and NAV armed, the PFD's speed target V2 148 before the roll, SRS at the take-off, phase 3. The
route, KAT place/bearing/distance points: PBD02 (KAT/360/25), PBD03 (KAT/320/60), PBD04 (KAT/260/70), PBD05 (KAT/220/45), then GOPRU, LIKRA,
cruise FL200. The OIS's T/D pause came 10 NM before PBD04 and was said ("Top of descent pause. ... Resume with RESUME FLIGHT on the OIS.",
the scratchpad's PAUSED AT TOD, SIM PAUSED). The approach phase came only 12 NM out at 249 kt (the straight-in from PBD05): a left orbit at
2000 ft slowed it, APPR, LOC and G/S from below, autoland (VLS 120, F 132, managed VAPP 122: about 150 t), A/BRK MED.
Sim attitude logged every 0.35 s beside each SFD capture (scratchpad `att_log.py`), the ND's TO waypoint and what the probe said every
4 to 5 s (`wpt_log.sh`), the probe now handing the ND's draw to the definition as MainForm does (every row unmuted).

### Task 1 Step 6: the next waypoint in flight

| TO waypoint on the ND (first read) | Said (probe) | Where |
| --- | --- | --- |
| PBD02 (before the log) | 06:04:28 "Next waypoint PBD02" | 1030 sequenced passing 1030 ft |
| PBD03 06:09:14 | 06:09:17 | turn at PBD02, 28 degrees left |
| PBD04 06:16:38 | 06:16:41 | after a heading pulled right for the SFD and a DIR TO PBD03 |
| PBD05 06:32:03 | 06:32:04 | descending turn at PBD04 |
| GOPRU 06:39:17 | 06:39:16 | |
| LIKRA 06:50:56 | 06:50:57 | GOPRU passed with the ND in PLAN (no TO line); said when ARC came back |
| VCBI04 06:51:30 | 06:51:31 | the same, after the BTV try in PLAN |
| IKONA 06:53:11 | 06:53:11 | in the flare: the missed approach's first point after the threshold |

Each passage said once; a PLAN detour (the ND draws no TO waypoint there) neither repeated the waypoint nor lost the change. Said also,
for the owner to weigh (the row is off by default): on the ground while the flight plan was entered, "Next waypoint VCBI" (05:52:44) and
"Next waypoint 1030" (05:53:58); in the flare, "Next waypoint IKONA"; after the landing, with the plan cleared, "Next waypoint PPOS".

### Task 4: the SFD's attitude and heading

| Capture (fixture) | Sim: pitch, bank, magnetic heading | SFD read (after the fix) |
| --- | --- | --- |
| Stand (`ground_SFD`, 2026-10-27) | 0.29 down, level | 0.4 down, wings level, 219 (the PFD's 219) |
| Climbing (`p08_sfd_climb`) | 8.29 up, 2.32 right, 351.4 | 8.3 up, 2 right, 351 |
| Level FL200 (`p08_sfd_level`) | 1.20 up, 0, 298.3 | 1.2 up, wings level, 298 |
| Turning left (`p08_sfd_turn_left`) | 4.67 up, 28.35 left | 4.7 up, 28 left |
| Turning right (`p08_sfd_turn_right`) | 1.34 up, 24.73 right, 327 turning | 1.3 up, 25 right, 326 |
| Descending turn (`p08_sfd_descent_turn`) | 0.39 down, 24.6 left, 163.3 | 0.4 down, 25 left, 163 |
| Inbound 040 (`p08_sfd_inbound_040`) | 0.47 down, 0.3 right, 041.7 | 042 (before the fix: no heading) |

The ladder: labels every 10 degrees 49 px apart (4.9 px a degree) at local x 116, each 8 px under its line, the horizon at y 125.0 at zero
pitch; the draw places the ladder by the pitch, then turns it about the aircraft symbol (144, 125.1) by the bank, so a label's local place
gives the pitch and the turn between two labels the bank (within 0.05 degrees of pitch and 0.03 of bank on every capture). Two heading
misreads found: the index is the yellow mark centred at x 142 (the stand's 145 read every heading 1.3 degrees high), and from 000 to 080
the scale's labels are one digit, so on the 040 inbound the SFD read no heading. In the right turn the ladder's lower 20 crossed the heading
row (165.7, 239.0); a label drawn turned is no heading label. Fixed in `1cff66915` and `b0e8df59f`.

### Task 2 Step 1 at VCBI (first airport, 1.2.8)

- **The map:** PLAN at the 2 NM zoom step (the map's label 1 NM) drew VCBI's ANF 45 NM out, in the cruise and the descent (phases 5 and 6),
  centred on the airport: runway 04 "(landing)" from the FMS arrival, "Landing runway: 04, 3350 M, WET-5", and the exits on it with their
  distance from the threshold as `A350NdMap` reads them: E 220 m, D 900, C 1420, B 2430, A 3120, END 3140.
- **SET LDG RWY** on runway 04, already the landing runway: nothing changed on the ND.
- **The autobrake:** the A/BRK pushbutton (row `A350_LDG_AUTO_BRK`) armed it in flight, the FMA `BRK MED`, cyan; `INI_AUTOBRAKE_LEVEL` and
  `INI_AUTOBRAKE_ARMED` read empty through the probe.
- **SET BTV EXIT:** grey on B in the cruise with A/BRK off; in the approach phase with A/BRK armed grey on B and C, offered on A and D.
  Pressed on A (8 NM out) it armed nothing: the FMA stayed `BRK MED`, the landing braked MED (`BRK MED, green`, `DECEL`), no BTV countdown.
  Pressing a grey item does nothing.
- **The LANDING tab** (the function panel, 749,993 closed): MODIFY RWY, LDG PERF CODE (WET-5), the runway with its ROW LDG DIST, and a field
  `BTV EXIT AT: none M FROM THRESHOLD`, grey on the ground after the landing with no active flight plan.
- Open: why A armed nothing (the order, A/BRK before or after the exit, the field above in flight, or the ANF's data without Navigraph);
  the second airport with Navigraph signed in. Next flight: try the LANDING tab's field and the order, then Steps 2-6.

### Task 3: the taxi turn lead (2026-11-03)

Step 1 as written cannot tell the A350 from the A330: VirtualPilot builds its `TaxiGuidanceManager` with the default lead (1.2 s; nothing
in `tools/VirtualPilot` sets `TurnLeadSeconds`), its `PilotModel` has no airframe (one yaw law for every aircraft), and the A350's lead
is the A330's (1.7 s), so the two runs would be one. Measured instead on the A350 itself, as the Fenix's leads were (`FenixA320Definition`:
the heading swept after the tone centred over the yaw rate then): on runway 04 a scripted tiller (`AXIS_STEERING_SET`, scratchpad
`a350_steer_step.py`) held at a fixed deflection until the yaw rate was steady, then centred at once; the heading the aircraft kept
sweeping over the yaw rate (the sim's `ROTATION VELOCITY BODY Y`) at the centring is the airframe's share of the lead.

| Speed | Tiller | Yaw at centring | Swept after | Run-on |
| --- | --- | --- | --- | --- |
| 17 kt | 0.30 | 1.9 deg/s | 1.6 deg | 0.83 s |
| 23 kt | 0.30 | 2.3 | 2.0 | 0.88 |
| 27 kt | 0.25 | 2.2 | 1.9 | 0.85 |
| 32 kt | 0.25 | 2.6 | 2.1 | 0.81 |
| 34 kt | 0.20 | 2.2 | 1.7 | 0.80 |
| 21-33 kt | 0.10-0.15 | 1.0-1.2 | 0.7-0.9 | 0.68-0.77 |

(The first step, 13 kt and 0.8 deg/s, too small to read: 0.38 s. Idle thrust carried the speed from 13 to 34 kt.) The scripted taxi of
2026-10-30 agrees: across 30 minutes at 15 to 21 kt and up to 14 deg/s, the yaw rate followed the tiller 0.9 to 1.0 s late (correlation
0.99 and 0.92, the heading's own rate lagging a further ~0.13 s). So the A350's airframe keeps turning about 0.8-0.85 s after the
tiller centres; with a pilot's reaction to the tone (about half a second) the lead it needs is about 1.3-1.4 s, under the 1.7 s it has.
On the Fenix the turns that centred with too long a lead (2.1-2.4 s) ended 12-20 degrees short. Left at 1.7 s for the owner's taxi by
ear (Step 2) with 1.35 s to compare; the test now pins the A350's own 1.7 (`TaxiTurnLeadTests`, plan 01's M6).

The take-off after this test was rejected at 81 kt: the step script's last centring of the steering axis was lost and the nosewheel held
3.3 degrees right through the roll (read back with the pedals neutral). The scratchpad take-off script now centres the axis and refuses
unless the nosewheel reads centred, brakes on a reject, and never sets climb thrust after one.

### Other things the flight showed

- After the landing the After Landing flow's flaps item timed out: the scratchpad landing script had stopped with the levers at reverse
  idle (its idle step comes below 40 kt, and the aircraft stopped first). Levers to idle, flaps up by hand; the script now sets idle when it
  stops. Then a placement on stand A6 and the Parking flow.
