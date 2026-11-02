# Plan 04: the ECAM on 1.2.8

Measured 2026-11-02 on the iniBuilds A350 1.2.8 at VCBI and on one flight from runway 04 back to its ILS Z (VCBI to VCBI, FL200, about 40
minutes: a scripted take-off, the AP from 45 ft, a cruise west over the sea, an autoland), through `A350Probe` built from plan 03's and 04's
commits with `A350_LOG=1`. Captures are `A350Probe ecamwatch` (2 Hz, each change once two reads agree) and `draw live CTR` with shapes.

## Task 1: the memo swap and the cabin-ready repeats

- **The watch:** `ecamwatch 1800` from FL110 in the climb through about 15 minutes at FL200, the descent and the approach (the cruise was cut to
  15 minutes to stay inside the A350's memory growth on one flight); the sim window was captured by PrintWindow at the start and set to be
  captured within 0.15 s of any memo printed in a colour not its own.
- **Memos shown:** WXR/PWS OFF (amber, the radar switched off on the SURV page for this), GND SPLRs ARMED (green), SEAT BELTS (green); no COMPANY
  MSG (it needs a company datalink message, which cannot be made offline).
- **Result:** 183 E/WD states, every one with WXR/PWS OFF amber, GND SPLRs ARMED and SEAT BELTS green; no swap, so no capture beyond the steady one.
  Every swap heard on 2026-10-31 had COMPANY MSG in it. The finding is neither (a) nor (b) for certain: the reader paired these three memos with
  their own fills for 30 minutes; whether COMPANY MSG's swaps were the reader's or the aircraft's needs a flight with a company message.
- **Cabin ready:** `INI_CABIN_IS_READY` read 0 for the whole flight (no CABIN READY memo, no cabin report) and turned 1 only when the OIS's panel
  states were set afterwards; the 10-31 repeats did not occur.

## What raises COMPANY MSG (2026-11-02, at the gate, with the owner's go-ahead for the datalink)

- The memo follows `INI_COMPANY_MSG_RECEIVED` (variable 703), set only by the A350's own datalink client: `AOCHandler::addCommMessage` (a message
  received on the datalink, the same function that sets `AcarsLogonActive` on a logon), `AOCHandler::SendCompletedLoadsheet` (the final
  loadsheet delivered, from `AOCHandler::UpdateAOC` after 180 or 360 s) and `addMessage`. The OIS's COMPANY COM inbox does not raise it: five unread
  messages there (METAR, TAF, a flight report) stood through the whole flight with no memo.
- The datalink was set up already (the owner's SimBrief ID, SayIntentions key and Hoppie logon filled in, read as lengths only; CPDLC SOURCE SAY
  INTENTIONS, whose app was not running), and `AcarsLogonActive` read 0.
- Tried with CPDLC SOURCE set to HOPPIE for the test: no ACARS logon came by itself; a D-ATIS update and a loadsheet request (OIS COMPANY COM,
  REQUESTS, LOADSHEET; then LOADSHEET, REQUEST FINAL LOADSHEET) raised nothing in 10 minutes. The cockpit has no free telex to send to its own
  callsign. CPDLC SOURCE was set back to SAY INTENTIONS, the radar to AUTO.
- So COMPANY MSG needs a message from outside: a telex sent to the aircraft's callsign by another datalink client, or a flight under SayIntentions
  that sends company messages (the 2026-10-31 flight's source).

## Task 2: the colour hold (owner's question 4)

Built for both causes (a ruling: the cause was not found): a memo-column line whose only change is its colour is said once it has held 3 s, and
not at all when it reverts; the main column's lines (procedures, cautions) keep speaking at once (`A350MemoHoldTests`).

## Task 4: the reversers

From the autoland's landing roll (`ecamwatch 900`): before touchdown the engine block had no REV; on the roll "REV" green over each engine (x 256
and 504, y 120) with "MAX MAX" as the thrust limit; after the stow, TOGA and no REV. The reverse came on at touchdown (137 kt), reverse idle at 43 kt,
idle at 39 kt; the A/BRK MED stop took about 30 s. The in-transit (amber) state was shorter than the reader's agreement and was not caught.
Fixtures: `p04_ctr_rev_approach.json`, `p04_ctr_rev_deployed.json`, `p04_ctr_rev_stowed.json`; the amber cases are built from the deployed capture.

## Task 5: flight phases and every SD page

`INI_EWD_FLIGHT_PHASE`, read every 5 s through the flight and every second through the shutdown and the panel states:

| Value | When |
|---|---|
| 1 | powered, engines off (after cold and dark and power back) |
| 12 | engines running on the ground: at the gate, lined up, stopped after landing |
| 10 | the take-off roll (102 kt) and the landing roll (112 kt) |
| 6 | just after lift-off (259 ft) |
| 7 | climbing (667 ft) |
| 8 | from about 2,000 ft through the climb, the cruise and the descent to final |
| 9 | on final, below about 650 ft |
| 11 | the landing roll below about 40 kt |
| 13 | the engines shut down (ON GROUND POWER), kept through cold and dark until power returns as 1 |

The short phases between (engine start, take-off power, 80 kt) fell between samples; an engine start after the panel-state jumps did not light
the engines (the jumps left the E/WD full of system faults), so the start phases are still to be read on a real start (plan 05's or 17's).
`INI_flight_phase` stayed 1 the whole flight (see "Other findings").

**SD pages**, on the ground (engines running, at the stand) and at FL200: each read as drawn, one fixture per page and place
(`p04_ctr_sd_<page>_<ground|air>.json`). Two misreads in flight, fixed test-first:
- the door page draws CAB V/S in flight; its value read as a bare "400": now "Cabin vertical speed: 400 FT/MIN, green";
- the STATUS key with nothing for STATUS drew another page under STATUS's number: the CRUISE page's values in cruise (read as fifteen bare
  numbers), the WHEEL page on the ground with BRAKES PARK BRK ON (its brakes read under "Status page"). Now "Status page, showing the Cruise page"
  and that page's names.
The ECP row's 13 and 15 are no page (13 stays on WHEEL; 15 selected CRUISE in flight).

## Other findings (for other plans)

- **Take-off (plan 05/07):** the take-off was the 2026-10-30 recipe (50 % N1 to N2 78 on the brakes, then FLX with FLEX 67 and V-speeds from the
  OIS's T.O PERF sent to the FMS). The FMA after the AP engaged at 45 ft read V/S +1000 and MAN FLX, not SRS, and `INI_flight_phase` stayed 1 for
  the whole flight; the ALT knob's pull gave OP CLB. With phase 1 the autoland flew G/S and LOC to touchdown with LAND3 SINGLE shown but no LAND,
  FLARE or ROLL OUT modes, and touched down at 146 kt, 1 NM past the threshold (the script retarded the levers at the wheels). Why SRS did not
  engage this time is open.
- **Surveillance (plan 07):** the SURV page came up with ALT RPTG and ADS-B RPTG OFF (TCAS STBY); ALT RPTG ON took TCAS out of STBY.
- **Repositioning:** placing the A350 on the ground wiped the F-PLN, and left "CHECK IRS/AIRPORT POS" in the scratchpad (the ND kept its GPIRS
  position). Placed with A/THR engaged it ran to 96 % N1 against the parking brake (now in the probe's notes).
