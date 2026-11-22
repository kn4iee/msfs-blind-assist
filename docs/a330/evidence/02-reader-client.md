# Plan 02 evidence: the client against the display reader

Measured on 2026-11-22 in MSFS 2024 (`FlightSimulator2024.exe` 1.9.24.0) with the A330-200 (GE) on the ground,
powered, the captain's MCDU on a flight plan page. The display reader was version 1.0.0 (protocol 1), built from
the team's private reference and run from a folder of its own. A throwaway test harness (not committed) drove the
app's own `A330ReaderClient`, `A330ReaderProcess` and a test subclass of `A330DisplayWindow` against it. MSFSBA's
own window was not opened, and nothing was written to the simulator.

## Task 6 results

| Check | Result |
| --- | --- |
| Start, the captain's MCDU wanted at 250 ms | `Running` after 0.67 s (the reader had already found the displays once in this simulator session); the first complete frame came at the same moment: 40 texts, 457 characters, no colours (spec M14) |
| The display window on the captain's MCDU | "A330 displays starting", then the page's 15 rows at 0.54 s |
| The reader ended by hand while the window was open (Step 3) | `Latest` gave nothing 50 ms later; the window's only row read "A330 displays starting" at 0.12 s, inside three 250 ms refreshes; the client started the reader again by itself and the list filled again at 5.8 s (the client's 5 s wait, then the start); nothing was spoken |
| Two clients in one process (Step 1, "each has its own") | two reader programs, each `Running` within 0.7 s |
| A client whose process ends with its reader still running (Step 1) | the reader ended with the process: the kill-on-close job works |
| No reader beside the app (Step 2) | `NotInstalled` at once, no program started; the spoken notice at 30 s is covered by `A330ReaderNoticeTests` and `A330DisplaysLifecycleTests` |
| A reader of another protocol version (Step 2) | not run: no such build exists; `A_hello_of_another_version_is_OutOfDate` covers it |
| The reader with no display wanted | stays `Starting`: the reader reports `Running` only once a display is wanted and found. So with no window open and no call-out wanting a display, the app's client stays `Starting`, which is a passing state: the windows say "A330 displays starting" and nothing is spoken |

## Refresh defaults (Step 4)

The reader's own plan measured a complete frame of the captain's MCDU, PFD and ND at every 250 ms refresh, for a
few milliseconds of work a refresh. The plan's defaults stand: 250 ms while a window shows a display
(`A330DisplayWindow`'s default `refreshMs`), and 1000 ms for an owner that reads in the background (plans 03, 08
and 09 set theirs when they want a display).
