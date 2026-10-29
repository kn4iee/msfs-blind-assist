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

// Review 2026-10-29 (I1): the OIS's choices act on the aircraft at once (NAVDATA, CPDLC SOURCE, the throttle calibration,
// the failures switch), and the shell committed each arrow step: every choice asks the shell to apply it on Enter only.
test('every choice is applied on Enter, never by the arrows', () => {
  for (const page of ['opt_throttle_calibration', 'opt_simulation', 'opt_sound_cabin_options', 'situations']) {
    const choices = scrape(page).filter(e => e.controlType === 'select');
    assert.ok(choices.length > 0, page + ': no choice');
    for (const c of choices) assert.equal(c.commit, 'enter', page + ': ' + c.text);
  }
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

// SIMULATION: PAUSE AT T.O.D / DIST (nm) is a YES / NO choice with the distance box beside it in the same group
// (live 2026-10-28: 30). The choice read, the box was skipped, so the distance could be neither read nor changed.
test('the pause at T.O.D distance reads beside its choice and takes a value', () => {
  const els = scrape('opt_simulation');
  const pause = find(els, e => e.controlType === 'select' && e.text === 'PAUSE AT T.O.D / DIST (nm)');
  assert.ok(pause, 'the choice');
  assert.equal(pause.value, 'YES');
  const dist = find(els, e => e.controlType === 'text' && e.text === 'PAUSE AT T.O.D DIST (nm)');
  assert.ok(dist, 'the distance box');
  assert.equal(dist.value, '30');
  assert.ok(els.indexOf(dist) === els.indexOf(pause) + 1, 'right after its choice');
});
