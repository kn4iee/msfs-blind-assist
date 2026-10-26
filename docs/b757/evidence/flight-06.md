# Plan 06 flight measurements

Flown by Daniel Wolak (plan 06's owner), BlueBird 757-200 RR, CYYZ → CYOW (flight 356), 2026-10-26, cruise FL290.
Reads by the simconnect MCP (`L:` reads through MobiFlight).

## M8: `L:BBS_VNAV_TOD`

| When | `L:BBS_VNAV_TOD` | Captain's PROG 1/3 |
|---|---|---|
| Cruise, about 97 NM direct from CYOW, 437 kt GS | 118.39569 | — |
| Some minutes later (several NM further on) | 118.39569 (unchanged) | DEST CYOW DTG 117, 1653Z, fuel 3.9; TO T/D 1633Z / 10 NM; ECON SPD .735 |

It does not count down: it is not the distance to T/D or to the destination. The T/D was then 107 NM from CYOW along
the route (117 − 10). Most likely it is the T/D's fixed position along the route, measured from the origin (session 8:
429.24 at the gate with a 568 NM route, a T/D 139 NM from KDEN, which fits). Unconfirmed: the total route distance
of this flight was not read.

For D and Shift+D (Task 3): read the PROG page (DEST DTG and ETA; TO T/D time and distance), as planned; do not use
`BBS_VNAV_TOD`. PROG 1/3's layout as seen: line 1 TO (next waypoint) DTG ETA FUEL, line 2 NEXT, line 3 DEST, line 4
ECON SPD and TO T/D ("1633Z/ 10NM"), line 6 POS REPORT and POS REF.
