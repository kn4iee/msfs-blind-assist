---
paths:
  - "tools/a350-probe/**"
  - "tools/a350-gen/**"
  - "tools/A350Probe/**"
  - "MSFSBlindAssist/Aircraft/A350/**"
  - "MSFSBlindAssist/Aircraft/IniBuildsA350*.cs"
  - "MSFSBlindAssist/Forms/A350/**"
  - "tests/MSFSBlindAssist.Tests/A350/**"
  - "MSFSBlindAssist/Resources/coherent-a350-ois-agent.js"
  - "MSFSBlindAssist/Resources/coherent-a350-fap-agent.js"
  - "tools/a350-ois-test/**"
---
# iniBuilds A350 rules

Loaded when Claude reads matching code. Background: docs/inibuilds-a350.md. Full text of each rule: docs/invariants/inibuilds-a350.md.

- [A350-1] Never write into the sim's memory: read the A350's displays from a read-only copy run through its own draw code, and operate them only through the sim's inputs (L:var writes, input events, the Coherent WASM_MOUSE_* route). Full: docs/invariants/inibuilds-a350.md#a350-1
- [A350-2] Copy the A350's memory by listing only its own allocation's regions, then reading them back-to-back into one buffer (about 8 ms); walking the whole process while reading tears the copy across frames. Full: docs/invariants/inibuilds-a350.md#a350-2
- [A350-3] Press an ECP or KCCU key the aircraft resets itself with one write of 1 and no 0 after it (a lone calculator write or the key's own input event): a 1 then 0 sent together land in one frame and do nothing. A key it never resets (KCCU ND, MFD) is held: 1, about 180 ms, then 0. Full: docs/invariants/inibuilds-a350.md#a350-3
- [A350-4] The ND takes mouse input only while the KCCU cursor screen is 1 and the MFD only while it is 2: move the cursor with the KCCU ND or MFD key before a Coherent mouse event. Full: docs/invariants/inibuilds-a350.md#a350-4
- [A350-5] Name a control's positions from its own tooltip expression (which value selects which INI.TOOLTIPS key), never from an assumed numbering: ANN LT counts .1 TEST from value 0, BAT SELECTOR counts .0 EMER 1 from value 0. Full: docs/invariants/inibuilds-a350.md#a350-5
- [A350-6] Type into an OIS field as key events (keydown, keypress, input, keyup per character, then Enter); a bare value set plus input/change shows in the field but COMPUTE ignores it. Full: docs/invariants/inibuilds-a350.md#a350-6
- [A350-7] A key whose input event only animates it while its click code flips the state (the HUD declutter keys) is a selector on that state, written as the click code writes it (`ClickWrites`); its `_Set` moves nothing, so never take a `_Set` that animates for a working press. Full: docs/invariants/inibuilds-a350.md#a350-7
- [A350-8] Tell the two OIS views apart by the page each loads (`ini-efb-a350-cpt` / `-fo`, the side probe in `CoherentPmdgEfbClient.ForA350Ois`), never by their VCockpit number. Full: docs/invariants/inibuilds-a350.md#a350-8
- [A350-9] Any exe hosting the A350 draw engine (Wasmtime) must keep `<CETCompat>false</CETCompat>`: Wasmtime leaves a trap with a non-local jump that a CET shadow stack fast-fails (0xC0000409), so one torn copy would kill MSFSBA. After a trap, `A350DrawEngine` starts a fresh instance (a trap leaves the wasm globals mid-call). Full: docs/invariants/inibuilds-a350.md#a350-9
- [A350-10] Never let a draw allocate from the copy's own heap: `A350ModulePatcher` answers malloc, calloc, realloc and the memaligns from `A350DrawEngine`'s head room and makes free a no-op (a torn copy's free lists trap dlmalloc), in each signature it knows; one it does not know runs, logged. Full: docs/invariants/inibuilds-a350.md#a350-10
- [A350-11] Bump `A350ModulePatcher.Version` whenever the patch changes: `A350ModuleCache.CachePath` keys the compiled module by it as well as the module's hash, and keyed by the hash alone a module compiled from the older patch is loaded and the change silently never applies. Full: docs/invariants/inibuilds-a350.md#a350-11
- [A350-12] A password input always, and while the OIS's STREAMER MODE is on a field labelled ID, KEY, LOGON, PASSWORD or TOKEN, is named by its length only (`A.isSecret`); its value still reaches the EFB shell, masked in a password box: never speak, log or capture it. No OIS fixture keeps a credential (`secrets.test.js`). Full: docs/invariants/inibuilds-a350.md#a350-12
- [A350-13] Plan an Altimeter entry as one entry from one read (`A350BaroEntry.Plan`), its targets by LINKED INSTR: at 1 and 2 the captain's side alone (its click or pull copies), the ISIS its own target only at 0 and 1; at 2 never click the ISIS while the sides are on STD, as its click takes them off STD. Full: docs/invariants/inibuilds-a350.md#a350-13
- [A350-14] The ISIS has its own STD, toggled only by its STD/CHANGE key and read only from the SFD's draw: while on it, nothing written moves its setting (KOHLSMAN SETTING MB:3 holds 1013.25), so `A350BaroEntry` plans no ISIS target then and says so, never a write that reads back unchanged. Full: docs/invariants/inibuilds-a350.md#a350-14
- [A350-15] Hold what the reader takes from the copy's memory against the A350's own L:vars at every start (`A350FactsCheck`, `A350DisplayService.CheckFacts`): a disagreeing table withholds what it disproves for the session and says so once (the variables' part keeps the displays reading); never make one silent, keep no verdict between sessions. Full: docs/invariants/inibuilds-a350.md#a350-15
- [A350-16] An MFD text reaches the pilot only through `A350MfdWording` (the window's `A350MfdPage.Shown`, a message's `A350MfdWording.Message`): no colour word; amber on a field says "required" (once), yellow "temporary" (never beside TMPY), magenta "constraint", any other colour nothing. `A350MfdPage.Read` keeps its colours for the reader's logic. Full: docs/invariants/inibuilds-a350.md#a350-16
- [A350-17] An A350 display window's control is the panel's own row (`A350WindowRows`): pressed through `PressRow` or `PressToggleFromWindow`, typed through the row's plan, labelled from `TryDescribeControlState`, the row armed as the window's echo; a window never writes a variable itself and says nothing on open, refresh or close. Full: docs/invariants/inibuilds-a350.md#a350-17
