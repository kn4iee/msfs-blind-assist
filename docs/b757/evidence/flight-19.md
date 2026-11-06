# Plan 19 measurements: the ILS and VOR variables, the EFB side menu at night, and the lighting sources

2026-11-05, in cruise at FL320 (the owner's flight), BlueBird 757-200 (PW) after BlueBird's 2026-10-31 update, BBS Nest
running, MSFSBA closed. Run by Claude. Writes through the simconnect MCP's calculator path in `mode: "execute"` with a
unique `<n> 0 *` prefix; reads through the Coherent debugger in the `BBSTablet` view (`tools/coherent-eval.ps1`) and the
L:var snapshot tool (`docs/b757/tools/live/snap.sh`). Every write was put back.

## 1. The ILS and VOR variables

BlueBird's public simvars table (support.bluebirdsim.com/docs/simvars; 1,235 L:vars, "checked against source as of 21
October 2026") lists `BBS_ILS_COURSE` (read/write, integer degrees 0-359) and the ILS knobs' step requests
`B757_ILS_FREQ_WHOLE_INC` / `B757_ILS_FREQ_FRAC_INC` (write +1/-1, "aircraft reads the request and clears it to 0"), which
plan 04 already uses, and no frequency read-back. The snapshot tool's 2,173 names (built before the update) hold no
frequency either. The updated `BBS752.wasm` (29,765,919 bytes, 2026-10-31) names six more, none in the public table:
`BBS_757_ILS_FREQ`, `BBS_757_ILS_CRS`, `BBS_757_VOR_L_FREQ`, `BBS_757_VOR_L_CRS`, `BBS_757_VOR_R_FREQ`,
`BBS_757_VOR_R_CRS`.

Read live, each about 1 s after the write before it:

| Step | `BBS_757_ILS_FREQ` | `BBS_757_ILS_CRS` | `BBS_ILS_COURSE` |
|---|---|---|---|
| at cruise, ILS parked | -1 | 0 | 0 |
| `1 (>L:B757_ILS_FREQ_WHOLE_INC)` (PARK → 108) | 108.1 | 0 | 0 |
| `95 (>L:BBS_ILS_COURSE)` | 108.1 | 95 | 95 |
| `1 (>L:B757_ILS_FREQ_FRAC_INC)` | 108.15 | 95 | 95 |
| fraction -1, whole -1, course 0 (put back) | -1 | 0 | 0 |

So `BBS_757_ILS_FREQ` is the ILS control panel's frequency, -1 while parked, following each click at once, and agrees
with plan 04's click model (PARK +1 gives 108 with the remembered .10; the fraction ring .10 → .15). `BBS_757_ILS_CRS`
mirrors `BBS_ILS_COURSE`. The stock `NAV ACTIVE FREQUENCY:1..4` read 110.50 throughout (BlueBird's receivers are its own).

VORs, FMC auto-tuned: `BBS_757_VOR_L_FREQ` 114.5 and `_R_FREQ` 115.1, then 116.05 and 116.4 about 40 minutes later as the
FMC retuned; `B757_MCP_VOR1_MODE_AUTO` and `VOR2_MODE_AUTO` 1 (the public table: 1 automatic, 0 manual tuning);
`BBS_757_VOR_L_CRS`/`_R_CRS` and `BBS_VOR_L_COURSE`/`_R_COURSE` 0. Not measured: what a VOR's `_FREQ` reads when it is
not tuned, and whether MSFS 2020 installs carry the variables (BlueBird ships one package for 2020 and 2024).

The owner, the same day: on BlueBird, a second press of the yoke A/P disconnect silences the disconnect warning.

## 2. The EFB side menu on a dim tablet

The owner reported "The tablet did not show …" after side-menu taps whose page had in fact switched, too sporadic to
test by hand. debug.log of 2026-11-05 held four: Cabin & doors (18:34:53, 19:04:12), ACARS dashboard (18:58:21), Ground
services (19:04:04); each read the app alone ("Aircraft", "Navigation") for the 4 s the press runner waits, then a
second press found the section.

Reproduced with the probe on the owner's open pop-out, `tour 2 Aircraft Navigation` (navigation taps only), saving each
page's first picture (`PROBE_TOUR_SAVE`): 7 of 14 side-menu taps and both app pages (each reopened on a section) read with
no section, the right name coming 4.4 to 6.3 s after the tap or not at all. `reread` of the 16 saved pictures through the
app's reader missed the same 9 pages every time: the miss is in the picture, not in the timing.

Measured on the pictures (luminance 0.299 R + 0.587 G + 0.114 B, the minimum over x 246-250 at each entry's row, as
`ParseMenu` measures): the tablet was dim (white text 117.6-121.1 of 255, AUTO brightness at night); raw, the selected
entry's band edge reads about 2.5 and the others 7.5-8.2. `Bbs757EfbPixels` scales the white to 255 (× 2.11-2.17), so
the selected entry stands about 10.7 to 13.6 from the median of about 16.9, against `ParseMenu`'s fixed threshold of 12,
while the unselected entries differ among themselves by about 1.5. The 16 pictures and their read lines are the
fixtures `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb/dim2/`.

## 3. The lighting sources

No 757 FCOM procedure text could be read online. Sources, BlueBird first (User Procedures v1.01, line numbers of the
pdftotext extraction, UP):
- UP 3210-3216 preflight: POSITION LIGHT ON, ANTI-COLLISION / WING LIGHTS OFF, LANDING LIGHTS OFF, LOGO LIGHT AS REQUIRED;
  UP 3096 "Verify runway turnoff lights OFF"; UP 5266 BEACON ON before start.
- UP 5862-5863 before takeoff: "For runway crossing or line-up, turn the WING, RUNWAY TURNOFF and TAXI lights ON. For
  takeoff, turn the STROBE, WING, LANDING and RUNWAY TURNOFF lights ON."
- UP 6327-6328 climb: passing FL180 leave BEACON and POSITION on, LOGO as appropriate, "extinguish the other departure
  lights" ("an operating convention for the guide's example").
- UP 6588-6589 approach: "Use the nose landing light as a landing-clearance reminder and runway turnoff lights according
  to the approach-clearance".
- UP 7304-7305 after landing: off STROBE, WING, left/right LANDING and RUNWAY TURNOFF, LOGO as required; "Retain or
  select the lighting needed for the actual taxi".
- UP 7468, 7499, 7533 shutdown: beacon off after the engines; turnoff off; POSITION ON, the others OFF.
- IFALPA briefing leaflet 09AGEBL01 (2008, with EUROCONTROL and the FAA):
  https://ifalpa.org/media/1997/09agebl01-use-of-external-lights-to-mitgate-runway-incursion-risk.pdf (taxi light on
  while taxiing; logo on before starting; turnoff lights on for taxi-in).
- AeroSavvy, Ken Hoke, a former 757/767 captain: https://aerosavvy.com/airplane-lights/ (taxi and turnoff lights on the
  nose strut; the nose light on at landing clearance; strobes on in cruise).

BlueBird's one nose switch is the map's "Taxi Light" (`B757_OVHD_btn_lights_taxi`, state `L:B757_OVHD_NOSE_LANDING_LIGHT`),
which the manual calls TAXI at line-up and the nose landing light on the approach. Plan 19 Task 7's table is the decision.
