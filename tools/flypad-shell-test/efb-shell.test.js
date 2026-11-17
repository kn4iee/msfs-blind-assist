'use strict';
// Tests for the SHIPPING EFB shell — the PageHtml verbatim string inside
// MSFSBlindAssist/Forms/FBWA380/FbwEfbForm.cs, the one document every EFB window (the flyPad, both
// PMDG tablets and the MD-11 EFB) actually renders. The string is extracted from the C# source,
// loaded under jsdom and driven through window.__render exactly as the form does, so a change to
// the shipping script is what these tests see. flypad-shell.html (reconcile.test.js) is a
// hand-maintained reconcile spec with no live region and no click announce; it cannot stand in
// for the announce behaviour pinned here.

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const FORM = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Forms', 'FBWA380', 'FbwEfbForm.cs');

// Pull the PageHtml verbatim string out of the C# source: "" is the verbatim escape for one
// quote, and the two {{...}} placeholders are what BuildPageHtml fills at runtime.
function shippingShellHtml() {
  const src = fs.readFileSync(FORM, 'utf8');
  const marker = 'PageHtml = @"';
  const start = src.indexOf(marker);
  assert.ok(start >= 0, 'PageHtml verbatim string not found in FbwEfbForm.cs');
  const open = start + marker.length;
  const close = src.indexOf('</html>";', open);
  assert.ok(close > open, 'PageHtml verbatim string has no </html>"; terminator');
  return src.slice(open, close + '</html>'.length)
    .replace(/""/g, '"')
    .replace(/\{\{TITLE\}\}/g, 'EFB')
    .replace(/\{\{NOUN\}\}/g, 'EFB');
}

function loadShell(t) {
  const html = shippingShellHtml();
  const dom = new JSDOM(html, { runScripts: 'outside-only' });
  const { window } = dom;
  const posted = [];
  window.chrome = { webview: { postMessage(m) { posted.push(m); }, addEventListener() {}, removeEventListener() {} } };
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
  assert.strictEqual(scripts.length, 1, 'expected exactly one inline <script> in PageHtml');
  // runScripts:'outside-only' gives window.eval the jsdom window's OWN realm, so the shell's bare
  // `document`/`window`/`setTimeout` resolve there. The MD-11 reader harness (tools/md11-efb-test/
  // run.js) documents the other case — a jsdom built WITHOUT runScripts hands back Node's own eval,
  // where those bare identifiers resolve against the Node global instead — so the Node globals are
  // pointed at this window too. Harmless when the realm eval is in force, and the difference stops
  // being a silent nothing-defined failure if a jsdom upgrade changes which one applies.
  //
  // Put them back when the test ends: this window is jsdom's, not Node's, and a leaked one
  // outliving its test is exactly the failure the assignment above exists to make visible.
  const prev = { window: global.window, document: global.document, had: 'window' in global };
  global.window = window;
  global.document = window.document;
  if (t && typeof t.after === 'function') {
    t.after(() => {
      if (prev.had) { global.window = prev.window; global.document = prev.document; }
      else { delete global.window; delete global.document; }
    });
  }
  window.eval(scripts[0]);
  assert.strictEqual(typeof window.__render, 'function', 'shell did not define window.__render');
  return {
    window,
    posted,
    render(page, items) { window.__render({ page, items }); },
    buttonByLabel(prefix) {
      return [...window.document.querySelectorAll('#list button')].find((b) => b.textContent.indexOf(prefix) === 0);
    },
    // What the aria-live region holds — the audible channel. announce() clears the region and
    // writes the text 30 ms later, so the read waits past that timer.
    async spoken() { await new Promise((r) => setTimeout(r, 80)); return window.document.getElementById('live').textContent; },
  };
}

// Items in the shape BuildRenderJson posts.
const btn = (idx, text, extra) => Object.assign({ idx, kind: 'button', controlType: '', text, clickable: true, level: 0, live: '', disabled: false }, extra || {});
const tab = (idx, text) => ({ idx, kind: 'tab', controlType: '', text, clickable: true, level: 0, live: '', disabled: false });

// The MD-11 reader stamps the arrow's key ('step-next:Runway'). Keyed by its label instead, the
// button would be a NEW node once '(now 06L)' became '(now 06R)': rebuilt under the pilot's focus,
// its new label never spoken.
test('a control flagged announceChange speaks its new label after the pilot\'s own press', async (t) => {
  const s = loadShell(t);
  s.render('Perf', [btn(5, 'Runway next (now 06L)', { announceChange: true, key: 'step-next:Runway' })]);
  assert.strictEqual(await s.spoken(), 'EFB page: Perf');
  const b = s.buttonByLabel('Runway next');
  assert.ok(b, 'stepper button not rendered');
  b.focus(); b.click();
  assert.strictEqual(await s.spoken(), 'Activating Runway next (now 06L)');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'click', idx: '5' })]);
  // The next scrape shows the field one step on: patched in place (focus kept) and spoken.
  s.render('Perf', [btn(5, 'Runway next (now 06R)', { announceChange: true, key: 'step-next:Runway' })]);
  assert.strictEqual(s.buttonByLabel('Runway next'), b, 'stepper button was rebuilt instead of patched');
  assert.strictEqual(b.textContent, 'Runway next (now 06R)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Runway next (now 06R)');
});

