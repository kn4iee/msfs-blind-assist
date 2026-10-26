# A350Probe — live harness for the iniBuilds A350

Drives A350 panel rows through MSFSBA's own code against a running sim and reads each one back. It runs the real
`SimConnectManager` and `IniBuildsA350Definition` and calls `HandleUIVariableSet` exactly as the panel does, so a
PASS means the row works from MSFSBA, not merely that a variable can be written.

Standalone build, not part of `MSFSBlindAssist.sln`:

```bash
dotnet build tools/A350Probe -c Debug -p:Platform=x64
```

Run `tools/A350Probe/bin/x64/Debug/net10.0-windows/A350Probe.exe` with the A350 loaded and MSFSBA closed. Output
goes to the console and to `probe_<mode>.tsv` beside the exe.

| Mode | What it does |
|---|---|
| `A350Probe list [panel]` | Offline. Every offered row and light: panel, key, kind, variable, name, positions. |
| `A350Probe live <panel or key> [L:vars]` | Every position of every row (each press too), read back after 2 s, restored. Logs the panel's lights and the listed L:vars that changed with each step. |
| `A350Probe set "KEY=v[#ms];…" [L:vars]` | Sets, waits (default 2000 ms), reads back, restores. |
| `A350Probe keep "KEY=v[#ms];…" [L:vars]` | The same without the restore. |
| `A350Probe disp <panel> [L:vars]` | The panel's status lines as MSFSBA shows them, and the listed L:vars, read fresh. |

The listed L:vars are registered for the run only (as `W:<name>`), for reading back a downstream effect the row's
own variable does not show. A `live` pass changes the aircraft while it runs; anything a restore cannot undo (a
fire agent, a jettison) is driven by hand, never through `live`.
