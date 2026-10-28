const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const button = (els, t) => els.find(e => e.kind === 'button' && e.text === t);

// A button that shows a list beside it (PANEL STATES' presets, SET FUEL's refuel kinds, the EFB
// dropdown) says whether the list is open, and says it again after a press.
test('PANEL STATES and the EFB menu say collapsed', () => {
  const els = scrape('menu');
  for (const t of ['PANEL STATES (collapsed)', 'EFB menu (collapsed)']) {
    const b = button(els, t);
    assert.ok(b, 'missing ' + t);
    assert.ok(b.announceChange);
  }
  assert.ok(!button(els, 'COLD AND DARK'), 'the presets are hidden');
});

// Opening removes the list's d-none: the button was seen as an expander while it was closed.
test('an open PANEL STATES says expanded and lists its presets', () => {
  const { load } = require('./run');
  const { window, A } = load('menu');
  A.scrape();
  const list = [...window.document.querySelectorAll('button')].find(b => b.textContent === 'PANEL STATES').nextElementSibling;
  list.classList.remove('d-none');
  for (const n of [list, ...list.querySelectorAll('*')]) n.removeAttribute('data-vis');
  const els = JSON.parse(A.scrape()).elements;
  assert.ok(button(els, 'PANEL STATES (expanded)'));
  for (const t of ['COLD AND DARK', 'ON GROUND POWER', 'ON APU', 'READY FOR TAKEOFF']) assert.ok(button(els, t), 'missing ' + t);
});

test('SET FUEL says its refuel list is collapsed', () => {
  assert.ok(button(scrape('loadsheet'), 'SET FUEL (collapsed)'));
});

// The EFB dropdown open (live 2026-10-28, the user: "duplicate buttons when the menu key is pressed"): its pages
// (MENU, FLT OPS STS, LOADSHEET, ...) are read before the bottom bar's, five of them with the same names. Each says
// it is the menu's, so the two LOADSHEETs, T.O PERFs and so on are told apart.
test('an open EFB menu names its items apart from the bottom bar', () => {
  const { load } = require('./run');
  const { window, A } = load('menu');
  const count = (list, t) => list.filter(e => e.kind === 'button' && e.text === t).length;
  const closed = JSON.parse(A.scrape()).elements;
  const dd = window.document.getElementById('efb-dropdown-menu-content');
  for (const n of [dd, ...dd.querySelectorAll('*')]) n.removeAttribute('data-vis');
  const els = JSON.parse(A.scrape()).elements;
  assert.ok(button(els, 'EFB menu (expanded)'));
  for (const t of ['MENU', 'FLT OPS STS', 'LOADSHEET', 'T.O PERF', 'GROUND EQUIP']) assert.ok(button(els, 'EFB menu: ' + t), 'missing EFB menu: ' + t);
  // Opening the menu adds no second plain LOADSHEET, T.O PERF or ENROUTE (the bar's, and the page's own, stay as they were).
  for (const t of ['LOADSHEET', 'T.O PERF', 'ENROUTE']) assert.equal(count(els, t), count(closed, t), 'no new plain ' + t);
});
