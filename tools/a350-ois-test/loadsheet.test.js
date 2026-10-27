const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const texts = els => els.map(e => e.text);

// LOADSHEET is built of cards: a full-width title cell, then its values, read as one line each.
test('each cabin zone and hold reads as one line', () => {
  const t = texts(scrape('loadsheet'));
  for (const line of ['FIRST: 0 PAX, FIRST/PREM ECO 0 LBS', 'PREM ECO: 0 PAX', 'FWD ECO: 0 PAX, FWD ECO 0 LBS',
    'AFT ECO: 0 PAX, AFT ECO 0 LBS', 'TOTALS: 0 / 324 PAX, 0 LBS', 'FWD CARGO HOLD: 0 LBS',
    'AFT CARGO HOLD: 0 LBS', 'BULK CARGO HOLD: 0 LBS'])
    assert.ok(t.includes(line), 'missing ' + line);
});

// A stacked field: its title cell above, its unit cell after; the next title starts the next field.
test('ZFW and FUEL LOAD are two fields with their own labels', () => {
  const f = scrape('loadsheet').filter(e => e.controlType === 'text');
  assert.deepEqual(f.map(e => e.text), ['ZFW, LBS', 'FUEL LOAD, LBS']);
});

test('the CG figures read whole and never as headings', () => {
  const els = scrape('loadsheet');
  assert.ok(texts(els).includes('25.90 % ZFWCG'));
  assert.ok(texts(els).includes('28.35 % GWCG'));
  assert.ok(!els.some(e => e.kind === 'heading' && /\d/.test(e.text)), 'no number heading');
});

test('the tank figures say which tank', () => {
  const t = texts(scrape('loadsheet'));
  for (const line of ['Left tank: 0 LBS', 'Right tank: 0 LBS', 'Center tank: 0 LBS'])
    assert.ok(t.includes(line), 'missing ' + line);
  assert.ok(!t.includes('0 LBS'), 'no bare figure');
});

test('the buttons are there, SET ZFW disabled', () => {
  const els = scrape('loadsheet');
  for (const b of ['SIMBRIEF IMPORT', 'REFRESH', 'SET ZFW', 'SET FUEL (collapsed)', 'RESEND LOADSHEET'])
    assert.ok(els.some(e => e.kind === 'button' && e.text === b), 'missing ' + b);
  assert.ok(els.find(e => e.text === 'SET ZFW').disabled);
});
