const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

// FLT OPS CLEAR / TURNAROUND asks first: a fixed overlay with a title, a message and CANCEL / START.
// The window speaks it as it appears (one alert: title, then the message with its sentences spaced),
// reads it once, and keeps both buttons.
test('a confirmation dialog is one assertive alert with its buttons', () => {
  const els = scrape('menu_turnaround');
  const a = els.filter(e => e.kind === 'alert');
  assert.equal(a.length, 1);
  assert.equal(a[0].text, 'TURNAROUND START: Are you sure you want to start the turnaround? This will clear all flight related data. Options are not affected.');
  assert.equal(a[0].live, 'assertive');
  assert.ok(!els.some(e => e.kind !== 'alert' && /Are you sure|^TURNAROUND START$/.test(e.text || '')), 'read once');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'CANCEL'));
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'START'));
});

test('the dialog comes before the menu it covers', () => {
  const els = scrape('menu_turnaround');
  const ia = els.findIndex(e => e.kind === 'alert');
  const im = els.findIndex(e => e.text === 'FLT OPS PLAN');
  assert.ok(ia >= 0 && ia < im);
});

// The Navigraph notice on FLT OPS PLAN: its message shares a row with its button.
test('a notice reads its title and its message, then its button', () => {
  const els = scrape('plan_popup');
  const a = els.find(e => e.kind === 'alert' && /^Navigraph/.test(e.text));
  assert.equal(a.text, 'Navigraph Authentication Required: Connect your Navigraph account in OIS Settings to use SimBrief Planning integration.');
  assert.ok(!els.some(e => e.kind !== 'alert' && /^Connect your Navigraph/.test(e.text || '')));
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'OIS SETTINGS'));
});

// An options pop-up (FROM RWY): its title is spoken, then its choices in order.
test('an options pop-up reads its title, then its choices', () => {
  const els = scrape('plan_popup');
  const i = els.findIndex(e => e.kind === 'alert' && e.text === 'Select Departure Runway');
  assert.ok(i >= 0);
  assert.deepEqual(els.slice(i + 1, i + 4).map(e => e.text), ['AUTO', '04', '22']);
});
