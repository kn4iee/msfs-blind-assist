// Writes L:vars through an OIS view's SimVar API, one write each (like a native SimConnect write).
// usage: node lset.mjs NAME=value [wait300] ...   (A350_OIS_SIDE=fo for the other OIS view)
// It opens an inspector socket on the OIS view: close MSFSBA's OIS window (or MSFSBA) first [DBG-9].
import { oisView, open } from './coherent.mjs';

try {
  const s = await open(await oisView(process.env.A350_OIS_SIDE || 'cpt'));
  try {
    for (const a of process.argv.slice(2)) {
      if (a.startsWith('wait')) { await new Promise(r => setTimeout(r, +a.slice(4))); continue; }
      const [k, v] = a.split('=');
      if (v === undefined || !Number.isFinite(+v)) throw new Error(`"${a}": write NAME=number`);
      await s.eval(`SimVar.SetSimVarValue('L:${k}','number',${+v})`);
    }
  } finally { s.close(); }
  process.exit(0);
} catch (e) { console.error('lset.mjs:', e.message); process.exit(1); }
