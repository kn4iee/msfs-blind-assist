const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const find = (els, re) => els.find(e => re.test(e.text || ''));

// GROUND's door map: each door is a clickspot whose colour is its state, keyed on the page (green
// Closed, blue Armed, red Open, orange Transit). Captured with the passenger doors armed, the cargo
// doors closed.
test('each door reads as a button with its state', () => {
  const els = scrape('ground');
  for (const d of ['L1', 'R1', 'L2', 'R2', 'L3', 'R3', 'L4', 'R4']) {
    const b = find(els, new RegExp('^Door ' + d + ': '));
    assert.ok(b && b.kind === 'button', 'missing door ' + d);
    assert.equal(b.text, 'Door ' + d + ': Armed');
    assert.ok(b.announceChange, 'a press moves the door: its new state is spoken');
  }
  for (const c of ['Forward', 'Aft', 'Bulk'])
    assert.ok(els.some(e => e.kind === 'button' && e.text === c + ' cargo door: Closed'), 'missing ' + c);
});

test('the cargo buttons beside the map carry their door state', () => {
  const els = scrape('ground');
  for (const t of ['FWD CARGO: Closed', 'AFT CARGO: Closed', 'BULK CARGO: Closed'])
    assert.ok(els.some(e => e.kind === 'button' && e.text === t), 'missing ' + t);
});

test('CHOCKS says whether the chocks are in place', () => {
  const b = find(scrape('ground'), /^CHOCKS/);
  assert.equal(b.text, 'CHOCKS: Removed');
});

// GROUND EQUIP: a piece of equipment at the aircraft is painted darkorange (live: GPU and CHOCKS with
// the GPU plugged in and the chocks placed), the rest unpainted.
test('each ground equipment button says whether it is connected', () => {
  const els = scrape('ground', { before: w => {
    for (const b of w.document.querySelectorAll('button'))
      if (b.textContent === 'GPU' || b.textContent === 'JETWAY') b.style.backgroundColor = 'darkorange';
  } });
  const b = t => els.some(e => e.kind === 'button' && e.text === t);
  assert.ok(b('GPU: Connected'), 'GPU connected');
  assert.ok(b('JETWAY: Connected'), 'jetway connected');
  for (const t of ['ASU', 'ACU', 'RAMP TRUCK', 'LUGGAGE', 'CATERING'])
    assert.ok(b(t + ': Not connected'), t + ' not connected');
  const gpu = els.find(e => e.text === 'GPU: Connected');
  assert.ok(gpu.announceChange, 'a press connects or removes it: its new state is spoken');
});

test('the pushback arrows are named', () => {
  const els = scrape('ground');
  for (const t of ['Push back, turn left', 'Push back, turn right', 'Push back straight', 'STOP'])
    assert.ok(els.some(e => e.kind === 'button' && e.text === t), 'missing ' + t);
  assert.ok(!els.some(e => e.kind === 'button' && !e.text), 'no nameless button');
});

// A switch and a slider outside the rows: the switch takes its <label for>, the slider the title above.
test('the GSX switch and the snow slider are labelled', () => {
  const els = scrape('ground');
  const gsx = els.find(e => e.controlType === 'checkbox');
  assert.equal(gsx.text, 'ENABLE GSX CONTROL');
  const snow = els.find(e => e.controlType === 'range');
  assert.equal(snow.text, 'COLD WEATHER ACCUMULATION');
  assert.ok(!els.some(e => e.kind === 'heading' && /ENABLE GSX|COLD WEATHER/.test(e.text)), 'read once, as the control');
});

// A light or the covers lights green when on (inline darkgreen); the avionics bay door goes the key's
// red when open. The two AVIONICS BAY buttons say which is which.
test('the lights and covers say on or off, the avionics door open or closed', () => {
  const els = scrape('ground_light_on');
  const b = t => els.some(e => e.kind === 'button' && e.text === t);
  assert.ok(b('CARGO BAY light: On'), 'cargo bay light on');
  assert.ok(b('AVIONICS BAY light: Off'));
  assert.ok(b('COVERS: Off'));
  assert.ok(b('AVIONICS BAY door: Closed'));
});

test('an open avionics bay door reads Open', () => {
  const els = scrape('ground', { before: w => {
    const b = [...w.document.querySelectorAll('.doors-sidebar button')].find(x => x.textContent === 'AVIONICS BAY');
    b.style.backgroundColor = 'rgb(153, 0, 0)';
  } });
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'AVIONICS BAY door: Open'));
});

// Review 2026-10-29 (I2): a set command whose index now names a button (the agent re-stamps every element on each scrape,
// and the shell learns the new indices a push later) went to typing, whose first act is a press: on GROUND it placed the
// chocks, moved a door or connected the GPU, then failed. setValue acts only on fields and choices; anything else is
// "not a field", untouched.
test('a set command on a button presses nothing', () => {
  const { load } = require('./run');
  const { window, A } = load('ground');
  const els = JSON.parse(A.scrape()).elements;
  let clicks = 0;
  window.document.addEventListener('click', () => clicks++, true);
  window.document.addEventListener('mousedown', () => clicks++, true);
  for (const b of els.filter(e => e.kind === 'button').slice(0, 5))
    assert.equal(A.setValue(b.idx, '1'), 'not a field', b.text);
  assert.equal(clicks, 0);
});

// [A350-6] (review 2026-10-29, I7: nothing exercised it) A field is typed as key events per character, then Enter: a bare
// value set shows in the field but COMPUTE ignores it.
test('a field is typed key by key, then Enter', () => {
  const { load } = require('./run');
  const { window, A } = load('loadsheet');
  const field = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'text' && !e.disabled);
  assert.ok(field, 'a field on LOADSHEET');
  const input = window.document.querySelector('[' + A.ATTR + '="' + field.idx + '"]');
  const seen = [];
  for (const type of ['keydown', 'keypress', 'input', 'keyup', 'change'])
    input.addEventListener(type, ev => seen.push(type + (ev.key ? ':' + ev.key : '')));
  assert.equal(A.setValue(field.idx, '12'), 'ok');
  assert.deepEqual(seen, ['input', 'keydown:1', 'keypress:1', 'input', 'keyup:1', 'keydown:2', 'keypress:2', 'input', 'keyup:2',
    'keydown:Enter', 'keyup:Enter', 'change']);
  assert.equal(input.value, '12');
});

// The OIS listens on a door's indicator, not its clickspot (live: a press on the clickspot did nothing).
test('pressing a door presses its indicator', () => {
  const { load } = require('./run');
  const { window, A } = load('ground');
  const ind = window.document.querySelector('#door-fwd-cargo .door-indicator');
  let clicks = 0;
  ind.addEventListener('click', () => clicks++);
  const door = JSON.parse(A.scrape()).elements.find(e => e.text === 'Forward cargo door: Closed');
  assert.equal(A.clickElement(door.idx), 'ok');
  assert.equal(clicks, 1);
});
