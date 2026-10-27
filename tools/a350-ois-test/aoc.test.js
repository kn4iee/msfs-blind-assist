const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const has = (els, kind, text) => els.some(e => e.kind === kind && e.text === text);

// COMPANY COM (AOC): a folder tree, the folder's messages, the open message.
test('the AOC tree: folders are headings, boxes are buttons with the open one selected', () => {
  const els = scrape('aoc');
  for (const h of ['INBOXES', 'REQUESTS', 'REPORTS']) assert.ok(has(els, 'heading', h), 'heading ' + h);
  for (const b of ['INBOX (selected)', 'OPS', 'METAR', 'SENT', 'LOADSHEET', 'ADC DELAY', 'DIVERSION', 'CLEAR ALL'])
    assert.ok(has(els, 'button', b), 'button ' + b);
});

test('a message is a button with its time and whether it is new', () => {
  const els = scrape('aoc');
  assert.ok(has(els, 'button', 'FLIGHTREPORT, 1016Z, new'));
  assert.ok(!els.some(e => /^(MSG|ZULU|NEW)$/.test(e.text || '')), 'no column headings');
  assert.ok(has(scrape('aoc_message'), 'button', 'FLIGHTREPORT, 1016Z'), 'read: no longer new');
});

test('the open message reads line by line', () => {
  const els = scrape('aoc_message');
  for (const l of ['FROM AOC', 'RECVD 1016', 'FLIGHTREPORT', 'AIRFRAME: A350-900', 'OUT: 0819Z', 'BLOCK TIME: 01:57', 'TOUCHDOWN VS: -1.00 FT/MN'])
    assert.ok(has(els, 'static', l), 'line ' + l);
  assert.ok(!els.some(e => /^[-.]+$/.test(e.text || '')), 'no separator lines');
});
