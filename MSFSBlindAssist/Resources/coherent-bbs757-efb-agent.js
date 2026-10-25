// BlueBird 757-200 EFB agent: presses buttons on BlueBird's tablet ("BBOS") by scripted clicks,
// over the Coherent GT remote debugger (port 19999). Installed by CoherentBbs757EfbClient into the
// cockpit view whose title holds "BBSNavigraphMap"; exposes window.__MSFSBA_BBS757EFB.
//
// The tablet is the WASM gauge efb_l, drawn into a <wasm-sim-canvas> inside its <wasm-instrument>.
// That element forwards the mouse to the WASM through its own four handlers (onMouseMove,
// OnMouseDown, OnMouseUp, onClick: each a Coherent.call WASM_MOUSE_* / WASM_CLICK with page
// coordinates). A click here calls those four with the canvas origin plus the tablet-local point,
// exactly as the mouse would (measured, docs/b757/evidence/efb-and-manual.md). No camera move, no
// real mouse. Nothing on the tablet can be read: the app checks results through files BlueBird
// writes.
//
// Sequences: clickSeq takes [[x, y, delayAfterMs], ...] and runs it in the page with setTimeout,
// one run at a time, so a sequence is never interleaved with another and never depends on the
// app's link staying up between its clicks. status(id) says how far a run got; cancel() stops one.
//
// Install: an agent that cannot click is no agent. When Coherent.call or the tablet's canvas (with
// its four handlers, at the measured size) is missing - the view's scripts or gauges not ready
// after an aircraft reload - the install answers NOT_READY instead of the marker, so the app tries
// again. A re-install of a healthy agent keeps it, and with it any run in progress.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow functions, no String.includes, top-level
// try/catch.
(function () {
  try {
    var VERSION = 1;
    var MARK = 'MSFSBA_BBS757_EFB_INSTALLED';
    var NOT_READY = 'MSFSBA_BBS757_EFB_NOT_READY:';
    // The tablet gauge's size in BlueBird 0.1.0 (measured 1397 x 1058). Another size means another
    // layout: the measured positions would miss, so nothing is clicked.
    var WIDTH = 1397, MIN_HEIGHT = 1048, MAX_HEIGHT = 1070, SIZE_SLACK = 2;
    var MAX_STEPS = 40, MAX_DELAY_MS = 10000;
    // A run still marked running this long after its last click was due is taken as dead.
    var STALE_MS = 5000;

    function canvasProblem(c) {
      if (!c) return 'canvas';
      if (typeof c.onMouseMove !== 'function' || typeof c.OnMouseDown !== 'function'
          || typeof c.OnMouseUp !== 'function' || typeof c.onClick !== 'function') return 'handlers';
      var r = c.getBoundingClientRect();
      if (Math.abs(r.width - WIDTH) > SIZE_SLACK || r.height < MIN_HEIGHT || r.height > MAX_HEIGHT)
        return 'size ' + Math.round(r.width) + 'x' + Math.round(r.height);
      return null;
    }

    // The tablet's canvas, looked up afresh each time: the gauge can be rebuilt under the page.
    function findCanvas() {
      var insts = document.querySelectorAll('wasm-instrument');
      for (var i = 0; i < insts.length; i++) {
        var url = insts[i].getAttribute('url') || '';
        if (/[?&]wasm_gauge=efb_l(&|$)/.test(url)) return insts[i].querySelector('wasm-sim-canvas');
      }
      return null;
    }

    function coherentProblem() {
      if (typeof Coherent === 'undefined' || !Coherent || typeof Coherent.call !== 'function') return 'Coherent';
      return null;
    }

    var old = window.__MSFSBA_BBS757EFB;
    if (old && old.version === VERSION && old._state && !coherentProblem() && !canvasProblem(findCanvas())) return MARK;

    var problem = coherentProblem() || canvasProblem(findCanvas());
    if (problem) return NOT_READY + problem;

    var A = { version: VERSION };
    var state = { nextId: 1, run: null };
    A._state = state;

    function isWhole(v) { return typeof v === 'number' && isFinite(v) && Math.floor(v) === v; }

    // One click at a tablet-local point: 'ok', or why not.
    A.click = function (x, y) {
      try {
        if (coherentProblem()) return 'no-coherent';
        var c = findCanvas();
        var p = canvasProblem(c);
        if (p) return 'no-canvas: ' + p;
        var r = c.getBoundingClientRect();
        if (!isWhole(x) || !isWhole(y) || x < 0 || y < 0 || x >= r.width || y >= r.height) return 'bad-point';
        var ev = { clientX: r.left + x, clientY: r.top + y, button: 0, preventDefault: function () { } };
        c.onMouseMove(ev);
        c.OnMouseDown(ev);
        c.OnMouseUp(ev);
        c.onClick(ev);
        return 'ok';
      } catch (e) {
        return 'error: ' + (e && e.message);
      }
    };

    function runIsLive(run) {
      return !!run && run.running && (Date.now() - run.startedAt) < run.totalMs + STALE_MS;
    }

    function parseSeq(json) {
      var steps;
      try { steps = JSON.parse(json); } catch (e) { return null; }
      if (!steps || typeof steps.length !== 'number' || steps.length < 1 || steps.length > MAX_STEPS) return null;
      for (var i = 0; i < steps.length; i++) {
        var s = steps[i];
        if (!s || s.length !== 3 || !isWhole(s[0]) || !isWhole(s[1]) || !isWhole(s[2])) return null;
        if (s[2] < 0 || s[2] > MAX_DELAY_MS) return null;
      }
      return steps;
    }

    function runInfo(run) {
      return { ok: true, id: run.id, n: run.n, done: run.done, running: runIsLive(run), error: run.error };
    }

    // Starts a run: the first click now, each next one after the previous step's delay.
    A.clickSeq = function (json) {
      if (runIsLive(state.run)) return { ok: false, error: 'busy' };
      var steps = parseSeq(json);
      if (!steps) return { ok: false, error: 'bad-seq' };
      var p = coherentProblem() || canvasProblem(findCanvas());
      if (p) return { ok: false, error: 'no-canvas: ' + p };

      var total = 0;
      for (var i = 0; i < steps.length; i++) total += steps[i][2];
      var run = { id: state.nextId++, n: steps.length, done: 0, running: true, error: null,
                  startedAt: Date.now(), totalMs: total, timer: null };
      state.run = run;

      function step(k) {
        run.timer = null;
        if (!run.running) return;
        var r = A.click(steps[k][0], steps[k][1]);
        if (r !== 'ok') {
          run.error = r;
          run.running = false;
          return;
        }
        run.done = k + 1;
        if (k + 1 >= steps.length) {
          run.running = false;
          return;
        }
        run.timer = setTimeout(function () { step(k + 1); }, steps[k][2]);
      }
      step(0);
      return { ok: true, id: run.id, n: run.n, done: run.done, totalMs: total, error: run.error };
    };

    // How far run `id` got.
    A.status = function (id) {
      var run = state.run;
      if (!run || run.id !== id) return { ok: false, error: 'unknown-run' };
      return runInfo(run);
    };

    // Stops the run in progress: no further click is made.
    A.cancel = function () {
      var run = state.run;
      if (!run || !run.running) return 'idle';
      if (run.timer) clearTimeout(run.timer);
      run.timer = null;
      run.running = false;
      run.error = 'canceled';
      return 'canceled';
    };

    A.ping = function () {
      var p = coherentProblem() || canvasProblem(findCanvas());
      return { ok: true, ready: !p, problem: p, running: runIsLive(state.run) };
    };

    window.__MSFSBA_BBS757EFB = A;
    return MARK;
  } catch (e) {
    return 'MSFSBA_BBS757_EFB_ERROR:' + (e && e.message);
  }
})()
