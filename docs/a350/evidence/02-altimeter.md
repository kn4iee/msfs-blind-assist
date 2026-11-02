# Plan 02: what each baro write does, by LINKED INSTR (A350 1.2.8)

Measured in flight 2026-11-02 at FL200 over the sea off Colombo, AP 1 and A/THR on, through `A350Probe repl` (build of `4fdb3c968`). Every step
read `XMLVAR_Baro1_Mode`, `XMLVAR_Baro2_Mode`, `XMLVAR_Baro_Selector_HPA_1`/`_2`, `KOHLSMAN SETTING MB:1`/`2`/`3`, `INI_BARO_PRESET_CPT`/`_FO`
with `_SHOW`, `INI_LINKED_INSTRUMENTS` after 2 s (the preset steps after 0.2 to 0.4 s). The owner's state was restored: LINKED INSTR NONE, both units
hectopascals, the ISIS on its baro.

## The option has three settings

The OIS's OPTIONS > AIRCRAFT > LINKED INSTR is a select, not on and off: `INI_LINKED_INSTRUMENTS` 0 NONE, 1 CPT & FO, 2 CPT/FO & ISIS.

## What moves what

Starting from both sides on STD (mode 3), hectopascals, all three at 1013.25 unless the row says otherwise.

| Step | 0 NONE | 1 CPT & FO | 2 CPT/FO & ISIS |
|---|---|---|---|
| ISIS short write `16510 3 r (>K:2:KOHLSMAN_SET)` | MB:3 1031.88 alone | MB:3 alone | MB:3 alone |
| ISIS knob click, after the write | MB:3 1032.88, sides stay STD | MB:3 1032.88, sides stay STD | **both sides leave STD**, all three 1032.88 |
| Captain's knob on STD | captain's preset shows (first click 1013, then steps); FO's does not show | the same: the copy is not of the preset | the same |
| First officer's knob on STD | FO's preset shows; captain's does not | the same | the same |
| Captain's pull | captain's side only, from its preset | **both sides**, from the captain's preset | **both sides and the ISIS** |
| First officer's pull | FO's side only | **both sides**, from the FO's preset | **both sides and the ISIS** |
| Captain's or FO's push | that side only | **both sides** | **both sides and the ISIS's own STD** |
| On QNH: a side's short write alone | that side | that side alone (nothing copies a bare write) | that side alone |
| On QNH: a side's click | that side, +1 | **both sides**, the clicked side's value | **all three**, the clicked side's value |
| On QNH: the ISIS's click | MB:3 | MB:3 | **all three** |
| On QNH: direct `KOHLSMAN_SET` 1 alone | holds, the PFD draws it | holds, the FO stays | holds, the others stay |
| A side's unit button | that side | **both sides** | **both sides** |
| A side's click in inches | +0.31 hPa, that side | both sides | all three |
| MSFSBA's `A350_CAPT_QNH_SET` 1040 on QNH (real timing) | 1040, captain only | 1040 on both | 1040 on all three |
| MSFSBA's `A350_FO_QNH_SET` 1030 on QNH | 1030, FO only | 1030 on both | 1030 on all three |

So on 1.2.8 a click from a short write lands one step past it, as it should. The spoken result of the last two rows was "Captain altimeter set 1040
hectopascals" and, for the copy, the background "First officer altimeter 1040 hectopascals", said just before it (the double this plan removes).

## The ISIS has its own STD, and that is what the first flight hit

