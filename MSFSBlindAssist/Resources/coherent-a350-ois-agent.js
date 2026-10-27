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

  A.visible = function (e) {
    var r = e.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
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

  // A field's label: the texts of its row other than its own cell ("TOW x1000 LBS" -> "TOW, x1000 LBS").
  A.rowLabel = function (input) {
    var row = input.closest ? input.closest('.row') : null;
    if (!row) return '';
    var cell = input;
    while (cell.parentElement && cell.parentElement !== row) cell = cell.parentElement;
    var parts = [];
    for (var k = row.firstElementChild; k; k = k.nextElementSibling) {
      if (k === cell) continue;
      var t = A.norm(k.innerText || k.textContent);
      if (t) parts.push(t);
    }
    return parts.join(', ');
  };

  // Claims a field's row-mates (its label and unit cells) before the walk reaches them, so they are
  // read once, inside the field's label, never also as loose text above it.
  A.claimRowCells = function (input) {
    var row = input.closest ? input.closest('.row') : null;
    if (!row || (input.closest && input.closest('.option-group'))) return;
    var cell = input;
    while (cell.parentElement && cell.parentElement !== row) cell = cell.parentElement;
    for (var k = row.firstElementChild; k; k = k.nextElementSibling) if (k !== cell) A.own(k);
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
      var out = [], page = '', idx = 0;
      var push = function (el, item) {
        if (el) { idx++; el.setAttribute(A.ATTR, String(idx)); item.idx = idx; } else item.idx = 0;
        out.push(item);
      };

      for (i = 0; i < all.length; i++) {
        e = all[i];
        if (A.owned(e) || !A.visible(e)) continue;
        var tag = e.tagName;
        var own = A.ownText(e);

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
            push(e, { kind: '', controlType: 'checkbox', text: A.rowLabel(e), value: e.checked ? 'true' : 'false' });
            continue;
          }
          var rl = A.rowLabel(e);
          var kept = e.getAttribute(A.LABEL);
          if (rl && rl.charAt(0) !== '<') e.setAttribute(A.LABEL, rl);
          else if (kept) rl = kept + (rl ? ' ' + rl : '');
          if (type === 'button' || type === 'submit') {
            var name = rl ? rl + ': ' + A.norm(e.value) : A.norm(e.value);
            // A list's choices (the runways under RWY SELECT) sit together in one container: a press
            // picks one, so they are plain buttons. A lone input button steps to its next value.
            var siblings = 0;
            for (var sb = e.parentElement.firstElementChild; sb; sb = sb.nextElementSibling)
              if (sb.tagName === 'INPUT' && (sb.type === 'button' || sb.type === 'submit')) siblings++;
            var choice = siblings > 1 || A.norm(e.value) === 'SELECT';
            push(e, choice
              ? { kind: 'button', text: name, clickable: true, disabled: e.disabled }
              : { kind: 'button', text: name, clickable: true, disabled: e.disabled, key: 'cycler:' + (rl || A.norm(e.value)), announceChange: true });
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
          var bt = A.norm(e.innerText);
          A.own(e);
          if (!bt) continue;
          var above = A.headingAbove(e);
          if (above) {
            // A unit toggle: each press steps it, so its label carries the value and speaks again.
            push(e, { kind: 'button', text: above + ': ' + bt, clickable: true, disabled: e.disabled === true,
              key: 'cycler:' + above, announceChange: true });
            continue;
          }
          var active = A.hasCls(e, 'active');
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

        if (!own || A.decor(own)) continue;

        // A read-out: a right-aligned label cell, its value in the next cell ("V1" + "142 kt").
        if (A.hasCls(e, 'text-end') && e.nextElementSibling && !e.nextElementSibling.querySelector('input,button')) {
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
        if ((A.hasCls(e, 'col-sm-12') || A.hasCls(e, 'menu-column-title')) && !e.querySelector('input,button')) {
          A.own(e);
          push(null, { kind: 'heading', level: 2, text: own });
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
