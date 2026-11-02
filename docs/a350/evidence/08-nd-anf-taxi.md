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
