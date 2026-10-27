'use strict';
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-a350-ois-agent.js');

// Loads a fixture (an OIS page captured live with capture.js, see README.md) and the agent into
// a jsdom window. data-vis="0" marks an element the OIS drew with no size (a hidden list, a page
// kept in the DOM); everything else measures 100 x 20.
function load(fixtureName, opts) {
  opts = opts || {};
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', fixtureName + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html><body>' + html + '</body></html>');
  const { window } = dom;
  global.window = window; global.document = window.document;
  window.Element.prototype.getBoundingClientRect = function () {
    const r = this.getAttribute('data-rect');   // "top,left,right,bottom", for a test that places one
    if (r) { const p = r.split(',').map(Number); return { top: p[0], left: p[1], right: p[2], bottom: p[3], width: p[2] - p[1], height: p[3] - p[0], x: p[1], y: p[0] }; }
    const zero = this.getAttribute('data-vis') === '0';
    return { top: 0, left: 0, right: zero ? 0 : 100, bottom: zero ? 0 : 20, width: zero ? 0 : 100, height: zero ? 0 : 20, x: 0, y: 0 };
  };
  // jsdom has no innerText; Coherent's reads like textContent here (a <br> adds nothing).
  Object.defineProperty(window.HTMLElement.prototype, 'innerText', { get() { return this.textContent; } });
  if (opts.before) opts.before(window);
  window.eval(fs.readFileSync(AGENT, 'utf8'));
  return { window, A: window.__MSFSBA_A350_OIS };
}

function scrape(fixtureName, opts) {
  const { A } = load(fixtureName, opts);
  return JSON.parse(A.scrape()).elements;
}

module.exports = { load, scrape };
