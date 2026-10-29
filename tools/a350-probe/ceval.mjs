// Runs JavaScript in a Coherent view and prints its value.
// usage: node ceval.mjs <titleNeedle | ois:cpt | ois:fo> <exprFile | -e expr> [index]
// "ois:cpt" / "ois:fo" find that side's OIS view by the page it loads [A350-8]; a title needle takes the index-th match.
// It opens an inspector socket on the view: close MSFSBA's window on it (or MSFSBA) first [DBG-9].
import fs from 'fs';
import { find, open } from './coherent.mjs';

const [needle, a2, a3, a4] = process.argv.slice(2);
let expr, idx = 0;
if (a2 === '-e') { expr = a3; idx = +(a4 || 0); } else { expr = fs.readFileSync(a2, 'utf8'); idx = +(a3 || 0); }
try {
  const s = await open(await find(needle, idx));
  try {
    const v = await s.eval(expr, 15000);
    console.log(typeof v === 'string' ? v : JSON.stringify(v));
  } finally { s.close(); }
  process.exit(0);
} catch (e) { console.log('ERR', e.message); process.exit(e.message.includes('no answer') ? 2 : 3); }
