import { createEmulator, toLines } from './emu2.mjs';
const emu = await createEmulator(); emu.load(process.argv[2]);
emu.answers.avar = (k) => ({ 'KOHLSMAN SETTING HG:1|inches of mercury': 29.92, 'KOHLSMAN SETTING HG:2|inches of mercury': 29.92, 'KOHLSMAN SETTING HG:3|inches of mercury': 29.92, 'TURB ENG N2:1|Percent': 0, 'TURB ENG N2:2|Percent': 0 })[k];
const t = performance.now(); const cv = emu.scanCustomVars(); console.log('custom vars', cv.size, (performance.now() - t).toFixed(0), 'ms');
for (const g of process.argv.slice(3)) {
  const [cls, n = 0] = g.split('#'); const o = emu.findObjects(cls)[+n]; emu.restore();
  const r = emu.draw('emu_' + cls + '_Draw', ...((cls === 'SFD' || cls === 'SND') ? [o, 0] : [o]));
  console.log('==', g); for (const l of toLines(r.texts)) console.log('  ' + l);
}
