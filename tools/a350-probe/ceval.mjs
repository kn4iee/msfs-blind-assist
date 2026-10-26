// usage: node ceval.mjs <titleNeedle> <exprFile|-e expr> [index]
import fs from 'fs';
const [needle, a2, a3, a4] = process.argv.slice(2);
let expr, idx = 0;
if (a2 === '-e') { expr = a3; idx = +(a4 || 0); } else { expr = fs.readFileSync(a2, 'utf8'); idx = +(a3 || 0); }
const pages = await (await fetch('http://127.0.0.1:19999/pagelist.json')).json();
const hits = pages.filter(p => p.title.includes(needle));
if (!hits.length) { console.log('no view', needle); process.exit(1); }
const page = hits[idx];
const ws = new WebSocket(`ws://127.0.0.1:19999/devtools/inspector/${page.id}`);
const t = setTimeout(() => { console.log('TIMEOUT'); process.exit(2); }, 15000);
ws.onopen = () => ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression: expr, returnByValue: true, awaitPromise: true } }));
ws.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id !== 1) return; clearTimeout(t);
  const r = d.result && d.result.result; 
  if (d.result && d.result.wasThrown) console.log('THROWN', JSON.stringify(r));
  else console.log(r && typeof r.value === 'string' ? r.value : JSON.stringify(r && r.value !== undefined ? r.value : d));
  ws.close(); process.exit(0); };
ws.onerror = (e) => { console.log('ERR', e.message); process.exit(3); };
