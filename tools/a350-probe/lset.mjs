// Write L:vars through the EFB view's SimVar API (one write each, like a native SimConnect write).
// usage: node lset.mjs NAME=value [wait300] ...
const pages = await (await fetch('http://127.0.0.1:19999/pagelist.json')).json();
const page = pages.find(p => p.title.includes('VCockpit10 - iniEfbA350'));
const ws = new WebSocket(`ws://127.0.0.1:19999/devtools/inspector/${page.id}`);
let id = 0; const pend = {};
const ev = (expr) => new Promise(res => { const i = ++id; pend[i] = res; ws.send(JSON.stringify({ id: i, method: 'Runtime.evaluate', params: { expression: expr, returnByValue: true } })); });
ws.onmessage = m => { const d = JSON.parse(m.data); if (pend[d.id]) { pend[d.id](d.result && d.result.result && d.result.result.value); delete pend[d.id]; } };
await new Promise(r => ws.onopen = r);
for (const a of process.argv.slice(2)) {
  if (a.startsWith('wait')) { await new Promise(r => setTimeout(r, +a.slice(4))); continue; }
  const [k, v] = a.split('='); await ev(`SimVar.SetSimVarValue('L:${k}','number',${v})`);
}
ws.close(); process.exit(0);
