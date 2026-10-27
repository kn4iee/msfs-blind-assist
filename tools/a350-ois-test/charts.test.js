const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const buttons = els => els.filter(e => e.kind === 'button').map(e => e.text);

// The chart and map pages have no page title: they are named by the bottom bar's active page.
test('a page without a title is named by the bottom bar', () => {
  const { load } = require('./run');
  for (const [f, page] of [['enroute', 'ENROUTE'], ['terml_chart', 'TERML CHART']]) {
    const { A } = load(f);
    assert.equal(JSON.parse(A.scrape()).page, page);
  }
});

// TERML CHART's controls are icons: each is named, none is dropped.
test('the chart icon buttons are named', () => {
  const b = buttons(scrape('terml_chart'));
  for (const n of ['Zoom in', 'Zoom out', 'Reset view', 'Chart list', 'Dark or light', 'Pan up', 'Pan left', 'Pan right', 'Pan down', 'Pinned'])
    assert.ok(b.includes(n), 'missing ' + n);
});

test('the chart search field is the airport', () => {
  const f = scrape('terml_chart').find(e => e.controlType === 'text');
  assert.equal(f.text, 'AIRP');
});

// ENROUTE: a map the OIS draws (Leaflet tiles): read as what it is. Its map-type panel is slid off
// the right of the screen until its tab is pressed, so only the tab reads.
test('the enroute map reads as a picture with named controls', () => {
  const els = scrape('enroute', { before: w => {
    const side = w.document.getElementById('enroutemap-sidebar');
    const tab = side.querySelector('button');
    for (const n of [side, ...side.querySelectorAll('*')]) if (n !== tab) n.setAttribute('data-rect', '100,1100,1400,900');
  } });
  assert.ok(els.some(e => e.kind === 'static' && e.text === 'Map picture (not readable)'));
  const b = buttons(els);
  for (const n of ['Map types', 'Recenter', 'Zoom in', 'Zoom out']) assert.ok(b.includes(n), 'missing ' + n);
  assert.ok(!b.includes('IFR High [Day]'), 'the hidden panel is not read');
  assert.ok(!b.some(t => /^[<>+-]$/.test(t)), 'no bare symbols');
});

// A searched airport's chart list: each chart is a pressable card (its title, then its type), with a
// pin button beside it.
test('the chart list reads as buttons with their type', () => {
  const els = scrape('terml_chart_list');
  const b = buttons(els);
  assert.ok(b.includes('RNAV STARs SAGOR 2A / IDIBI 2A, STAR'));
  assert.ok(b.includes('AFC'), 'a type equal to the title is not repeated');
  assert.ok(b.includes('Pin RNAV STARs SAGOR 2A / IDIBI 2A'));
  assert.ok(!els.some(e => e.kind === 'heading' && /RNAV/.test(e.text)), 'not headings');
  assert.ok(!els.some(e => e.kind === 'static' && /^(STAR|SID|AFC|APC)$/.test(e.text)), 'no loose types');
});
