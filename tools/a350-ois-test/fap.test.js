const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

const fap = (name, opts) => scrape(name, Object.assign({ agent: 'fap' }, opts));
const texts = els => els.map(e => e.text);
const buttons = els => els.filter(e => e.kind === 'button').map(e => e.text);

test('the five tabs come first, the selected one said, with the local time after them', () => {
  const els = fap('fap_cabin');
  assert.deepEqual(buttons(els).slice(0, 5), ['Cabin Status tab (selected)', 'Lights tab', 'Temp tab', 'Doors/Slides tab', 'Water/Waste tab']);
  assert.match(els[5].text, /^Local time: \d\d:\d\d$/);
});

test('Cabin Status: its title, its buttons, its tiles and nothing read twice', () => {
  const els = fap('fap_cabin');
  const t = texts(els);
  assert.ok(t.includes('Cabin Status'));
  assert.ok(t.includes('Aircraft-wide Adjustment'));
  for (const b of ['Screen Off', 'Caution', 'Cabin Ready: Off', 'Lights page', 'Temp page', 'Doors/Slides page', 'Water/Waste page', 'Smoke Detect page'])
    assert.ok(buttons(els).includes(b), b);
  assert.equal(new Set(t).size, t.length, 'no line twice: ' + t.join(' | '));
});

test('a tile whose press does nothing says it has no page', () => {
  const els = fap('fap_cabin', { before: w => {
    const imgs = w.document.querySelectorAll('.col > img');
    imgs[4].onclick = () => {
      //this.props.fap.page.set("FAPTemp");
    };
  } });
  assert.ok(buttons(els).includes('Smoke Detect, has no page'));
});

test('Cabin Ready follows the sim, not the paint', () => {
  global.SimVar = { GetSimVarValue: () => 1 };
  try {
    const on = fap('fap_cabin');
    assert.ok(buttons(on).includes('Cabin Ready: On'));
    assert.equal(on.find(e => e.text === 'Cabin Ready: On').announceChange, true);
  } finally { delete global.SimVar; }
});

test('Lights: the Zones button opens the list, each zone with its level', () => {
  assert.ok(buttons(fap('fap_lights')).includes('Zones'));
  const b = buttons(fap('fap_lights_zones'));
  assert.ok(b.includes('Zones (selected)'), b.join(' | '));
  for (const z of ['Cabin: Off', 'Avionics Bay: Off', 'Cargo Bay: Off']) assert.ok(b.includes(z), z);
  assert.ok(b.includes('Night Mode, cabin now Off'));
  assert.ok(b.includes('Main On, cabin now Off'));
});

test('Lights: a picked zone is said and its brightness keys show, its level marked', () => {
  const els = fap('fap_lights_cabin');
  const b = buttons(els);
  assert.ok(b.includes('Cabin: Off (selected)'), b.join(' | '));
  for (const k of ['Bright', 'Medium Dim', 'Low Dim', 'Off (current)']) assert.ok(b.includes(k), k);
  assert.ok(texts(els).includes('Local'));
});

test('Lights: a key says its result after a press, and Night Mode and Main On the cabin they set', () => {
  const els = fap('fap_lights_cabin');
  for (const name of ['Bright', 'Off (current)', 'Night Mode, cabin now Off', 'Main On, cabin now Off'])
    assert.equal(els.find(e => e.text === name).announceChange, true, name);
  const { window, A } = load('fap_lights_cabin', { agent: 'fap' });
  const tile = [...window.document.querySelectorAll('.is-button')].find(e => e.textContent.startsWith('CabinOff'));
  tile.querySelector('.col-sm-8').lastChild.nodeValue = 'Bright';
  const b = buttons(JSON.parse(A.scrape()).elements);
  assert.ok(b.includes('Bright (current)'), b.join(' | '));
  assert.ok(b.includes('Off'));
  assert.ok(b.includes('Night Mode, cabin now Bright'));
});

