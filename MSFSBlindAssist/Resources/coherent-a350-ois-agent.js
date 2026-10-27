// iniBuilds A350 OIS (its EFB) in-page agent: installed at runtime into an OIS Coherent view
// ("VCockpitNN - iniEfbA350"; the captain's loads ini-efb-a350-cpt.html, the first officer's
// ini-efb-a350-fo.html). The OIS is plain HTML (Bootstrap-style rows and option groups), measured
// live 2026-10-27 on package 1.2.7.
//
// ES5 ONLY. Coherent GT is Chromium 49: var, no arrow functions, no template literals, no
// String.includes / Array.from / Object.assign, and no PointerEvent (a press is mousedown, mouseup,
// click, which the OIS's buttons take; a bare el.click() navigates too but the cyclers want the mouse
// sequence).
//
// Contract, identical to coherent-pmdg-efb-agent.js so the shared FbwEfbForm consumes it:
//   scrape()            -> JSON string {ok, page, elements:[...]}
//   clickElement(idx)   -> press the element carrying that stamped idx
//   setValue(idx, text) -> pick an option (an option group), type into a field, or move a range
// Element: {idx,text,value,controlType,kind,clickable,level,disabled,options,min,max,step,key,announceChange}
//
// What the OIS is made of, and how each reads:
//   .page-title                      the page: a level-1 heading and the scrape's page name
//   .option-group                    a row: .option-group-label + radios (a select of their labels), a
//                                    number/text input (a field, labelled with the row's limits), a
//                                    range, or read-only text
//   input inside a .row              a field labelled by the row's other cells ("TOW, x1000 LBS")
//   input[type=button]               a cycler: each press steps to its next value ("RWY COND: DRY" ->
//                                    WET), so its label carries the value and the shell speaks it again
//                                    after a press (announceChange); "SELECT" ones open a list instead
//   button, .is-button               a button; .active marks the selected tab ("(selected)")
//   .text-end label + next cell      a read-out ("V1: 142 kt")
//   h1-h6, .menu-column-title        headings (a menu column's title: MISSION, PERFORMANCE)
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_A350_OIS_INSTALLED';
  A.ATTR = 'data-a350-ois-idx';
  // A field's own label, kept on the input: the OIS replaces a label cell with a validation result
  // once a value is typed (AIRP becomes "<OK>"), and the field must still say what it is.
  A.LABEL = 'data-a350-ois-label';
  A.OWN = 'data-a350-ois-own';

  A.norm = function (s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, ''); };

  // Drawn, and not slid off the side of the screen (ENROUTE's map-type panel waits at right: -300px).
  A.visible = function (e) {
    var r = e.getBoundingClientRect();
    return r.width > 0 && r.height > 0 && r.right > 0 && r.left < window.innerWidth;
  };

  // The icon buttons, which carry no text: TERML CHART's chart controls, ENROUTE's map controls.
  A.ICON_NAMES = {
    ctrlZoomIn: 'Zoom in', ctrlZoomOut: 'Zoom out', ctrlReset: 'Reset view', ctrlSidebar: 'Chart list',
    ctrlDarkLight: 'Dark or light', ctrlPanUp: 'Pan up', ctrlPanLeft: 'Pan left', ctrlPanRight: 'Pan right',
    ctrlPanDown: 'Pan down', ctrlPrevPage: 'Previous page', ctrlNextPage: 'Next page', filter_pinned: 'Pinned',
    'mapcontrol-recenter': 'Recenter', 'mapcontrol-zoomin': 'Zoom in', 'mapcontrol-zoomout': 'Zoom out'
  };

  // Icon pictures inside buttons, by file (GROUND's pushback chevrons).
  A.IMAGE_NAMES = { 'chevron-left': 'Left', 'chevron-right': 'Right', 'chevron-up': 'Up', 'chevron-down': 'Down' };
  A.PUSHBACK_NAMES = { 'chevron-left': 'Push back, turn left', 'chevron-right': 'Push back, turn right', 'chevron-down': 'Push back straight' };

  A.sectionTitle = function (e) {
    var row = e.closest ? e.closest('.row') : null;
    var h = row ? row.querySelector('h1,h2,h3') : null;
    return h ? A.norm(h.textContent) : '';
  };

  // A button's name: its icon's, the map-type panel's tab ("<"), its picture's, or its text.
  A.buttonText = function (b) {
    if (b.id && A.ICON_NAMES[b.id]) return A.ICON_NAMES[b.id];
    var t = A.norm(b.innerText);
    if (/^[<>]$/.test(t) && b.closest && b.closest('#enroutemap-sidebar')) return 'Map types';
    if (!t) {
      var img = b.querySelector('img');
      var file = img ? (/([^\/]+)\.\w+$/.exec(img.getAttribute('src') || '') || [])[1] : '';
      if (file) return (A.sectionTitle(b) === 'PUSHBACK' && A.PUSHBACK_NAMES[file]) || A.IMAGE_NAMES[file] || '';
    }
    return t;
  };

  // GROUND's door map: a clickspot per door, its indicator's colour its state, keyed on the page:
  // green Closed, blue Armed, red Open, orange Transit.
  A.colourState = function (c) {
    c = String(c || '').toLowerCase().replace(/\s/g, '');
    var named = { blue: [0, 0, 255], navy: [0, 0, 128], darkblue: [0, 0, 139], green: [0, 128, 0], lime: [0, 255, 0],
      darkgreen: [0, 100, 0], red: [255, 0, 0], darkred: [139, 0, 0], maroon: [128, 0, 0], orange: [255, 165, 0], darkorange: [255, 140, 0] };
    var m = /^rgba?\((\d+),(\d+),(\d+)/.exec(c), rgb = m ? [+m[1], +m[2], +m[3]] : named[c];
    if (!rgb && /^#[0-9a-f]{6}$/.test(c)) rgb = [parseInt(c.substr(1, 2), 16), parseInt(c.substr(3, 2), 16), parseInt(c.substr(5, 2), 16)];
    if (!rgb) return '';
    var r = rgb[0], g = rgb[1], b = rgb[2];
    if (b > r && b > g) return 'Armed';
    if (g > r && g > b) return 'Closed';
    if (r > b && g > 80) return 'Transit';
    if (r > g && r > b) return 'Open';
    return '';
  };
  A.doorState = function (spot) {
    var ind = spot && spot.querySelector('.door-indicator');
    return ind ? A.colourState(ind.style.backgroundColor) : '';
  };
  A.doorName = function (id) {
    var m = /^door-([lr]\d)$/.exec(id);
    if (m) return 'Door ' + m[1].toUpperCase();
    return { 'door-fwd-cargo': 'Forward cargo door', 'door-aft-cargo': 'Aft cargo door', 'door-bulk-cargo': 'Bulk cargo door' }[id] || 'Door';
  };
  // The sidebar's cargo buttons press the same doors.
  A.CARGO_DOORS = { 'FWD CARGO': 'door-fwd-cargo', 'AFT CARGO': 'door-aft-cargo', 'BULK CARGO': 'door-bulk-cargo' };

  // The title of the section a button sits in: the nearest earlier cell of its row holding a heading
  // (GROUND's sidebar: DOORS & EXITS, LIGHTS, EXTERIOR).
  A.headingBefore = function (b) {
    var row = b.closest ? b.closest('.row') : null;
    if (!row) return '';
    var cell = b;
    while (cell.parentElement && cell.parentElement !== row) cell = cell.parentElement;
    for (var k = cell.previousElementSibling; k; k = k.previousElementSibling) {
      var h = k.querySelector && k.querySelector('h1,h2,h3');
      if (h) return A.norm(h.textContent);
    }
    return '';
  };

  // GROUND's sidebar toggles: a light or the covers is lit green when on; the avionics bay door takes
  // the door key's colours. -> {name, state}, or null for any other button.
  A.sidebarToggle = function (b, text) {
    if (!(b.closest && b.closest('.doors-sidebar'))) return null;
    var section = A.headingBefore(b), bg = b.style.backgroundColor;
    if (section === 'LIGHTS') return { name: text + ' light', state: A.colourState(bg) === 'Closed' ? 'On' : 'Off' };
    if (text === 'COVERS') return { name: text, state: A.colourState(bg) === 'Closed' ? 'On' : 'Off' };
    if (section === 'DOORS & EXITS' && text === 'AVIONICS BAY') return { name: text + ' door', state: A.colourState(bg) || 'Closed' };
    return null;
  };

  // A button's state where the page shows it elsewhere: a cargo door's colour, the chocks' picture.
  A.buttonState = function (b, text) {
    if (b.closest && b.closest('.doors-sidebar') && A.CARGO_DOORS[text])
      return A.doorState(document.getElementById(A.CARGO_DOORS[text]));
    if (text === 'CHOCKS') {
      var ch = document.getElementById('chocks_nose_front');
      if (ch) return parseFloat(ch.style.opacity || '0') > 0 ? 'Placed' : 'Removed';
    }
    return '';
  };

  // A control outside the rows: its <label for> (GROUND's GSX switch), else the title above its
  // container (the snow slider under COLD WEATHER ACCUMULATION). -> the label element, or null.
  A.ownLabelEl = function (input) {
    if (input.id) {
      var l = document.querySelector('label[for="' + input.id + '"]');
      if (l) return l;
    }
    var prev = input.parentElement && input.parentElement.previousElementSibling;
    return prev && prev.querySelector && !A.holdsControl(prev) && prev.querySelector('h1,h2,h3,h4') ? prev : null;
  };

  // The pictures the OIS draws, read as what they are.
  A.picture = function (e) {
    if (e.id === 'enroutemap' || A.hasCls(e, 'leaflet-container')) return 'Map picture (not readable)';
    if (e.id === 'chart_image_box') return 'Chart picture (not readable)';
    return '';
  };

  A.ownText = function (e) {
    var s = '';
    for (var c = e.firstChild; c; c = c.nextSibling) if (c.nodeType === 3) s += c.nodeValue;
    return A.norm(s);
  };

  A.cls = function (e) { return typeof e.className === 'string' ? e.className : ''; };
  A.hasCls = function (e, c) { return (' ' + A.cls(e) + ' ').indexOf(' ' + c + ' ') >= 0; };

  A.owned = function (e) {
    for (var p = e; p && p !== document.body; p = p.parentElement)
      if (p.getAttribute && p.getAttribute(A.OWN) === '1') return true;
    return false;
  };

  A.own = function (e) { e.setAttribute(A.OWN, '1'); };

  // The decorative bits every page carries: the "EFB" and "iniBuilds" marks.
  A.decor = function (text) { return text === 'EFB' || text === 'iniBuilds'; };

  A.press = function (e) {
    var r = e.getBoundingClientRect();
    var o = { bubbles: true, cancelable: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, button: 0, view: window };
    var names = ['mousedown', 'mouseup', 'click'];
    for (var i = 0; i < names.length; i++) e.dispatchEvent(new MouseEvent(names[i], o));
  };

  // [A350-6] Typing is key events per character, then Enter: a bare value set shows in the field but
  // COMPUTE ignores it.
  A.type = function (t, text) {
    A.press(t);
    t.focus();
    var set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    set.call(t, '');
    t.dispatchEvent(new Event('input', { bubbles: true }));
    for (var i = 0; i < text.length; i++) {
      var c = text.charAt(i), code = c.charCodeAt(0);
      t.dispatchEvent(new KeyboardEvent('keydown', { key: c, keyCode: code, which: code, bubbles: true }));
      t.dispatchEvent(new KeyboardEvent('keypress', { key: c, keyCode: code, charCode: code, which: code, bubbles: true }));
      set.call(t, t.value + c);
      t.dispatchEvent(new Event('input', { bubbles: true }));
      t.dispatchEvent(new KeyboardEvent('keyup', { key: c, keyCode: code, which: code, bubbles: true }));
    }
    t.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 13, which: 13, bubbles: true }));
    t.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', keyCode: 13, which: 13, bubbles: true }));
    t.dispatchEvent(new Event('change', { bubbles: true }));
    t.blur();
  };

  // A text with a space where the OIS breaks a line ("...the turnaround?<br>This will..."): Coherent's
  // innerText runs the two together.
  A.text = function (e) {
    var raw = function (n) {
      var s = '';
      for (var c = n.firstChild; c; c = c.nextSibling) {
        if (c.nodeType === 3) s += c.nodeValue;
        else if (c.nodeType === 1) s += c.tagName === 'BR' ? ' ' : raw(c);
      }
      return s;
    };
    return A.norm(raw(e));
  };

  // A cell holding a control of its own (a fuel row's unit button) is read as that control, never as
  // part of a neighbour's label.
  A.CONTROL = 'button, select, input:not([type=hidden])';
  A.holdsControl = function (cell) { return !!(cell.querySelector && cell.querySelector(A.CONTROL)); };

  // A full-width cell: a title over what follows it in a stacked row (LOADSHEET's "ZFW" over its
  // field and unit, then "FUEL LOAD" over the next).
  A.isTitleCell = function (k) { return A.hasCls(k, 'col-sm-12') && !A.holdsControl(k); };

  // The cells that label a field: the other plain cells of its row ("TOW", "x1000 LBS"); in a stacked
  // row, those from its title cell up to the next title.
  A.labelCells = function (input) {
    var row = input.closest ? input.closest('.row') : null;
    if (!row) return [];
    var cell = input;
    while (cell.parentElement && cell.parentElement !== row) cell = cell.parentElement;
    // Only a grid cell of the row is labelled by it (GROUND's fixed GSX panel sits in the doors' row).
    if (!/(^|\s)col(-|\s|$)/.test(A.cls(cell))) return [];
    var first = row.firstElementChild, stop = null;
    if (!A.hasCls(cell, 'col-sm-12')) {
      for (var b = cell.previousElementSibling; b; b = b.previousElementSibling)
        if (A.isTitleCell(b)) { first = b; break; }
      for (var f = cell.nextElementSibling; f; f = f.nextElementSibling)
        if (A.isTitleCell(f)) { stop = f; break; }
    }
    var cells = [];
    for (var k = first; k && k !== stop; k = k.nextElementSibling)
      if (k !== cell && !A.holdsControl(k)) cells.push(k);
    return cells;
  };

  // The texts of a field's label cells.
  A.rowParts = function (input) {
    var cells = A.labelCells(input), parts = [];
    for (var i = 0; i < cells.length; i++) {
      var t = A.norm(cells[i].innerText || cells[i].textContent);
      if (t) parts.push(t);
    }
    return parts;
  };

  // A field's label: its row's texts ("TOW x1000 LBS" -> "TOW, x1000 LBS").
  A.rowLabel = function (input) { return A.rowParts(input).join(', '); };

  // The OIS writes a field's validation result into its label cell in angle brackets (an accepted
  // airport's "AIRP" becomes "<OK>"), and may re-create the field when it does. The label is kept on
  // the field and in a page-wide map that outlives the agent being re-installed; a field first seen
  // with only its result is named by its box placeholder (four boxes are an airport, three a runway).
  A.STATUS = /^<(.+)>$/;
  A.BOX_NAMES = { '[][][][]': 'AIRP', '[][][]': 'RWY' };
  A.memo = window.__MSFSBA_A350_OIS_LABELS || (window.__MSFSBA_A350_OIS_LABELS = {});

  A.boxName = function (input) {
    var ph = input.getAttribute('placeholder');
    if (!ph && input.parentElement) {
      var p = input.parentElement.querySelector('input[placeholder]');
      ph = p ? p.getAttribute('placeholder') : '';
    }
    return A.BOX_NAMES[ph] || '';
  };

  // -> {label, text}: the field's own name, and that name with the OIS's result ("AIRP, OK").
  A.fieldLabel = function (input, memoKey) {
    var parts = A.rowParts(input), names = [], status = [];
    for (var i = 0; i < parts.length; i++) {
      var m = A.STATUS.exec(parts[i]);
      if (m) status.push(A.norm(m[1])); else names.push(parts[i]);
    }
    var label = names.join(', ');
    if (label) {
      input.setAttribute(A.LABEL, label);
      A.memo[memoKey] = label;
    } else {
      var ol = A.ownLabelEl(input);
      label = (ol && A.text(ol)) || input.getAttribute(A.LABEL) || A.memo[memoKey] || A.boxName(input);
    }
    var said = status.join(', ');
    return { label: label, text: label && said ? label + ', ' + said : (label || said) };
  };

  // Claims a field's row-mates (its label and unit cells) before the walk reaches them, so they are
  // read once, inside the field's label, never also as loose text above it.
  A.claimRowCells = function (input) {
    if (input.closest && input.closest('.option-group')) return;
    var cells = A.labelCells(input);
    for (var i = 0; i < cells.length; i++) A.own(cells[i]);
    if (!cells.length) { var ol = A.ownLabelEl(input); if (ol) A.own(ol); }
  };

  // An options button's name from its row: the label cells beside it ("FROM RWY" + AUTO). When the
  // row also holds a field, the button is that field's unit ("BLOCK" + field + WEIGHT).
  A.buttonRowName = function (button) {
    var row = button.closest ? button.closest('.row') : null;
    if (!row) return '';
    var cell = button;
    while (cell.parentElement && cell.parentElement !== row) cell = cell.parentElement;
    if (cell === button) return '';
    var names = [], field = false;
    for (var k = row.firstElementChild; k; k = k.nextElementSibling) {
      if (k === cell) continue;
      if (k.querySelector('input[type=text],input[type=number],input:not([type])')) field = true;
      else if (A.hasCls(k, 'text-end') && !A.holdsControl(k)) {
        var t = A.norm(k.innerText || k.textContent);
        if (t) names.push(t);
        A.own(k);
      }
    }
    var name = names.join(', ');
    return name && field ? name + ' unit' : name;
  };

  // A dialog: a fixed overlay (or the OIS's .popup-overlay: Resend Loadsheet) over the page with a title, a message and its buttons (TURNAROUND START:
  // CANCEL / START). -> its title and message as one line, owning both; null when e is not one.
  A.dialogText = function (e) {
    var over = (e.style && e.style.position === 'fixed') || A.hasCls(e, 'popup-overlay');
    if (!over || !e.querySelector('button')) return null;
    var h = e.querySelector('h1,h2,h3,h4,h5,h6');
    var title = h ? A.text(h) : '', msg = [];
    if (h) A.own(h);
    // The message: every text of the overlay outside its title and its controls (it may share a row
    // with a button: the Navigraph notice), each owned so the walk reads only the controls.
    var walk = function (n) {
      for (var k = n.firstElementChild; k; k = k.nextElementSibling) {
        if (k === h) continue;
        if (A.holdsControl(k)) { walk(k); continue; }
        if (k.matches && k.matches(A.CONTROL)) continue;
        var t = A.text(k);
        if (t) msg.push(t);
        A.own(k);
      }
    };
    walk(e);
    var m = msg.join(' ');
    return title && m ? title + ': ' + m : (title || m);
  };

  // A unit toggle: a heading directly above a button ("Weight Unit" over "x1000 LBS").
  A.headingAbove = function (button) {
    var h = button.previousElementSibling;
    return h && /^H[1-6]$/.test(h.tagName) ? A.norm(h.innerText) : '';
  };

  A.scrape = function () {
    try {
      var all = document.body.querySelectorAll('*');
      var i, e;
      for (i = 0; i < all.length; i++) {
        all[i].removeAttribute(A.ATTR);
        all[i].removeAttribute(A.OWN);
      }
      var inputs = document.body.querySelectorAll('input');
      for (i = 0; i < inputs.length; i++) {
        var it = (inputs[i].type || 'text').toLowerCase();
        if (it !== 'radio' && it !== 'hidden' && A.visible(inputs[i])) A.claimRowCells(inputs[i]);
      }
      var buttons = document.body.querySelectorAll('button');
      for (i = 0; i < buttons.length; i++) if (A.visible(buttons[i])) A.buttonRowName(buttons[i]);
      // The bottom bar's active page names the page; a page title, when it has one, replaces it (the
      // chart and map pages have none). Fields' remembered labels are kept per page.
      var act = document.querySelector('.bottom-bar-button.active');
      var out = [], page = act ? A.norm(act.innerText) : '', idx = 0, seen = {};
      var push = function (el, item) {
        if (el) { idx++; el.setAttribute(A.ATTR, String(idx)); item.idx = idx; } else item.idx = 0;
        out.push(item);
      };

      for (i = 0; i < all.length; i++) {
        e = all[i];
        if (A.owned(e) || !A.visible(e)) continue;
        var tag = e.tagName;
        var own = A.ownText(e);

        // Pop-ups answer a press and are spoken as they appear: the notie toast ("Please enter a
        // departure airport") and a confirmation dialog, whose buttons are read after it.
        if (A.hasCls(e, 'notie-container')) {
          // A spent toast slides up off the screen (top: -112px) and stays in the DOM at full size.
          if (e.getBoundingClientRect().bottom <= 0) { A.own(e); continue; }
          var inner = e.querySelector('.notie-textbox-inner') || e;
          A.own(inner);
          if (inner !== e) A.own(e);
          var nt = A.text(inner);
          if (nt) push(null, { kind: 'alert', live: 'assertive', text: nt });
          continue;
        }
        var dlg = A.dialogText(e);
        if (dlg !== null) {
          if (dlg) push(null, { kind: 'alert', live: 'assertive', text: dlg });
          continue;
        }

        if (A.hasCls(e, 'page-title')) {
          page = own || A.norm(e.innerText);
          push(null, { kind: 'heading', level: 1, text: page });
          A.own(e);
          continue;
        }

        if (A.hasCls(e, 'option-group')) {
          A.own(e);
          var labelEl = e.querySelector('.option-group-label');
          var label = labelEl ? A.norm(labelEl.innerText) : '';
          var radios = e.querySelectorAll('input[type=radio]');
          if (radios.length) {
            var opts = [], value = '';
            for (var r = 0; r < radios.length; r++) {
              var lab = radios[r].id ? e.querySelector('label[for="' + radios[r].id + '"]') : null;
              if (!lab) lab = radios[r].nextElementSibling;
              var word = lab ? A.norm(lab.innerText) : '';
              opts.push(word);
              if (radios[r].checked) value = word;
            }
            push(e, { kind: '', controlType: 'select', text: label, value: value, options: opts, clickable: true });
            continue;
          }
          var field = e.querySelector('input[type=number],input[type=text],input:not([type])');
          if (field) {
            var hint = A.norm((e.innerText || '').replace(label, ''));
            push(field, { kind: '', controlType: 'text', text: label + (hint ? ', ' + hint : ''), value: field.value, disabled: field.disabled });
            continue;
          }
          var range = e.querySelector('input[type=range]');
          if (range) {
            push(range, {
              kind: '', controlType: 'range', text: label, value: range.value,
              min: parseFloat(range.min || '0'), max: parseFloat(range.max || '100'), step: parseFloat(range.step || '1')
            });
            continue;
          }
          push(null, { kind: 'static', text: A.norm(e.innerText) });
          continue;
        }

        if (tag === 'INPUT') {
          var type = (e.type || 'text').toLowerCase();
          if (type === 'radio' || type === 'hidden') continue;
          if (type === 'checkbox') {
            push(e, { kind: '', controlType: 'checkbox', text: A.fieldLabel(e, page + '|checkbox|' + (seen.cb = (seen.cb || 0) + 1)).text, value: e.checked ? 'true' : 'false' });
            continue;
          }
          var fk = page + '|' + type;
          seen[fk] = (seen[fk] || 0) + 1;
          var fl = A.fieldLabel(e, fk + '|' + seen[fk]);
          var rl = fl.text;
          if (type === 'button' || type === 'submit') {
            var name = rl ? rl + ': ' + A.norm(e.value) : A.norm(e.value);
            // A list's choices (the runways under RWY) sit together in one container, and the button
            // showing the picked one holds that list: a press picks or opens, so they are plain
            // buttons. A lone input button steps to its next value.
            var choice = e.parentElement.querySelectorAll('input[type=button],input[type=submit]').length > 1
              || A.norm(e.value) === 'SELECT';
            push(e, choice
              ? { kind: 'button', text: name, clickable: true, disabled: e.disabled }
              : { kind: 'button', text: name, clickable: true, disabled: e.disabled, key: 'cycler:' + (fl.label || A.norm(e.value)), announceChange: true });
            continue;
          }
          if (type === 'range') {
            push(e, { kind: '', controlType: 'range', text: rl, value: e.value, min: parseFloat(e.min || '0'), max: parseFloat(e.max || '100'), step: parseFloat(e.step || '1') });
            continue;
          }
          push(e, { kind: '', controlType: 'text', text: rl, value: e.value, disabled: e.disabled });
          continue;
        }

        if (tag === 'SELECT') {
          var so = [], sv = '';
          for (var s = 0; s < e.options.length; s++) { so.push(A.norm(e.options[s].text)); if (e.options[s].selected) sv = A.norm(e.options[s].text); }
          push(e, { kind: '', controlType: 'select', text: A.rowLabel(e), value: sv, options: so });
          A.own(e);
          continue;
        }

        if (tag === 'BUTTON' || A.hasCls(e, 'is-button')) {
          var bt = A.buttonText(e);
          A.own(e);
          if (!bt) continue;
          var above = A.headingAbove(e);
          if (above) {
            // A unit toggle: each press steps it, so its label carries the value and speaks again.
            push(e, { kind: 'button', text: above + ': ' + bt, clickable: true, disabled: e.disabled === true,
              key: 'cycler:' + above, announceChange: true });
            continue;
          }
          var rowName = A.buttonRowName(e);
          if (rowName) {
            // It opens the OIS's options pop-up, so it is a plain button named by its row.
            push(e, { kind: 'button', text: rowName + ': ' + bt, clickable: true, disabled: e.disabled === true, key: 'button:' + rowName });
            continue;
          }
          var tg = A.sidebarToggle(e, bt);
          if (tg) {
            push(e, { kind: 'button', text: tg.name + ': ' + tg.state, clickable: true, disabled: e.disabled === true, key: 'button:' + tg.name, announceChange: true });
            continue;
          }
          var active = A.hasCls(e, 'active'), bs = A.buttonState(e, bt);
          if (bs) {
            push(e, { kind: 'button', text: bt + ': ' + bs, clickable: true, disabled: e.disabled === true, key: 'button:' + bt, announceChange: true });
            continue;
          }
          push(e, { kind: 'button', text: bt + (active ? ' (selected)' : ''), clickable: true, disabled: e.disabled === true, key: 'button:' + bt });
          continue;
        }

        if (/^H[1-6]$/.test(tag)) {
          var nb = e.nextElementSibling;
          if (nb && (nb.tagName === 'BUTTON' || A.hasCls(nb, 'is-button'))) continue;   // read with its button
          var ht = A.norm(e.innerText);
          A.own(e);
          if (ht) push(null, { kind: 'heading', level: parseInt(tag.charAt(1), 10), text: ht });
          continue;
        }

        // TERML CHART's list: a pressable card per chart (its title, then its type), and its pin.
        if (A.hasCls(e, 'chart-button')) {
          A.own(e);
          var ch = e.querySelector('h1,h2,h3'), cp = e.querySelector('p');
          var ct = ch ? A.text(ch) : A.text(e), ctype = cp ? A.text(cp) : '';
          push(e, { kind: 'button', text: ctype && ctype !== ct ? ct + ', ' + ctype : ct, clickable: true, key: 'chart:' + ct });
          var pin = e.nextElementSibling;
          if (pin && A.hasCls(pin, 'chart-pin-button')) {
            A.own(pin);
            push(pin, { kind: 'button', text: 'Pin ' + ct, clickable: true, key: 'pin:' + ct });
          }
          continue;
        }

        if (A.hasCls(e, 'door-clickspot')) {
          A.own(e);
          var dn = A.doorName(e.id), ds = A.doorState(e);
          // The OIS listens on the indicator (live: a press on the clickspot itself does nothing).
          push(e.querySelector('.door-indicator') || e, { kind: 'button', text: ds ? dn + ': ' + ds : dn, clickable: true, key: 'door:' + e.id, announceChange: true });
          continue;
        }

        var pic = A.picture(e);
        if (pic) {
          A.own(e);
          push(null, { kind: 'static', text: pic });
          continue;
        }

        // LOADSHEET's tank figures, drawn over the aircraft picture: named by their tank.
        if (A.hasCls(e, 'fuel-container')) {
          A.own(e);
          var tank = A.hasCls(e, 'left-fuel') ? 'Left tank' : A.hasCls(e, 'right-fuel') ? 'Right tank' : 'Center tank';
          push(null, { kind: 'static', text: tank + ': ' + A.text(e), key: 'readout:' + tank });
          continue;
        }

        if (!own || A.decor(own)) continue;

        // The OIS's error line ("ERR: NO WIND INPUT", red, after COMPUTE): a validation error the
        // window speaks as it appears.
        if (/^ERR\b/.test(own)) {
          A.own(e);
          push(null, { kind: 'alert', live: 'assertive', text: A.norm(e.innerText) || own });
          continue;
        }

        // A row of plain texts reads as one line ("DEPARTURE" "to" "ARRIVAL"); a card, a title cell over
        // its values, as "title: values" (LOADSHEET: "FWD ECO: 0 PAX, FWD ECO 0 LBS").
        var pr = e.parentElement;
        var plain = pr && A.hasCls(pr, 'row') && !A.holdsControl(pr) && !pr.querySelector('.row');
        var card = plain && pr.firstElementChild === e && A.hasCls(e, 'col-sm-12') && pr.children.length >= 2;
        if (card || (plain && pr.children.length > 2)) {
          A.own(pr);
          var line = [];
          for (var lc = pr.firstElementChild; lc; lc = lc.nextElementSibling) { var lt = A.text(lc); if (lt) line.push(lt); }
          push(null, { kind: 'static', text: card ? line[0] + ': ' + line.slice(1).join(', ') : line.join(' '),
            key: card ? 'readout:' + line[0] : undefined });
          continue;
        }

        // A read-out: a right-aligned label cell, its value in the next cell ("V1" + "142 kt").
        if (A.hasCls(e, 'text-end') && e.parentElement.children.length === 2 && e.nextElementSibling
          && !e.nextElementSibling.querySelector('input,button')) {
          var val = A.norm(e.nextElementSibling.innerText);
          A.own(e); A.own(e.nextElementSibling);
          push(null, { kind: 'static', text: own + ': ' + (val || '-'), key: 'readout:' + own });
          continue;
        }

        // Inline spans read as their parent's whole text, once.
        if (tag === 'SPAN' && e.parentElement && !A.owned(e.parentElement)) {
          var pt = A.norm(e.parentElement.innerText);
          A.own(e.parentElement);
          if (pt && !A.decor(pt)) push(null, { kind: 'static', text: pt });
          continue;
        }

        // A section title is a heading to jump by: a full-width row title ("CONDITIONS", "RESULTS") or
        // a menu column's ("MISSION", "PERFORMANCE").
        if ((A.hasCls(e, 'col-sm-12') || A.hasCls(e, 'menu-column-title')) && !e.firstElementChild) {
          A.own(e);
          push(null, { kind: 'heading', level: 2, text: own });
          continue;
        }

        // A text with inline parts ("28.35 <span>% GWCG</span>") reads whole, once.
        if (e.firstElementChild) {
          A.own(e);
          push(null, { kind: 'static', text: A.text(e) });
          continue;
        }
        push(null, { kind: 'static', text: own });
      }
      return JSON.stringify({ ok: true, page: page, elements: out });
    } catch (err) {
      return JSON.stringify({ ok: false, error: String(err && err.message || err) });
    }
  };

  A.find = function (idx) { return document.body.querySelector('[' + A.ATTR + '="' + idx + '"]'); };

  A.clickElement = function (idx) {
    try {
      var e = A.find(idx);
      if (!e) return 'missing';
      A.press(e);
      return 'ok';
    } catch (err) { return 'error: ' + err.message; }
  };

  A.setValue = function (idx, text) {
    try {
      var e = A.find(idx);
      if (!e) return 'missing';
      if (A.hasCls(e, 'option-group')) {
        var labels = e.querySelectorAll('label');
        var want = A.norm(text).toUpperCase();
        for (var i = 0; i < labels.length; i++)
          if (A.norm(labels[i].innerText).toUpperCase() === want) { A.press(labels[i]); return 'ok'; }
        return 'no option ' + text;
      }
      if (e.tagName === 'SELECT') {
        for (var s = 0; s < e.options.length; s++)
          if (A.norm(e.options[s].text) === A.norm(text)) {
            e.selectedIndex = s;
            e.dispatchEvent(new Event('change', { bubbles: true }));
            return 'ok';
          }
        return 'no option ' + text;
      }
      if (e.type === 'range') {
        var set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        set.call(e, text);
        e.dispatchEvent(new Event('input', { bubbles: true }));
        e.dispatchEvent(new Event('change', { bubbles: true }));
        return 'ok';
      }
      if (e.type === 'checkbox') {
        if ((e.checked ? 'true' : 'false') !== String(text)) A.press(e);
        return 'ok';
      }
      A.type(e, String(text));
      return 'ok';
    } catch (err) { return 'error: ' + err.message; }
  };

  window.__MSFSBA_A350_OIS = A;
  return A.INSTALLED;
})();
