const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, load } = require('./run');

const find = (els, pred) => els.find(pred);

// CALLOUTS: the RA callouts are a group of switches, each its own checkbox.
test('each RA callout is a checkbox', () => {
  const els = scrape('opt_callouts');
  for (const n of ['5', '10', '50', '100', '2500']) {
    const c = find(els, e => e.controlType === 'checkbox' && e.text === 'RA CALLOUTS: ' + n);
    assert.ok(c, 'missing callout ' + n);
  }
  assert.ok(!els.some(e => e.kind === 'static' && /^RA CALLOUTS/.test(e.text)), 'not one line');
});

// VOLUME CONTROL: the smaller sliders' labels have their own class; RESET VOLUME holds a button.
test('every volume slider is labelled and the reset button reads', () => {
  const els = scrape('opt_volume_control');
  const ranges = els.filter(e => e.controlType === 'range');
  assert.ok(ranges.length >= 10);
  assert.ok(ranges.every(r => r.text), 'no unlabelled slider');
  for (const t of ['MASTER VOLUME', 'INTERIOR ENGINE', 'COCKPIT WIND']) assert.ok(ranges.some(r => r.text === t), 'missing ' + t);
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'RECOMMENDED VALUES'));
  assert.ok(els.some(e => e.kind === 'static' && /^This will reset all volume settings/.test(e.text)));
});

// THROTTLE CALIBRATION: free-standing radio sets (a label cell, then a cell of YES / NO radios).
test('the throttle calibration choices, field and axes read', () => {
  const els = scrape('opt_throttle_calibration');
  const roa = find(els, e => e.controlType === 'select' && e.text === 'REVERSER ON AXIS');
  assert.ok(roa, 'REVERSER ON AXIS');
  assert.equal(roa.value, 'NO');
  assert.deepEqual(roa.options, ['YES', 'NO']);
  assert.ok(find(els, e => e.controlType === 'select' && e.text === 'USE GLOBAL DEADZONE'));
  assert.ok(find(els, e => e.kind === 'button' && e.text === 'IDLE'), 'IDLE is a plain button');
  assert.ok(find(els, e => e.controlType === 'text' && e.text === 'DZ %' && e.value === '5'));
  for (const t of ['AXIS 1: 0%', 'AXIS 2: 0%']) assert.ok(find(els, e => e.kind === 'static' && e.text === t), t);
  assert.ok(!els.some(e => e.kind === 'static' && /^(YES|NO|REVERSER ON AXIS)$/.test(e.text)), 'no loose texts');
});

test('a free-standing radio set takes a pick', () => {
  const { window, A } = load('opt_throttle_calibration');
  const roa = JSON.parse(A.scrape()).elements.find(e => e.text === 'REVERSER ON AXIS');
  assert.equal(A.setValue(roa.idx, 'YES'), 'ok');
  assert.ok(window.document.getElementById('opt_tc_roa1').checked);
});
