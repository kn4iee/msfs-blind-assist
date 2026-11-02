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

// "ois:cpt" and "ois:fo" find their view by opening and closing a socket on it, and Coherent frees a page's one socket about half a second
// after the close (measured 2026-11-02 on 1.2.8): the open that follows straight away is refused, so it is tried again.
async function openWhenFree(page) {
  for (let i = 0; ; i++) {
    try { return await open(page); } catch (e) {
      if (i >= 5) throw e;
      await new Promise(r => setTimeout(r, 500));
    }
  }
}

try {
  const s = await openWhenFree(await find(a.needle));
  try {
    const html = await s.eval(fs.readFileSync(a.script, 'utf8'), 15000);
    if (typeof html !== 'string' || !html) throw new Error('the capture returned nothing');
    fs.mkdirSync(path.dirname(a.file), { recursive: true });
    fs.writeFileSync(a.file, clean(html, a.view));
    console.log(`${a.file}: ${html.length} characters. Read it before committing it; npm test runs secrets.test.js on it.`);
  } finally { s.close(); }
  process.exit(0);
} catch (e) { console.error('ERR', e.message); process.exit(3); }
