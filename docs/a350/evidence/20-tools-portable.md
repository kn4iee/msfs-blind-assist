# Plan 20: the A350 tools made portable

What in `tools/A350Probe` and `tools/a350-probe` was tied to one machine, and what replaces it (Task 1). Searched for
`C:\`, `Users`, `AppData`, drive letters, the user's name and `19999`, and ran each script with no argument.

## `tools/A350Probe` (Task 2): nothing machine-specific left

| Value | Where | Replaced by |
| --- | --- | --- |
| The A350 module | display-engine modes | `A350ModuleCache.FindInstalledModule()`: MSFSBA's own package lookup (`MsfsPackagesLocator`) |
| The compiled-module cache | display-engine modes | `%LOCALAPPDATA%`, MSFSBA's own cache folder ([A350-11]); a Wasmtime variant gets `%TEMP%\a350wt-<variant>`, never the app's |
| The `.tsv` copy of a mode's output | `Program.cs` | `%TEMP%\a350probe_<mode>.tsv` |
| Fixtures and saves | `A350_DRAW_JSON`, `A350_SAVE` | the folder or prefix given, under a new name only |
| The Coherent debugger, `127.0.0.1:19999` | the `nd` mode | Ruling: kept. It is the sim's own fixed debugger port, the one MSFSBA's `A350MfdInput`, `A350NdInput` and `A350MailboxInput` use; the probe drives the app's code, so it uses what the app uses. |

`A350Probe list` runs offline (1,035 rows, exit 0, 2026-11-03). The README's environment table already lists every
variable the probe reads. No code change.

## `tools/a350-probe` (Task 3)

| Script | Was | Now |
| --- | --- | --- |
| `wdis.py` (and through it `beh.py`, `make_importsigs.py`, `vtables.py`, `patch2.py`, `patchtext.py`, `memscan.py`, `varenum.py`) | `A350_PACKAGE`, else one fixed MS Store Community path | `package.py`: `A350_PACKAGE`, else `InstalledPackagesPath` from the sim's own `UserCfg.opt` (`%APPDATA%\Microsoft Flight Simulator 2024`, then the Store build's `LocalCache`; never the `NextBoot` line) and the package in `Community` or `Community2024`, as `MsfsPackagesLocator` finds it. None found: a sentence naming `A350_PACKAGE`, never a stack. 6 offline tests (`test_package.py`), now run in CI. |
| `beh.py` | crashed without an output folder (`IndexError`); the behaviour path joined with `\` | usage line; `os.path.join` |
| `wasm.py` | crashed without its two arguments | usage line (`MODULE.wasm OUT_DIR`) |
| `fastdump2.py` | with no argument, did nothing and said nothing | usage line |
| `ceval.mjs`, `lset.mjs`, `press.mjs`, `wclick.sh` | ran with no target (a timeout, or a socket opened for nothing) | usage line |
| `draw.mjs`, `rawtext.mjs`, `sdpages.mjs`, `simq.mjs`, `xy.mjs` | a stack (`ENOENT importsigs.json`) before a fresh run, a `TypeError` without a copy | `emu2.mjs` names the missing generated file and the script that builds it; each script has a usage line |
| `fastlocate.py`, `memscan.py`, `memlib.py`, `make_importsigs.py`, `vtables.py`, `patch2.py`, `patchtext.py` | write and read their data files beside themselves (`os.path.dirname(__file__)`) | unchanged: already independent of the folder they are run from |

Kept, not retired: the superseded Node draws (`draw.mjs`, `xy.mjs`, `simq.mjs`) already say which `A350Probe` mode
replaced them and are kept for the record (the README and `docs/tooling.md` say so); `sdpages.mjs`, `rawtext.mjs`
and the Coherent scripts have no probe mode doing the same.

Run on this PC (2026-11-03, A350 1.2.8): `make_importsigs.py` (1 s), `vtables.py` (701 vtables), `patch2.py`
(14 draw exports), `beh.py` (700 input events) all found the package through `UserCfg.opt` with no `A350_PACKAGE`;
the generated files stayed git-ignored.

## Rulings

- The Coherent port stays 19999 in both tools: the sim's fixed debugger port, the app's own; `coherent.mjs` already
  takes `COHERENT_PORT`. Cost if wrong: one environment variable for the probe's `nd` mode.
- The package lookup's tests went into CI (`tests.yml`, beside the generator's), a file outside the plan's list:
  the MD-11 generator's comment there records how an untested path lookup went stale. Cost if wrong: one CI step.