// The MD-11 door/GPU/chocks tiles carry their state after a colon, and the flip a press produces is
// the OUTCOME the pilot asked for — the same class as the stepper, flagged the same way and keyed
// the same way (by the tile's name, 'tile:Passenger 1L').
test('a flagged tile speaks the state its own press produced', async (t) => {
  const s = loadShell(t);
  s.render('Services', [btn(9, 'Passenger 1L: Closed', { announceChange: true, key: 'tile:Passenger 1L' })]);
  assert.strictEqual(await s.spoken(), 'EFB page: Services');
  const door = s.buttonByLabel('Passenger 1L');
  door.focus(); door.click();
  assert.strictEqual(await s.spoken(), 'Activating Passenger 1L: Closed');
  s.render('Services', [btn(9, 'Passenger 1L: Open', { announceChange: true, key: 'tile:Passenger 1L' })]);
  assert.strictEqual(s.buttonByLabel('Passenger 1L'), door, 'tile was rebuilt instead of patched');
  assert.strictEqual(await s.spoken(), 'Passenger 1L: Open');
});

test('a control without the flag stays silent when its label changes after a press', async (t) => {
  const s = loadShell(t);
  // The PMDG tablet's nav bar: the pressed tab gains ' (current page)' on the next scrape.
  s.render('Dashboard', [tab(1, 'Dashboard (current page)'), tab(2, 'Performance'), btn(7, 'Baggage')]);
  assert.strictEqual(await s.spoken(), 'EFB page: Dashboard');
  const perf = s.buttonByLabel('Performance');
  perf.focus(); perf.click();
  assert.strictEqual(await s.spoken(), 'Activating Performance');
  s.render('Dashboard', [tab(1, 'Dashboard'), tab(2, 'Performance (current page)'), btn(7, 'Baggage')]);
  assert.strictEqual(s.buttonByLabel('Performance'), perf, 'tab was rebuilt instead of patched');
  assert.strictEqual(perf.textContent, 'Performance (current page)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Activating Performance', 'the press echo "(current page)" was announced');
  // The flyPad's service tiles: the pressed tile gains ' (called)'.
  const bag = s.buttonByLabel('Baggage');
  bag.focus(); bag.click();
  assert.strictEqual(await s.spoken(), 'Activating Baggage');
  s.render('Dashboard', [tab(1, 'Dashboard'), tab(2, 'Performance (current page)'), btn(7, 'Baggage (called)')]);
  assert.strictEqual(s.buttonByLabel('Baggage'), bag, 'tile was rebuilt instead of patched');
  assert.strictEqual(bag.textContent, 'Baggage (called)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Activating Baggage', 'the press echo "(called)" was announced');
});

// B5 (review round 2 of PR 189): the reconcile key is EXPLICIT, or it is the label — never a text
// heuristic. The flyPad ATC page repeats 'Set Active' / 'Set Standby' on every frequency card, so its
// agent prefixes the card: 'UNICOM 122.800: Set Active'. The after-colon strip this shell briefly
// carried keyed both buttons 'UNICOM 122.800', held apart only by DOM order, so a re-sort patched the
// Set Standby label onto the node the pilot was sitting on.
test('without a key, two labels that differ only after a colon are two keys, and a re-sort keeps each on its own node', (t) => {
  const s = loadShell(t);
  s.render('ATC', [btn(3, 'UNICOM 122.800: Set Active'), btn(4, 'UNICOM 122.800: Set Standby')]);
  const active = s.buttonByLabel('UNICOM 122.800: Set Active');
  const standby = s.buttonByLabel('UNICOM 122.800: Set Standby');
  assert.ok(active && standby && active !== standby, 'both ATC buttons rendered');
  assert.strictEqual(active.getAttribute('data-key'), 'btn|UNICOM 122.800: Set Active#1');
  assert.strictEqual(standby.getAttribute('data-key'), 'btn|UNICOM 122.800: Set Standby#1');
  s.render('ATC', [btn(3, 'UNICOM 122.800: Set Standby'), btn(4, 'UNICOM 122.800: Set Active')]);
  assert.strictEqual(s.buttonByLabel('UNICOM 122.800: Set Active'), active, 'Set Active was re-homed onto another node');
  assert.strictEqual(s.buttonByLabel('UNICOM 122.800: Set Standby'), standby, 'Set Standby was re-homed onto another node');
  assert.strictEqual(active.getAttribute('data-idx'), '4', 'the moved node carries its own current idx');
  assert.strictEqual(standby.getAttribute('data-idx'), '3');
});

