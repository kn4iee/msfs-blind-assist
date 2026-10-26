# Plan 05 flight measurements

Flown by Daniel Wolak (plan 05's owner), BlueBird 757-200 RR, CYYZ → CYOW (flight 356), 2026-10-26. Reads by the
simconnect MCP (`L:` reads through MobiFlight), the pilot pressing in MSFSBA's windows.

## M2: Speed Intervene in a VNAV climb

Before the press (VNAV climb, LNAV and VNAV lights on, A/T armed, SPD and THR lights off):

| Variable | Value |
|---|---|
| `L:BBS_IAS_BLANKED` | 1 (window blank) |
| `L:B757_MCP_VNAV_BUTTON` | 1 |
| `L:BBS_AUTO_THROTTLE` | 1 |
| Shift+S (today's readout of `L:BBS757_ORANGE_AIRSPEED_BUGS`) | "250": the bug held behind the blank window |

Speed Intervene pressed from the Ctrl+S window (Alt+I, `L:B757_MCP_IAS_MACH_KNOB_BUTTON` 1 then 0), at 12,230 ft,
262 kt IAS, +3,960 ft/min:

| Variable | After |
|---|---|
| `L:BBS_IAS_BLANKED` | 0 (window open) |
| `L:BBS757_ORANGE_AIRSPEED_BUGS` | 261 (about the current IAS, not the 250 held before) |
| `L:B757_MCP_VNAV_BUTTON` | 1 (VNAV stays engaged) |
| `L:BBS_AIRSPEED_IN_MACH` | 0 |

Findings: Speed Intervene opens the window with the current speed and keeps VNAV (Task 2's "the speed box opens again
when Speed Intervene opens the window" holds); while the window is blank, Shift+S today says the stale bug ("250"),
which Task 2's `SpeedReadout` replaces with "MCP speed window blank. Speed Intervene opens it."

### M2 in the VNAV descent

Just after T/D (28,440 ft, 285 kt, M 0.73, −2,070 ft/min): `BBS_IAS_BLANKED` 1, `BBS_AIRSPEED_IN_MACH` 0 (back in
IAS; whether VNAV changed it at T/D or the pilot's Speed Intervene press after M1 did is not known), VNAV target
`BBS757_ORANGE_AIRSPEED_BUGS` 286.69, while the drums behind the blank window still held **283** (the last MCP
speed): in VNAV the hidden window and the flown target differ. Speed Intervene pressed at 26,116 ft, 286 kt,
−1,700 ft/min: `BBS_IAS_BLANKED` 0, bug **287** (the VNAV target rounded), drums 2/9/8 = 287, VNAV stays on. Same as
the climb: intervene opens the window on the speed VNAV was flying.

## M3: the altitude selector push (inconclusive)

VNAV descent through 20,786 ft at −2,070 ft/min, MCP altitude 1,500, VNAV on, ALT HOLD off. The pilot pressed
"ALT SEL Push" on the Glareshield MCP panel: the app sent `1 (>L:B757_MCP_ALT_SEL_PUSH,bool)` then 0 (12:36:31),
the variable read 0 after. VNAV stayed on; the captain's LEGS page after the press: LANRK 293/13530, VISOL 240/6900,
ULAGU 237/5340, DEMKO 210/5000, VOLAG 210/4430, unchanged.

Inconclusive: those odd altitudes (13530, 5340, 4430) look like VNAV path predictions, which the CDU shows in small
font, not constraints, and the aircraft was already on the path with nothing ahead to level it off, so there was
nothing to delete. The CDU window does not say which entries are small font (a constraint or a prediction): that is
plan 09's (CDU presentation) to carry. To repeat with a case that can show an effect: at cruise with a lower MCP
altitude before T/D (the real aircraft starts an early descent), or in a climb under an "at or below" constraint.

## M1: Mach changeover, readout and knob steps

Level at FL290 (28,999 ft, 282 kt, M 0.73), VNAV and LNAV engaged, the window opened by Speed Intervene.

**The drums.** `B757_MCP_Gau_SPD_AUTO_100/10/1` are wheel positions: the hundreds wheel reads 10·d, the tens and
units wheels read 10·(d+1) (so 0 reads 10, 8 reads 90). This corrects `spec-gaps.md` §3.4's "0 reads 10" note, which
was the same rule seen at 200 kt. In IAS at 283 kt they read 20/90/40.

**The changeover.** VNAV did NOT switch to Mach by itself at FL290 (flag 0). The pilot's IAS/MACH press (Alt+M) set
`L:BBS_AIRSPEED_IN_MACH` 1. In Mach: `L:BBS_TARGET_MACH` stayed **0** (it does not track the target: do not read it),
`L:BBS757_ORANGE_AIRSPEED_BUGS` became the target's IAS equivalent (281.1, fractional), and the drums showed the Mach:
hundreds wheel = tenths digit (70 = 7), tens wheel = hundredths (40 = 3), units wheel constant 8 (unused in Mach):
".73". Read Mach from the drums: tenths = `_100`/10, hundredths = `_10`/10 − 1.

**Steps** (`K:AP_SPD_VAR_INC/DEC`, one calculator call per burst; the drums move 1-2 s after the call):

| Burst | From | To | Change (hundredths) |
|---|---|---|---|
| 1 up | .73 | .74 | +1 |
| 5 up | .74 | .79 | +5 |
| 10 down | .79 | .69 | −10 |
| 20 down | .69 | .40 | −29 (stopped at .40: linear would be .49, the tiers −30) |
| 30 up | .40 | .95 | +55 |
| 10 down, 10 down, 2 down (restore) | .95 | .73 | −10, −10, −2 |

Findings: one step is 0.01 Mach; bursts of up to 10 are linear; longer bursts speed up like the knots (B757-3:
events 1-10 one each, 11-25 two each; 26-30 gave three each here, or the target stopped at a .95 ceiling: one run
cannot tell). The floor is **.40**. There is NO ceiling at the aircraft's .86: the target reached **.95**, so the app
must clamp its own range (0.40-0.86) and never send a burst that could pass it. `Bbs757Mach.Bursts` can reuse
`SpeedBursts`' tiers in hundredths; correct in linear bursts of at most 10.

Transport: MobiFlight drops a calculator string byte-identical to the one before it (SIM-17): a second identical
"10 DEC" burst was lost until prefixed. The app's `ExecuteCalculatorCodeUnique` already prevents this. The MCP tool
also refused a 400-character string of 20 separate events ("negative count"); the app's loop form
(`SpeedBurstCode`) works.

## M6: the SPD button in flight

Pressed from the Ctrl+S window in the VNAV climb, then read at 16,180 ft, 300 kt IAS (M 0.61), +2,920 ft/min:
`L:B757_MCP_SPD_BUTTON` 0 (no light), `THR` 0, `FLCH` 0, `VNAV` 1, `L:BBS_AUTO_THROTTLE` 1, `L:BBS_IAS_BLANKED` 1,
`L:BBS757_ORANGE_AIRSPEED_BUGS` 304.375 (a fractional, computed VNAV target). With VNAV engaged the SPD press did not
engage A/T speed mode (the pilot heard no change). Still to see: SPD with VNAV off (FLCH or V/S), and whether the
window was re-blanked by the SPD press or by a second Speed Intervene press (pilot to confirm).

Task 1's caption: `BBS_IAS_BLANKED` was never delivered until it was made `IsAnnounced` (only a Continuous AND
announced var joins the batch); fixed in 2b78ba47f the same morning. In flight after the press, the caption read
"Window open" (the pilot, by screen reader): in-sim check passed.