Toggled by its STD/CHANGE key (`A350_ISIS_KNOB_BUTTON_1`), read only from its display (the SFD's `STD` line against its `Baro:` line, `A350_DISP_SFD`).

- While the ISIS is on STD, `KOHLSMAN SETTING MB:3` holds 1013.25 and **nothing moves it**: not a direct write, not a click, not MSFSBA's row ("ISIS QNH did not
  take 1020. ISIS altimeter set 1013 hectopascals", at every timing tried from 100 ms to 3 s, and the correction after it). The setting underneath survives: the key
  brought back 1020.
- With LINKED INSTR at 2, a side's push puts the ISIS on STD with them, and the ISIS's key taken off STD takes both sides to QNH with it.
- **It stays on STD when the option goes back to NONE or CPT & FO.** With LINKED INSTR at 0 or 1, a side's pull or push does not touch it, so only its own key gets it back.

This is the flight of 2026-10-31: the option was at 2, both sides were on STD, the ISIS with them, so its click took both sides to QNH and every later
write to the ISIS read back 1013.25. It is not a 1.2.8 change to the clicks.

## Other things measured

- A preset goes out of view within 8 s (`_SHOW` 0) but its value is kept (`INI_BARO_PRESET_FO` still read 1014), and a later pull uses it: a first officer's pull with the
  option at 2 set all three to 1014.25 from a preset 40 s old.
- A pull keeps the setting's fraction: preset 1015 from 1013.25 gives 1015.25, which the cockpit floors to 1015.
- The probe's `a` read now and then returns nothing (0.00 in the raw log); each was gone at the next read. Not the aircraft.

## Task 4: the FCU knobs turned from outside MSFSBA (fixed build, in flight)

Each knob clicked through `A350Probe rpn` as hardware sends it (`AIRLINER_FCU_SPD_KNOB`, `_HDG_KNOB`, `_ALT_KNOB`, `_VS_KNOB`, both baro knobs), what
MSFSBA said 3 s later (the probe's `say`). Before the fix speed, heading and V/S said nothing; altitude and both baros spoke.

| Knob | Said |
|---|---|
| Speed +1, +1, -1, -1 | (first, a baseline), "Speed 252 knots", "Speed 251 knots", "Speed 250 knots" |
| Heading +1, +1, -1, -1 | (baseline), "Heading 272 degrees", "Heading 271 degrees", "Heading 270 degrees" |
| Altitude +1, -1 | (baseline), "Altitude 20000 feet" |
| V/S -1 (dashed: a preview), +1 | (baseline), "Vertical speed 100 feet per minute"; the preview's lapse back to dashes said nothing |
| Captain's and first officer's baro on STD | "Captain altimeter preset 1013 hectopascals", "First officer altimeter preset 1013 hectopascals" |
| MSFSBA's own typed speed 260, then 250 | "FCU speed 260, selected", "FCU speed 250, selected": once each, the window not again |
| A quiet minute in cruise afterwards | nothing |

The first change after the probe connected is the window's baseline (the shared FCU call-out's settle), as MainForm's start is.

## Task 5: the window in the cockpit (fixed build)

Through the real Altimeter window (Ctrl+B, `hotkey FCUSetBaro`, the box's own Enter), speech as the probe's `say` heard it:

- In flight, both sides STD in hectopascals, at each setting: 30.5 typed: "QNH must be between 900 and 1100 hectopascals." and nothing moved.
- 1033 typed on STD: at 0 "Captain altimeter preset 1033 hectopascals", "First officer altimeter preset 1033 hectopascals", "ISIS altimeter set 1033 hectopascals";
  at 1 the captain's preset and the ISIS; at 2, with the ISIS on its own STD, the captain's preset and "ISIS not set: it is on STD.". Both sides stayed on STD.
- The window's QNH button then set 1033.25 on both sides (and the ISIS at 2).
- 1020 typed on QNH: one confirmation per target; at 1 "Captain altimeter set 1020 hectopascals" and the ISIS's, the first officer's copy not said; at 2 the captain's
  alone, all three at 1020.
- The QNH button pressed 4 s into an entry (first build): the captain's stepper said "…shows no preset here, as on the ground…" and "Captain altimeter left STD"; fixed
  and run again: "First officer altimeter preset 1040 hectopascals", "ISIS altimeter set 1040 hectopascals", "Captain altimeter 1040 hectopascals".
- On the ground at VCBI, at 0: 1009 typed on QNH set all three ("Captain altimeter set 1009 hectopascals", the first officer's, the ISIS's); 29.92 refused; 1013 restored all
  three. On 1.2.8 the preset shows on the ground as well (a click showed 1013 on STD, at a standstill).

Not fixed here: pulled from STD, a baro first says its old setting, then the new one ("Captain altimeter 1013 hectopascals", "Captain altimeter 1033 hectopascals"):
the mode arrives a batch before the setting. The call-out that would hold it is in `IniBuildsA350Definition.Announcements.cs` (plan 11's): handed off.

## What this decides for the entry

- The sides: at 1 and 2 the captain's side alone (its click or its pull copies), as question 2's answer allows now the copy is shown.
- The ISIS at 2: no target of its own (the captain's click copies it, the captain's pull brings it); on STD its click would take the sides off STD.
- The ISIS at 0 and 1: its own target, on STD as well, since its write and click leave the sides alone. **This differs from Task 2's text** ("linked: the captain's side only"), which
  assumed one "on" setting; at 1 nothing else would ever set it.
- An ISIS known to be on STD takes no target at any setting (nothing would move it), and the entry says so.
