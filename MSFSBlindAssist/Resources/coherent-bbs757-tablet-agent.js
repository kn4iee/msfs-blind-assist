// BlueBird 757-200 tablet agent: both CDU screens, CDU key presses and the Navigraph device code,
// over the Coherent GT remote debugger (port 19999). Installed by CoherentBbs757TabletClient into
// the hidden helper view "VCockpitNN - BBSTablet" (bbs-tablet.html); exposes window.__MSFSBA_BBS757.
//
// Screens: the systems WASM sends each CDU's screen on the Comm Bus when it changes
// (BBS_CDU_DISPLAY_1 captain, BBS_CDU_DISPLAY_2 first officer), as MobiFlight WinCtrl JSON
// {"Target":"Display","Data":[336 cells]}. Nothing arrives while a page is static, so the agent
// keeps the last frame per CDU; the captain's is also cached by BlueBird's own <bbs-tablet>
// element (lastMobiFlightCduPayload), which seeds CDU 1 at install. The first officer's has no
// cache: it arrives on its next change only (docs/b757/evidence/cdu.md).
//
// Keys: L:B757_CDU_<1|2>_<KEY> held at 1 for HOLD_MS, then 0. The hold is fixed and short on
// purpose: a CLR held about a second clears the whole scratchpad, as on the aircraft.
//
// A re-install on the same page (a reconnect of the app's socket) keeps the agent and its frames:
// the first officer's frame could not be had back until his screen next changes.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow functions, no String.includes, top-level
// try/catch. Never returns or logs a Navigraph token.
(function () {
  try {
    var VERSION = 1;
    var MARK = 'MSFSBA_BBS757_TABLET_INSTALLED';
    var HOLD_MS = 250;

    var old = window.__MSFSBA_BBS757;
    if (old && old.version === VERSION && old._state && old._state.listener) return MARK;

    var A = { version: VERSION };
    var state = {
      inst: String(Math.floor(Math.random() * 1000000000)),
      frames: { 1: null, 2: null },
      seq: { 1: 0, 2: 0 },
      listener: null,
      ready: false
    };
    A._state = state;

    // An older agent's frames carry over; its listener is let go so it stops writing.
    if (old && old._state) {
      try {
        if (old._state.frames) { state.frames[1] = old._state.frames[1] || null; state.frames[2] = old._state.frames[2] || null; }
        if (state.frames[1]) state.seq[1] = 1;
        if (state.frames[2]) state.seq[2] = 1;
        if (old._state.listener && old._state.listener.unregister) old._state.listener.unregister();
      } catch (e) { }
    }

    // A screen payload: JSON with a non-empty Data array and, when it names one, the Display target.
    A.isScreen = function (raw) {
      if (typeof raw !== 'string' || raw.length === 0) return false;
      try {
        var j = JSON.parse(raw);
        return !!(j && j.Data && j.Data.length > 0 && (!j.Target || j.Target === 'Display'));
      } catch (e) { return false; }
    };

    A.store = function (cdu, raw) {
      if (!A.isScreen(raw)) return;
      state.frames[cdu] = raw;
      state.seq[cdu] = state.seq[cdu] + 1;
    };

    A.tablet = function () { return document.querySelector('bbs-tablet'); };

    // The captain's screen as BlueBird's helper keeps it.
    A.seedCaptain = function () {
      if (state.frames[1]) return;
      try {
        var t = A.tablet();
        if (t) A.store(1, t.lastMobiFlightCduPayload);
      } catch (e) { }
    };

    try {
      state.listener = RegisterViewListener('JS_LISTENER_COMM_BUS', function () {
        state.ready = true;
        state.listener.on('BBS_CDU_DISPLAY_1', function (json) { A.store(1, json); });
        state.listener.on('BBS_CDU_DISPLAY_2', function (json) { A.store(2, json); });
      });
    } catch (e) {
      state.listener = null;
    }
    A.seedCaptain();

    // One CDU's latest screen. The frame text is sent only when it differs from the caller's
    // tag ("inst:seq"), so an unchanged screen costs a few bytes per poll.
    A.scrape = function (cdu, since) {
      cdu = cdu === 2 ? 2 : 1;
      if (cdu === 1) A.seedCaptain();
      var raw = state.frames[cdu];
      var tag = state.inst + ':' + state.seq[cdu];
      return {
        ok: true,
        cdu: cdu,
        listening: !!state.listener,
        ready: state.ready,
        hasFrame: !!raw,
        tag: tag,
        frame: raw && tag !== since ? raw : null
      };
    };

    A.KEYS = {};
    (function () {
      var names = ['INIT', 'RTE', 'DEPARR', 'ATC', 'VNAV', 'FIX', 'LEGS', 'HOLD', 'FMCCOMM', 'PROG', 'EXEC',
        'MENU', 'NAVRAD', 'PREV', 'NEXT', 'NUMPER', 'NUMPLUS', 'KEYSP', 'KEYDEL', 'KEYSLASH', 'KEYCLR'];
      var i;
      for (i = 1; i <= 6; i++) { names.push('L' + i); names.push('R' + i); }
      for (i = 0; i <= 9; i++) names.push('NUM' + i);
      for (i = 0; i < 26; i++) names.push('KEY' + String.fromCharCode(65 + i));
      for (i = 0; i < names.length; i++) A.KEYS[names[i]] = true;
    })();

    // One key: 1, held HOLD_MS, then 0. Spacing between keys is the app's (400 ms after release).
    A.press = function (cdu, key) {
      if (cdu !== 1 && cdu !== 2) return 'bad-cdu';
      if (typeof key !== 'string' || !A.KEYS[key]) return 'bad-key';
      var name = 'L:B757_CDU_' + cdu + '_' + key;
      try { SimVar.SetSimVarValue(name, 'number', 1); }
      catch (e) { return 'error: ' + (e && e.message); }
      setTimeout(function () {
        try { SimVar.SetSimVarValue(name, 'number', 0); } catch (e) { }
      }, HOLD_MS);
      return 'ok';
    };

    // Navigraph sign-in state for the EFB window. Only presence, the user code and the address:
    // never the device code or any token.
    A.navigraph = function () {
      var t = A.tablet();
      if (!t) return { ok: false };
      var a = t.nav_device_auth || null;
      var code = a && typeof a.user_code === 'string' ? a.user_code : null;
      var uri = null;
      if (a && typeof a.verification_uri_complete === 'string') uri = a.verification_uri_complete;
      else if (a && typeof a.verification_uri === 'string') uri = a.verification_uri;
      return { ok: true, signedIn: !!t.ngAccessToken, userCode: code, uri: uri };
    };

    A.ping = function () { return { ok: true, tablet: !!A.tablet(), listening: !!state.listener, ready: state.ready }; };

    window.__MSFSBA_BBS757 = A;
    return MARK;
  } catch (e) {
    return 'MSFSBA_BBS757_TABLET_ERROR:' + (e && e.message);
  }
})()
