# Plan 10: the RMPs

## Task 1: the NAVAID audio selector and IDENT (2026-11-03, 1.2.8, on the stand at VCBI, KAT 114.1 tuned and in range)

**The RAD NAV selector** (`AIRLINER_RMP_RAD_NAV_MODE_n`, `L:INI_RMP_RAD_NAV_MODE_n`: ADF 1, ADF 2, LS, VOR 1, VOR 2, MKR). On each of the
three RMPs, through MSFSBA's panel row, every position set and read back, and each was said ("Captain RAD NAV: VOR 1", "First officer
RAD NAV: MKR", "RMP 3 RAD NAV: ADF 1"). No navaid is heard on any position: stock `NAV SOUND:1` to `:3`, `ADF SOUND:1` and `:2` and
`MARKER SOUND` stayed 0 throughout, with the VOICE reception pulled and INT/RAD on RAD too. The aircraft has no navaid audio on 1.2.8:
its module names the selector's variable and nothing else (no ident event, no `NAV SOUND`), and `sound.xml` plays only the knob's
click on it. A sighted pilot hears none either; MSFSBA adds none. The RMP screens show nothing of the selector.

**IDENT.** The manual's RMP IDENT is the transponder's ("the RMP only allows setting the code and transmitting the IDENT", p. 75). On
1.2.8 the SQWK page reads `AUTO, grey`, `SQWK: 2000, cyan`, `*IDENT, cyan`; `*IDENT` is the third left-hand key (ADK3, x 43, y 514).
Pressed from the panel row: stock `TRANSPONDER IDENT:1` on for about 18 s; the key read `IDENT` (no star) for about 2 s; the RMP's
message `TRANSMITTING IDENT, white` stayed until MSG CLR, long after the ident ended (the manual's key 4 clears RMP messages).
`L:INI_TCAS_IDENT_COMMAND` stayed 0.

- From the RMP window, Enter on the `*IDENT` line pressed it (the page's line keys), and read back `IDENT, cyan. TRANSMITTING IDENT,
  white`. The window had no key for it from another page: Alt+T types a squawk but leaves the RMP on its page.
- Fixed: Alt+I, the A380 window's IDENT key, presses the SQWK page and then IDENT (`A350Rmp.IdentKeys`; ADK3 on another page is
  VHF3's swap) and says the RMP's message (`IdentReadBack`). Live from the VHF page on RMP 1, then on RMP 2 and RMP 3: each switched to
  SQWK, `TRANSPONDER IDENT:1` came on, and the window said `TRANSMITTING IDENT, white`.
- `p10_rmp_sqwk_ident.json`: the 1.2.8 SQWK page with the message, pinned.