// The MD-11 State page swaps 'Ready to Fly: Set as default' for its check mark, which the reader
// reads as 'Ready to Fly is the default' — no suffix rule relates the two labels, the agent's key does.
test('an explicit key keeps a control on its node when its label changes shape', (t) => {
  const s = loadShell(t);
  s.render('State', [btn(12, 'Ready to Fly: Set as default', { key: 'tile-action:Ready to Fly' })]);
  const b = s.buttonByLabel('Ready to Fly');
  assert.strictEqual(b.getAttribute('data-key'), 'btn|k:tile-action:Ready to Fly#1');
  s.render('State', [btn(12, 'Ready to Fly is the default', { key: 'tile-action:Ready to Fly' })]);
  assert.strictEqual(s.buttonByLabel('Ready to Fly'), b, 'the keyed control was rebuilt instead of patched');
  assert.strictEqual(b.textContent, 'Ready to Fly is the default', 'visible label did not update');
});

// The MD-11 State page once more, now for the control that REPLACES the one just pressed. The check
// mark the EFB swaps in is stamped on no DOM node, so a press on it could only reach the reader's
// A.find(idx) -> null and be dropped with nothing said; the reader emits it DISABLED instead. This
// is what the shell then owes the pilot: the node survives (same key), the outcome of their press is
// still spoken because the element is flagged, and a further press is refused out loud and posts
// nothing — rather than the shell's cheerful "Activating Ready to Fly is the default" over a control
// that does nothing.
test('a flagged control that arrives disabled keeps its node, speaks its outcome, then refuses a press', async (t) => {
  const s = loadShell(t);
  s.render('State', [btn(12, 'Ready to Fly: Set as default', { announceChange: true, key: 'tile-action:Ready to Fly' })]);
  assert.strictEqual(await s.spoken(), 'EFB page: State');
  const b = s.buttonByLabel('Ready to Fly');
  b.focus(); b.click();
  assert.strictEqual(await s.spoken(), 'Activating Ready to Fly: Set as default');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'click', idx: '12' })]);

  s.render('State', [btn(12, 'Ready to Fly is the default', { announceChange: true, disabled: true, key: 'tile-action:Ready to Fly' })]);
  assert.strictEqual(s.buttonByLabel('Ready to Fly'), b, 'the keyed control was rebuilt instead of patched');
  assert.strictEqual(b.textContent, 'Ready to Fly is the default, dimmed', 'visible label did not update');
  assert.strictEqual(b.getAttribute('data-disabled'), 'true');
  assert.strictEqual(await s.spoken(), 'Ready to Fly is the default, dimmed', 'the outcome of the press went unspoken');

  b.click();
  assert.strictEqual(await s.spoken(), 'Unavailable');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'click', idx: '12' })], 'the refused press posted a command');
});

// The A350 OIS writes its result into a field's label after Enter ("AIRP" becomes "AIRP, OK"); its reader keys the field by
// name (review 2026-10-29, OIS I4), so the field the pilot just typed into keeps its node and the focus.
const field = (idx, text, value, extra) => Object.assign({ idx, kind: '', controlType: 'text', text, value, clickable: false, level: 0, live: '', disabled: false }, extra || {});
test('a keyed field keeps its node and the focus when its label gains a result', (t) => {
  const s = loadShell(t);
  s.render('T.O PERF', [field(4, 'AIRP', 'VCBI', { key: 'field:AIRP' })]);
  const inp = s.window.document.querySelector('#list input');
  inp.focus();
  s.render('T.O PERF', [field(4, 'AIRP, OK', 'VCBI', { key: 'field:AIRP' })]);
  assert.strictEqual(s.window.document.querySelector('#list input'), inp, 'the field was rebuilt');
  assert.strictEqual(s.window.document.activeElement, inp, 'the focus left the field');
  assert.strictEqual(inp.getAttribute('aria-label'), 'AIRP, OK', 'the result is not in the name');
});

// The A350 OIS's IDs under STREAMER MODE (review 2026-10-29, OIS I6): a field the reader flags secret is a password field,
// so neither the screen reader nor the screen shows what it holds, and it is a plain field again once no longer secret.
test('a secret field is a password field, and plain again once no longer secret', (t) => {
  const s = loadShell(t);
  s.render('Options', [field(8, 'HOPPIE LOGON, hidden, 4 characters', 'ABCD', { key: 'field:HOPPIE LOGON', secret: true })]);
  const inp = s.window.document.querySelector('#list input');
  assert.strictEqual(inp.type, 'password');
  assert.strictEqual(inp.value, 'ABCD', 'Enter must commit the value it holds, never an empty one');
  s.render('Options', [field(8, 'HOPPIE LOGON', 'ABCD', { key: 'field:HOPPIE LOGON' })]);
  assert.strictEqual(s.window.document.querySelector('#list input'), inp, 'the field was rebuilt');
  assert.strictEqual(inp.type, 'text');
  s.render('Options', [field(8, 'HOPPIE LOGON, hidden, 4 characters', 'ABCD', { key: 'field:HOPPIE LOGON', secret: true })]);
  assert.strictEqual(inp.type, 'password');
});

