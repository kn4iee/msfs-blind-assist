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

The listed L:vars are registered for the run only (as `W:<name>`) and read as data, never through the calculator,
for reading back a downstream effect the row's own variable does not show. A `live` pass changes the aircraft while
it runs; anything a restore cannot undo (a fire agent, a jettison, an engine master, the thrust levers) is driven by hand,
never through `live`. A `live` pass presses each button once and does not press it back: a latching button (an EFIS filter,
A/BRK, OIS VIEW OFFSIDE, TCAS ABV) is left toggled, and one left on can block another row (OIS VIEW OFFSIDE blocks OIS ON
CENTER). Press them back with `keep` after a sweep.

Environment: `A350_SECTIONS="Overhead,Glareshield,Instrument"` picks the sections offered (default `Overhead`), and
`A350_MUTE="key,key"` unticks those Ctrl+M rows for a `listen`, in memory only (the settings file is never written).
