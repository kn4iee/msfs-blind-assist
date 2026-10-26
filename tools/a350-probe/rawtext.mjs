import { createEmulator } from './emu2.mjs';
const emu = await createEmulator(); emu.load(process.argv[2]); emu.scanCustomVars();
const [cls, n = 0] = process.argv[3].split('#'); const o = emu.findObjects(cls)[+n];
const r = emu.draw('emu_' + cls + '_Draw', o);
for (const t of r.texts) if (/[^\x20-\x7e]/.test(t.text) || t.text === '') console.log(t.x.toFixed(0), t.y.toFixed(0), JSON.stringify(t.text), [...t.text].map(c => c.charCodeAt(0).toString(16)).join(' '));