test('a slider or a choice kept on its node is named by its new label', (t) => {
  const s = loadShell(t);
  const slider = { idx: 2, kind: '', controlType: 'range', text: 'MASTER VOLUME', value: '50', min: 0, max: 100, step: 1, clickable: false, level: 0, live: '', disabled: false, key: 'field:MASTER VOLUME' };
  s.render('Options', [slider, choice(7, 'NAVDATA', 'MSFS', ['NAVIGRAPH', 'MSFS'], { commit: 'enter', key: 'choice:NAVDATA' })]);
  const rg = s.window.document.querySelector('#list input[type=range]');
  const sel = selectOf(s, 'NAVDATA');
  s.render('Options', [Object.assign({}, slider, { text: 'MASTER VOLUME, muted' }),
    choice(7, 'NAVDATA, restart needed', 'MSFS', ['NAVIGRAPH', 'MSFS'], { commit: 'enter', key: 'choice:NAVDATA' })]);
  assert.strictEqual(s.window.document.querySelector('#list input[type=range]'), rg, 'the slider was rebuilt');
  assert.strictEqual(rg.getAttribute('aria-label'), 'MASTER VOLUME, muted');
  assert.strictEqual(selectOf(s, 'NAVDATA'), sel, 'the choice was rebuilt');
  assert.strictEqual(sel.getAttribute('aria-label'), 'NAVDATA, restart needed, Enter applies');
});

test('the explicit key wins over the label: the same text under another key is another node', (t) => {
  const s = loadShell(t);
  s.render('Perf', [btn(5, 'Runway next', { key: 'step-next:Runway' })]);
  const first = s.buttonByLabel('Runway next');
  s.render('Perf', [btn(5, 'Runway next', { key: 'step-next:Arrival Runway' })]);
  assert.notStrictEqual(s.buttonByLabel('Runway next'), first, 'a different key was patched onto the old node');
});

// With the heuristics gone, an element WITHOUT a key is keyed by its whole label again (the flyPad's
// and the PMDG tablet's keys are exactly what they were before PR 189).
test('without a key, "(now …)" and an after-colon tail are part of the key again', (t) => {
  const s = loadShell(t);
  s.render('Perf', [btn(5, 'Runway next (now 06L)'), btn(6, 'GPU: Connect')]);
  const step = s.buttonByLabel('Runway next');
  const gpu = s.buttonByLabel('GPU');
  assert.strictEqual(step.getAttribute('data-key'), 'btn|Runway next (now 06L)#1');
  assert.strictEqual(gpu.getAttribute('data-key'), 'btn|GPU: Connect#1');
  s.render('Perf', [btn(5, 'Runway next (now 06R)'), btn(6, 'GPU: Disconnect')]);
  assert.notStrictEqual(s.buttonByLabel('Runway next'), step, 'an unkeyed "(now …)" change was patched in place');
  assert.notStrictEqual(s.buttonByLabel('GPU'), gpu, 'an unkeyed after-colon change was patched in place');
});

// A choice as BuildRenderJson posts it (the A350 OIS turns radio groups into these).
const choice = (idx, text, value, options, extra) => Object.assign({ idx, kind: '', controlType: 'select', text, value, options, clickable: true, level: 0, live: '', disabled: false }, extra || {});
const selectOf = (s, label) => [...s.window.document.querySelectorAll('#list select')].find((e) => e.getAttribute('aria-label').indexOf(label) === 0);
const key = (s, el, k) => el.dispatchEvent(new s.window.KeyboardEvent('keydown', { key: k, bubbles: true }));
function arrowTo(s, el, value) { key(s, el, 'ArrowDown'); el.value = value; el.dispatchEvent(new s.window.Event('change', { bubbles: true })); }

// Review 2026-10-29 (OIS I1): in WebView2 the arrows on a closed select change its value and fire 'change' at each step,
// and the shell committed every change, so a pilot arrowing just to hear the options pressed each one on the OIS: the
// NAVDATA source, the CPDLC source, the throttle calibration, the failures switch. A choice the agent flags
// commit: 'enter' is applied on Enter (or a mouse pick) only; left without Enter it shows its applied value again.
test('a choice flagged commit enter is applied on Enter, never by the arrows', (t) => {
  const s = loadShell(t);
  s.render('Options', [choice(7, 'NAVDATA', 'NAVIGRAPH', ['NAVIGRAPH', 'MSFS', 'DEFAULT'], { commit: 'enter' })]);
  const sel = selectOf(s, 'NAVDATA');
  sel.focus();
  arrowTo(s, sel, 'MSFS');
  arrowTo(s, sel, 'DEFAULT');
  assert.deepStrictEqual(s.posted, [], 'an arrow step was applied');
  key(s, sel, 'Enter');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'set', idx: '7', value: 'DEFAULT', controlType: 'select' })]);
});

