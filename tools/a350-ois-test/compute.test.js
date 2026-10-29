'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

// COMPUTE's results arrive a few seconds after the press (T.O PERF, live 2026-10-28), where a sighted
// pilot sees them appear: the window speaks them once as they arrive and hold (a read later: the OIS may
// still be filling them), as the read-back of the press. The answer stays first among the alerts for a
// few seconds (the window speaks only its first alert, once), so a toast still up never hides it.

const COMPUTED = {
  RWY: '04', TODA: '10961', 'MTOW(perf)': '712.3', V1: '141', VR: '142', THRUST: '52', V2: '150', FLAPS: '2',
  THS: '28.4', 'THR RED': '1026', 'EO ACCEL': '1026',
};

// The RESULTS read-outs' value cells (the first span beside each label), as the OIS fills them.
function results(window) {
  const doc = window.document;
  const heading = Array.from(doc.querySelectorAll('div')).find(d => d.textContent === 'RESULTS');
  return Array.from(doc.querySelectorAll('.text-end'))
    .filter(l => heading.compareDocumentPosition(l) & window.Node.DOCUMENT_POSITION_FOLLOWING)
    .filter(l => l.getAttribute('data-vis') !== '0');
}

function fill(window, values) {
  for (const label of results(window)) {
    if (!(label.textContent in values)) continue;
    label.nextElementSibling.querySelector('span').textContent = values[label.textContent];
  }
}

const alerts = A => JSON.parse(A.scrape()).elements.filter(e => e.kind === 'alert');

function pressCompute(A) {
  const compute = JSON.parse(A.scrape()).elements.find(e => e.text === 'COMPUTE');
  assert.equal(A.clickElement(compute.idx), 'ok');
}

test('the results COMPUTE brings are spoken once, as they arrive and hold', () => {
  const { window, A } = load('toperf_airp_ok');
  let now = 1000000;
  window.Date.now = () => now;
  pressCompute(A);
  assert.deepEqual(alerts(A), [], 'still computing: nothing to say yet');
  fill(window, COMPUTED);
  assert.deepEqual(alerts(A), [], 'just filled: the OIS may still be filling');
  const said = alerts(A);
  assert.deepEqual(said.map(a => a.text), ['Results: RWY: 04, TODA: 10961 FT, MTOW(perf): 712.3 x1000 LBS, V1: 141 kt, '
    + 'VR: 142 kt, THRUST: 52 ° C, V2: 150 kt, FLAPS: 2, THS: 28.4 %, THR RED: 1026, EO ACCEL: 1026 ft']);
  assert.equal(said[0].live, 'assertive');
  now += 1000;
  assert.equal(alerts(A).length, 1, 'kept a few seconds: the window speaks it once');
  now += A.ANSWER_KEEP_MS;
  assert.deepEqual(alerts(A), [], 'then the results read as lines, and no alert');
});

// Review 2026-10-28 (minor 5): "filled" meant any line with a value, so results arriving a few at a time
// (RWY and THS first, as beside an error line) were spoken incomplete and the press's answer was spent.
test('results still filling are spoken once they hold', () => {
  const { window, A } = load('toperf_airp_ok');
  pressCompute(A);
  fill(window, { RWY: '04', THS: '28.4' });
  assert.deepEqual(alerts(A), [], 'a few values: they may still be filling');
  fill(window, COMPUTED);
  assert.deepEqual(alerts(A), [], 'changed again: wait for them to hold');
  const said = alerts(A);
  assert.equal(said.length, 1);
  assert.match(said[0].text, /^Results: RWY: 04, TODA: 10961 FT/);
});

// Review 2026-10-28 (minor 5): the answer was in one read only and last among the alerts; the window speaks
// only its first alert, so a toast still up hid it, and it was gone at the next read.
test('the answer comes before a toast still up', () => {
  const { window, A } = load('toperf_airp_ok');
  pressCompute(A);
  fill(window, COMPUTED);
  alerts(A);
  const toast = window.document.createElement('div');
  toast.className = 'notie-container';
  toast.innerHTML = '<div class="notie-textbox-inner">Please enter a departure airport</div>';
  window.document.body.appendChild(toast);
  const said = alerts(A).map(a => a.text);
  assert.equal(said.length, 2);
  assert.match(said[0], /^Results: /);
  assert.equal(said[1], 'Please enter a departure airport');
});

test('results that change with no COMPUTE press are not spoken', () => {
  const { window, A } = load('toperf_airp_ok');
  A.scrape();
  fill(window, COMPUTED);
  assert.deepEqual(alerts(A), []);
});

// Pressing COMPUTE again: if the OIS empties the results while it computes, the refilled values are
// the press's answer even when they match the last ones.
test('results emptied while computing are spoken when they come back', () => {
  const { window, A } = load('toperf_airp_ok');
  fill(window, COMPUTED);
  pressCompute(A);
  fill(window, Object.fromEntries(Object.keys(COMPUTED).map(k => [k, '-'])));
  assert.deepEqual(alerts(A), []);
  fill(window, COMPUTED);
  assert.deepEqual(alerts(A), []);
  assert.equal(alerts(A).length, 1);
});

// A COMPUTE that fails fills a cell or two (RWY, THS: toperf_err) beside the red error line: the error
// is the answer, already spoken as its own alert.
test('a COMPUTE that ends in an error says only the error', () => {
  const { window, A } = load('toperf_airp_ok');
  pressCompute(A);
  const doc = window.document;
  const heading = Array.from(doc.querySelectorAll('div')).find(d => d.textContent === 'RESULTS');
  const err = doc.createElement('div');
  err.className = 'row';
  err.innerHTML = '<div>ERR: NO WIND INPUT</div>';
  heading.parentElement.parentElement.insertBefore(err, heading.parentElement);
  fill(window, { RWY: '04', THS: '28.4' });
  assert.deepEqual(alerts(A).map(a => a.text), ['ERR: NO WIND INPUT']);
});

test('LDG PERF results are spoken the same way', () => {
  const { window, A } = load('ldgperf');
  const lines = () => results(window).map(l => l.nextElementSibling.querySelector('span'));
  const before = lines().map(s => s.textContent);
  lines().forEach(s => { s.textContent = '-'; });
  A.scrape();
  pressCompute(A);
  lines().forEach((s, i) => { s.textContent = before[i]; });
  assert.deepEqual(alerts(A), []);
  const said = alerts(A);
  assert.equal(said.length, 1);
  assert.match(said[0].text, /^Results: RWY: 22, LW: 450 x1000 LBS, LD: 4338 FT/);
});
