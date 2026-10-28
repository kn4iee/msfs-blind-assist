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
  // A CSS colour as [r, g, b], or null.
  A.rgb = function (c) {
    c = String(c || '').toLowerCase().replace(/\s/g, '');
    var named = { blue: [0, 0, 255], navy: [0, 0, 128], darkblue: [0, 0, 139], green: [0, 128, 0], lime: [0, 255, 0],
      darkgreen: [0, 100, 0], red: [255, 0, 0], darkred: [139, 0, 0], maroon: [128, 0, 0], orange: [255, 165, 0],
      darkorange: [255, 140, 0], yellow: [255, 255, 0], gold: [255, 215, 0], black: [0, 0, 0], white: [255, 255, 255] };
    var m = /^rgba?\((\d+),(\d+),(\d+)/.exec(c), rgb = m ? [+m[1], +m[2], +m[3]] : named[c];
    if (!rgb && /^#[0-9a-f]{6}$/.test(c)) rgb = [parseInt(c.substr(1, 2), 16), parseInt(c.substr(3, 2), 16), parseInt(c.substr(5, 2), 16)];
    return rgb || null;
  };

  // A colour as the word a pilot would say: green, amber, red, blue, white, black.
  A.colourWord = function (c) {
    var rgb = A.rgb(c);
    if (!rgb) return '';
    var r = rgb[0], g = rgb[1], b = rgb[2];
    if (r > 200 && g > 200 && b > 200) return 'white';
    if (r < 40 && g < 40 && b < 40) return 'black';
    if (b > r && b > g) return 'blue';
    if (g > r && g > b) return 'green';
    if (r > b && g > 80) return 'amber';
    return 'red';
  };

  A.colourState = function (c) {
    var rgb = A.rgb(c);
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

  // A list a button shows beside it: a div of two or more buttons and nothing else.
  A.isButtonList = function (d) {
    if (!d || d.tagName !== 'DIV' || d.children.length < 2) return false;
    for (var k = d.firstElementChild; k; k = k.nextElementSibling) if (k.tagName !== 'BUTTON') return false;
    return true;
  };

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
    // MAINTENANCE's engine cowls: red while open.
    if (text === 'LEFT ENG' || text === 'RIGHT ENG')
      return { name: text + ' cowl', state: A.colourWord(b.style.backgroundColor) === 'red' ? 'Open' : 'Closed' };
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
    // GROUND EQUIP: a piece of equipment at the aircraft is painted darkorange, the rest unpainted.
    else if (A.headingBefore(b) === 'GROUND EQUIP')
      return A.colourWord(b.style.backgroundColor) === 'amber' ? 'Connected' : 'Not connected';
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

  // MAINTENANCE's rows of two cells: a gauge (a bordered value beside its label, either way round)
  // reads "label: value"; a fluid level (its quantity beside a bar as wide as it is full) reads
  // "Y: 30.5L, 74%". -> the line, owning the row; '' when h is in neither.
  A.gaugeLine = function (h) {
    var cell = h.parentElement, row = cell && cell.parentElement;
    // A title over its value in one box (THROTTLE CALIBRATION: AXIS 1 over 0%).
    if (cell && cell.children.length === 2 && cell.firstElementChild === h && /^H[1-6]$/.test(cell.lastElementChild.tagName)
      && /\d/.test(A.text(cell.lastElementChild))) {
      A.own(cell);
      return A.text(h) + ': ' + A.text(cell.lastElementChild);
    }
    if (!row || !A.hasCls(row, 'row') || row.children.length !== 2 || A.holdsControl(row)) return '';
    var other = row.firstElementChild === cell ? row.lastElementChild : row.firstElementChild;
    var hs = row.querySelectorAll('h1,h2,h3,h4');
    if (hs.length === 2) {
      var val = A.hasCls(hs[0], 'border') ? hs[0] : A.hasCls(hs[1], 'border') ? hs[1] : null;
      if (!val) return '';
      A.own(row);
      return A.text(val === hs[0] ? hs[1] : hs[0]) + ': ' + A.text(val);
    }
    var bar = hs.length === 1 && !A.text(other) ? other.querySelector('.h-100') : null;
    if (!bar) return '';
    var w = /([\d.]+)%/.exec(bar.style.width || '');
    var pct = w ? Math.round(parseFloat(w[1])) : (A.hasCls(bar, 'w-100') ? 100 : null);
    A.own(row);
    var q = A.text(h);
    return pct === null ? q : q + (/\d/.test(q) ? ', ' : ': ') + pct + '%';
  };

  // MAINTENANCE's tyre (_w_) and brake (_b_) boxes on the gear picture: their colours, summed up.
  A.WHEEL = /^(nose|main)_(?:([lr])([frm])_)?([wb])_(left|right)$/;
  A.wheelName = function (m) {
    if (m[1] === 'nose') return 'nose ' + m[5];
    return (m[2] === 'l' ? 'left' : 'right') + ' main ' + { f: 'front', r: 'rear', m: 'middle' }[m[3]] + ' ' + m[5];
  };
  A.wheelLines = function () {
    var all = document.querySelectorAll('[id]'), groups = { w: [], b: [] };
    for (var i = 0; i < all.length; i++) {
      var m = A.WHEEL.exec(all[i].id);
      if (!m || !A.visible(all[i])) continue;
      A.own(all[i]);
      groups[m[4]].push({ name: A.wheelName(m), colour: A.colourWord(all[i].style.backgroundColor) || 'unknown' });
    }
    var lines = [];
    var sum = function (title, list) {
      if (!list.length) return;
      var count = {}, most = '';
      for (var j = 0; j < list.length; j++) {
        count[list[j].colour] = (count[list[j].colour] || 0) + 1;
        if (!most || count[list[j].colour] > count[most]) most = list[j].colour;
      }
      var odd = [];
      for (var k = 0; k < list.length; k++) if (list[k].colour !== most) odd.push(list[k].name + ': ' + list[k].colour);
      lines.push(title + ': ' + (odd.length ? most + ', except ' + odd.join(', ') : 'all ' + most));
    };
    sum('Tyres', groups.w);
    sum('Brakes', groups.b);
    return lines;
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
    if (cell !== input && !/(^|\s)col(-|\s|$)/.test(A.cls(cell))) return [];
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
        // A box holding the title and the message (the Navigraph login's user code over its h2) is read
        // child by child: its whole text would repeat the title and run it into the message.
        if (A.holdsControl(k) || (h && k.contains(h))) { walk(k); continue; }
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

  // The Situation Manager's cards: a .menu-button box (not a button) with a title and a description,
  // its button below. -> the card's title for an element inside the card's column, or ''.
  A.cardTitle = function (e) {
    // The card is a child of one of the button's near ancestors (its column), never a cousin further off.
    for (var p = e.parentElement, n = 0; p && n < 4; p = p.parentElement, n++)
      for (var c = p.firstElementChild; c; c = c.nextElementSibling)
        if (c.tagName === 'DIV' && A.hasCls(c, 'menu-button') && c.firstElementChild && c.firstElementChild.tagName === 'H1')
          return A.text(c.firstElementChild);
    return '';
  };

  // A free-standing radio set (THROTTLE CALIBRATION: an .option-group-label cell, then a cell of
  // YES / NO radios): its container and its label element.
  A.radioSet = function (radio) {
    if (radio.closest && radio.closest('.option-group')) return null;
    var box = radio.parentElement, prev = box && box.previousElementSibling;
    var lab = prev && (A.hasCls(prev, 'option-group-label') ? prev : prev.querySelector && prev.querySelector('.option-group-label, h1, h2, h3, h4'));
    return box ? { box: box, labelEl: lab ? prev : null, label: lab ? A.text(lab) : '' } : null;
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
      for (i = 0; i < inputs.length; i++) {
        if ((inputs[i].type || '').toLowerCase() !== 'radio' || !A.visible(inputs[i])) continue;
        var rs0 = A.radioSet(inputs[i]);
        if (rs0 && rs0.labelEl) A.own(rs0.labelEl);
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
          var labelEl = e.querySelector('.option-group-label, .option-group-label-vol');
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
            // A box beside the choice (SIMULATION's PAUSE AT T.O.D / DIST (nm): YES or NO, then the distance): its
            // own field, named by the label's part after the slash.
            var beside = e.querySelector('input[type=number],input[type=text],input:not([type])');
            if (beside) {
              var slash = label.indexOf(' / ');
              var name = slash > 0 ? label.substring(0, slash) + ' ' + label.substring(slash + 3) : label + ', value';
              push(beside, { kind: '', controlType: 'text', text: name, value: beside.value, disabled: beside.disabled });
            }
            continue;
          }
          var cbs = e.querySelectorAll('input[type=checkbox]');
          if (cbs.length) {
            // A group of switches (CALLOUTS' RA callouts): each its own checkbox.
            for (var cb = 0; cb < cbs.length; cb++) {
              var cl = cbs[cb].id ? e.querySelector('label[for="' + cbs[cb].id + '"]') : null;
              push(cbs[cb], { kind: '', controlType: 'checkbox', text: label + (cl ? ': ' + A.text(cl) : ''), value: cbs[cb].checked ? 'true' : 'false' });
            }
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
          if (A.holdsControl(e)) {
            // A group holding buttons (RESET VOLUME): its title, then the walk reads what it holds.
            e.removeAttribute(A.OWN);
            if (labelEl) A.own(labelEl);
            if (label) push(null, { kind: 'heading', level: 3, text: label });
            continue;
          }
          push(null, { kind: 'static', text: A.norm(e.innerText) });
          continue;
        }

        // A free-standing option label (THROTTLE CALIBRATION's RESET CALIBRATION) titles what follows.
        if (A.hasCls(e, 'option-group-label') || A.hasCls(e, 'option-group-label-vol')) {
          A.own(e);
          if (own) push(null, { kind: 'heading', level: 3, text: own });
          continue;
        }

        if (tag === 'INPUT') {
          var type = (e.type || 'text').toLowerCase();
          if (type === 'hidden') continue;
          if (type === 'radio') {
            var rs = A.radioSet(e);
            if (!rs || A.owned(rs.box)) continue;
            A.own(rs.box);
            var ropts = [], rval = '', rads = rs.box.querySelectorAll('input[type=radio]');
            for (var ri = 0; ri < rads.length; ri++) {
              var rl2 = rads[ri].id ? document.querySelector('label[for="' + rads[ri].id + '"]') : null;
              var rw = rl2 ? A.text(rl2) : '';
              ropts.push(rw);
              if (rads[ri].checked) rval = rw;
            }
            push(rs.box, { kind: '', controlType: 'select', text: rs.label, value: rval, options: ropts, clickable: true });
            continue;
          }
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
          if (A.hasCls(e, 'failure-button')) {
            // FAILURES: a press toggles the failure; an active one is drawn bg-danger.
            push(e, { kind: 'button', text: bt + ': ' + (A.hasCls(e, 'bg-danger') ? 'Active' : 'Inactive'), clickable: true,
              key: 'failure:' + bt, announceChange: true });
            continue;
          }
          var ct = A.cardTitle(e);
          if (ct) {
            push(e, { kind: 'button', text: ct + ': ' + bt, clickable: true, disabled: e.disabled === true, key: 'button:' + ct, announceChange: true });
            continue;
          }
          var tg = A.sidebarToggle(e, bt);
          if (tg) {
            push(e, { kind: 'button', text: tg.name + ': ' + tg.state, clickable: true, disabled: e.disabled === true, key: 'button:' + tg.name, announceChange: true });
            continue;
          }
          // A button showing a list beside it (PANEL STATES' presets, SET FUEL's refuel kinds).
          var lst = e.nextElementSibling, expKey = 'exp:' + page + '|' + bt;
          if (A.isButtonList(lst) && (A.hasCls(lst, 'd-none') || A.memo[expKey])) {
            A.memo[expKey] = 1;   // its list is hidden by d-none: an expander, also once open
            push(e, { kind: 'button', text: bt + (A.visible(lst) ? ' (expanded)' : ' (collapsed)'), clickable: true,
              disabled: e.disabled === true, key: 'button:' + bt, announceChange: true });
            continue;
          }
          var active = A.hasCls(e, 'active'), bs = A.buttonState(e, bt);
          if (bs) {
            push(e, { kind: 'button', text: bt + ': ' + bs, clickable: true, disabled: e.disabled === true, key: 'button:' + bt, announceChange: true });
            continue;
          }
          // Painted by the OIS with no known meaning: its colour, as a sighted pilot sees it.
          var paint = A.colourWord(e.style.backgroundColor) || A.colourWord((/^\s*([a-z]+|#[0-9a-f]{6}|rgba?\([^)]*\))/i.exec(e.style.background || '') || [])[1]);
          if (paint && paint !== 'black' && paint !== 'white') {
            push(e, { kind: 'button', text: bt + ' (' + paint + ')', clickable: true, disabled: e.disabled === true, key: 'button:' + bt, announceChange: true });
            continue;
          }
          push(e, { kind: 'button', text: bt + (active ? ' (selected)' : ''), clickable: true, disabled: e.disabled === true, key: 'button:' + bt });
          continue;
        }

        if (/^H[1-6]$/.test(tag)) {
          var gl = A.gaugeLine(e);
          if (gl) { push(null, { kind: 'static', text: gl, key: 'readout:' + gl.split(':')[0] }); continue; }
          var nb = e.nextElementSibling;
          if (nb && (nb.tagName === 'BUTTON' || A.hasCls(nb, 'is-button'))) continue;   // read with its button
          var ht = A.norm(e.innerText);
          A.own(e);
          if (ht) push(null, { kind: 'heading', level: parseInt(tag.charAt(1), 10), text: ht });
          continue;
        }

        // The header's EFB dropdown: its chevron opens the page menu.
        if (e.id === 'efb-dropdown-menu-button') {
          A.own(e);
          var dd = document.getElementById('efb-dropdown-menu-content');
          push(e, { kind: 'button', text: 'EFB menu' + (dd && A.visible(dd) ? ' (expanded)' : ' (collapsed)'), clickable: true,
            key: 'button:EFB menu', announceChange: true });
          continue;
        }

        if (tag === 'DIV' && A.hasCls(e, 'menu-button') && e.querySelector('h1')) {
          A.own(e);
          var sh = e.querySelector('h1'), st = A.text(sh), sd = A.norm(A.text(e).replace(st, ''));
          push(null, { kind: 'static', text: sd ? st + ': ' + sd : st });
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

        // COMPANY COM (AOC): its tree (a box is pressable, a folder header is a heading), its message
        // table (a row per message: preview, time, the "**" new marker), and the open message.
        if (e.closest && e.closest('.aoc-tree') && A.hasCls(e.parentElement, 'aoc-tree') && !A.holdsControl(e)) {
          A.own(e);
          var at = A.text(e);
          if (!at) continue;
          if (A.hasCls(e, 'selectable'))
            push(e, { kind: 'button', text: at + (A.hasCls(e, 'selected') ? ' (selected)' : ''), clickable: true, key: 'aoc:' + at });
          else push(null, { kind: 'heading', level: 3, text: at });
          continue;
        }
        if (tag === 'THEAD') { A.own(e); continue; }
        if (tag === 'TR' && A.hasCls(e, 'messagerow')) {
          A.own(e);
          var tds = e.querySelectorAll('td');
          var preview = tds[0] ? A.text(tds[0]).replace(/-{3,}\.*|\.{3,}/g, '').replace(/\s+$/, '') : '';
          var zulu = tds[1] ? A.text(tds[1]) : '', isNew = tds[2] ? A.text(tds[2]) !== '' : false;
          push(e, { kind: 'button', text: preview + (zulu ? ', ' + zulu + 'Z' : '') + (isNew ? ', new' : ''), clickable: true, key: 'msg:' + (e.getAttribute('data-uid') || preview) });
          continue;
        }
        if (tag === 'PRE') {
          A.own(e);
          // A line an item: a line breaks at a BR or at a newline in the text, inside any element the PRE holds (the
          // OFP is a PRE of bold runs and text whose breaks are newlines, no BR: split at BR alone, it was one item).
          var lines = [], cur = '';
          var walkPre = function (n) {
            for (var pc = n.firstChild; pc; pc = pc.nextSibling) {
              if (pc.nodeType === 1 && pc.tagName === 'BR') { lines.push(cur); cur = ''; }
              else if (pc.nodeType === 1) walkPre(pc);
              else if (pc.nodeType === 3) {
                var parts = pc.nodeValue.split('\n');
                cur += parts[0];
                for (var pk = 1; pk < parts.length; pk++) { lines.push(cur); cur = parts[pk]; }
              }
            }
          };
          walkPre(e);
          lines.push(cur);
          for (var li = 0; li < lines.length; li++) {
            var ln = A.norm(lines[li]);
            if (ln && !/^[-.=_*\s]+$/.test(ln)) push(null, { kind: 'static', text: ln });
          }
          continue;
        }

        if (e.id && A.WHEEL.test(e.id)) {
          var wl = A.wheelLines();
          for (var wi = 0; wi < wl.length; wi++) push(null, { kind: 'static', text: wl[wi], key: 'readout:' + wl[wi].split(':')[0] });
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
      if (A.hasCls(e, 'option-group') || e.querySelector('input[type=radio]')) {
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