test('a choice flagged commit enter left without Enter shows its applied value again', (t) => {
  const s = loadShell(t);
  s.render('Options', [choice(7, 'NAVDATA', 'NAVIGRAPH', ['NAVIGRAPH', 'MSFS', 'DEFAULT'], { commit: 'enter' })]);
  const sel = selectOf(s, 'NAVDATA');
  sel.focus();
  arrowTo(s, sel, 'MSFS');
  sel.blur();
  assert.strictEqual(sel.value, 'NAVIGRAPH');
  assert.deepStrictEqual(s.posted, []);
});

test('a choice flagged commit enter takes a mouse pick at once and says how it applies', (t) => {
  const s = loadShell(t);
  s.render('Options', [choice(7, 'NAVDATA', 'NAVIGRAPH', ['NAVIGRAPH', 'MSFS'], { commit: 'enter' })]);
  const sel = selectOf(s, 'NAVDATA');
  assert.strictEqual(sel.getAttribute('aria-label'), 'NAVDATA, Enter applies');
  sel.value = 'MSFS';
  sel.dispatchEvent(new s.window.Event('change', { bubbles: true }));   // no key before it: a click
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'set', idx: '7', value: 'MSFS', controlType: 'select' })]);
});

// Every other EFB's choices are applied as they change, as before.
test('a choice without the flag is applied as it changes', (t) => {
  const s = loadShell(t);
  s.render('Settings', [choice(3, 'Weight unit', 'KG', ['KG', 'LBS'])]);
  const sel = selectOf(s, 'Weight unit');
  sel.focus();
  arrowTo(s, sel, 'LBS');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'set', idx: '3', value: 'LBS', controlType: 'select' })]);
});

// Review 2026-10-29 (OIS I1): a group with nothing picked (CABIN LIGHTING) arrives with the value '', and the select showed
// its first option as if it were picked; picking that option then changed nothing. It shows "(none)", which cannot be
// picked, and stays so as the page refreshes.
test('a choice whose value is none of its options shows (none), not its first option', (t) => {
  const s = loadShell(t);
  s.render('Options', [choice(9, 'CABIN LIGHTING', '', ['FULL', 'DIM', 'NIGHT'])]);
  const sel = selectOf(s, 'CABIN LIGHTING');
  assert.strictEqual(sel.options[sel.selectedIndex].textContent, '(none)');
  assert.ok(sel.options[0].disabled, 'the placeholder can be picked');
  s.render('Options', [choice(9, 'CABIN LIGHTING', '', ['FULL', 'DIM', 'NIGHT']), btn(10, 'BACK')]);
  assert.strictEqual(selectOf(s, 'CABIN LIGHTING').options[selectOf(s, 'CABIN LIGHTING').selectedIndex].textContent, '(none)');
  s.render('Options', [choice(9, 'CABIN LIGHTING', 'DIM', ['FULL', 'DIM', 'NIGHT'])]);
  const now = selectOf(s, 'CABIN LIGHTING');
  assert.strictEqual(now.value, 'DIM');
  assert.ok(![...now.options].some((o) => o.textContent === '(none)'), 'the placeholder stayed once a value was picked');
});

// Plan 06 Task 2, the owner's answer to question 17 (2026-10-31): on the A350's OIS and FAP focus follows what the press did.
// The form asks for it per payload (focus: true); every other EFB keeps the behaviour pinned above.
const follow = (s, page, items) => s.window.__render({ page, items, focus: true });

test('with focus following, a press that opens another page puts focus on its title, said once', async (t) => {
  const s = loadShell(t);
  follow(s, 'FLT OPS MENU', [btn(1, 'LOADSHEET'), btn(2, 'T.O PERF')]);
  await s.spoken();
  const perf = s.buttonByLabel('T.O PERF');
  perf.focus(); perf.click();
  assert.strictEqual(await s.spoken(), 'Activating T.O PERF');
  follow(s, 'T.O PERF', [btn(3, 'SYNC FMGS'), btn(4, 'COMPUTE')]);
  const title = s.window.document.getElementById('pgttl');
  assert.strictEqual(s.window.document.activeElement, title, 'focus did not move to the new page');
  assert.strictEqual(title.textContent, 'EFB: T.O PERF');
  assert.strictEqual(await s.spoken(), 'Activating T.O PERF', 'the title was said a second time through the live region');
});

test('with focus following, a page that changes by itself leaves focus alone and is announced', async (t) => {
  const s = loadShell(t);
  follow(s, 'GROUND', [btn(1, 'CLOSE ALL')]);
  await s.spoken();
  follow(s, 'LOADSHEET', [btn(2, 'SET FUEL')]);   // nobody pressed anything, focus was not in the list
  assert.notStrictEqual(s.window.document.activeElement, s.window.document.getElementById('pgttl'));
  assert.strictEqual(await s.spoken(), 'EFB page: LOADSHEET');
});

