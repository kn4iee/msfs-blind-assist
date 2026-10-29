// Presses KCCU keys (or any momentary L:var) through an OIS view's SimVar API: 1, held 180 ms, then 0, 250 ms apart.
// usage: node press.mjs INI_MCDU1_INIT wait500 INI_MCDU1_V INI_MCDU1_ENTER   (A350_OIS_SIDE=fo for the other OIS view)
// It opens an inspector socket on the OIS view: close MSFSBA's OIS window (or MSFSBA) first [DBG-9].
import { oisView, open } from './coherent.mjs';

try {
  const s = await open(await oisView(process.env.A350_OIS_SIDE || 'cpt'));
  try {
    for (const k of process.argv.slice(2)) {
      if (k.startsWith('wait')) { await new Promise(r => setTimeout(r, +k.slice(4))); continue; }
      await s.eval(`SimVar.SetSimVarValue('L:${k}','number',1)`); await new Promise(r => setTimeout(r, 180));
      await s.eval(`SimVar.SetSimVarValue('L:${k}','number',0)`); await new Promise(r => setTimeout(r, 250));
    }
    console.log('page1=', await s.eval(`SimVar.GetSimVarValue('L:INI_FMC_PAGE1','number')`));
  } finally { s.close(); }
  process.exit(0);
} catch (e) { console.error('press.mjs:', e.message); process.exit(1); }
