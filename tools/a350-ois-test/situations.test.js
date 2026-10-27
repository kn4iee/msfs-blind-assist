const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

// The Situation Manager (captain's OIS when the preferred seat side is CPT).
test('the situations switch is a choice labelled SITUATIONS', () => {
  const s = scrape('situations').find(e => e.controlType === 'select');
  assert.equal(s.text, 'SITUATIONS');
  assert.equal(s.value, 'DISABLED');
  assert.deepEqual(s.options, ['ENABLED', 'DISABLED']);
});

test('each situation reads its title and description, its button its title', () => {
  const els = scrape('situations');
  assert.ok(els.some(e => e.kind === 'static' && e.text === 'DEPRESSURISATION: Simulate a cabin depressurisation event at altitude'));
  const b = els.find(e => e.kind === 'button' && e.text === 'DEPRESSURISATION: DISABLED');
  assert.ok(b, 'the button names its situation');
  assert.ok(b.disabled);
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'SMOKE IN THE CABIN: DISABLED'));
});

// FAILURES: a press toggles a failure; an active one is drawn bg-danger (live: RMP 3 FAULT).
test('each failure says whether it is active', () => {
  const els = scrape('failures');
  const b = els.find(e => e.kind === 'button' && /^RMP 3 FAULT/.test(e.text));
  assert.equal(b.text, 'RMP 3 FAULT: Inactive');
  assert.ok(b.announceChange);
  const on = scrape('failures', { before: w => {
    [...w.document.querySelectorAll('.failure-button')].find(x => x.textContent === 'RMP 3 FAULT').classList.add('bg-danger');
  } });
  assert.ok(on.some(e => e.kind === 'button' && e.text === 'RMP 3 FAULT: Active'));
  assert.ok(els.some(e => e.kind === 'heading' && e.text === 'ATA-24 - ELECTRICAL POWER'));
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'RESET ALL'));
});

// Live: a card's title climbed to every button of the page (DEPRESSURISATION: FLT OPS MENU). Only a
// button in the card's own column takes it.
test('a card title stays with its own button', () => {
  const els = scrape('card_neighbour');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'DEPRESSURISATION: DISABLED'));
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'FLT OPS MENU'), els.map(e => e.text).join(' | '));
});