test('with focus following, a pressed control rebuilt under a new label keeps focus at its place', async (t) => {
  const s = loadShell(t);
  follow(s, 'T.O PERF', [btn(1, 'SYNC FMGS'), btn(2, 'COMPUTE'), btn(3, 'SEND TO FMGS')]);
  const compute = s.buttonByLabel('COMPUTE');
  compute.focus(); compute.click();
  // The OIS writes its result into the button's label, which no suffix rule strips: a new node.
  follow(s, 'T.O PERF', [btn(1, 'SYNC FMGS'), btn(2, 'COMPUTE DONE'), btn(3, 'SEND TO FMGS')]);
  assert.strictEqual(s.window.document.activeElement, s.buttonByLabel('COMPUTE DONE'), 'focus left the pressed control');
});

test('without focus following, a page change only announces and a rebuilt control lets focus go', async (t) => {
  const s = loadShell(t);
  s.render('Dashboard', [btn(1, 'Ground'), btn(2, 'Arm')]);
  await s.spoken();
  const arm = s.buttonByLabel('Arm');
  arm.focus(); arm.click();
  s.render('Dashboard', [btn(1, 'Ground'), btn(2, 'Armed now')]);
  assert.notStrictEqual(s.window.document.activeElement, s.buttonByLabel('Armed now'));
  const ground = s.buttonByLabel('Ground');
  ground.focus(); ground.click();
  s.render('Ground', [btn(3, 'Doors')]);
  assert.notStrictEqual(s.window.document.activeElement, s.window.document.getElementById('pgttl'));
  assert.strictEqual(await s.spoken(), 'EFB page: Ground');
});

const alertItem = (idx, text) => ({ idx, kind: 'alert', controlType: '', text, clickable: false, level: 0, live: 'assertive', disabled: false });

test('with focus following, a dialog takes focus on its first button and gives it back to its opener when it closes', (t) => {
  const s = loadShell(t);
  follow(s, 'FLT OPS MENU', [btn(1, 'FLT OPS PLAN'), btn(2, 'TURNAROUND')]);
  const turn = s.buttonByLabel('TURNAROUND');
  turn.focus(); turn.click();
  follow(s, 'FLT OPS MENU', [alertItem(3, 'TURNAROUND START: Are you sure?'), btn(4, 'CANCEL'), btn(5, 'START'),
    btn(1, 'FLT OPS PLAN'), btn(2, 'TURNAROUND')]);
  const cancel = s.buttonByLabel('CANCEL');
  assert.strictEqual(s.window.document.activeElement, cancel, 'focus did not go to the dialog');
  cancel.click();
  follow(s, 'FLT OPS MENU', [btn(1, 'FLT OPS PLAN'), btn(2, 'TURNAROUND')]);
  assert.strictEqual(s.window.document.activeElement, s.buttonByLabel('TURNAROUND'), 'focus did not come back to the opener');
});

test('with focus following, a list that opens under a button takes focus on its first item, and back on the button when it closes', (t) => {
  const s = loadShell(t);
  follow(s, 'OPTIONS', [btn(1, 'PANEL STATES (collapsed)'), btn(2, 'AIRCRAFT')]);
  const states = s.buttonByLabel('PANEL STATES');
  states.focus(); states.click();
  follow(s, 'OPTIONS', [btn(1, 'PANEL STATES (expanded)'), btn(5, 'COLD & DARK'), btn(6, 'TURNAROUND'), btn(2, 'AIRCRAFT')]);
  const cold = s.buttonByLabel('COLD & DARK');
  assert.strictEqual(s.window.document.activeElement, cold, 'focus did not go to the list');
  cold.click();
  follow(s, 'OPTIONS', [btn(1, 'PANEL STATES (collapsed)'), btn(2, 'AIRCRAFT')]);
  assert.strictEqual(s.window.document.activeElement, s.buttonByLabel('PANEL STATES'), 'focus did not come back to the button');
});

test('with focus following, a dialog that appears by itself leaves focus where it was', (t) => {
  const s = loadShell(t);
  follow(s, 'LOADSHEET', [btn(1, 'SET FUEL'), btn(2, 'SET ZFW')]);
  const zfw = s.buttonByLabel('SET ZFW');
  zfw.focus();   // reading, no press
  follow(s, 'LOADSHEET', [alertItem(3, 'REFUEL: complete'), btn(4, 'OK'), btn(1, 'SET FUEL'), btn(2, 'SET ZFW')]);
  assert.strictEqual(s.window.document.activeElement, zfw);
});

