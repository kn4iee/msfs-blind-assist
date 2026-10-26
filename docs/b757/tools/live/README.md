# Live investigation scripts (session 8)

The scripts used for the session 8 measurements in `docs/b757/evidence/live-findings.md`, kept so a contributor can
repeat or extend them (plans 02-15's [owner] and [in flight] steps). They go when plan 16 deletes `docs/b757/`.
Run them from Git Bash with MSFS 2024, the BlueBird 757 and BBS Nest running; they use `tools/coherent-eval.ps1`
(the Coherent debugger on port 19999). Close MSFSBA first, or it holds the `BBSTablet` view ([DBG-9]).

Reads are always fine. Anything marked WRITES or TAPS changes the aircraft: only with the owner's go-ahead, and put
back what you changed.

| Script | What it does |
|---|---|
| `snap.sh out.json` | All 2,173 BlueBird L:vars (names in `snap.js`) read in one call, about 0.2 s. |
| `diff_snaps.py a.json b.json noise.txt` | What changed between two snapshots, minus the vars that drift on their own (`noise.txt`: 65 names from session 8; add any that drift in your session). |
| `press.sh LVAR [holdms] [wait_s]` | WRITES: snapshot, hold the L:var at 1 for holdms, back to 0, snapshot, diff. |
| `cdu.sh CDU [KEY ...]` | WRITES when given keys: presses CDU keys through the app's tablet agent, then prints the CDU's 14 rows. |
| `efb-tap.sh x,y ...` | TAPS: EFB-local points through the app's EFB agent (positions: `Bbs757EfbLayout`, `live-findings.md`). |
| `nd-mirror-on.js`, `nd-mirror-off.js` | Lay a copy of the EFB picture over the cockpit displays so a window capture shows the tablet. Run with `pwsh -File tools/coherent-eval.ps1 -Title <view> -ExprFile <script>` in the VCockpit view whose page holds the EFB's `WasmGaugeLiveView` image (VCockpit08 in session 8; the number can change: list the views at `http://localhost:19999/pagelist.json` and use the one where the script does not answer "no efb img"). Always run `nd-mirror-off.js` afterwards. |
| `capture.ps1 -Out x.png [-X -Y -W -H]` | A capture of the simulator window (PrintWindow), optionally cropped. With the mirror on and the ADI/EHSI instrument view (index 0) showing, the captain's ND was at (21,735) 704 × 859 in session 8. |

`snap.js` was built from the printable names in BlueBird's `BBS752.wasm` plus `docs/b757/evidence/package/lvars.tsv`;
plan 01 Task 7 adds a generator for it (`docs/b757/tools/lvar-snapshot/`) and a noise-learning step.
