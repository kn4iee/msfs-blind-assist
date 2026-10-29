// The texts a gauge draws holding characters outside printable ASCII (or empty), each with its position and codes:
// how a symbol drawn from the font (an arrow, a degree sign) shows up. usage: node rawtext.mjs copy.bin Gauge[#n]
import { createEmulator } from './emu2.mjs';
const emu = await createEmulator(); emu.load(process.argv[2]); emu.scanCustomVars();
const [cls, n = 0] = process.argv[3].split('#'); const o = emu.findObjects(cls)[+n];
if (o === undefined) { console.log(`NO ${cls} OBJECT #${n} in this copy`); process.exit(1); }
const r = emu.draw('emu_' + cls + '_Draw', ...((cls === 'SFD' || cls === 'SND') ? [o, 0] : [o]));
if (r.error) { console.log(`ERROR (a torn copy traps: take another) ${r.error.message}`); process.exit(1); }
for (const t of r.texts) if (/[^\x20-\x7e]/.test(t.text) || t.text === '') console.log(t.x.toFixed(0), t.y.toFixed(0), JSON.stringify(t.text), [...t.text].map(c => c.charCodeAt(0).toString(16)).join(' '));
