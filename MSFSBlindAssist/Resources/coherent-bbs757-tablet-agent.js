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
// Install: an agent that cannot listen is no agent. When the view's own scripts have not run yet
// (an aircraft reload: the app can reach a new view before they do), RegisterViewListener or
// SimVar is missing, and the install answers NOT_READY instead of the marker, so the app tries
// again. A listener whose ready callback has not come within REREGISTER_MS is registered again on
// the next install. A re-install of a healthy agent (a reconnect of the app's socket) keeps it and
// its frames: the first officer's frame could not be had back until his screen next changes.
// A replaced agent's listener is left registered, never unregistered: unregistering could take the
// tablet's own Comm Bus listener with it.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow functions, no String.includes, top-level
// try/catch. Never returns or logs a Navigraph token.
(function () {
  try {
    var VERSION = 2;
    var MARK = 'MSFSBA_BBS757_TABLET_INSTALLED';
    var NOT_READY = 'MSFSBA_BBS757_TABLET_NOT_READY:';
    var HOLD_MS = 250;
    var REREGISTER_MS = 4000;

    var old = window.__MSFSBA_BBS757;
    if (old && old.version === VERSION && old._state && old._state.listener
        && (old._state.ready || (Date.now() - old._state.registeredAt) < REREGISTER_MS)) return MARK;

    if (typeof RegisterViewListener !== 'function') return NOT_READY + 'RegisterViewListener';
    if (typeof SimVar === 'undefined' || !SimVar || typeof SimVar.SetSimVarValue !== 'function')
      return NOT_READY + 'SimVar';

    var A = { version: VERSION };
    var state = {
      inst: String(Math.floor(Math.random() * 1000000000)),
      frames: { 1: null, 2: null },
      seq: { 1: 0, 2: 0 },
      listener: null,
      ready: false,
      registeredAt: 0
    };
    A._state = state;

    // An older agent's frames carry over.
    if (old && old._state && old._state.frames) {
      state.frames[1] = old._state.frames[1] || null;
      state.frames[2] = old._state.frames[2] || null;
      if (state.frames[1]) state.seq[1] = 1;
      if (state.frames[2]) state.seq[2] = 1;
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

    // The handlers go on once the listener says it is ready, whether that comes during
    // RegisterViewListener or after it.
    var listener = null, readyEarly = false;
    function hook() {
      if (state.ready || !listener) return;
      state.ready = true;
      listener.on('BBS_CDU_DISPLAY_1', function (json) { A.store(1, json); });
      listener.on('BBS_CDU_DISPLAY_2', function (json) { A.store(2, json); });
    }
    try {
      listener = RegisterViewListener('JS_LISTENER_COMM_BUS', function () {
        if (listener) hook(); else readyEarly = true;
      });
    } catch (e) {
      return NOT_READY + 'listener ' + (e && e.message);
    }
    if (!listener) return NOT_READY + 'listener';
    state.listener = listener;
    state.registeredAt = Date.now();
    if (readyEarly) hook();
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

    // The cockpit's 69 keys per CDU, as cockpit XML names them (Bbs757CduKeys.All in the app;
    // Bbs757CduKeysTests reads this list).
    var KEY_NAMES = [
      'L1', 'L2', 'L3', 'L4', 'L5', 'L6', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6',
      'INIT', 'RTE', 'DEPARR', 'ATC', 'VNAV', 'FIX', 'LEGS', 'HOLD', 'FMCCOMM', 'PROG', 'EXEC',
      'MENU', 'NAVRAD', 'PREV', 'NEXT',
      'NUM0', 'NUM1', 'NUM2', 'NUM3', 'NUM4', 'NUM5', 'NUM6', 'NUM7', 'NUM8', 'NUM9', 'NUMPER', 'NUMPLUS',
      'KEYA', 'KEYB', 'KEYC', 'KEYD', 'KEYE', 'KEYF', 'KEYG', 'KEYH', 'KEYI', 'KEYJ', 'KEYK', 'KEYL', 'KEYM',
      'KEYN', 'KEYO', 'KEYP', 'KEYQ', 'KEYR', 'KEYS', 'KEYT', 'KEYU', 'KEYV', 'KEYW', 'KEYX', 'KEYY', 'KEYZ',
      'KEYSP', 'KEYDEL', 'KEYSLASH', 'KEYCLR'
    ];
    A.KEYS = {};
    for (var k = 0; k < KEY_NAMES.length; k++) A.KEYS[KEY_NAMES[k]] = true;

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
