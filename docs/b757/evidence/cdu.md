# BBS 757 CDU: reading the screens and pressing keys (measured 2026-10-25)

## Screen text: the Comm Bus, both CDUs, full grid

The systems WASM sends each CDU's screen on the sim's Comm Bus whenever it changes: event `BBS_CDU_DISPLAY_1`
(captain) and `BBS_CDU_DISPLAY_2` (first officer). The hidden helper page `bbs-tablet.js` (Coherent view
"VCockpit28 - BBSTablet") listens and forwards the captain's frames to MobiFlight's WinCtrl socket
`ws://localhost:8320/winwing/cdu-captain`; nothing serves that socket on this PC, so nothing else consumes them.

A read-only listener injected into that view receives both, with no port and no MobiFlight Connector involved:

```js
var l = RegisterViewListener('JS_LISTENER_COMM_BUS', function () {
  l.on('BBS_CDU_DISPLAY_1', function (json) { /* captain */ });
  l.on('BBS_CDU_DISPLAY_2', function (json) { /* first officer */ });
});
```

Each payload is a JSON string in MobiFlight's WinCtrl CDU format: `{"Target":"Display","Data":[...]}`, 336 cells =
14 rows × 24 columns, row-major. A cell is `[]` (blank) or `[char, color, size, inverse]`: color a letter (`g` green
seen so far), size `0` large / `1` small, inverse `0`/`1`. Characters include `°`. Measured: the IDENT page on power-up
(both CDUs), POS INIT 1/4 after LSK R6 (captain only; the first officer's stayed on IDENT, so the two are independent),
a typed scratchpad "K", the scratchpad message "NAV DATA OUT OF DATE" (bundled nav data, see below).

```
          IDENT
 MODEL           ENGINES
757-200           PW2037
 NAV DATA         ACTIVE
B572503001 MAR20APR16/25
           FEB20MAR19/25
 OP PROGRAM      CO DATA
4618BBSR02C07     BB1001
 OPC             DRAG/FF
8912RA422WL4   +0.0/+2.0
------------------------
<INDEX         POS INIT>
NAV DATA OUT OF DATE
```

Delivery: only on change (no frame arrives while a page is static), and the first frame came when AC power came up
(APU generator). The listener must therefore keep the last frame per CDU. A listener attached late still gets the
**captain's** current screen at once: the helper's custom element `<bbs-tablet>` keeps it as
`lastMobiFlightCduPayload` (read live: a 2,500-character JSON string; its other fields are `mobiFlightCduSocket`,
`mobiFlightCduConnected`, `mobiFlightCduReconnectTime`). The **first officer's** screen has no such cache: it arrives
on its next change only, so the app must hold the listener from aircraft load or provoke a harmless redraw. The view and listener are lost on an aircraft reload (re-inject on every load). Line 13 is the
scratchpad. The Coherent debugger can be held by another process (seen 2026-10-25: port bound, no answer until the
owner killed some processes); the app's existing Coherent clients already handle that case.

## Keys: held L:var pulses

Every key is an L:var the cockpit template holds at 1 while pressed: `L:B757_CDU_<n>_<KEY>` with `<n>` 1 (captain) or
2 (first officer) and `<KEY>` one of `L1`-`L6`, `R1`-`R6`, `INIT`, `RTE`, `DEPARR`, `ATC`, `VNAV`, `FIX`, `LEGS`,
`HOLD`, `FMCCOMM`, `PROG`, `EXEC`, `MENU`, `NAVRAD`, `PREV`, `NEXT`, `NUM0`-`NUM9`, `NUMPER` (.), `NUMPLUS` (+/-),
`KEYA`-`KEYZ`, `KEYSP`, `KEYDEL`, `KEYSLASH`, `KEYCLR` (all 69 per CDU from the cockpit XML).

Measured over the MobiFlight calculator path (each write with a unique `<n> 0 *` prefix, [SIM-17]):
- `1` then `0` about 0.3 s apart: LSK R6 worked; four letters typed this way were either missed or wiped by the
  next test, unproven.
- `1`, hold 0.25 s, `0`: a letter appears every time tried (K), CLR removes one character.
- CLR held 1 s: clears the whole scratchpad including a message, as on the real CDU. **So the hold length matters**:
  the app must hold a short, fixed time (0.15-0.25 s, to measure) and never hold CLR long by accident.

## Open for the CDU plan

- The shortest reliable hold and the fastest safe key rate (typing a route). Planned: gap plan 08 Task 2 measures it
  (spec-gaps M10, R76) and records it in `flight-08.md`.
- Colors other than green (`w`, `m`, `c`, `a` …) and inverse cells: read them on a page that has them (LEGS with a
  modified route, the EXEC light).
- The EXEC light and the MSG / OFST / DSPY annunciators: look for L:vars in the control map.
- Nav data: the FMS runs the bundled AIRAC 2503 ("NAV DATA OUT OF DATE"); the EFB's AIRAC page / Navigraph sign-in
  updates it. The app's own navdata may then disagree with the FMS until the pilot updates.
