const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const texts = els => els.map(e => e.text || '');
const indexOf = (els, t) => els.findIndex(e => (e.text || '') === t);

// The OIS's shortcut bar runs along the bottom of the screen, under the page (live 2026-11-16: FLT OPS MENU to ENROUTE at
// y 1391 of 1467), but it comes before the page in the document. Read in document order every page opened with eight
// navigation buttons before its own title (Gus, 2026-11-16: "the two menus ... I don't know if a sighted person has to hit
// both"). It is read where it is drawn, after the page; the header's EFB menu stays first, where it is drawn.
const BAR = ['FLT OPS MENU', 'GROUND', 'LOADSHEET', 'OFP', 'T.O PERF', 'LDG PERF', 'TERML CHART', 'ENROUTE'];

test('the bottom bar is read after the page', () => {
  for (const fixture of ['menu', 'ground_128', 'toperf_rwy_ok']) {
    const els = scrape(fixture);
    const title = els.findIndex(e => e.kind === 'heading' && e.level === 1);
    assert.ok(title >= 0, fixture + ': a page title');
    const tail = texts(els.slice(-BAR.length)).map(t => t.replace(/ \(selected\)$/, ''));
    assert.deepEqual(tail, BAR, fixture + ': the bar ends the page');
    assert.ok(els.slice(0, title).every(e => !BAR.includes((e.text || '').replace(/ \(selected\)$/, ''))),
      fixture + ': no bar button before the page title');
    assert.ok(/^EFB menu/.test(els[0].text), fixture + ': the EFB menu, top left, still first');
  }
});

// GROUND as drawn (live 2026-11-16, 1.2.8): the aircraft picture in the middle with its colour key above it and a
// clickspot on each door, front to back; PUSHBACK above GROUND EQUIP on its right. In the document the GROUND EQUIP block
// comes before PUSHBACK and every door after both, so the doors were read last, far from their key. The map reads in the
// order drawn: the picture's column, then the column to its right, each top to bottom.
const RECTS = {   // top, left, right, bottom, measured live 2026-11-16
  'door-map-image': [140, 1192, 1592, 1392],
  'door-l1': [236, 1278, 1372, 365], 'door-r1': [236, 1412, 1506, 365],
  'door-fwd-cargo': [330, 1440, 1490, 480],
  'door-l2': [455, 1281, 1361, 575], 'door-r2': [455, 1423, 1503, 575],
  'door-l3': [915, 1281, 1361, 1035], 'door-r3': [915, 1423, 1503, 1035],
  'door-aft-cargo': [1010, 1440, 1490, 1160], 'door-bulk-cargo': [1125, 1295, 1345, 1175],
  'door-l4': [1203, 1278, 1363, 1326], 'door-r4': [1203, 1420, 1505, 1326]
};
function placeGround(w) {
  Object.defineProperty(w, 'innerWidth', { value: 2355, configurable: true });   // the OIS's own width: nothing placed is off-screen
  const map = w.document.getElementById('door-map');
  for (const k of map.children) {
    const t = k.textContent || '';
    const r = RECTS[k.id] || (/^KEY:/.test(t.trim()) ? [140, 847, 1438, 185]
      : /PUSHBACK/.test(t) ? [356, 1817, 2317, 642] : /GROUND EQUIP/.test(t) ? [654, 1817, 2317, 1312] : null);
    if (r) k.setAttribute('data-rect', r.join(','));
  }
}

test('GROUND reads the aircraft picture front to back, then PUSHBACK, then GROUND EQUIP', () => {
  const els = scrape('ground_128', { before: placeGround });
  const at = t => els.findIndex(e => (e.text || '').startsWith(t));
  const order = ['KEY:', 'Door L1', 'Door R1', 'Forward cargo door', 'Door L2', 'Door R2', 'Door L3', 'Door R3',
    'Aft cargo door', 'Bulk cargo door', 'Door L4', 'Door R4', 'PUSHBACK', 'Push back, turn left', 'GROUND EQUIP', 'CHOCKS'];
  const seen = order.map(at);
  assert.ok(seen.every(i => i >= 0), 'all found: ' + order.filter((t, n) => seen[n] < 0).join(', '));
  for (let n = 1; n < seen.length; n++) assert.ok(seen[n] > seen[n - 1], order[n - 1] + ' before ' + order[n]);
  // The left column (DOORS & EXITS to EXTERIOR) still comes first.
  assert.ok(indexOf(els, 'DOORS & EXITS') < at('KEY:'), 'the left column first');
});

