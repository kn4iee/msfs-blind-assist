# A350 Every Display Decoded Implementation Plan (plan 24)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Everything the A350's displays draw is said (owner, 2026-11-03: "the pfd, nd, standby stuff, sd, and all other displays, to have everything decoded no matter what"; "take the noise, all of it, and denoise it, keeping everything if possible, or only dropping crap that absolutely is stupid"): every text, glyph, sprite and shape on the PFDs, NDs, HUDs, the SFD and SND, the E/WD and SD, the MFDs, the FCU, the RMPs and the BAT display, in words, with its colour, and only true junk dropped (a label half clipped at a tape's edge, background art, a symbol font's key legends).

**Architecture:** Measure, then close the gaps display by display. Two tools measure: `tools/a350-probe/coverage.py` lists each drawn text whose words the display's reading never says, across a corpus of rounds (`A350Probe displays` with `A350_DRAW_JSON`, one folder per round); `tools/a350-probe/sprites.py` cuts every drawn sprite out of its texture onto numbered contact sheets with where and how often it is drawn. Each gap is either read (a reader change with a fixture from the corpus and a test watched failing first), turned into words (a glyph: `°` degrees, `!C` °C, a dot leader a colon, an MFD `D` suffix degrees), or listed as junk with its reason in this plan's ledger. A symbol whose meaning is its place on a scale (a tape's green dot, a VFE, an ILS diamond) is read as the value the scale gives it.

**Tech Stack:** C# 13 / .NET 10, xUnit; Python 3 with Pillow (`tools/a350-probe`); `tools/A350Probe displays`.

**Spec:** the owner's words above; `docs/inibuilds-a350.md` (how each display is read); the 2026-11-03 corpus (139 rounds over two flights, ground to FL200, the approach, both gear travels).

## Global Constraints

- The index's "Shared conventions" apply; [A350-1] (never write the sim's memory) above all.
- Every item said carries its colour where the display gives one (owner: colours everywhere).
- A reading never invents: a symbol not yet understood is said by what it is ("magenta diamond at 2 dots right") before it is left out.
- Plans 03, 04, 07, 08, 09, 10, 14 and 15 own the readers this plan changes; all have landed and none is open, so this plan changes them for its own tasks (index, "File ownership") and says so in its record.

## Review Focus

- A scale's number half drawn at the tape's edge said as a value ("26" for 260): dropped only when clipped (Task per display).
- A symbol read on one flight phase and missing on another (ground, climb, approach): each display's test runs every corpus phase it has.
- A comma-decimal culture (de-DE, sv-SE) on every new number said.
- The same item said twice by two readers (the PFD's and the HUD's FMA): each display says its own once.

---

### Task 0 [controller]: Claim this plan

As every plan's Task 0 (the index's workflow): check the remote, set the record below, commit `docs(a350): claim plan 24` with the `Plan: 24` trailer, rebase and push before Task 1.

### Task 1: The tools

`coverage.py` and `sprites.py` in `tools/a350-probe` with usage lines, the README and `docs/tooling.md`; a test of each one's arguments in `test_package.py`'s style.

### Task 2: HUD

The ILS frequency, the altitude box, GS, the speed and altitude scales cleaned (no clipped labels), the pitch ladder, every HUD sprite.

### Task 3: SFD and SND

The ILS frequency and course, the tapes' scales, the attitude scale, the compass rose's labels, every sprite.

### Task 4: FCU and RMP

Every FCU display text (the windows' previews, the EFIS key legends' states), the RMP's STBY label and every RMP field.

### Task 5: PFD

Every sprite of `PFD/PFDA350Elements.png`, `CautionTapes.png`, `SpeedTape.png`, `AltTape.png` and `AHScale.png`: the speed tape's symbols as speeds (green dot, S, F, VFE next, VLS, VMAX, alpha prot and max, the trend), the altitude tape's target and the attitude's symbols, the ILS deviations, the gear.

### Task 6: ND

Every sprite of `ND/NDElements.png` and `CompassArc.png`: waypoints, navaids, airports, the aircraft, the pseudo-waypoints (T/C, T/D, decel), traffic, the vertical display's symbols; every map label said with its symbol.

### Task 7: E/WD, SD and MFD

What `coverage.py` lists there after the above (the MFD's markers and suffixes, the SD's remaining sprites).

### Task 8 [sim]: Passes

A live pass of every display on the ground, in climb, cruise and approach, comparing what is said with the coverage tools' lists; each gap left is read or ledgered as junk.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** landed, waiting on a flight for Task 8's last items
- **Claimed by:** Francesco Tissera, with Claude, 2026-11-03
- **Worked by:** Claude (Opus 5.5), inline
- **Started:** 2026-11-03, from `dc5118f57`
- **Landed:** 2026-11-03
- **Tests at landing:** the A350 tests 2,233 passed; `tools/a350-probe` 12 (`coverage.py`'s 6)
- **Tasks:** 1 (the tools, and `A350Probe reread`), 2 (HUD), 3 (SFD, SND), 4 (FCU; the RMP's only gap was a false hit), 5 (PFD), 6 (ND
  path symbols), 7 (SD symbols; the E/WD's and MFD's texts were already covered) done, each change measured on the whole corpus and
  live on the ground at VCBI; the ledger (`docs/a350/ledger/24-displays.md`) lists what is read, what is no gap, what is junk and why
- **Waiting on the owner:** none; on the next flight: the ND's sprites (the second flight's NDs drew none), the HUD's flight path vector,
  the SND's needles with a navaid tuned
- **In-sim results:** the ledger; `docs/a350/evidence/flight-2026-11-03.md` (the corpus's flights)
- **Changed from the plan:** the RMP and the MFD needed nothing; the HUD's green dot, caution strips, F and S stay with the PFD's (no
  reference to fit their offsets); `A350Probe reread` added so a reader change is measured without the sim
- **Rulings made while working:** symbols named from the phase they were drawn in, the manual and the FlyByWire A32NX's guide where the
  iniBuilds manual names none (the level-off arrows, the intercept point, the linear deviation); an unconfirmed one (the ND's circled 1
  and 2, F/CTL's small circles) is left on the ledger, never guessed; the FCU's EFIS key legends are junk (static); cost if wrong: one
  symbol misnamed, or one left unsaid
