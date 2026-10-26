// press KCCU keys via the EFB view's SimVar API (L:var 1 then 0, held ~150ms)
const keys = process.argv.slice(2);
const pages = await (await fetch('http://127.0.0.1:19999/pagelist.json')).json();
const page = pages.find(p => p.title.includes('VCockpit10 - iniEfbA350'));
const ws = new WebSocket(`ws://127.0.0.1:19999/devtools/inspector/${page.id}`);
let id = 0; const pend = {};
const ev = (expr) => new Promise(res => { const i = ++id; pend[i] = res; ws.send(JSON.stringify({ id: i, method: 'Runtime.evaluate', params: { expression: expr, returnByValue: true } })); });
ws.onmessage = m => { const d = JSON.parse(m.data); if (pend[d.id]) { pend[d.id](d.result && d.result.result && d.result.result.value); delete pend[d.id]; } };
await new Promise(r => ws.onopen = r);
for (const k of keys) {
  if (k.startsWith('wait')) { await new Promise(r => setTimeout(r, +k.slice(4))); continue; }
  await ev(`SimVar.SetSimVarValue('L:${k}','number',1)`); await new Promise(r => setTimeout(r, 180));
  await ev(`SimVar.SetSimVarValue('L:${k}','number',0)`); await new Promise(r => setTimeout(r, 250));
}
console.log('page1=', await ev(`SimVar.GetSimVarValue('L:INI_FMC_PAGE1','number')`));
ws.close(); process.exit(0);
