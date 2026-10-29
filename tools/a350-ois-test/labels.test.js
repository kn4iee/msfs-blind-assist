const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, load } = require('./run');

// T.O PERF after an airport is accepted: the OIS writes its validation result "<OK>" into the
// AIRP label cell. The field still says what it is, then the result, as a word a reader speaks.
test('an accepted airport reads "AIRP, OK" even on first sight', () => {
  const els = scrape('toperf_airp_ok');
  const f = els.find(e => e.controlType === 'text' && e.value === 'VCBI');
  assert.ok(f, 'the airport field is there');
  assert.equal(f.text, 'AIRP, OK');
});

test('a label seen before the result keeps it: "AIRP, OK"', () => {
  const { window, A } = load('toperf_airp_ok');
  const input = window.document.querySelector('input[value="VCBI"]');
  input.closest('.row').querySelector('.text-end').textContent = 'AIRP';
  A.scrape();
  input.closest('.row').querySelector('.text-end').textContent = '<OK>';
  const f = JSON.parse(A.scrape()).elements.find(e => e.value === 'VCBI');
  assert.equal(f.text, 'AIRP, OK');
});

test('the label survives the OIS re-creating the field', () => {
  const { window, A } = load('toperf_airp_ok');
  const input = window.document.querySelector('input[value="VCBI"]');
  const cell = input.closest('.row').querySelector('.text-end');
  cell.textContent = 'AIRP';
  A.scrape();
  input.parentElement.replaceChild(input.cloneNode(true), input);
  cell.textContent = '<OK>';
  const f = JSON.parse(A.scrape()).elements.find(e => e.value === 'VCBI');
  assert.equal(f.text, 'AIRP, OK');
});

// Review 2026-10-29 (I4): after Enter the OIS writes its result into the field's label cell ("AIRP" becomes "AIRP, OK"),
// and the shell keys an unkeyed control by its text: the field was rebuilt under the pilot's focus. A field keys on its
// name alone, so the result changes what it says, never which node it is.
test('a field keeps its key when the OIS writes its result into the label', () => {
  const { window, A } = load('toperf_airp_ok');
  const input = window.document.querySelector('input[value="VCBI"]');
  const cell = input.closest('.row').querySelector('.text-end');
  cell.textContent = 'AIRP';
  const before = JSON.parse(A.scrape()).elements.find(e => e.value === 'VCBI');
  cell.textContent = '<OK>';
  const after = JSON.parse(A.scrape()).elements.find(e => e.value === 'VCBI');
  assert.equal(before.text, 'AIRP');
  assert.equal(after.text, 'AIRP, OK');
  assert.equal(before.key, 'field:AIRP');
  assert.equal(after.key, before.key);
});

test('every field, slider and switch is keyed by its name', () => {
  for (const page of ['toperf_airp_ok', 'loadsheet', 'opt_volume_control', 'opt_callouts', 'opt_throttle_calibration', 'opt_simulation', 'ground'])
    for (const e of scrape(page).filter(e => ['text', 'range', 'checkbox'].includes(e.controlType)))
      assert.ok(/^field:./.test(e.key || '') && !/, OK$/.test(e.key), page + ': ' + e.text + ' keyed ' + e.key);
});

// With a runway picked, the RWY button shows the runway; a press opens the list of runways, so it
// is a plain button (no cycler re-announce), named with its label and the result.
test('the picked runway reads "RWY, OK: 04" and is a list opener, not a cycler', () => {
  const els = scrape('toperf_rwy_ok');
  const b = els.find(e => e.kind === 'button' && /: 04$/.test(e.text));
  assert.ok(b, 'the runway button is there');
  assert.equal(b.text, 'RWY, OK: 04');
  assert.ok(!b.announceChange, 'a list opener does not re-announce like a cycler');
});

test('the hidden runway choices are not read', () => {
  const els = scrape('toperf_rwy_ok');
  assert.equal(els.filter(e => e.kind === 'button' && /: (04|22)$/.test(e.text || '')).length, 1);
});

test('no element text carries the OIS angle brackets', () => {
  for (const f of ['toperf_airp_ok', 'toperf_rwy_ok'])
    for (const e of scrape(f)) assert.ok(!/[<>]/.test(e.text || ''), f + ': ' + e.text);
});

test('a real cycler (RWY COND) still re-announces its value', () => {
  const b = scrape('toperf_rwy_ok').find(e => /^RWY COND: /.test(e.text || ''));
  assert.ok(b && b.announceChange, 'RWY COND steps its value');
  assert.equal(b.key, 'cycler:RWY COND');
});
