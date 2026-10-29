// No fixture keeps the owner's credentials (review 2026-10-29: a capture of OPTIONS > LOGIN WITH NAVIGRAPH kept the
// SimBrief user ID and the Navigraph login code). capture.js blanks them; this fails if a fixture still has one.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const dir = path.join(__dirname, 'fixtures');
// The OIS's row labels are upper case; matched as shown text only (a window that starts inside a tag drops the partial
// tag, so an attribute such as id="..." is never read as a label).
const SENSITIVE = /USER ID|\bID\b|API KEY|\bKEY\b|LOGON|PASSWORD|TOKEN|SECRET|\bPIN\b/;
const DUMMY = /^(|0+|X+|0AAA000A)$/;

test('no fixture keeps a credential in a text field', () => {
  for (const f of fs.readdirSync(dir).filter(n => n.endsWith('.html'))) {
    const html = fs.readFileSync(path.join(dir, f), 'utf8');
    for (const m of html.matchAll(/<input\b[^>]*>/gi)) {
      const tag = m[0];
      const type = (/\btype="([^"]*)"/i.exec(tag) || [])[1] || 'text';
      if (/^(radio|checkbox|range|button|submit)$/i.test(type)) continue;
      const value = (/\bvalue="([^"]*)"/i.exec(tag) || [])[1] || '';
      const label = html.slice(Math.max(0, m.index - 300), m.index).replace(/^[^<]*>/, ' ').replace(/<[^>]*>/g, ' ');
      if (/^password$/i.test(type) || SENSITIVE.test(label))
        assert.match(value, DUMMY, `${f}: a field labelled as a credential keeps a value`);
    }
  }
});

test('no fixture keeps a Navigraph login code', () => {
  for (const f of fs.readdirSync(dir).filter(n => n.endsWith('.html'))) {
    const html = fs.readFileSync(path.join(dir, f), 'utf8');
    for (const m of html.matchAll(/Code:\s*(?:<[^>]*>\s*)*([0-9A-Z]{6,12})</g))
      assert.equal(m[1], '0AAA000A', `${f}: a login code is kept`);
  }
});
