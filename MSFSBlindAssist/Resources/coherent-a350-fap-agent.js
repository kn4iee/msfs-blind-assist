// iniBuilds A350 FAP (the Flight Attendant Panel) in-page agent: installed at runtime into a FAP Coherent view
// ("VCockpitNN - iniFAPA350"; the two views show the same panel). The FAP is plain HTML, all its pages kept in the DOM with
// only one shown, measured live 2026-10-30 on package 1.2.7 against its own script (ini-fap-a350.js).
//
// ES5 ONLY. Coherent GT is Chromium 49 (see coherent-a350-ois-agent.js): var, no arrow functions, no template literals.
//
// Contract, identical to coherent-a350-ois-agent.js so the shared FbwEfbForm consumes it:
//   scrape()            -> JSON string {ok, page, elements:[...]}, or {ok, unchanged:true} when the page has not changed
//   clickElement(idx)   -> press the element carrying that stamped idx
//   setValue(idx, text) -> the FAP has no field: 'not a field'
// Element: {idx,text,kind,clickable,level,key,announceChange,live}
//
// What the FAP is made of, and how each reads:
//   #iniFAP > img                 the screen off picture, over everything: a sighted pilot sees nothing else until it is pressed
//   #bottom-bar .is-button        the tabs; .active marks the selected one
//   #logo-bar h1                  the local time
//   #renderer > .visiblePage      the shown page
//   .is-button                    a button; lit lime when set. One with no press handler (Caution, the Temp page's Zones, its zones and
//                                 its requested figure) only looks like one: a read-out here
//   .col > h2 + img               Cabin Status's tiles: the picture takes the press and goes to that page (Smoke Detect's does nothing)
//   .door-clickspot               a door: its indicator's colour is its state, and its indicator takes the press
//   .row.p-3 with scale ticks     a tank: its bar (height and colour), its figure in the row below, its name and volume below that
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_A350_FAP_INSTALLED';
  A.ATTR = 'data-a350-fap-idx';
  A.OWN = 'data-a350-fap-own';

  A.norm = function (s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, ''); };

  A.visible = function (e) {
    var r = e.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };

  A.cls = function (e) { return typeof e.className === 'string' ? e.className : ''; };
  A.hasCls = function (e, c) { return (' ' + A.cls(e) + ' ').indexOf(' ' + c + ' ') >= 0; };
  A.own = function (e) { e.setAttribute(A.OWN, '1'); };
  A.owned = function (e) {
    for (var p = e; p && p !== document.body; p = p.parentElement)
      if (p.getAttribute && p.getAttribute(A.OWN) === '1') return true;
    return false;
  };

  // An element's texts, a part at each line break or child element ("Cabin<br>Medium Dim" -> Cabin, Medium Dim; "29"," °C" -> 29 °C).
  A.parts = function (e) {
    var out = [], cur = '';
    var flush = function () { var t = A.norm(cur); if (t) out.push(t); cur = ''; };
    var walk = function (n) {
      for (var c = n.firstChild; c; c = c.nextSibling) {
        if (c.nodeType === 3) cur += c.nodeValue;
        else if (c.nodeType === 1) { flush(); if (c.tagName !== 'BR' && A.visible(c)) { walk(c); flush(); } }
      }
    };
    walk(e);
    flush();
    return out;
  };

  // A CSS colour as [r, g, b], or null.
  A.rgb = function (c) {
    c = String(c || '').toLowerCase().replace(/\s/g, '');
    var named = { blue: [0, 0, 255], green: [0, 128, 0], lime: [0, 255, 0], red: [255, 0, 0], orange: [255, 165, 0], white: [255, 255, 255], black: [0, 0, 0] };
    var m = /^rgba?\((\d+),(\d+),(\d+)/.exec(c), rgb = m ? [+m[1], +m[2], +m[3]] : named[c];
    if (!rgb && /^#[0-9a-f]{6}$/.test(c)) rgb = [parseInt(c.substr(1, 2), 16), parseInt(c.substr(3, 2), 16), parseInt(c.substr(5, 2), 16)];
    return rgb || null;
  };

  // A colour as the word a pilot would say, or ''.
  A.colourWord = function (c) {
    var rgb = A.rgb(c);
    if (!rgb) return '';
    var r = rgb[0], g = rgb[1], b = rgb[2];
    if (r > 200 && g > 200 && b > 200) return 'white';
    if (Math.max(r, g, b) - Math.min(r, g, b) <= 30) return r < 40 ? 'black' : 'grey';
    if (b > r && b > g) return 'blue';
    if (g > r && g > b) return 'green';
    if (r > b && g > 80) return 'amber';
    if (r > g && r > b) return 'red';
    return '';
  };

  // The lit lime a set button is painted (Cabin Ready, the Temp page's Zones).
  A.isLit = function (e) {
    var rgb = A.rgb(e.style.backgroundColor);
    return !!rgb && rgb[0] === 191 && rgb[1] === 239 && rgb[2] === 15;
  };

  A.press = function (e) {
    var r = e.getBoundingClientRect();
    var o = { bubbles: true, cancelable: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, button: 0, view: window };
    var names = ['mousedown', 'mouseup', 'click'];
    for (var i = 0; i < names.length; i++) e.dispatchEvent(new MouseEvent(names[i], o));
  };

  // A press handler with nothing in it (Smoke Detect's: only a comment).
  A.inert = function (e) {
    return !!e.onclick && /^[^{]*\{\s*(\/\/[^\n]*\s*|\/\*[\s\S]*?\*\/\s*)*\}\s*$/.test(String(e.onclick));
  };

  // Whether the page's press handlers can be seen: a tab always has one. (Never in a fixture: a saved page has lost them.)
  A.handlersKnown = function () {
    var tab = document.querySelector('#bottom-bar .is-button');
    return !!(tab && tab.onclick);
  };

  // The Lights page's level of a zone tile, or '': "Cabin", "Medium Dim".
  A.zoneLevel = function (root, test) {
    var tiles = root.querySelectorAll('.is-button');
    for (var i = 0; i < tiles.length; i++) {
      var bits = A.parts(tiles[i]);
      if (bits.length === 2 && test(tiles[i], bits[0])) return bits[1];
    }
    return '';
  };

  // Cabin Ready: the sim's own L:var when the page can read it, as the paint follows only a press on this view.
  A.cabinReady = function (e) {
    try {
      if (typeof SimVar !== 'undefined') return SimVar.GetSimVarValue('L:INI_FAP_CABIN_READY', 'number') > 0;
    } catch (err) {}
    return A.isLit(e);
  };

  A.DOORS = {
    'door-l1': 'Door 1 Left', 'door-r1': 'Door 1 Right', 'door-l2': 'Door 2 Left', 'door-r2': 'Door 2 Right',
    'door-l3': 'Door 3 Left', 'door-r3': 'Door 3 Right', 'door-l4': 'Door 4 Left', 'door-r4': 'Door 4 Right'
  };

  // A door's state, from the colour the page's legend names.
  A.doorState = function (indicator) {
    var word = A.colourWord(indicator && indicator.style.backgroundColor);
    return { blue: 'locked, slide armed, blue', green: 'locked, slide disarmed, green', amber: 'moving, amber', red: 'open, red' }[word] || word;
  };

  // A tank (Water/Waste): one line per bar: its name, figure, colour and volume.
  A.tankLines = function (block) {
    var values = block.nextElementSibling;
    var names = values && values.nextElementSibling;
    var lines = [];
    for (var i = 0; i < block.children.length; i++) {
      var bar = block.children[i].firstElementChild;
      var isBar = bar && !bar.firstElementChild && /\bh-100\b/.test(A.cls(bar)) === false && !/border/.test(bar.getAttribute('style') || '');
      var figure = values && values.children[i] ? A.norm(values.children[i].textContent) : '';
      if (!isBar || !figure) continue;
      var labels = names && names.children[i] ? A.parts(names.children[i]) : [];
      var colour = A.colourWord(bar.style.backgroundColor);
      var line = (labels[0] || 'Quantity') + ': ' + figure + (colour ? ', ' + colour : '');
      if (labels.length >= 3) line += ', ' + labels[1] + ' ' + labels[2];
      lines.push(line);
    }
    return lines;
  };

  // What each page's picture shows. It never changes: the aircraft seen from above, as a sighted pilot sees it.
  A.PICTURES = {
    'A350FAP_LightsOutline': 'Picture: the aircraft from above, the cabin lit in light blue from front to back',
    'A350FAP_TempZonesOutline': 'Picture: the aircraft from above, the cabin in three zones from front to back',
    'A350FAP_DoorsSlidesOutline': 'Picture: the aircraft from above, four doors along each side',
    'A350FAP_WasteOutline': 'Picture: the aircraft from above, ten white marks along the cabin',
    'A350FAP_SmokeDetOutline': 'Picture: the aircraft from above, ten white marks along the cabin'
  };
  A.pictureLine = function (img) {
    var file = (/([^\/]+)\.\w+$/.exec(img.getAttribute('src') || '') || [])[1];
    return (file && A.PICTURES[file]) || '';
  };

  A.collectPage = function () {
    try {
      var all = document.querySelectorAll('[' + A.ATTR + '],[' + A.OWN + ']');
      for (var c = 0; c < all.length; c++) { all[c].removeAttribute(A.ATTR); all[c].removeAttribute(A.OWN); }
      var out = [], idx = 0, page = '';
      var push = function (el, item) {
        if (el) { idx++; el.setAttribute(A.ATTR, String(idx)); item.idx = idx; } else item.idx = 0;
        out.push(item);
      };

      // A toast answers a press (Water tank refilled, Unable to toggle when moving or engines running): spoken as it appears.
      var toasts = document.querySelectorAll('.notie-container');
      for (var n = 0; n < toasts.length; n++) {
        if (toasts[n].getBoundingClientRect().bottom <= 0 || !A.visible(toasts[n])) continue;   // a spent one slides off the top
        var inner = toasts[n].querySelector('.notie-textbox-inner') || toasts[n];
        var tt = A.norm(inner.textContent);
        if (tt) push(null, { kind: 'alert', live: 'assertive', text: tt });
      }

      var shot = document.querySelector('#iniFAP > img');
      if (shot && A.visible(shot)) {
        page = 'Screen off';
        push(null, { kind: 'heading', level: 1, text: page });
        push(shot, { kind: 'button', text: 'Wake the screen', clickable: true, key: 'button:wake' });
        return JSON.stringify({ ok: true, page: page, elements: out });
      }

      var tabs = document.querySelectorAll('#bottom-bar .is-button');
      for (var t = 0; t < tabs.length; t++) {
        var tabName = A.norm(tabs[t].textContent).replace('/ ', '/');
        var selected = A.hasCls(tabs[t], 'active');
        if (selected) page = tabName;
        push(tabs[t], { kind: 'button', text: tabName + (selected ? ' tab (selected)' : ' tab'), clickable: true, key: 'tab:' + tabName });
      }
      var clock = document.querySelector('#logo-bar h1');
      if (clock && A.norm(clock.textContent)) push(null, { kind: 'static', text: 'Local time: ' + A.norm(clock.textContent), key: 'readout:Local time' });

      var root = document.querySelector('#renderer > .visiblePage');
      if (!root) {
        page = 'Blank';
        push(null, { kind: 'static', text: 'The screen is blank: press a tab', key: 'readout:blank' });
        return JSON.stringify({ ok: true, page: page, elements: out });
      }

      var els = root.querySelectorAll('*');
      var picture = '';
      var known = A.handlersKnown();
      // The Lights page's keys say the level they act on, so a press speaks its result: a key's own level is the picked zone's,
      // and Night Mode and Main On act on the cabin.
      var picked = A.zoneLevel(root, function (tile) { return A.hasCls(tile, 'active'); });
      var cabin = A.zoneLevel(root, function (tile, name) { return name === 'Cabin'; });
      for (var i = 0; i < els.length; i++) {
        var e = els[i];
        if (A.owned(e) || !A.visible(e)) continue;
        var tag = e.tagName;

        if (tag === 'IMG') {
          if (!picture) picture = A.pictureLine(e);
          continue;
        }

        // The Cabin Status tiles: a title over a picture, and the picture takes the press.
        if (A.hasCls(e, 'col') && e.children.length === 2 && e.firstElementChild.tagName === 'H2' && e.lastElementChild.tagName === 'IMG') {
          A.own(e);
          var tile = A.norm(e.firstElementChild.textContent);
          push(e.lastElementChild, { kind: 'button', text: tile + (A.inert(e.lastElementChild) ? ', has no page' : ' page'), clickable: true, key: 'tile:' + tile });
          continue;
        }

        if (A.hasCls(e, 'is-button')) {
          A.own(e);
          var bits = A.parts(e), name = bits.join(': ');
          if (!name) continue;
          if (name === 'Cabin Ready') {
            push(e, { kind: 'button', text: name + ': ' + (A.cabinReady(e) ? 'On' : 'Off'), clickable: true, key: 'button:' + name, announceChange: true });
            continue;
          }
          // The Temp page's requested temperature and its two keys. Their label carries the figure, so a press speaks it.
          if (name === '+' || name === '-') {
            var mid = name === '+' ? e.nextElementSibling : e.previousElementSibling;
            var now = mid ? A.norm(mid.textContent) : '';
            var word = name === '+' ? 'Requested temperature up' : 'Requested temperature down';
            push(e, { kind: 'button', text: word + (now ? ', now ' + now : ''), clickable: true, key: 'button:' + word, announceChange: true });
            continue;
          }
          if (name === 'Night Mode' || name === 'Main On') {
            push(e, { kind: 'button', text: name + (cabin ? ', cabin now ' + cabin : ''), clickable: true, key: 'button:' + name, announceChange: true });
            continue;
          }
          if (/^(Bright|Medium Dim|Low Dim|Off)$/.test(name)) {
            push(e, { kind: 'button', text: name + (name === picked ? ' (current)' : ''), clickable: true, key: 'button:' + name, announceChange: true });
            continue;
          }
          if (/^-?\d+ °C$/.test(name)) name = 'Requested temperature: ' + name;
          if (A.hasCls(e, 'active')) name += ' (selected)';
          else if (A.isLit(e)) name += ' (lit green)';
          if (known && !e.onclick) {
            push(null, { kind: 'static', text: name, key: 'readout:' + bits[0] });
            continue;
          }
          push(e, { kind: 'button', text: name, clickable: true, key: 'button:' + bits[0], announceChange: bits.length > 1 });
          continue;
        }

        if (A.hasCls(e, 'door-clickspot')) {
          A.own(e);
          var ind = e.querySelector('.door-indicator');
          push(ind || e, { kind: 'button', text: (A.DOORS[e.id] || 'Door') + ': ' + A.doorState(ind), clickable: true, key: 'door:' + e.id, announceChange: true });
          continue;
        }

        // The door labels beside the picture: said with each door.
        if ((A.hasCls(e, 'col') || A.hasCls(e, 'text-end')) && e.parentElement && e.parentElement.querySelector('.door-clickspot')) {
          A.own(e);
          continue;
        }

        // The legend: a swatch beside its text.
        if (A.hasCls(e, 'col-sm-3') && e.nextElementSibling && A.hasCls(e.nextElementSibling, 'col-sm-9') && e.firstElementChild) {
          A.own(e.parentElement);
          var said = A.parts(e.nextElementSibling).join(', ');
          var swatch = A.colourWord(e.firstElementChild.style.backgroundColor);
          push(null, { kind: 'static', text: (swatch ? swatch.charAt(0).toUpperCase() + swatch.substr(1) + ': ' : '') + said });
          continue;
        }

        // A tank's bar, figure, name and volume.
        if (A.hasCls(e, 'row') && A.hasCls(e, 'p-3') && e.querySelector('.px-4.py-1')) {
          A.own(e);
          var below = e.nextElementSibling;
          if (below) { A.own(below); if (below.nextElementSibling) A.own(below.nextElementSibling); }
          var lines = A.tankLines(e);
          for (var l = 0; l < lines.length; l++) push(null, { kind: 'static', text: lines[l], key: 'readout:' + lines[l].split(':')[0] });
          continue;
        }

        // The Temp page's scale: its limits and the mark of the requested temperature.
        if (A.hasCls(e, 'my-5') && e.querySelector('.px-4.py-1')) {
          A.own(e);
          var ticks = e.querySelectorAll('.px-4.py-1'), scale = [];
          for (var k = 0; k < ticks.length; k++) scale.push(A.norm(ticks[k].textContent));
          push(null, { kind: 'static', text: 'Scale: ' + scale.join(', ') + ', a red mark at the requested temperature' });
          continue;
        }

        if (A.hasCls(e, 'alert')) {
          A.own(e);
          push(null, { kind: 'static', text: A.norm(e.textContent) + ', red', key: 'readout:alert' });
          continue;
        }

        if (/^H[1-6]$/.test(tag)) {
          A.own(e);
          var ht = A.norm(e.textContent);
          if (!ht) continue;
          // The first h1 is the page's title.
          var first = A.hasCls(e, 'p-3');
          if (first) page = ht;
          push(null, { kind: 'heading', level: first ? 1 : 2, text: ht });
          continue;
        }
      }
      if (picture) push(null, { kind: 'static', text: picture });
      return JSON.stringify({ ok: true, page: page, elements: out });
    } catch (err) {
      return JSON.stringify({ ok: false, error: String(err && err.message || err) });
    }
  };

  // The dirty gate, as the OIS's: a scrape with nothing changed in the DOM since the last full one answers {unchanged:true}.
  // Every FORCE_FULL_EVERY-th scrape is full whatever the flag (a toast's slide is an animation the observer cannot see). The
  // observer is disconnected while the page is read, as the reader's own stamping would otherwise dirty the next poll.
  A._everScraped = false;
  A._dirty = true;
  A._pollCount = 0;
  A._obs = null;
  A.OBSERVER_OPTS = { childList: true, subtree: true, characterData: true, attributes: true };
  A.FORCE_FULL_EVERY = 10;
  A._markDirty = function () { A._dirty = true; };

  A.scrape = function () {
    A._pollCount++;
    var forceFull = !A._everScraped || A._pollCount % A.FORCE_FULL_EVERY === 0;
    if (!A._dirty && !forceFull) return JSON.stringify({ ok: true, unchanged: true });
    A._dirty = false;   // cleared before the read: a change landing during it dirties the next poll
    A._everScraped = true;
    if (A._obs) { try { A._obs.disconnect(); } catch (e1) {} }
    try { return A.collectPage(); }
    finally { if (A._obs) { try { A._obs.observe(document.body, A.OBSERVER_OPTS); } catch (e2) {} } }
  };

  A.find = function (idx) { return document.querySelector('[' + A.ATTR + '="' + idx + '"]'); };

  A.clickElement = function (idx) {
    A._dirty = true;
    try {
      var e = A.find(idx);
      if (!e) return 'missing';
      A.press(e);
      return 'ok';
    } catch (err) { return 'error: ' + err.message; }
  };

  A.setValue = function () { return 'not a field'; };

  try { if (window.__MSFSBA_A350_FAP_OBS) window.__MSFSBA_A350_FAP_OBS.disconnect(); } catch (e0) {}
  try {
    A._obs = new MutationObserver(A._markDirty);
    A._obs.observe(document.body, A.OBSERVER_OPTS);
    window.__MSFSBA_A350_FAP_OBS = A._obs;
  } catch (e3) {
    A._obs = null;
    A.FORCE_FULL_EVERY = 1;   // no observer: never gate (slower, never stale)
  }

  window.__MSFSBA_A350_FAP = A;
  return A.INSTALLED;
})();
