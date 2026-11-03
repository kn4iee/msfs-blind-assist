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

## Task 2: STBY RAD NAV (2026-11-03, 1.2.8)

- The NAV page draws "STBY RAD NAV" as its title (x 330, y 104), beside no key, and its LS and VOR keys grey while
  `L:INI_CPT_RADIO_MODE` is 0. iniBuilds' RMP guide (forum topic 24959) says to press the NAV key, then "(5) Standby Radio
  Navigation (STBY RAD NAV) key". The 1.2.8 cockpit has no such key: the behaviour file has no STBY or RADIO_MODE component (RMP
  keys are NAV, the keypad, the line and activation keys, the page keys and the audio part), and the module only reads the variable.
  As on 1.2.7, no cockpit control turns it on: a sighted pilot clicking where the guide points gets nothing.
- MSFSBA's row (`A350_RMP_STBY_RAD_NAV_n`, writing the variable) and the window's line still work on 1.2.8: On made LS and VOR white
  and opened their pages, Off greyed them again; each said ("Captain STBY RAD NAV: On").
- Question 9 is the owner's: keep the row (the documented function, reachable no other way), or drop it as a shortcut.

## Task 3: Output N's receivers (2026-11-03, 1.2.8, on the stand)

- The MFD's POSITION/NAVAIDS reaches the stock receivers at once: VOR 2 typed 116.30 put `NAV ACTIVE FREQUENCY:2` on 116.3 (no
  signal), its ident KAT (DUPLICATE NAMES, the first) back on 114.1 with KAT received; VOR 1 116.30 the same on NAV 1; the LS typed
  110.30 put NAV 3 on 110.3 with IKE received. Output N then said "VOR 1: 116.30, no signal. VOR 2: 114.10, KAT, KATUNAYAKE, DME 1.4
  nautical miles" with the LS from the PFD.
- The RMP's STBY RAD NAV VOR page reaches the stock receiver too (VOR2 116.30 course 240: `NAV ACTIVE FREQUENCY:2` 116.3, `NAV OBS:2`
  240; the MFD shows them grey). Its LS page does not, as on 1.2.7: 109.50 typed showed on the RMP, on the MFD's LS (grey) and on
  the PFD's frequency row, while NAV 3 stayed on 110.30 (IKE). The PFD then drew 110.30 on its ident row and 109.50 under it, and N
  said "LS: 110.30 109.50, 1.0 NM, magenta", as drawn. Nothing for MSFSBA to change: N reads the LS as the PFD shows it; that the
  backup LS never reaches the receiver is the aircraft's.
- Put back: STBY RAD NAV off, VOR 1 and 2 on KAT, the LS on VCBI's 109.90 (IKW).

## Task 4: the RMP pages on 1.2.8 (2026-11-03)

Every page a fixture pins re-captured from RMP 1 by its own keys (scratchpad `rmp_recap.sh`, fixtures `p10_rmp_*`): VHF, HF, MENU,
DATALINK ROUTER, INIT, COM STATUS, VHF PROVIDER, NAV off and on, LS, VOR, SQWK. Each reads as on 1.2.7, line for line and key for key
(`Each_page_reads_alike_on_1_2_8`); what differs is state: the frequencies tuned, and the transponder in AUTO, so the squawk line
reads "SQUAWK : 2000" with no "- STBY" and the SQWK page "AUTO, grey". The probe's new `rmplines` reads a saved RMP draw.