// ---- opt-in Enter-only edit box (item key commit = 'enter') ----
const fld = (idx, text, value, extra) => Object.assign({ idx, kind: 'field', controlType: 'text', text, value, clickable: false, level: 0, live: '', disabled: false }, extra || {});
const keydown = (win, el, k) => el.dispatchEvent(new win.KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
const sets = (s) => s.posted.map((m) => JSON.parse(m)).filter((m) => m.type === 'set');
// The pilot typing: the box's text changes and the browser fires 'input', as a keystroke does (the Enter-only box counts only
// text typed since the shell last wrote it, so a test that sets the value without the event is no typing).
const typeIn = (win, box, v) => { box.value = v; box.dispatchEvent(new win.Event('input', { bubbles: true })); };

test('An_enter_only_box_posts_on_enter_only', (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  typeIn(s.window, box, '250');
  box.dispatchEvent(new s.window.Event('change', { bubbles: true }));
  assert.strictEqual(sets(s).length, 0, 'change must not post');
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s), [{ type: 'set', idx: '3', value: '250', controlType: 'text' }]);
});

test('Leaving_an_enter_only_box_restores_it', async (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  typeIn(s.window, box, '250');
  box.dispatchEvent(new s.window.Event('blur'));
  assert.strictEqual(box.value, '100');
  assert.strictEqual(sets(s).length, 0);
  assert.strictEqual(await s.spoken(), 'Zero fuel weight not changed');
});

test('A_box_without_commit_posts_on_change_as_before', (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100')]);
  const box = s.window.document.querySelector('input.in');
  box.value = '250';
  box.dispatchEvent(new s.window.Event('change', { bubbles: true }));
  assert.deepStrictEqual(sets(s), [{ type: 'set', idx: '3', value: '250', controlType: 'text' }]);
  box.value = '260';
  box.dispatchEvent(new s.window.Event('blur'));
  assert.strictEqual(box.value, '260', 'no restore without commit');
});

// Left after Enter, before the read-back came: the box shows what the tablet shows (D2), its text before the typing, and says
// nothing (the set is on its way); the read-back's value then shows in it.
test('An_enter_only_box_left_before_the_read_back_shows_the_tablets_text_until_it_arrives', async (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  typeIn(s.window, box, '250');
  keydown(s.window, box, 'Enter');
  assert.strictEqual(sets(s).length, 1);
  box.dispatchEvent(new s.window.Event('blur'));
  assert.strictEqual(box.value, '100');
  assert.strictEqual(await s.spoken(), 'EFB page: Fuel', 'only the page announce, nothing about the box');
  s.render('Fuel', [fld(3, 'Zero fuel weight', '250', { commit: 'enter' })]);
  assert.strictEqual(box.value, '250');
});

// The final review's I1. The read-back arrives while the pilot is still in the box (the normal case after Enter), in the tablet's
// own text: the Airport box upper-cases "kpdx". The box keeps the pilot's text while they are in it, and leaving it shows the
// tablet's text with nothing said: the runner said "Airport: KPDX", and "Airport not changed" after it would be wrong. A second
// Enter on the text the pilot typed types it again (the re-review's N1): the shell cannot tell a read-back from the push of a
// failed typing, and after a failure Enter is the pilot's retry.
test('An_enter_only_box_left_after_the_tablet_changed_the_text_shows_it_and_says_nothing', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = 'kpdx';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KPDX', { commit: 'enter' })]);
  assert.strictEqual(s.window.document.activeElement, box, 'the re-render kept the focus');
  assert.strictEqual(box.value, 'kpdx', 'the box the pilot is in is never written');
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s).map((m) => m.value), ['kpdx', 'kpdx'], 'a second Enter on the typed text types it again');
  box.blur();
  assert.strictEqual(box.value, 'KPDX');
  assert.strictEqual(await s.spoken(), 'EFB page: PERF calc', 'only the page announce, nothing about the box');
});

// The tablet took none of it (Airport drops digits, so "k1" leaves KSEA): the window's next read shows the same value, so no new value
// arrives at all. Leaving the box shows the tablet's text again, and says nothing (the runner said what the tablet reads).
test('An_enter_only_box_whose_text_the_tablet_rejected_shows_the_tablets_text_on_leaving', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = 'k1';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  box.blur();
  assert.strictEqual(box.value, 'KSEA');
  assert.strictEqual(await s.spoken(), 'EFB page: PERF calc', 'only the page announce, nothing about the box');
  assert.strictEqual(sets(s).length, 1);
});

// A long text goes in several runs, and the window may read the box between them: a partial value arrives, then the final one (the
// scrub's made-up key, 44 characters: two runs). In the box throughout, the pilot leaves it showing the final value with nothing
// said; left between the two, it shows the partial one, silently, and follows the final one when it arrives.
const acarsKey = (v) => [fld(4, 'Sayintentions ACARS key', v, { commit: 'enter' })];
const longKey = 'MADEUPMADEUPMADEUPMADEUPMADEUPMADEUPMADEUPMA', partKey = longKey.slice(0, 40);