// Without a layout (every rect alike) the map keeps the document's order: nothing is lost or repeated.
test('the door map keeps every item when nothing is placed', () => {
  const placed = scrape('ground_128', { before: placeGround });
  const plain = scrape('ground_128');
  assert.deepEqual(texts(plain).slice().sort(), texts(placed).slice().sort());
});

// T.O PERF's and LDG PERF's SYNC keys (Gus, 2026-11-16: "it's not really clear what that sync button does"). The A350
// manual 1.5 (Performance, and the FMS walk-through): SYNC "inserts the current conditions (live METAR) and take off
// weight", SYNC FMS the departure (arrival) airport and runway as the FMS holds them, SYNC SIMBRIEF as SimBrief does.
test('the SYNC keys say what they fill', () => {
  const to = texts(scrape('toperf_rwy_ok'));
  for (const t of ['SYNC: the conditions from the latest METAR, and the take-off weight',
    'SYNC FMS: the departure airport and runway from the FMS', 'SYNC SIMBRIEF: the departure airport and runway from SimBrief'])
    assert.ok(to.includes(t), 'T.O PERF: ' + t);
  const ldg = texts(scrape('ldgperf'));
  for (const t of ['SYNC: the conditions from the latest METAR, and the landing weight',
    'SYNC FMS: the arrival airport and runway from the FMS', 'SYNC SIMBRIEF: the arrival airport and runway from SimBrief'])
    assert.ok(ldg.includes(t), 'LDG PERF: ' + t);
});

// FLT OPS MENU's OPS LIBRARY is drawn greyed (menu-button-disabled, no disabled attribute): it is said unavailable.
test('a greyed menu button is disabled', () => {
  const ops = scrape('menu').find(e => e.text === 'OPS LIBRARY');
  assert.ok(ops, 'OPS LIBRARY');
  assert.equal(ops.disabled, true);
});

// The OFP's Text + and Text - change the text size; the section list after them (OFP, Summary & Fuel, ... NOTAM) is not
// theirs, and Text - does not open it (live 2026-11-16: hidden before and after a press). A list is a button's only when
// the two stand alone together (PANEL STATES, SET FUEL).
test("the OFP's Text - is no expander when the section list is hidden", () => {
  const els = scrape('ofp', { before: w => {
    const minus = [...w.document.querySelectorAll('button')].find(b => b.textContent === 'Text -');
    minus.nextElementSibling.classList.add('d-none');
    for (const n of [minus.nextElementSibling, ...minus.nextElementSibling.querySelectorAll('*')]) n.setAttribute('data-vis', '0');
  } });
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Text -'), 'plain Text -');
  assert.ok(!els.some(e => /^Text - \(/.test(e.text || '')), 'never collapsed or expanded');
});

// Live 2026-11-16 (OMDB): the header's message count ("6 MESSAGES", top centre) has a click handler that opens the AOC page,
// where the messages are, and the window read it as plain text, so a pilot could not reach the messages from it.
test('the header message count is a button', () => {
  for (const fixture of ['menu', 'aoc']) {
    const els = scrape(fixture);
    const count = els.find(e => /^\d+ MESSAGES?$/.test(e.text || ''));
    assert.ok(count, fixture + ': the count is read');
    assert.equal(count.kind, 'button', fixture);
    assert.equal(count.clickable, true, fixture);
    assert.ok(count.idx > 0, fixture + ': it can be pressed');
  }
});

// Live 2026-11-16 (OMDB): AUTOSAVE's custom saves read "P17 RWY04", "EDIT", "DELETE", eight times over, so a pilot tabbing
// heard EDIT and DELETE without the save they act on. Each row's EDIT and DELETE carry its save's name, as a row's buttons do.
test('a custom save\'s EDIT and DELETE name their save', () => {
  const els = texts(scrape('autosave_create_128'));
  for (const save of ['P05 RWY04', 'P04 GATE', 'P02 FL200']) {
    const at = els.indexOf(save);
    assert.ok(at >= 0, save);
    assert.equal(els[at + 1], save + ': EDIT');
    assert.equal(els[at + 2], save + ': DELETE');
  }
  assert.ok(!els.includes('EDIT') && !els.includes('DELETE'), 'no bare EDIT or DELETE');
});
