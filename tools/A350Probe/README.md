# A350Probe — live harness for the iniBuilds A350

Drives A350 panel rows through MSFSBA's own code against a running sim and reads each one back. It runs the real
`SimConnectManager` and `IniBuildsA350Definition` and calls `HandleUIVariableSet` exactly as the panel does, so a
PASS means the row works from MSFSBA, not merely that a variable can be written. It also runs MSFSBA's display engine
on a live or saved copy of the A350's memory.

Standalone build, not part of `MSFSBlindAssist.sln` (needs the .NET 10 SDK):

```bash
dotnet build tools/A350Probe -c Debug -p:Platform=x64
```

Run `tools/A350Probe/bin/x64/Debug/net10.0-windows/A350Probe.exe` with the A350 loaded. Like MSFSBA, the exe keeps
`<CETCompat>false</CETCompat>` [A350-9]. Nothing it does writes into the sim's memory [A350-1]: copies are read with a
query-and-read process handle, and the modes that set a value in a copy (`sdmap`, `sdsweep`) set it in that copy only.

**Before a run.** Close MSFSBA for the modes that drive the aircraft, or it speaks every change as well; the reading
modes (`list`, `vars`, `disp`, `cache`, `labels`, `listen`) can run beside it. Close MSFSBA's OIS window before `ois`:
Coherent takes one inspector socket per page [DBG-9]. The SimConnect modes print to the console and to
`%TEMP%\a350probe_<mode>.tsv`; the display-engine modes print to the console only.

**Exit codes.** 0 done; 1 a display-engine mode that could not run (no A350 module, no draw); 2 an unknown mode (the
list of modes is printed) or a fatal error; 3 a SimConnect mode with no sim connected, after about 2 s (nothing is
driven).

**Safety.** A `live` pass changes the aircraft while it runs. What a restore cannot undo is never in a `live` pass and is
driven by hand with `keep`: the fire buttons and agents, the cargo smoke agents, the IDG disconnects, RAT MAN ON, the
hydraulic supply buttons, the engine masters, the thrust levers, the gravity gear handle, the fuel jettison (ARM and
ACTIVE) and MASK MAN ON. A `live` pass presses each button once and does not press it back: a latching button (an EFIS
filter, A/BRK, OIS VIEW OFFSIDE, TCAS ABV) is left toggled, and one left on can block another row (OIS VIEW OFFSIDE
blocks OIS ON CENTER); press them back with `keep` after a sweep. A typed-value row (the FCU windows, the QNHs, the
cabin targets) reads UNTESTED in a `live` pass: drive it with `set`. `hotkey` never runs Ctrl+E's E/WD toggle, which
saves the pilot's settings file.

**Credentials.** `ois` prints a field labelled ID, KEY, LOGON, PASSWORD or TOKEN, a password box, or one the agent marks
secret, as its length only, whatever STREAMER MODE is set to, and never echoes a value typed into one ([A350-12],
`A350OisSecrets`). An `eval:` step prints whatever its expression returns: never return a credential field's value.

## SimConnect modes

