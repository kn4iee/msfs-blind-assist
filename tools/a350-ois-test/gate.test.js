const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

// Review 2026-10-29 (OIS M1): every 600 ms poll shipped the whole page (225 KB on the OFP) for the client to parse and
// hash. As the MD-11 reader does, a scrape with nothing changed since the last full one answers {unchanged:true}.
const tick = () => new Promise(r => setTimeout(r, 0));

test('an unchanged page is answered as unchanged, a changed one in full', async () => {
  const { window, A } = load('ground', { gate: true });
  assert.ok(JSON.parse(A.scrape()).elements, 'the first scrape is full');
  await tick();
  assert.deepEqual(JSON.parse(A.scrape()), { ok: true, unchanged: true }, 'its own stamping does not dirty the page');
  window.document.querySelector('button').textContent = 'CHANGED';
  await tick();
  assert.ok(JSON.parse(A.scrape()).elements, 'a change reads the page again');
});

test('a toggle flipped with no attribute change still reads the page again', async () => {
  const { window, A } = load('opt_callouts', { gate: true });
  A.scrape();
  await tick();
  const box = window.document.querySelector('input[type=checkbox]');
  box.checked = !box.checked;   // a property: no mutation
  box.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.ok(JSON.parse(A.scrape()).elements);
});

test('every tenth scrape is full whatever the observer saw', async () => {
  const { A } = load('ground', { gate: true });
  const answers = [];
  for (let i = 0; i < A.FORCE_FULL_EVERY + 1; i++) { answers.push(JSON.parse(A.scrape()).unchanged === true); await tick(); }
  assert.equal(answers.filter(u => !u).length, 2, 'the first and the tenth are full');
});

test('a press marks the page changed', async () => {
  const { A } = load('ground', { gate: true });
  const first = JSON.parse(A.scrape()).elements.find(e => e.kind === 'button' && e.text === 'CHOCKS: Removed');
  await tick();
  A.clickElement(first.idx);
  assert.ok(JSON.parse(A.scrape()).elements);
});

// A COMPUTE answer changes with time (it is held three seconds), not with the DOM: every scrape is full meanwhile.
test('while a compute answer is awaited every scrape is full', async () => {
  const { A } = load('ground', { gate: true });
  A.scrape();
  await tick();
  A.pendingCompute = { page: 'GROUND', sig: '', at: Date.now() };
  assert.ok(JSON.parse(A.scrape()).elements);
});
