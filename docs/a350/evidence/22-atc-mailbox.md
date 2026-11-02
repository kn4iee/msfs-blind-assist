# Plan 22: the ATC mailbox, one real exchange

Measured 2026-11-02 on the iniBuilds A350 1.2.8 at VCBI, parked at stand A6 after a landing (engines off, APU on), with the OIS's
CPDLC source set to SayIntentions and SayIntentions running with the owner signed in. The owner's go-ahead: "after you land you might as
well check everything on ATC COM on the FMS to see if a real clearance goes through, say departure clearance, and the page layout and
all that, and print it out." Nothing was typed into a logon or credential field: SayIntentions' own sign-in carries the link.

The pages were read with `A350Probe repl`'s `lines` (the MFD window's reader), the mailbox with `A350Probe mailbox` (the mailbox
window's `A350Mailbox.Read` and `A350MailboxInput`), the printer's paper over the Coherent debugger, and `INI_ATC_MSG_*` with
`A350Probe sample` every 100 ms. MSFSBA itself was not running: what it says is replayed in tests on the live values.

## The request

FMS INIT: FLT NBR UAE111 (SayIntentions' callsign for this flight), FROM VCBI, TO VRMM. ATC COM, REQUEST, CLEARANCE (GENERIC and
OCEANIC grey on the ground), DEPARTURE: DEPARTURE ARPT, GATE, D-ATIS CODE, DESTINATION and STATION are required (amber), ACFT TYPE
comes as A359. Typed VCBI, A6, X (SayIntentions' ATIS was information X-ray), VRMM, VCBI; XFR TO MAILBOX turned white.

## The exchange

| Time (sim) | Mailbox | What happened |
| --- | --- | --- |
| 2036Z | TO VCBI: REQUEST PREDEP CLEARANCE UAE111 A359 TO VRMM AT VCBI STAND A6 ATIS X (on cyan) | SEND: SENDING |
| 2038Z | FROM VCBI: DEPART REQUEST STATUS . FSM 2030 261102 VCBI UAE111 RCD REQUEST BEING PROCESSED STANDBY IF NO RESPONSE IN 2 MINS REQUEST NEW FLIGHT IN APP (PGE 1/3 to 3/3, OPEN, CLOSE) | `INI_ATC_MSG_LIGHT` went to 1 at 21:31:19 local |
| 2039Z | FROM VCBI: CLRD TO OMDB. BASU2D DEPARTURE. MNTN 2500. SQUAWK 1441. (MSG 2/2, OPEN; WILCO, STANDBY, UNABLE) | the clearance: SayIntentions' own flight plan (OMDB), not the request's VRMM |
| | PRINT | the paper below |
| | WILCO, then SEND | WILCO on cyan, SEND and CANCEL in WILCO's and STANDBY's places; SENDING; the light went out |

The printout (both printer views, read over the Coherent debugger):

```
   MAILBOX MESSAGE RECEIVED                              DATE : 02 NOV 26
                                                          TIME : 20:40
    FROM: VCBI
   MSG:
      CLRD TO OMDB. BASU2D DEPARTURE. MNTN 2500. SQUAWK 1441.
```

MSG RECORD, ALL MSG then listed each message as a button ("2039Z FROM VCBI CLRD TO OMDB. BASU2D DEPARTURE. MNTN 2500. : >>>") with
ERASE ALL and PRINT * (grey); its EXPAND page read "2039Z FROM VCBI WILCO" over the clearance, with RETURN TO LIST and PRINT. Later
lists held the downlinks too ("2046Z TO VCBI REQUEST LOGON", "2048Z TO VCBI LOGOFF", "2043Z TO VCBI REQ ATIS VCBI") and the earlier
company message ("2032Z FROM INIB MSFSBA1 FLIGHTREPORT").

## The reply keys

Four more requests to SayIntentions, each answered the same way (status, then the same clearance):

| Key | Mailbox | MSG RECORD |
| --- | --- | --- |
| WILCO + SEND | WILCO on cyan, then SENDING with WILCO | WILCO |
| STANDBY + SEND | STANDBY on cyan, then SENDING with WILCO (the aircraft's own line) | STANDBY; RECALL showed the message SENT and OPEN with only CLOSE and CANCEL: no WILCO or UNABLE is offered after STANDBY |
| UNABLE + SEND | UNABLE on cyan (UNABLE stays a key), then SENDING with WILCO | WILCO: the aircraft records an UNABLE as WILCO |
| CLOSE, RECALL, PRINT, both arrow pairs | as named | — |

MSFSBA's press acts once: STANDBY at (1545, 84.5) was pressed and the CANCEL drawn there after it was not. ACCEPT and REJECT never
showed: no uplink on the ground offered them. CANCEL was not pressed on an uplink.

## The light and the aural

`INI_ATC_MSG_SOUND` drives the module's `atc_message` aural (`sound.xml`). Sampled every 100 ms through the fourth request:

- `INI_ATC_MSG_LIGHT` flashes about 0.56 s lit and 0.6 s dark from the moment an unanswered uplink is on the mailbox until it is
  closed or answered.
- `INI_ATC_MSG_SOUND` is 1 for about 1 s, first 10 s after the light starts, then every 10 to 11 s while it flashes.
- `INI_ATC_MSG_CMD` stayed 0 throughout.
- The clearance that arrived behind the open status message added no new cue: the light kept flashing. Closing the status message put
  the light out at once, and the clearance then waited OPEN on the mailbox with the light dark; WILCO and SEND moved nothing. A fifth
  request with the status closed before the clearance came showed the same: the clearance never lit the light.

What MSFSBA says: the glareshield's two ATC MSG lamps read `(L:INI_ATC_MSG_LIGHT) 1 ==` and are one light, so the lamp speech says
"ATC message light on" once a flashing run starts and "ATC message light off" once it has stayed dark 3 s. Replayed on the live
timing in `A350UplinkCalloutTests` (a run said once, two runs said in order). Task 2 (an uplink call-out of its own) is therefore not
needed: its condition, that MSFSBA says nothing with the window closed, is not met. A sighted pilot gets no more than this either: a
queued uplink behind an open one lights nothing.

## The mailbox as MSFSBA reads it

Captured with the display service (the mailbox window's own capture path) and pinned in `A350MailboxTests`: a three-page status
message (`p22_ctr_mailbox_status.json`: header green, body white, MSG 3/3, OPEN cyan, PGE 1/3, CLOSE), the clearance
(`p22_ctr_mailbox_clearance.json`: WILCO, STANDBY, UNABLE, no SEND) and WILCO picked (`p22_ctr_mailbox_wilco_staged.json`: "WILCO, on
cyan", SEND, CANCEL, UNABLE). Every state read right; nothing to fix in `A350Mailbox`.

## The rest of ATC COM

- **CONNECT.** NOTIFY TO ATC (typed VCBI), NOTIFY: "NOTIFIED TO VCBI AT 2046Z", green. ACTIVE ATC stayed empty for three minutes; DISCONNECT
  ALL sent LOGOFF (2048Z) and NOTIFY came back. Minutes later the page showed ACTIVE ATC: VCBI and ADS-C CONNECTED, and the mailbox's idle
  lines read "ACTIVE ATC: VCBI" and "ADS OFF": SayIntentions' connection arrived after the logoff. MODIFY MAX UPLINK DELAY, the ADS-C
  and ADS-C EMERGENCY options read.
- **REQUEST.** On the ground only CLEARANCE is enabled (VERTICAL, LATERAL, SPEED, WHEN CAN WE EXPECT, OTHER, ADD TEXT grey), connected or not.
- **REPORT & MODIFY.** Its title only, connected or not.
- **MSG RECORD.** ALL MSG and MONITORED MSG; the list and EXPAND above.
- **D-ATIS.** Three rows (VCBI DEP, VRMM ARR, an empty ARR) from the F-PLN. SEND REQUEST on VCBI brought SayIntentions' ATIS at 2043Z:
  "{VCBI BANDARANAIKE INTL COLOMBO ARPT, INFO X-RAY. 2010Z. ARVG AND DPTG RWY 04. WIND CALM. VIS 10 KM. SCT AT 1700. TEMP 25, DP 25.
  QNH 1011. TRL 130. ADVS ON INITIAL CTC YOU HAVE INFO X-RAY." UPDATE OR PRINT opens UPDATE, AUTO UPDATE (grey), PRINT, AUTO PRINT; PRINT
  printed "D-ATIS METAR REPORT" with the whole text. The "{" is SayIntentions' (it is on the paper too).
- **EMER.** Read only: ADS-C EMERGENCY ARMED/OFF, MAYDAY, PANPAN, CANCEL EMER, CLIMBING TO, DESCENDING TO, DIVERTING/VIA, OFFSETTING,
  REQUEST VOICE CONTACT, ENDURANCE & PERSONS, ADD FREETEXT. Nothing pressed.

## Found for plan 09 (the MFD window)

On the D-ATIS page once an ATIS is in (capture `p22_mfd_datis.json`, through the display service as the MFD window reads it):

1. The ATIS's fifth line, drawn as "INITIAL CTC YOU HAVE INFO .......", is not read as a line; it becomes the label of the airport fields
   below it: "INITIAL CTC YOU HAVE INFO 1: VRMM" and "INITIAL CTC YOU HAVE INFO 2: none" where they read "Airport: VRMM" and
   "Airport: none" before the ATIS came.
2. With UPDATE OR PRINT's menu open, "menu open" is said on the `>>>` button, not on UPDATE OR PRINT.
3. On the DEPARTURE REQUEST form, ACFT TYPE, DESTINATION and STATION each read twice: an "empty" line above the value.

These are `A350MfdPage`'s (plan 09's file); left for that plan, which is next but one in order.
