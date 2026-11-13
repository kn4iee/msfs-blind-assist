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

// Review 2026-11-04 (OIS O2): a read that throws must leave the page dirty, or the next polls answer unchanged on stale indexes.
test('a scrape that fails leaves the next one full', async () => {
  const { A } = load('ground', { gate: true });
  A.scrape();
  await tick();
  const collect = A.collectPage;
  A._dirty = true;
  A.collectPage = () => { throw new Error('torn read'); };
  assert.throws(() => A.scrape());
  A.collectPage = collect;
  await tick();
  assert.ok(JSON.parse(A.scrape()).elements);
});

// Second review pass (2026-11-13): the read catches its own failure and answers {ok:false}, so the scrape's catch above never
// saw one: the page stayed clean and the next polls answered unchanged for up to ten polls. A read that fails inside the page
// walk leaves the next scrape full.
test('a read that fails inside the page walk leaves the next scrape full', async () => {
  const { A } = load('ground', { gate: true });
  A.scrape();
  await tick();
  const visible = A.visible;
  A._dirty = true;
  A.visible = () => { throw new Error('torn read'); };
  assert.equal(JSON.parse(A.scrape()).ok, false, 'the failed read says so');
  A.visible = visible;
  await tick();
  assert.ok(JSON.parse(A.scrape()).elements, 'and the page is read again at once');
});
