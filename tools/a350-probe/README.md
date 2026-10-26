# iniBuilds A350 probes

Live investigation tools for the iniBuilds A350 (1.2.7 and later). They read the A350's memory out of the running sim and run the A350's own draw code on that copy, so every WASM display can be read as text; and they drive the displays over the Coherent debugger. Background and every measured address: [docs/inibuilds-a350.md](../../docs/inibuilds-a350.md). They grew out of the 3 October 2026 investigation of 1.2.6.

Needs Python 3.14+ (standard library only), Node 24+, and the sim running with the A350 loaded. The package path defaults to the MS Store Community folder; set `A350_PACKAGE` otherwise. Run everything from this folder.

**Nothing here writes into the sim's memory.** Operating the cockpit goes through the sim's own inputs: L:var writes and the Coherent mouse route. Generated files (`*.wasm`, `*.cwasm`, `*.bin`, `*.json`) are built from your own installed A350 and are git-ignored; never share them.

## Once per A350 version

```bash
python vtables.py            # vtables.json: C++ class per vtable
python make_importsigs.py    # importsigs.json
python patch2.py             # a350-all.wasm (hooked scratch module) + ui_draws.json
python beh.py .              # a350_inputevents.json (the 696 cockpit controls) and a350_refs.json
```

`varenum.py` holds the `VariablesEnum` name-table address and `fastlocate.py` the anchors; re-derive both after an update (the doc says how).

## Each sim session

```bash
python fastlocate.py         # membase.json: where the A350's memory is
python fastdump2.py c.bin    # an 8 ms consistent copy (several names = several copies)
node draw.mjs c.bin CTR EFIS#1 MFD FCU RMP#0 SFD SND   # text with colours, in reading order
node xy.mjs c.bin MFD        # raw positions, selection frames and MFD/ND element rectangles
node sdpages.mjs c.bin 1 2 3 # any SD page, drawn on the copy only
node simq.mjs c.bin EFIS#1   # which sim variables a draw asks for
```

## Operating

```bash
node press.mjs INI_MCDU1_INIT wait500 INI_MCDU1_V INI_MCDU1_ENTER   # KCCU keys (1, hold 180 ms, 0)
bash wclick.sh "VCockpit07 - WasmInstrument" 30 216 247             # click the MFD (guid 30) at gauge x, y
node ceval.mjs "VCockpit10 - iniEfbA350" -e "<js>"                   # run JavaScript in the OIS/EFB
```

`efbclick.js`, `efbset.js`, `efbfields.js` and `efbels.js` are OIS/FAP helpers to paste into `ceval.mjs`. The ND and MFD accept mouse input only while the KCCU cursor is on them (`INI_MCDU1_ND` / `INI_MCDU1_MFD`).