test('a tile with no press handler is a read-out, once the handlers can be seen', () => {
  const live = w => {
    for (const b of w.document.querySelectorAll('.is-button')) b.onclick = () => 1;
    for (const b of w.document.querySelectorAll('.is-button')) {
      const t = b.textContent;
      if (t === 'Caution' || t === 'Zones' || /^Zone [ABC]/.test(t) || /^\d+ °C$/.test(t.trim())) b.onclick = null;
    }
  };
  const els = fap('fap_temp', { before: live });
  const b = buttons(els), t = texts(els);
  for (const dead of ['Caution', 'Zones (lit green)', 'Requested temperature: 22 °C']) {
    assert.ok(t.includes(dead), dead);
    assert.ok(!b.includes(dead), dead);
  }
  assert.ok(t.some(x => /^Zone A: \d+ °C$/.test(x)));
  assert.ok(!b.some(x => /^Zone A/.test(x)));
  assert.ok(b.includes('Requested temperature up, now 22 °C'));
  assert.ok(b.includes('Screen Off'));
});

test('Temp: the zones, the requested temperature and its two keys carrying the figure', () => {
  const els = fap('fap_temp');
  const b = buttons(els);
  assert.ok(b.some(x => /^Zone A: \d+ °C$/.test(x)), b.join(' | '));
  assert.ok(b.some(x => /^Zone B: \d+ °C$/.test(x)));
  assert.ok(b.some(x => /^Zone C: \d+ °C$/.test(x)));
  assert.ok(b.includes('Requested temperature: 22 °C'));
  assert.ok(b.includes('Requested temperature up, now 22 °C'));
  assert.ok(b.includes('Requested temperature down, now 22 °C'));
  assert.ok(b.includes('Zones (lit green)'));
  assert.ok(texts(els).some(x => /^Scale: 30°C, 24°C, 18°C/.test(x)));
});

test('Temp: the flight deck holding the temperature is said, in place of the keys', () => {
  const els = fap('fap_temp', { before: w => {
    const d = w.document;
    for (const n of d.querySelectorAll('.alert')) { n.removeAttribute('data-vis'); n.parentElement.removeAttribute('data-vis'); n.parentElement.parentElement.removeAttribute('data-vis'); }
    for (const n of d.querySelectorAll('.my-5, .my-5 *, .col-sm-5, .col-sm-5 *')) n.setAttribute('data-vis', '0');
  } });
  assert.ok(texts(els).includes('Temperature controlled by flightdeck, red'), texts(els).join(' | '));
  assert.ok(!buttons(els).some(x => x.startsWith('Requested temperature')));
});

test('Doors: every door by name with its state from the colour, and the legend', () => {
  const els = fap('fap_doors');
  const b = buttons(els);
  for (const d of ['Door 1 Left', 'Door 1 Right', 'Door 2 Left', 'Door 2 Right', 'Door 3 Left', 'Door 3 Right', 'Door 4 Left', 'Door 4 Right'])
    assert.ok(b.some(x => x.startsWith(d + ': ')), d);
  const t = texts(els);
  assert.ok(t.includes('Blue: Door: Locked, Slide: Armed'), t.join(' | '));
  assert.ok(t.includes('Green: Door: Locked, Slide: Disarmed'));
  assert.ok(t.includes('Amber: Moving'));
  assert.ok(t.includes('Red: Door: Open'));
  assert.ok(!t.includes('Door 1 Left'), 'the labels are said with the doors');
});

test('Doors: the colour says the state, and the press lands on the indicator', () => {
  const states = [['rgb(0, 255, 0)', 'locked, slide disarmed, green'], ['blue', 'locked, slide armed, blue'], ['orange', 'moving, amber'], ['rgb(153, 0, 0)', 'open, red']];
  for (const [colour, said] of states) {
    const { window, A } = load('fap_doors', { agent: 'fap' });
    const ind = window.document.querySelector('#door-l2 .door-indicator');
    ind.style.backgroundColor = colour;
    const els = JSON.parse(A.scrape()).elements;
    const door = els.find(e => e.text.startsWith('Door 2 Left: '));
    assert.equal(door.text, 'Door 2 Left: ' + said);
    assert.equal(door.announceChange, true);
    let pressed = 0;
    ind.addEventListener('click', () => pressed++);
    assert.equal(A.clickElement(door.idx), 'ok');
    assert.equal(pressed, 1);
  }
});

