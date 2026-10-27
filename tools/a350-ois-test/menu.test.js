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
