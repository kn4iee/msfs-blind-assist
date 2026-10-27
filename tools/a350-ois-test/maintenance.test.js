const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const statics = els => els.filter(e => e.kind === 'static').map(e => e.text);

// MAINTENANCE: a gauge is a bordered value beside its label, either way round: "label: value".
test('each gauge reads label then value', () => {
  const s = statics(scrape('maintenance'));
  for (const re of [/^BATTERY 1 STATUS: \d+\.\d%$/, /^BATTERY 2 STATUS: \d+\.\d%$/, /^EMERGENCY BATT 1 STATUS: \d+\.\d%$/,
    /^EMERGENCY BATT 2 STATUS: \d+\.\d%$/, /^APU OPERATING HOURS: 00:00:00$/, /^APU START CYCLES: 2$/])
    assert.ok(s.some(t => re.test(t)), 'missing ' + re);
  assert.ok(!scrape('maintenance').some(e => e.kind === 'heading' && /^[\d.:%]+$/.test(e.text)), 'no value headings');
});

// A fluid level: its quantity beside a bar whose width is how full it is.
test('each fluid level reads its quantity and how full', () => {
  const s = statics(scrape('maintenance'));
  for (const re of [/^Y: 30\.5L, 74%$/, /^G: 37\.\dL, 7\d%$/, /^E1: 22\.\dQT, 9\d%$/, /^E2: 22\.\dQT, 9\d%$/, /^OIL QTY: 100%$/])
    assert.ok(s.some(t => re.test(t)), 'missing ' + re);
});

// The tyre and brake boxes on the gear picture: their colour, summed up.
test('tyres and brakes read as a summary of their colours', () => {
  const s = statics(scrape('maintenance'));
  assert.ok(s.includes('Tyres: all green'), s.join(' | '));
  assert.ok(s.includes('Brakes: all green'));
});

test('a tyre of another colour is named', () => {
  const s = statics(scrape('maintenance', { before: w => { w.document.getElementById('main_rf_w_left').style.backgroundColor = 'red'; } }));
  assert.ok(s.includes('Tyres: green, except right main front left: red'), s.join(' | '));
});

// The engine cowl buttons go red while the cowl is open (live: LEFT ENGINE COWL TOGGLED).
test('the cowl buttons say open or closed', () => {
  const btn = (els, re) => els.find(e => e.kind === 'button' && re.test(e.text));
  assert.equal(btn(scrape('maintenance'), /^LEFT ENG/).text, 'LEFT ENG cowl: Closed');
  const open = scrape('maintenance', { before: w => {
    [...w.document.querySelectorAll('button')].find(b => b.textContent === 'LEFT ENG').style.backgroundColor = 'red';
  } });
  assert.equal(btn(open, /^LEFT ENG/).text, 'LEFT ENG cowl: Open');
  assert.ok(btn(open, /^LEFT ENG/).announceChange);
});

// A button the OIS paints without a known meaning says its colour, as a sighted pilot sees it.
test('a painted button of unknown meaning says its colour', () => {
  const els = scrape('maintenance', { before: w => {
    [...w.document.querySelectorAll('button')].find(b => b.textContent === 'DEPLOY/STOW RAT').style.background = 'green';
  } });
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'DEPLOY/STOW RAT (green)'));
});
