const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const byText = (els, t) => els.find(e => e.text === t);

// FLT OPS PLAN: an options button sits in a labelled row ("FROM RWY" + AUTO) and opens the OIS's
// options pop-up, so it reads with its row's label and is a plain button, not a cycler.
test('an options button reads with its row label', () => {
  const els = scrape('plan');
  for (const t of ['FROM RWY: AUTO', 'TO RWY: AUTO', 'ALTN: PLAN', 'DEPTIME: NOW +30m', 'CONT: AUTO', 'RSRV: AUTO', 'PLAN: LIDO', 'UNITS: LBS', 'ADV OPTS: SHOW']) {
    const b = byText(els, t);
    assert.ok(b && b.kind === 'button', 'missing button ' + t);
    assert.ok(!b.announceChange, t + ' opens a pop-up, it does not step');
  }
  assert.ok(!els.some(e => e.kind === 'static' && /^(FROM RWY|ALTN|DEPTIME|CONT|RSRV|UNITS|ADV OPTS)$/.test(e.text)), 'no orphan row labels');
});

// A fuel row: the field, then its unit button in the same row ("BLOCK" + field + WEIGHT). The field
// is "BLOCK"; the button is the field's unit, read on its own.
test('a field and its unit button in one row read apart', () => {
  const els = scrape('plan');
  const block = els.filter(e => e.controlType === 'text' && /^BLOCK/.test(e.text));
  assert.equal(block.length, 1);
  assert.equal(block[0].text, 'BLOCK');
  assert.ok(byText(els, 'BLOCK unit: WEIGHT'), 'the unit button is there');
  assert.ok(els.some(e => e.controlType === 'text' && /^TAXI, lbs$/i.test(e.text)), 'a plain unit cell stays in the label');
});

test('a row of plain texts reads as one line', () => {
  const els = scrape('plan');
  assert.ok(byText(els, 'DEPARTURE to ARRIVAL'));
  assert.ok(!els.some(e => /^DEPARTURE: /.test(e.text || '')));
});

// The OIS's notie toasts ("Please enter a departure airport") answer a press, then vanish: spoken.
test('a notie toast is one assertive alert', () => {
  const els = scrape('plan_toast');
  const a = els.filter(e => e.kind === 'alert');
  assert.equal(a.length, 1);
  assert.equal(a[0].text, 'Please enter a departure airport');
  assert.equal(a[0].live, 'assertive');
  assert.equal(els.filter(e => /departure airport/.test(e.text || '')).length, 1);
});
