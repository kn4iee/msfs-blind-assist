// Captures the page an OIS side or the FAP shows as a fixture, from any shell and any folder (README.md):
//   node capture-run.mjs <ois:cpt | ois:fo | fap> <fixture name> [--out <folder>] [--port <port>]
// It opens an inspector socket on the view: close MSFSBA's window on it (or MSFSBA) first [DBG-9].
import fs from 'fs';
import path from 'path';
import { createRequire } from 'module';

const require = createRequire(import.meta.url);
const { parse, clean } = require('./capture-args.js');

let a;
try { a = parse(process.argv.slice(2), process.env); } catch (e) { console.error(e.message); process.exit(1); }
process.env.COHERENT_PORT = a.port;   // coherent.mjs reads it when it loads
const { find, open } = await import('../a350-probe/coherent.mjs');

try {
  const s = await open(await find(a.needle));
  try {
    const html = await s.eval(fs.readFileSync(a.script, 'utf8'), 15000);
    if (typeof html !== 'string' || !html) throw new Error('the capture returned nothing');
    fs.mkdirSync(path.dirname(a.file), { recursive: true });
    fs.writeFileSync(a.file, clean(html, a.view));
    console.log(`${a.file}: ${html.length} characters. Read it before committing it; npm test runs secrets.test.js on it.`);
  } finally { s.close(); }
  process.exit(0);
} catch (e) { console.error('ERR', e.message); process.exit(3); }
