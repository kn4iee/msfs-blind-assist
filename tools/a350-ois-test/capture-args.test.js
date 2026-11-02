// capture-run.mjs takes its view, fixture name, folder and port from its arguments or the environment (plan 06 Task 5), and does
// the clean-up the README's sed did, so a fixture is captured the same way from any shell and any folder.
const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');
const { parse, clean } = require('./capture-args.js');

test('a capture writes to this folder\'s fixtures through port 19999 by default', () => {
  const a = parse(['ois:fo', 'ground_v128'], {});
  assert.equal(a.needle, 'ois:fo');
  assert.equal(a.script, path.join(__dirname, 'capture.js'));
  assert.equal(a.file, path.join(__dirname, 'fixtures', 'ground_v128.html'));
  assert.equal(a.port, '19999');
});

test('the folder and the port come from the environment', () => {
  const a = parse(['ois:cpt', 'menu_v128'], { A350_FIXTURES: path.join('D:', 'caps'), COHERENT_PORT: '19998' });
  assert.equal(a.file, path.join('D:', 'caps', 'menu_v128.html'));
  assert.equal(a.port, '19998');
});

test('arguments win over the environment', () => {
  const a = parse(['--out', 'here', 'ois:cpt', 'menu_v128', '--port', '20000'], { A350_FIXTURES: 'there', COHERENT_PORT: '19998' });
  assert.equal(a.file, path.join('here', 'menu_v128.html'));
  assert.equal(a.port, '20000');
});

test('the FAP is captured with its own script from its own view', () => {
  const a = parse(['fap', 'fap_doors_v128'], {});
  assert.equal(a.needle, 'iniFAPA350');
  assert.equal(a.script, path.join(__dirname, 'capture-fap.js'));
  assert.equal(a.file, path.join(__dirname, 'fixtures', 'fap_doors_v128.html'));
});

test('a wrong view, a name that is not a plain fixture name, or a port that is not a number is refused with the usage', () => {
  for (const argv of [[], ['ois:cpt'], ['VCockpit01', 'x'], ['ois:cpt', '../x'], ['ois:cpt', 'a b'], ['ois:cpt', 'x', 'y'],
    ['ois:cpt', 'x', '--port', 'abc'], ['ois:cpt', 'x', '--out']])
    assert.throws(() => parse(argv, {}), /usage: node capture-run\.mjs/, JSON.stringify(argv));
});

test('an OIS capture drops a previous agent\'s labels and the glass-out markers, as the README\'s sed did', () => {
  const html = '<div data-a350-ois-label="GPU" class="row"><span class="__glassout_marker_inst" data-x="1">GPU</span></div>';
  assert.equal(clean(html, 'ois:fo'), '<div class="row"><span>GPU</span></div>\n');
});

test('a FAP capture is kept as captured', () => {
  const html = '<div id="iniFAP" data-a350-ois-label="kept">x</div>';
  assert.equal(clean(html, 'fap'), html + '\n');
});