| Mode | What it does |
|---|---|
| `A350Probe list [panel]` | Offline. Every offered row and light: panel, key, kind, variable, name, positions. |
| `A350Probe vars` | Offline. Every variable the definition registers (key, sim name), to find a cockpit indicator nothing reads. |
| `A350Probe live <panel or key[,key]> [L:vars]` | Every position of every row (each press too), read back after 2 s, restored. Logs the panel's lights and the listed L:vars that changed with each step. |
| `A350Probe set "KEY=v[#ms];…" [L:vars]` | Sets, waits (default 2000 ms), reads back, restores; prints the listed L:vars after each. A key on no offered panel says so. |
| `A350Probe keep "KEY=v[#ms];…" [L:vars]` | The same without the restore. |
| `A350Probe disp <panel> [L:vars]` | The panel's status lines as MSFSBA shows them, and the listed L:vars, read fresh. |
| `A350Probe labels <panel>` | What each row reads beside its name from live values (its button state), or `-`. |
| `A350Probe listen <seconds>` | What MSFSBA would say on its own meanwhile: the definition's speech (lights) and MainForm's generic announce of a switch, behind both of MainForm's mute gates, and the take-off speeds as entered (the display reader runs as MainForm runs it, its own call-outs muted). Move things from outside while it listens. |
| `A350Probe cache <key,key>` | What MSFSBA's cache holds for each key, then a fresh read. |
| `A350Probe rpn "code#ms;…" [L:vars]` | Raw calculator strings through MSFSBA's own sender, each followed by its pause, then the listed L:vars read fresh: for timing measurements. |
| `A350Probe sample "code#ms;…" [L:vars]` | The strings run in the background while the listed L:vars are read every 100 ms, each change printed with its time: how a value moves inside a hold (a flashing light, a timed test). |
| `A350Probe mailbox [BUTTON\|x:y]` | The ATC mailbox as its window reads it, a button or a point of the CTR pressed first through `A350MailboxInput`. Never `SEND` without the owner's go-ahead: it goes out to the network. |
| `A350Probe fap ["STEP > STEP …"]` | The FAP through MSFSBA's own client (`CoherentPmdgEfbClient.ForA350Fap`), with `ois`'s steps. |
| `A350Probe ois "cpt\|fo [STEP > STEP …]"` | The OIS through MSFSBA's own client (`CoherentPmdgEfbClient.ForA350Ois`): prints the page; a STEP presses the element with that text (exact, else the first starting with it), `TEXT=VALUE` sets it, `@N` presses agent index N, `eval:EXPRESSION` runs a diagnostic expression in the page over the client's own socket. `A350_OIS_ALL=1` prints after every step. |
| `A350Probe mfd "click:S:X:Y;type:S:TEXT;key:S:KEY"` | MFD clicks (the Coherent mouse route) and KCCU typing and keys through `A350MfdInput`, side S 0 or 1. The cursor keys (`ND`, `MFD`) are held and let go, as the aircraft never resets them [A350-3]. |
| `A350Probe nd "views;click:S:X:Y;key:S:KEY;type:S:TEXT"` | `views` lists every WasmInstrument view with its gauge (`guid\|url`); a click on the side's EFIS gauge (the ND is right of 836) moves the side's KCCU cursor to the ND first [A350-4]. The EFIS view is found by its url's side, never by its title. |
| `A350Probe rmpwin "keys"` | The RMP window (Ctrl+Shift+R) itself, with the delegates the hotkey passes, fed keys as typed: a digit or `.`, `enter`, `back`, `esc`, `f5`, `ctrl+N`, `alt+N`, `alt+LETTER`, `wait:MS`, `rmp:N` (the RMP box). Prints what it says, then its lines. |
| `A350Probe hotkey "Action;wait:MS;type:TEXT;click:BUTTON;read;close"` | The definition's hotkeys (a `HotkeyAction` name) with the display service running; `type:` and `click:` act on the FCU window the last one opened, `read` and `close` on any window it opened (the MFD window too). |
| `A350Probe displays [name,name]` | Every display on demand as its status display reads it (the Displays panels, and the E/WD as Alt+E reads it), each from a good live draw, the draw code's sim asks answered from SimConnect's cache as MainForm answers them. `A350_DRAW_JSON=<dir>` saves the E/WD capture as a fixture. |
| `A350Probe gsx "open;pick:TEXT;wait:MS;close"` | GSX through MSFSBA's own Remote API client, state and menu model (the Access GSX window's path): opens the menu, picks the entry holding TEXT; prints each menu and GSX's message slot as they change. |

The listed L:vars are registered for the run only (as `W:<name>`) and read as data, never through the calculator,
for reading back a downstream effect the row's own variable does not show.

## Display-engine modes

These run no SimConnect and no form (MSFSBA may run beside them). `copy.bin` is a saved copy (`snapshot`, or
`tools/a350-probe/fastdump2.py`); `live` takes one now.

