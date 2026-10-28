# A350Probe — live harness for the iniBuilds A350

Drives A350 panel rows through MSFSBA's own code against a running sim and reads each one back. It runs the real
`SimConnectManager` and `IniBuildsA350Definition` and calls `HandleUIVariableSet` exactly as the panel does, so a
PASS means the row works from MSFSBA, not merely that a variable can be written.

Standalone build, not part of `MSFSBlindAssist.sln`:

```bash
dotnet build tools/A350Probe -c Debug -p:Platform=x64
```

Run `tools/A350Probe/bin/x64/Debug/net10.0-windows/A350Probe.exe` with the A350 loaded. Close MSFSBA for the modes
that drive the aircraft, or it speaks every change as well; the reading modes (`list`, `disp`, `cache`, `labels`,
`listen`) can run beside it. Output goes to the console and to `%TEMP%\a350probe_<mode>.tsv`.

| Mode | What it does |
|---|---|
| `A350Probe list [panel]` | Offline. Every offered row and light: panel, key, kind, variable, name, positions. |
| `A350Probe live <panel or key> [L:vars]` | Every position of every row (each press too), read back after 2 s, restored. Logs the panel's lights and the listed L:vars that changed with each step. |
| `A350Probe set "KEY=v[#ms];…" [L:vars]` | Sets, waits (default 2000 ms), reads back, restores; prints the listed L:vars after each. |
| `A350Probe keep "KEY=v[#ms];…" [L:vars]` | The same without the restore. |
| `A350Probe disp <panel> [L:vars]` | The panel's status lines as MSFSBA shows them, and the listed L:vars, read fresh. |
| `A350Probe labels <panel>` | What each row reads beside its name from live values (its button state), or `-`. |
| `A350Probe listen <seconds>` | What MSFSBA would say on its own meanwhile: the definition's speech (lights) and MainForm's generic announce of a switch, behind both of MainForm's mute gates. Move things from outside while it listens. |
| `A350Probe cache <key,key>` | What MSFSBA's cache holds for each key, then a fresh read. |
| `A350Probe rpn "code#ms;…" [L:vars]` | Raw calculator strings through MSFSBA's own sender, each followed by its pause, then the listed L:vars read fresh: for timing measurements. |
| `A350Probe ois "cpt\|fo [STEP > STEP …]"` | The OIS through MSFSBA's own client (`CoherentPmdgEfbClient.ForA350Ois`): prints the page; a STEP presses the element with that text (exact, else the first starting with it), `TEXT=VALUE` sets it, `@N` presses agent index N, `eval:EXPRESSION` runs a diagnostic expression in the page over the client's own socket. `A350_OIS_ALL=1` prints after every step. |
| `A350Probe mfd "click:S:X:Y;type:S:TEXT;key:S:KEY"` | MFD clicks (the Coherent mouse route) and KCCU typing and keys through `A350MfdInput`, side S 0 or 1. |
| `A350Probe rmpwin "keys"` | The RMP window (Ctrl+Shift+R) itself, with the delegates the hotkey passes, fed keys as typed: a digit or `.`, `enter`, `back`, `esc`, `f5`, `ctrl+N`, `alt+N`, `alt+LETTER`, `wait:MS`, `rmp:N` (the RMP box). Prints what it says, then its lines. |
| `A350Probe displays [name,name]` | Every display on demand as its status display reads it (the Displays panels, and the E/WD as Alt+E reads it), each from a good live draw, the draw code's sim asks answered from SimConnect's cache as MainForm answers them. `A350_DRAW_JSON=<dir>` saves the E/WD capture as a fixture. The SD page is known only with `A350_SECTIONS` holding `Pedestal` (the ECAM control panel's row). |
| `A350Probe gsx "open;pick:TEXT;wait:MS;close"` | GSX through MSFSBA's own Remote API client, state and menu model (the Access GSX window's path): opens the menu, picks the entry holding TEXT; prints each menu and GSX's message slot as they change. |

The display engine's offline modes run no SimConnect and no form (MSFSBA may run beside them):

| Mode | What it does |
|---|---|
| `A350Probe draw <copy.bin\|live> Gauge[#n] …` | Runs the gauges' own draw code on a memory copy and prints the drawn texts, frames and UI elements. `A350_SHAPES=1` records the shapes too; `A350_DRAW_JSON=<dir>` saves each capture as a test fixture (keep only a capture whose `Ok` is true: a torn copy traps). |
| `A350Probe mfdlines <mfd.json\|live> <side>` | The MFD page as `A350MfdPage` reads it, with each line's click point; `A350_SAVE=<file>` keeps the live draw. |
| `A350Probe snapshot <file …>` | Saves live memory copies for offline draws. |
| `A350Probe service [rounds]`, `pfdlisten <seconds>` | The display service as MainForm runs it; the FMA call-outs and the draw code's sim asks over a time. |
| `A350Probe sdmap <copy.bin> <page>` | Names the variable behind each SD value by setting it in the copy. |
| `A350Probe navdb <ICAO …>` | What MSFSBA's navdata holds for an airport (runways, ILS), against the A350's own database. |
| `A350Probe memfind TEXT …`, `memread LINEAR[:COUNT] …` | Finds a text in a live copy of the A350's memory and reads bytes there: state no display shows (the RMP's keypad entries, one 12-byte string per line, were found this way). Read-only, as every mode [A350-1]. |

The listed L:vars are registered for the run only (as `W:<name>`) and read as data, never through the calculator,
for reading back a downstream effect the row's own variable does not show. A `live` pass changes the aircraft while
it runs; anything a restore cannot undo (a fire agent, a jettison, an engine master, the thrust levers) is driven by hand,
never through `live`. A `live` pass presses each button once and does not press it back: a latching button (an EFIS filter,
A/BRK, OIS VIEW OFFSIDE, TCAS ABV) is left toggled, and one left on can block another row (OIS VIEW OFFSIDE blocks OIS ON
CENTER). Press them back with `keep` after a sweep.

Environment: `A350_SECTIONS="Overhead,Glareshield,Instrument"` picks the sections offered (default `Overhead`), and
`A350_MUTE="key,key"` unticks those Ctrl+M rows for a `listen`, in memory only (the settings file is never written).
