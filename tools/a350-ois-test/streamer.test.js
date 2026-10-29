const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, load } = require('./run');

// Review 2026-10-29 (I6): OPTIONS > 3RD PARTY holds the SimBrief user ID, the SayIntentions API key and the Hoppie logon.
// With STREAMER MODE on they are kept secret (a streaming pilot's audience hears the screen reader), and a password field
// is never read out. A secret field says it is hidden and how long it is, never what it holds, and still takes a value.
// The fixture's values are dummies (secrets.test.js).
const IDS = ['SIMBRIEF USER ID', 'SAYINTENTIONS API KEY', 'HOPPIE LOGON'];
const streamerOn = w => {
  w.document.getElementById('opt_streamer_mode_off').checked = false;
  w.document.getElementById('opt_streamer_mode_on').checked = true;
};
const inputOf = (window, A, e) => window.document.querySelector('[' + A.ATTR + '="' + e.idx + '"]');
const hidden = (name, value) => name + ', hidden, ' + (!value ? 'empty' : value.length === 1 ? '1 character' : value.length + ' characters');

test('with STREAMER MODE off the IDs read as fields', () => {
  const els = scrape('opt_navigraph_login');
  for (const id of IDS) {
    const f = els.find(e => e.key === 'field:' + id);
    assert.ok(f, id);
    assert.equal(f.text, id);
    assert.ok(!f.secret, id + ' kept secret');
  }
});

test('with STREAMER MODE on each ID is hidden, its length said', () => {
  const { window, A } = load('opt_navigraph_login', { before: streamerOn });
  const els = JSON.parse(A.scrape()).elements;
  assert.equal(els.find(e => e.text === 'STREAMER MODE').value, 'ON');
  for (const id of IDS) {
    const f = els.find(e => e.key === 'field:' + id);
    assert.equal(f.secret, true, id);
    assert.equal(f.text, hidden(id, inputOf(window, A, f).value));
  }
  assert.deepEqual(els.filter(e => e.secret).map(e => e.key), IDS.map(id => 'field:' + id), 'nothing else is kept secret');
});

test('a password field is hidden, in an option group or on its own', () => {
  const { window, A } = load('opt_navigraph_login', { before: w => {
    const g = [...w.document.querySelectorAll('.option-group')].find(x => /SIMBRIEF USER ID/.test(x.textContent));
    g.querySelector('input').type = 'password';
  } });
  const f = JSON.parse(A.scrape()).elements.find(e => e.key === 'field:SIMBRIEF USER ID');
  assert.ok(f, 'the field is still read');
  assert.equal(f.secret, true);
  assert.equal(f.text, hidden('SIMBRIEF USER ID', inputOf(window, A, f).value));

  const sheet = load('loadsheet', { before: w => { [...w.document.querySelectorAll('input')].find(i => /^(text|number)$/.test(i.type) && !i.disabled).type = 'password'; } });
  const p = JSON.parse(sheet.A.scrape()).elements.find(e => e.secret);
  assert.ok(p, 'the password field on LOADSHEET is read');
  assert.match(p.text, /, hidden, (empty|1 character|\d+ characters)$/);
});

test('a hidden ID still takes a value, typed key by key', () => {
  const { window, A } = load('opt_navigraph_login', { before: streamerOn });
  const f = JSON.parse(A.scrape()).elements.find(e => e.key === 'field:HOPPIE LOGON');
  assert.equal(A.setValue(f.idx, 'ABCD'), 'ok');
  assert.equal(inputOf(window, A, f).value, 'ABCD');
});