| Mode | What it does |
|---|---|
| `A350Probe draw <copy.bin\|live> Gauge[#n] …` | Runs the gauges' own draw code on a memory copy and prints the drawn texts, frames and UI elements (xy.mjs's format). Between gauges, `poke:LINEAR=VALUE` sets a 32-bit value in the copy (never in the sim) and draws on from it, and `reload` takes a fresh copy (`live` only). |
| `A350Probe snapshot <file …>` | Saves live memory copies for offline draws. |
| `A350Probe service [rounds]` | The display service as MainForm runs it: ready, then rounds of CTR, both EFIS and the MFD on one fresh copy each, timed, with the WD rows of the last round. |
| `A350Probe pfdlisten <seconds>` | MSFSBA's PFD monitor on the live sim: the FMA call-outs it would speak, the draw code's sim asks, and each side's PFD at the end. |
| `A350Probe ecamwatch <seconds> [fixtureDir]` | The CTR at 2 Hz: each change of the E/WD and SD, once two reads agree, with colours; with a folder, each capture saved as `ctr_NNN.json`. |
| `A350Probe ecamlisten <seconds>` | MSFSBA's own E/WD monitor: each line it would speak, with the time. |
| `A350Probe mfdlines <mfd.json\|live> <side>` | The MFD page as `A350MfdPage` reads it, with each line's click point. |
| `A350Probe ndlines <efis.json\|live> <side>` | The ND as MSFSBA reads it (the airport map by kind). A live draw has no aircraft position: use a capture taken with `A350_LAT`/`A350_LON` for the airport map. |
| `A350Probe sdtext <ctr.json> <page>` | A saved CTR draw read as the ECAM Control Panel's status display reads it. |
| `A350Probe sdmap <copy.bin> <page>` | Names the variable behind each SD value by setting each variable in the copy and redrawing. |
| `A350Probe sdsweep <copy.bin> <page> NAME=v1,v2 …` | What one variable's values (`L:x`, or `V:x` in the Variables array) do to an SD page: every shape and text that differs. |
| `A350Probe navdb <ICAO …>` | What MSFSBA's navdata holds for an airport (runways, ILS), against the A350's own database. |
| `A350Probe speeds [seconds]` | The FMS's take-off speeds as MSFSBA finds them: the facts from the installed module (the active flight plan's address and each speed's offset), then a live read each second for that long. |
| `A350Probe memfind TEXT …`, `memread LINEAR[:COUNT] …` | Finds a text in a live copy of the A350's memory and reads bytes there: state no display shows (the RMP's keypad entries, one 12-byte string per line, were found this way). |
| `A350Probe patch [out.wasm [other.wasm]]` | Builds the patched module (`A350ModulePatcher`), saves it, and compares it with another. |
| `A350Probe trap <export> <arg>`, `traptiny` | How a trap inside the A350's code (or a two-instruction module's) comes back to .NET, with its own compile cache in %TEMP% (never the app's [A350-11]). |

`sdmap` and `sdsweep` address the Variables array at 1.2.7's addresses and first check the copy has that layout
(`INI_FMC_PAGE1` at entry 215); on another version they refuse until the addresses are re-derived.

## Environment

| Variable | Modes | What it does |
|---|---|---|
| `A350_SECTIONS="Overhead,Glareshield"` | SimConnect modes | The sections offered. Default: MSFSBA's own five (Overhead, Glareshield, Instrument, Pedestal, Cockpit); an unknown name is refused with the list. |
| `A350_HEAR=1` | `hotkey` | The background monitor's call-outs heard too, marked `[background]`, and told when the MFD window is open as MainForm tells it: an FMS message is said once, by the window while it is open, never again as it closes. |
| `A350_MUTE="key,key"` | `listen` | Unticks those Ctrl+M rows, in memory only (the settings file is never written). |
| `A350_TRACE="key,key"` | `listen` | Prints each delivery of those keys with its time, to see what the speech was made from. |
| `A350_SPEEDS=0` | `listen` | Leaves the display reader out: no take-off speeds, no memory copies. |
| `A350_OIS_ALL=1` | `ois` | Prints the page after every step. |
| `A350_DRAW_JSON=<dir>` | `draw`, `displays` | Saves each good capture as a test fixture, under a new name only (never over a fixture a test pins); a trapped draw is never saved. |
| `A350_SHAPES=1` | `draw` | Records the shapes (sprites, fills) too. |
| `A350_TRAP=1` | `draw` | Prints a trapped draw's whole trap message. |
| `A350_N2=<percent>` | `draw` | The engine N2 the draw code's sim asks are answered with (default 0). |
| `A350_LAT`, `A350_LON` | `draw` | The aircraft's position for the EFIS's own asks (the airport map is drawn around it). |
| `A350_SAVE=<prefix>` | `service` | Saves each good draw of the last round as `<prefix>_CTR0.json`, `_EFIS0`, `_EFIS1`, `_MFD0`, new names only. |
| `A350_SAVE=<file>` | `mfdlines live` | Saves the live MFD draw, never over an existing file. |
| `A350_SD=<page>` | `service` | Also prints the engine display and that SD page as MSFSBA reads them. |
| `A350_SDMAP_VALUES=4321,…` | `sdmap` | The values each variable is set to (default 4321). |
| `A350_WT_VARIANT=noopt\|unwind\|debug\|guard0` | `trap`, `draw` | A Wasmtime configuration to try, compiled into its own cache under `%TEMP%\a350wt-<variant>` (never the app's [A350-11]); `draw` builds its engine differently for `guard0` only. |
| `A350_BIG_STACK=1` | display-engine modes | Runs the mode on a thread with a 256 MB stack. |
