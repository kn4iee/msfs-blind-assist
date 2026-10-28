const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const statics = els => els.filter(e => e.kind === 'static').map(e => e.text);

// OFP: the SimBrief OFP is a PRE (#ofpData, inside the PRE #ofpcontent) of 3145 lines whose breaks are newlines in its
// text, bold runs among them, and no BR (live 2026-10-28). It reads a line an item; split only at BR, the whole OFP was
// one 130 KB item.
test('the OFP reads line by line', () => {
  const s = statics(scrape('ofp'));
  assert.ok(s.includes('[ OFP ]'), s.slice(0, 5).join(' | '));
  assert.ok(s.includes('UAE0111 27OCT2026 VCBI-OMDB A359 GNXWB RELEASE 1943 27OCT26'));
  assert.ok(s.includes('MAXIMUM TOW 283000 LAW 207000 ZFW 196112 AVG W/C M009'));
  assert.ok(s.length > 1000, String(s.length));
  assert.ok(s.every(t => t.length < 300), 'no item runs lines together: ' + Math.max(...s.map(t => t.length)));
});

// A rule of dashes is decoration, never a line of its own.
test('the OFP drops its rules', () => {
  assert.ok(!statics(scrape('ofp')).some(t => /^-+$/.test(t)));
});
