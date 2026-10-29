# iniBuilds A350 probes

Live investigation scripts for the iniBuilds A350 (1.2.7 and later): they copy the A350's memory out of the running sim, read-only, and run the A350's own draw code on that copy, and they operate the cockpit through the sim's own inputs. Background and every measured address: [docs/inibuilds-a350.md](../../docs/inibuilds-a350.md). They grew out of the 3 October 2026 investigation of 1.2.6, and they are the prototype MSFSBA's display engine was ported from: for the display engine itself, use `tools/A350Probe` (its `draw`, `snapshot`, `sdmap` modes), which runs MSFSBA's own code.

**Needs:** Python 3.14+ (standard library only), Node 22+ (the scripts use Node's built-in `WebSocket`), and the sim running with the A350 loaded. The package path defaults to the MS Store Community folder; set `A350_PACKAGE` otherwise. Run everything from this folder. The install of each is in [docs/tooling.md](../../docs/tooling.md#prerequisites-what-you-need-installed-to-run-these-tools).

**Nothing here writes into the sim's memory** [A350-1]: every process handle is opened for query and read only. Operating the cockpit goes through the sim's own inputs: L:var writes and the Coherent mouse route.

**Generated files** (`*.wasm`, `*.cwasm`, `*.bin`, `*.json`) are built from your own installed A350 and are git-ignored; never share them. A memory copy is 37 MB on a fresh A350 and grows to about 500 MB over a long flight: delete the `*.bin` copies when you are done.

**Credentials.** The OIS OPTIONS > 3RD PARTY page holds your SimBrief ID, API keys and logon codes. `efbfields.js` prints a field labelled ID, KEY, LOGON, PASSWORD or TOKEN, and every password box, as its length only, and `efbels.js` prints no field's value ([A350-12]). An expression you run yourself through `ceval.mjs` prints whatever it returns: never return a credential field's value.

## The Coherent debugger (`coherent.mjs`)

`ceval.mjs`, `press.mjs`, `lset.mjs` and `wclick.sh` reach the sim's Coherent GT debugger (`127.0.0.1:19999`) through `coherent.mjs`:

- Every request and evaluation has a timeout, so a dead process holding the port (see "Coherent port 19999" in docs/tooling.md) gives an error, never a hang. `COHERENT_PORT` overrides the port.
- An OIS view is found by the page it loads, `ois:cpt` or `ois:fo`, never by its VCockpit number, which changes [A350-8].
- **Coherent takes one inspector socket per page [DBG-9].** Close MSFSBA's window on a view (its OIS window: Shift+T or Ctrl+Shift+T) or MSFSBA itself before a script opens one, or the two fight over the page.

## Once per A350 version

```bash
python vtables.py            # vtables.json: the C++ class of each vtable
python make_importsigs.py    # importsigs.json
python patch2.py             # a350-all.wasm (the hooked scratch module for the Node draws) + ui_draws.json
python beh.py .              # a350_inputevents.json (the 696 cockpit controls) and a350_refs.json
```

Re-derive after an update, by name (the doc's "Addresses for 1.2.7" says how): `varenum.py`'s `VariablesEnum` name-table address (`TABLE`, 920832 on 1.2.7, where `INI_FMC_PAGE1` is entry 215), `sdpages.mjs`' SD page address (`SDPAGE`, 1451120), `fastlocate.py`'s anchor names, `vtables.py`'s typeinfo range (below 1,280,000 on 1.2.7), and the same addresses in A350Probe's `sdmap` and `sdsweep` (which check the layout and refuse another version).

`beh.py` reads the behaviour file with `tools/a350-gen/behavior_xml.py`, the map generator's reader.

## Each sim session

```bash
python fastlocate.py         # membase.json: where the A350's memory is (about a second)
python fastdump2.py c.bin    # a copy (several names: several copies)
```

`fastdump2.py` first checks that `membase.json` is still this sim and that the A350's anchors are at its base (run `fastlocate.py` again after a sim restart or an aircraft reload), and saves no copy with a failed or short read. A copy takes about 8 ms on a fresh A350; as its memory grows it takes longer (about 650 ms at 495 MB) and can span frames, so a draw on it can trap: take several copies and keep one that draws [A350-2].

To read a copy, use A350Probe (MSFSBA's own engine, which answers the A350's allocator [A350-10], so copies taken in flight draw):

```bash
A350Probe draw c.bin CTR EFIS#1 MFD FCU RMP#0 SFD SND
```

The Node draw scripts are the prototype's, kept for the record. They run `patch2.py`'s module, which does not answer the allocator, so a draw on a copy taken in flight often traps in dlmalloc; each says so (and says a gauge missing from the copy) rather than showing a blank screen:

```bash
node draw.mjs c.bin CTR EFIS#1 MFD     # text with colours, in reading order
node xy.mjs c.bin MFD                  # raw positions, selection frames and MFD/ND element rectangles
node sdpages.mjs c.bin 1 2 3           # any SD page, set and drawn on the copy only
node simq.mjs c.bin EFIS#1             # which sim variables a draw asks for
node rawtext.mjs c.bin EFIS            # texts holding characters outside printable ASCII, with their codes
```

`memscan.py` run on its own is the slow whole-process scan (it reads every committed region: minutes): use it only if `fastlocate.py` finds nothing. `fastdump.py` holds the region listing `fastdump2.py` uses. `memlib.py` (`Mem`: a copy's regions with typed reads and the class of an object by its vtable) and `varenum.py` (`NAMES`: the variable names by index) are for an interactive `python -i` session on a copy. `wasm.py module.wasm outdir` writes a module's section summary (`wasm_summary.json`), function names (`wasm_fnames.txt`) and data strings (`wasm_strings.txt`) into `outdir`; `wdis.py` is the module reader the others import.

## Operating

```bash
node press.mjs INI_MCDU1_INIT wait500 INI_MCDU1_V INI_MCDU1_ENTER   # KCCU keys: 1, held 180 ms, 0 (A350_OIS_SIDE=fo: the other OIS view)
node lset.mjs INI_SOME_VAR=1 wait300 INI_OTHER=0                     # L:var writes, one each
bash wclick.sh "VCockpit07 - WasmInstrument" 30 216 247             # click the MFD (that view, guid 30) at gauge x, y
node ceval.mjs ois:cpt -e "document.querySelector('.page-title').innerText"   # JavaScript in an OIS view
```

`press.mjs` and `lset.mjs` write through an OIS view's SimVar API, so close MSFSBA's OIS window first. The ND and the MFD take mouse input only while the KCCU cursor is on them [A350-4]: press `INI_MCDU1_ND` or `INI_MCDU1_MFD` first (`press.mjs` holds and lets go, as the aircraft never resets those two keys [A350-3]). The MFD's view and guid change per session: `A350Probe nd views` lists every `WasmInstrument` view with its gauge.

The OIS helpers are functions to call through `ceval.mjs`, for example `node ceval.mjs ois:cpt -e "$(cat efbbtn.js)('COMPUTE', 0)"`:

| Helper | Arguments | What it does |
| --- | --- | --- |
| `efbfields.js` | none (no call: run the file) | Lists the page's inputs, selects and buttons with their row labels and values (credentials as their length) |
| `efbels.js` | none (no call: run the file) | Lists what can be clicked, with its cursor (no field values) |
| `efbbtn.js` | `(text, nth)` | Clicks the nth button whose text is `text` |
| `efbclick.js` | `(text, nth)` | Clicks the nth element of any kind whose text is `text` |
| `efbtype.js` | `(index, text)` | Types `text` key by key into the index-th visible text box, as the OIS needs for COMPUTE to take it [A350-6] |
| `efbset.js` | `(label, value)` | Sets a field's value directly: shown in the field, but COMPUTE ignores it [A350-6]. Use `efbtype.js` for anything COMPUTE reads |
| `efbfail.js` | `(name)` | Toggles a failure on the Situation Manager's FAILURES tab and says whether it is active |
