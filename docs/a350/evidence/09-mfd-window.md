# Plan 09: the MFD window

## Task 2: the role words (2026-11-03, offline)

The window's shown line is `A350MfdPage.Shown`: a field and the scratchpad end with ", edit", a list opener reads as a combo box,
collapsed or expanded, a grey key ", dimmed"; the reader's own lines keep their words (the probe, the scripts, the ND and mailbox windows
read them). Openers by their element class: `DropdownVariable` (DEPARTURE's RWY, SID, TRANS), `MainHeaderUIButton` (the page headers'
ACTIVE, POSITION, SEC INDEX, DATA), `MainHeaderDropdownMenu` (INIT's MODE, the FMS 1 selector); `BaseUIButton` is a plain key.

## Task 5 Step 2: the ATC COM pages in the air (2026-11-03, VCBI circuit, climbing through 8000 ft to FL100, no CPDLC connection)

Every REQUEST key, grey on the ground but CLEARANCE, is live in the air. Each opens a list beside it:

| Key | List (grey = not offered) | Typed |
| --- | --- | --- |
| VERTICAL | CLIMB TO, DESCEND TO, ALT/FL (grey), BLOCK ALT/FL, CRUISE CLIMB, ITP (grey) | CLIMB TO: "120" read back `REQUEST CLB TO: FL 120`, drawn grey; XFR TO MAILBOX then offered |
| LATERAL | DIRECT TO, OFFSET, WX DEVIATION, HEADING, TRACK, SID/STAR, TAILORED ARRIVAL, REROUTING (grey) | HEADING: "250" refused, no message |
| SPEED | SPEED, SPEED RANGE (grey) | SPD/MACH refused 250 (no message), 220, 300, 300KT and M.80 (ENTRY OUT OF RANGE) at 249 kt, 10,000 ft |
| WHEN CAN WE EXPECT | HIGHER LEVEL, LOWER LEVEL, CLIMB TO, DESCEND TO, CRUISE CLIMB, SPEED, SPEED RANGE, BACK ON ROUTE: all grey | |
| OTHER | FREETEXT, VOICE CONTACT, OWN SEPARATION & VMC, VMC DES: all grey | |
| CLEARANCE | GENERIC (grey), DEPARTURE, OCEANIC | |
| ADD TEXT | DUE TO WEATHER, A/C PERFORMANCE, TURBULENCE, TECHNICAL, MEDICAL, AT PILOTS DISCRETION, FREETEXT: all grey | |

REPORT & MODIFY draws its title alone (the capture's other texts are hidden pages'). EMER reads `EMER button, amber`. The refused entries
and the all-grey lists are the aircraft's on 1.2.8 with no CPDLC connection; nothing the window could change.

Three window reads to fix (fixtures `p09_mfd_atc_*`):

- A list hanging **beside** its key (VERTICAL's, at x 485 left of the keys at 680) reads interleaved with the other keys: "CLIMB TO, VERTICAL,
  DESCEND TO, LATERAL, ...". The reader puts a list after the control it hangs **under** or over; one beside it found no owner.
- A field just clicked, its cursor in it, reads "SPD/MACH: required required, cyan".
- Digits typed into it read with a space: "SPD/MACH: 25 0, cyan".