test('Water/Waste: the tanks by name with figure, colour and volume; the scale ticks are not lines', () => {
  const els = fap('fap_water');
  const t = texts(els);
  assert.ok(t.includes('Left: 0%, white, Max Volume 530 L'), t.join(' | '));
  assert.ok(t.includes('Right: 0%, white, Max Volume 530 L'));
  assert.ok(t.includes('Quantity: 100%, white'));
  assert.ok(t.includes('Waste Quantity'));
  assert.ok(t.includes('Water Quantity'));
  assert.ok(!t.includes('50%'));
  for (const k of ['Refill Water', 'Empty Waste']) assert.ok(buttons(els).includes(k), k);
});

test('a tank past its mark says the colour it is painted', () => {
  const { window, A } = load('fap_water', { agent: 'fap' });
  const bar = window.document.querySelectorAll('.row.p-3')[1].firstElementChild.firstElementChild;
  bar.style.background = 'orange';
  const t = texts(JSON.parse(A.scrape()).elements);
  assert.ok(t.includes('Quantity: 100%, amber'), t.join(' | '));
});

test('the screen off shows nothing else, and a press on it wakes the screen', () => {
  const { window, A } = load('fap_cabin', { agent: 'fap', before: w => { w.document.querySelector('#iniFAP > img').removeAttribute('data-vis'); } });
  const img = window.document.querySelector('#iniFAP > img');
  let els = JSON.parse(A.scrape());
  assert.equal(els.page, 'Screen off');
  assert.deepEqual(texts(els.elements), ['Screen off', 'Wake the screen']);
  let pressed = 0;
  img.addEventListener('click', () => pressed++);
  assert.equal(A.clickElement(els.elements[1].idx), 'ok');
  assert.equal(pressed, 1);
  img.setAttribute('data-vis', '0');
  els = JSON.parse(A.scrape());
  assert.equal(els.page, 'Cabin Status');
});

test('a toast is an alert while it is up, and gone once it slides off the top', () => {
  const { window, A } = load('fap_water', { agent: 'fap', before: w => {
    const d = w.document.createElement('div');
    d.className = 'notie-container';
    d.innerHTML = '<div class="notie-textbox-inner">Water tank refilled</div>';
    w.document.body.appendChild(d);
  } });
  const up = JSON.parse(A.scrape()).elements.filter(e => e.kind === 'alert');
  assert.deepEqual(up.map(e => e.text), ['Water tank refilled']);
  window.document.querySelector('.notie-container').setAttribute('data-rect', '-112,0,100,-20');
  assert.equal(JSON.parse(A.scrape()).elements.filter(e => e.kind === 'alert').length, 0);
});

test('no page shown: the tabs, and the screen said to be blank', () => {
  const { window, A } = load('fap_cabin', { agent: 'fap' });
  window.document.querySelector('#renderer > .visiblePage').className = 'hiddenPage';
  const r = JSON.parse(A.scrape());
  assert.equal(r.page, 'Blank');
  assert.ok(texts(r.elements).includes('The screen is blank: press a tab'));
  assert.ok(buttons(r.elements).includes('Lights tab'));
});

test('the page is not read again until it changes', async () => {
  const { window, A } = load('fap_cabin', { agent: 'fap', gate: true });
  assert.ok(JSON.parse(A.scrape()).elements);
  await new Promise(r => setTimeout(r, 0));
  assert.deepEqual(JSON.parse(A.scrape()), { ok: true, unchanged: true });
  window.document.querySelector('h1').textContent = 'Changed';
  await new Promise(r => setTimeout(r, 0));
  assert.ok(JSON.parse(A.scrape()).elements);
});

test('the FAP has no field to type into', () => {
  const { A } = load('fap_cabin', { agent: 'fap' });
  assert.equal(A.setValue(1, 'x'), 'not a field');
});
