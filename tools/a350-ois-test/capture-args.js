'use strict';
// capture-run.mjs's arguments and its clean-up, as functions capture-args.test.js can call (plan 06 Task 5).
const path = require('path');

const USAGE = 'usage: node capture-run.mjs <ois:cpt | ois:fo | fap> <fixture name> [--out <folder>] [--port <port>]\n'
  + '  the folder defaults to A350_FIXTURES, else this folder\'s fixtures; the port to COHERENT_PORT, else 19999';

/** The capture asked for: which view, which script, which file, which port. Throws the usage on anything else. */
function parse(argv, env) {
  let out = env.A350_FIXTURES || path.join(__dirname, 'fixtures');
  let port = env.COHERENT_PORT || '19999';
  const rest = [];
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--out' || argv[i] === '--port') {
      const v = argv[++i];
      if (v === undefined) throw new Error(USAGE);
      if (argv[i - 1] === '--out') out = v; else port = v;
    } else rest.push(argv[i]);
  }
  const [view, name] = rest;
  if (rest.length !== 2 || !/^(ois:(cpt|fo)|fap)$/.test(view) || !/^[A-Za-z0-9_-]+$/.test(name) || !/^\d+$/.test(port))
    throw new Error(USAGE);
  const fap = view === 'fap';
  return {
    view,
    needle: fap ? 'iniFAPA350' : view,
    script: path.join(__dirname, fap ? 'capture-fap.js' : 'capture.js'),
    file: path.join(out, name + '.html'),
    port,
  };
}

/** The fixture's text: an OIS page loses a previous agent's labels and the glass-out markers (the OIS as first seen). */
function clean(html, view) {
  if (view !== 'fap')
    html = html.replace(/ data-a350-ois-label="[^"]*"/g, '').replace(/ class="__glassout_marker_inst"[^>]*>/g, '>');
  return html + '\n';
}

module.exports = { parse, clean, USAGE };
