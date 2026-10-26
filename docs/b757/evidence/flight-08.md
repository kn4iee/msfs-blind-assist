# Plan 08 measurements: the CDU key timing (spec-gaps M10, R76)

2026-10-26, on the ground, BlueBird 757 on APU power, BBS Nest running, MSFSBA closed; the owner present and gave the
go-ahead. Run by Claude over the Coherent debugger in the `BBSTablet` view (page 12), where the app's tablet agent
(v4) was still installed from the owner's MSFSBA session. Only the first officer's CDU was written: its scratchpad
(the page, POS INIT 1/4, was never changed: no line select key or page key was pressed).

## Method

`KATL1100.5` typed into CDU 2's scratchpad: each key's `L:B757_CDU_2_<KEY>` held at 1 for H ms, then 0, then G ms
before the next key (one in-page `setTimeout` chain per run, so the timing is the page's, as the agent's own press
timing is); 800 ms later line 13 of the agent's last CDU 2 frame was compared with the text; then one CLR held
1300 ms, and 1200 ms later the scratchpad was checked blank (a leftover was cleared with another long CLR before the
next run). H in 250, 150, 100 (the client's floor); G in 400, 200, 100, 50; five runs per pair; 60 runs in 473 s.
The text has repeated characters ("11", "00"), which fail first when a release is too short to be seen. The stock
`FRAME RATE` is not a SimConnect variable (the simconnect tool answered SIMVAR_NOT_FOUND) and the gauge's
`SimVar.GetSimVarValue('FRAME RATE')` read 0, so the frame cadence is inferred from how late the page's timers fired.

## Results

| Hold / gap set (ms) | Typed right | Hold measured, mean (max) | Gap measured, mean (max) |
|---|---|---|---|
| 250 / 400 (today's) | 5/5 | 268 (275) | 413 (438) |
| 250 / 200 | 5/5 | 269 (279) | 219 (238) |
| 250 / 100 | 5/5 | 268 (280) | 118 (143) |
| 250 / 50 | 5/5 | 269 (275) | 65 (71) |
| 150 / 400 | 5/5 | 168 (173) | 413 (435) |
| 150 / 200 | 5/5 | 168 (180) | 217 (238) |
| 150 / 100 | 5/5 | 167 (172) | 116 (138) |
| 150 / 50 | 5/5 | 167 (172) | 67 (79) |
| 100 / 400 | 5/5 | 119 (137) | 414 (436) |
| 100 / 200 | 5/5 | 116 (137) | 219 (235) |
| 100 / 100 | 5/5 | 117 (139) | 119 (136) |
| 100 / 50 | 5/5 | 112 (135) | 67 (91) |

Every pair typed every character, repeats included. The timers fired about 17-20 ms late on average (up to about
40 ms), consistent with a frame every 16-20 ms (about 50-60 fps; inferred, not read).

**Chosen (plan 08's rule: the fastest pair with 5/5, then one step slower in G):** hold 100 ms, gap 100 ms, about
0.2 s a key (0.22-0.24 s as the timers fire), against 0.65 s today: "KATL" about 0.9 s instead of 2.6 s.

**Not done:** the repeat of the fastest pair with the sim visibly loaded (an outside view, a busy airport); only
the gate with the cockpit view was measured. The one-step margin in G and the missed-key check (plan 08 Task 2)
cover a slower frame; if a loaded sim drops keys, the next step is 100/200.

## Finding: one long CLR does not always clear the scratchpad

In 15 of the 60 runs, the CLR held 1300 ms removed only the last character ("KATL1100." was left), as a short CLR
does; a second CLR held 1300 ms then always cleared the rest (15 of 15). The other 45 long CLRs cleared all of it.
Session 8 had seen a 1 s hold clear everything (`cdu.md`); it does, most of the time. This bears on plan 01's
`Bbs757CduPages.WipeScratchpadAsync`, whose fallback after a long CLR that leaves text is one 250 ms CLR: on this
evidence that removes one more character and the wipe answers false (so `EnterAsync` refuses rather than typing
over a leftover). A second long CLR would clear it. Fixed the same day at the owner's request: the fallback is now a
second CLR held 1300 ms (plan 01's record).

## The measurement script (not committed as a file: `docs/b757/tools/**` is frozen after plan 01)

```js
(function () {
  var A = window.__MSFSBA_BBS757;
  if (!A || !A._state) return 'no agent';
  if (window.__MSFSBA_TYPING && !window.__MSFSBA_TYPING.done) return 'already running';
  var KEYS = ['KEYK', 'KEYA', 'KEYT', 'KEYL', 'NUM1', 'NUM1', 'NUM0', 'NUM0', 'NUMPER', 'NUM5'];
  var WANT = 'KATL1100.5';
  var HOLDS = [250, 150, 100], GAPS = [400, 200, 100, 50], RUNS = 5;
  var SETTLE = 800, CLR_HOLD = 1300, AFTER_CLR = 1200;
  var pairs = [];
  for (var h = 0; h < HOLDS.length; h++) for (var g = 0; g < GAPS.length; g++) pairs.push([HOLDS[h], GAPS[g]]);
  var T = window.__MSFSBA_TYPING = { done: false, started: Date.now(), results: [], log: [] };
  function pad() {
    var raw = A._state.frames[2];
    if (!raw) return null;
    try {
      var d = JSON.parse(raw).Data, out = '';
      for (var c = 13 * 24; c < 14 * 24; c++) {
        var cell = d[c];
        out += (cell && cell.length && typeof cell[0] === 'string' && cell[0].length) ? cell[0].charAt(0) : ' ';
      }
      return out.replace(/\s+$/, '').replace(/^\s+/, '');
    } catch (e) { return 'ERR'; }
  }
  function set(key, v) { SimVar.SetSimVarValue('L:B757_CDU_2_' + key, 'number', v); }
  function after(ms, f) { setTimeout(f, ms); }
  function clearLong(then) {
    set('KEYCLR', 1);
    after(CLR_HOLD, function () { set('KEYCLR', 0); after(AFTER_CLR, then); });
  }
  var p = 0, run = 0;
  function nextRun() {
    if (p >= pairs.length) { T.done = true; T.ended = Date.now(); return; }
    var hold = pairs[p][0], gap = pairs[p][1];
    var before = pad();
    if (before) {
      T.clears = (T.clears || 0) + 1;
      T.log.push('cleared leftover "' + before + '"');
      if (T.clears > 3) { T.done = true; T.ended = Date.now(); T.log.push('stopped: the scratchpad would not clear'); return; }
      clearLong(nextRun);
      return;
    }
    T.clears = 0;
    var k = 0, holds = [], gaps = [], tPress = 0, tRelease = 0;
    function press() {
      if (k >= KEYS.length) { after(SETTLE, check); return; }
      var now = Date.now();
      if (tRelease) gaps.push(now - tRelease);
      tPress = now;
      set(KEYS[k], 1);
      after(hold, function () {
        tRelease = Date.now();
        holds.push(tRelease - tPress);
        set(KEYS[k], 0);
        k++;
        after(gap, press);
      });
    }
    function check() {
      var got = pad();
      var avg = function (a) { var s = 0; for (var i = 0; i < a.length; i++) s += a[i]; return a.length ? Math.round(s / a.length) : 0; };
      var mx = function (a) { var m = 0; for (var i = 0; i < a.length; i++) if (a[i] > m) m = a[i]; return m; };
      T.results.push({ hold: hold, gap: gap, run: run + 1, got: got, ok: got === WANT,
        holdAvg: avg(holds), holdMax: mx(holds), gapAvg: avg(gaps), gapMax: mx(gaps), at: Date.now() - T.started });
      clearLong(function () {
        var left = pad();
        if (left) T.log.push('after CLR, scratchpad still "' + left + '" (hold ' + hold + ', gap ' + gap + ', run ' + (run + 1) + ')');
        run++;
        if (run >= RUNS) { run = 0; p++; }
        nextRun();
      });
    }
    press();
  }
  nextRun();
  return 'started: ' + pairs.length + ' pairs x ' + RUNS + ' runs';
})()
```

Run with `pwsh -File tools/coherent-eval.ps1 -Title BBSTablet -ExprFile <file>`, then read `window.__MSFSBA_TYPING`
with a second eval until `done`.
