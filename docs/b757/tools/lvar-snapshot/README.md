# The L:var snapshot tool

Reads every BlueBird L:var in one Coherent debugger call (session 8 measured about 0.2 s for 2,173 names) and diffs
two snapshots, so a later plan can find which variable an action moves: snapshot, act, snapshot, diff. Session 8 did
this with the scripts in `docs/b757/tools/live/` for `docs/b757/evidence/live-findings.md`; this folder regenerates
the name list and learns the noise list. It goes when plan 16 deletes `docs/b757/`.

Run from this folder, with PowerShell 7 and Python 3, MSFS 2024, the BlueBird 757 and BBS Nest running. Close MSFSBA
first: it holds the `BBSTablet` view, and Coherent allows one inspector socket per view ([DBG-9]).

Reads are always fine. Any action between two snapshots that changes the aircraft (a switch, a button, a CDU key)
needs the owner's go-ahead; put back what you changed.

## 1. Generate snap.js (once)

```
python -I make_snap_js.py "<Community>\bluebirdsim-aircraft-757-200\SimObjects\Airplanes\bbs-757-200-base\panel\BBS752.wasm" ..\..\evidence\package\lvars.tsv snap.js
```

The names are the BlueBird-prefixed strings in `BBS752.wasm` (read as bytes, never run) plus the first column of
`lvars.tsv`, sorted. The result is one ES5 expression, git-ignored; the same command gives the same file.

## 2. Learn the noise list

Some variables move on their own (brake and APU temperatures, bogie speeds, the standby ADI). With the aircraft
idle, take two snapshots a few seconds apart and add every name that differs:

```
pwsh -File snap.ps1 -Out idle1.json
pwsh -File snap.ps1 -Out idle2.json
python -I diff_snaps.py idle1.json idle2.json --noise noise.txt --learn-noise
```

`noise.txt` keeps the names it already has, so start from a copy of `../live/noise.txt` (65 names from session 8)
and run this again whenever a diff shows something that drifts. The snapshots (`*.json`) and your `noise.txt` are
git-ignored in this folder.

## 3. Snapshot, act, snapshot, diff

```
pwsh -File snap.ps1 -Out before.json
# do the one thing, then wait a moment
pwsh -File snap.ps1 -Out after.json
python -I diff_snaps.py before.json after.json --noise noise.txt
```

The diff prints `NAME: old -> new` for every name that changed and is not in the noise list; `(missing)` means the
name was not readable in that snapshot. No output means nothing moved.

If `snap.ps1` says the debugger answered nothing, the simulator is not running, the 757 is not loaded, or MSFSBA
still holds the view.
