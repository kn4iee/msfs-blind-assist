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
// The page clock: this view's document is made anew on every flight load, so the moment it loaded
// (Date.now() - performance.now(), the real clock, measured against the host's UTC) tells which
// flight load BlueBird's load-time checks belong to. It is fixed once per document, kept on window
// so a newer agent on the same page keeps it, and sent with every answer as `page`.
//
// Sequences: clickSeq takes [[x, y, delayAfterMs], ...] and runs it in the page with setTimeout,
// one run at a time, so a sequence is never interleaved with another and never depends on the
// app's link staying up between its clicks. Before a new run starts, any older run's pending click
// is cleared. Its optional guards are checked here, on the page that will be clicked:
// notBeforeLoadMs (the page loaded at or after it), minAgeMs (the page at least this old),
// samePageAsMs (the page that loaded then), onceKey (no run with this key started on this page).
// A sequence with any click near the AIRAC page's UPDATE spot, which is BlueBird's REMOVE whenever
// its load-time check saw no newer cycle, is refused ('unguarded-update') unless it carries all
// three of onceKey, notBeforeLoadMs and minAgeMs: only the app's nav data update does.
// status(id) says how far a run got; cancel() stops one.
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
    // 3: the unguarded-update refusal. The app's clickSeq call asks for this version or later.
    var VERSION = 3;
    var MARK = 'MSFSBA_BBS757_EFB_INSTALLED';
    var NOT_READY = 'MSFSBA_BBS757_EFB_NOT_READY:';
    // The tablet gauge's size in BlueBird 0.1.0 (measured 1397 x 1058). Another size means another
    // layout: the measured positions would miss, so nothing is clicked.
    var WIDTH = 1397, MIN_HEIGHT = 1048, MAX_HEIGHT = 1070, SIZE_SLACK = 2;
    // The UPDATE/REMOVE spot on the AIRAC page (Bbs757EfbLayout.UpdateButton) and how far either
    // side of it a click counts as one on it (UpdateSpotHalfWidth, UpdateSpotHalfHeight).
    var UPDATE_X = 1291, UPDATE_Y = 337, UPDATE_HALF_W = 100, UPDATE_HALF_H = 40;
    var MAX_STEPS = 40, MAX_DELAY_MS = 10000;
    // A run still marked running this long after its last click was due is taken as dead.
    var STALE_MS = 5000;

    // This document's load time, once (see the page clock above).
    if (typeof window.__MSFSBA_BBS757EFB_PAGE !== 'number') {
      if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function')
        window.__MSFSBA_BBS757EFB_PAGE = Math.round(Date.now() - performance.now());
    }
    // The once-per-page keys already used, kept on window like the page time.
    if (!window.__MSFSBA_BBS757EFB_ONCE || typeof window.__MSFSBA_BBS757EFB_ONCE !== 'object')
      window.__MSFSBA_BBS757EFB_ONCE = {};

    function pageTime() {
      return typeof window.__MSFSBA_BBS757EFB_PAGE === 'number' ? window.__MSFSBA_BBS757EFB_PAGE : null;
    }

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

    // An older agent on this page is replaced: its run, if any, stops first (one run at a time).
    if (old && typeof old.cancel === 'function') {
      try { old.cancel(); } catch (e) { }
    }

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

    // Stops a run: its pending click is cleared, so nothing more of it is clicked.
    function stopRun(run, reason) {
      if (!run) return false;
      if (run.timer) clearTimeout(run.timer);
      run.timer = null;
      if (!run.running) return false;
      run.running = false;
      run.error = reason;
      return true;
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

    function refuse(error) { return { ok: false, error: error, page: pageTime() }; }

    function touchesUpdateSpot(steps) {
      for (var i = 0; i < steps.length; i++) {
        if (Math.abs(steps[i][0] - UPDATE_X) <= UPDATE_HALF_W && Math.abs(steps[i][1] - UPDATE_Y) <= UPDATE_HALF_H) return true;
      }
      return false;
    }

    // A click near the UPDATE spot needs every guard the nav data update runs under.
    function updateGuarded(opts) {
      return !!opts && opts.onceKey != null && opts.notBeforeLoadMs != null && opts.minAgeMs != null;
    }

    // Why this page may not run a guarded sequence, or null.
    function guardProblem(opts) {
      if (!opts) return null;
      var page = pageTime();
      var needsPage = opts.notBeforeLoadMs != null || opts.minAgeMs != null || opts.samePageAsMs != null;
      if (needsPage && page === null) return 'no-page-time';
      if (opts.notBeforeLoadMs != null && !(page >= opts.notBeforeLoadMs)) return 'page-too-early';
      if (opts.minAgeMs != null && !(Date.now() - page >= opts.minAgeMs)) return 'page-too-young';
      if (opts.samePageAsMs != null && page !== opts.samePageAsMs) return 'other-page';
      if (opts.onceKey != null && window.__MSFSBA_BBS757EFB_ONCE[String(opts.onceKey)]) return 'already-ran';
      return null;
    }

    function runInfo(run) {
      return { ok: true, id: run.id, n: run.n, done: run.done, running: runIsLive(run), error: run.error, page: pageTime() };
    }

    // Starts a run: the first click now, each next one after the previous step's delay.
    A.clickSeq = function (json, opts) {
      if (runIsLive(state.run)) return refuse('busy');
      var steps = parseSeq(json);
      if (!steps) return refuse('bad-seq');
      if (opts != null && typeof opts !== 'object') return refuse('bad-seq');
      if (touchesUpdateSpot(steps) && !updateGuarded(opts)) return refuse('unguarded-update');
      var g = guardProblem(opts);
      if (g) return refuse(g);
      var p = coherentProblem() || canvasProblem(findCanvas());
      if (p) return refuse('no-canvas: ' + p);

      // A dead run may still hold a pending click: cleared before the new run starts.
      stopRun(state.run, 'canceled');
      if (opts && opts.onceKey != null) window.__MSFSBA_BBS757EFB_ONCE[String(opts.onceKey)] = Date.now();

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
      return { ok: true, id: run.id, n: run.n, done: run.done, totalMs: total, error: run.error, page: pageTime() };
    };

    // How far run `id` got.
    A.status = function (id) {
      var run = state.run;
      if (!run || run.id !== id) return refuse('unknown-run');
      return runInfo(run);
    };

    // Stops the run in progress: no further click is made.
    A.cancel = function () {
      return { ok: true, canceled: stopRun(state.run, 'canceled'), page: pageTime() };
    };

    A.ping = function () {
      var p = coherentProblem() || canvasProblem(findCanvas());
      return { ok: true, ready: !p, problem: p, running: runIsLive(state.run), page: pageTime() };
    };

    window.__MSFSBA_BBS757EFB = A;
    return MARK;
  } catch (e) {
    return 'MSFSBA_BBS757_EFB_ERROR:' + (e && e.message);
  }
})()