function enterLongKey(s) {
  s.render('AIRAC data', acarsKey(''));
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = longKey;
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  return box;
}

test('An_enter_only_box_takes_a_partial_value_then_the_final_one_while_the_pilot_is_in_it', async (t) => {
  const s = loadShell(t);
  const box = enterLongKey(s);
  s.render('AIRAC data', acarsKey(partKey));
  s.render('AIRAC data', acarsKey(longKey));
  box.blur();
  assert.strictEqual(box.value, longKey);
  assert.strictEqual(sets(s).length, 1);
  assert.strictEqual(await s.spoken(), 'EFB page: AIRAC data', 'only the page announce, nothing about the box');
});

test('An_enter_only_box_left_between_a_partial_value_and_the_final_one_follows_the_final_one', async (t) => {
  const s = loadShell(t);
  const box = enterLongKey(s);
  s.render('AIRAC data', acarsKey(partKey));
  box.blur();
  assert.strictEqual(box.value, partKey);
  s.render('AIRAC data', acarsKey(longKey));
  assert.strictEqual(box.value, longKey);
  assert.strictEqual(await s.spoken(), 'EFB page: AIRAC data', 'only the page announce, nothing about the box');
});

// Typing again after the read-back is a new edit: leaving the box without Enter then puts the tablet's text back and says so, as
// any edit left without Enter does.
test('Typing_again_after_the_read_back_is_a_new_edit_that_leaving_does_not_type', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = 'kpdx';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KPDX', { commit: 'enter' })]);
  box.value = 'KPDXY';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  box.blur();
  assert.strictEqual(box.value, 'KPDX');
  assert.strictEqual(sets(s).length, 1);
  assert.strictEqual(await s.spoken(), 'Airport not changed');
});

// The re-review's N1. A typing that failed ("the tablet's keyboard did not come up", "not typed") leaves the item's value as it was,
// and the pilot's natural retry is Enter again on the text they typed: it types it again.
test('After_a_failed_typing_a_second_Enter_types_it_again', (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  typeIn(s.window, box, 'KPDX');
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);   // the failure's push: the value unchanged
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s).map((m) => m.value), ['KPDX', 'KPDX']);
});

// The pilot sits in a box they have not typed in while BlueBird changes its value (PERF calc fills the runway length from the
// airport): the box shows the tablet's new value, Enter posts nothing (it would type the stale one back), and leaving says nothing.
test('An_untouched_box_the_pilot_is_in_follows_the_tablet_and_Enter_posts_nothing', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(4, 'Length (FT)', '3500', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  s.render('PERF calc', [fld(4, 'Length (FT)', '4000', { commit: 'enter' })]);
  assert.strictEqual(box.value, '4000');
  keydown(s.window, box, 'Enter');
  assert.strictEqual(sets(s).length, 0);
  box.blur();
  assert.strictEqual(box.value, '4000');
  assert.strictEqual(await s.spoken(), 'EFB page: PERF calc', 'only the page announce, nothing about the box');
});

// Enter, then left before the read-back (the box shows the tablet's old text), then back in when the read-back arrives: the box shows
// the read-back, since nothing was typed since the shell wrote it, and Enter posts nothing (it would type the old text back over it).
test('Back_in_the_box_left_before_the_read_back_it_shows_the_read_back_and_Enter_posts_nothing', (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  typeIn(s.window, box, 'kpdx');
  keydown(s.window, box, 'Enter');
  box.blur();
  assert.strictEqual(box.value, 'KSEA');
  box.focus();
  s.render('PERF calc', [fld(3, 'Airport', 'KPDX', { commit: 'enter' })]);
  assert.strictEqual(box.value, 'KPDX');
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s).map((m) => m.value), ['kpdx']);
});

// The final review's M6: nothing on an Enter-only box said that only Enter types it. It carries that as its accessible description
// (aria-description, which NVDA says after the box's name, role and value when it gets the focus); a box with no commit key, every
// other EFB's, carries none.
test('An_enter_only_box_says_that_Enter_types_it_and_another_box_says_nothing_more', (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' }), fld(4, 'Zero fuel weight', '100')]);
  const [enterOnly, other] = s.window.document.querySelectorAll('input.in');
  assert.strictEqual(enterOnly.getAttribute('aria-description'), 'Press Enter to type it on the tablet.');
  assert.strictEqual(enterOnly.getAttribute('aria-label'), 'Airport', 'the name stays the label alone');
  assert.strictEqual(other.getAttribute('aria-description'), null);
  assert.strictEqual(other.getAttribute('aria-describedby'), null);
});

test('Enter_with_the_text_unchanged_posts_and_says_nothing', async (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  keydown(s.window, box, 'Enter');
  assert.strictEqual(sets(s).length, 0);
  assert.strictEqual(await s.spoken(), 'EFB page: Fuel', 'only the page announce, nothing about the box');
});
