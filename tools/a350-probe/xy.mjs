import { createEmulator, colorName } from './emu2.mjs';
const emu = await createEmulator(); emu.load(process.argv[2]);
const [cls, n = 0] = process.argv[3].split('#');
const o = emu.findObjects(cls)[+n];
const r = emu.draw('emu_' + cls + '_Draw', ...((cls === 'SFD' || cls === 'SND') ? [o, 0] : [o]));
for (const t of r.texts) console.log(t.x.toFixed(0).padStart(5), t.y.toFixed(0).padStart(5), '(', t.lx.toFixed(0), t.ly.toFixed(0), ')', colorName(t.color).padEnd(7), JSON.stringify(t.text));
for (const f of r.frames) console.log('FRAME', f.x.toFixed(0), f.y.toFixed(0), f.w.toFixed(0), f.h.toFixed(0), colorName(f.color), f.wide ? 'WIDE' : '');
for (const e of r.elements) console.log('EL', e.cls.padEnd(22), 'ptr', e.ptr, 'rect', e.rect.map(v => v.toFixed(0)).join(','), 'abs', e.x.toFixed(0), e.y.toFixed(0), 'texts', JSON.stringify(r.texts.slice(e.textStart, e.textStart + 3).map(t => t.text)));
