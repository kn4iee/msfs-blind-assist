const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

// COMPUTE with a field missing: the OIS shows a red "ERR: NO WIND INPUT" line above RESULTS. It is a
// validation error, so the window speaks it as it appears (kind alert, assertive), and reads it once.
test('an OIS error line is one assertive alert', () => {
  const els = scrape('toperf_err');
  const alerts = els.filter(e => e.kind === 'alert');
  assert.equal(alerts.length, 1);
  assert.equal(alerts[0].text, 'ERR: NO WIND INPUT');
  assert.equal(alerts[0].live, 'assertive');
  assert.ok(!els.some(e => e.kind !== 'alert' && /NO WIND INPUT/.test(e.text || '')), 'not also a plain line');
});

test('a page with no error has no alert', () => {
  assert.equal(scrape('toperf_rwy_ok').filter(e => e.kind === 'alert').length, 0);
});

// A notie toast slides up off the screen when it is done (top: -112px) and stays in the DOM at full
// size: it is gone for a sighted pilot, so it is not read.
test('a toast slid off the top is not read', () => {
  const els = scrape('plan_toast', { before: w => {
    w.document.querySelector('.notie-container').setAttribute('data-rect', '-112,0,2355,0');
  } });
  assert.ok(!els.some(e => /departure airport/.test(e.text || '')));
});
