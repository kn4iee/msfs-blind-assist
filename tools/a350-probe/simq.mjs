import { createEmulator } from './emu2.mjs';
const emu = await createEmulator(); emu.load(process.argv[2]);
for (const g of process.argv.slice(3)) {
  const [cls, n = 0] = g.split('#'); const o = emu.findObjects(cls)[+n];
  emu.simq.log.clear(); emu.restore();
  emu.draw('emu_' + cls + '_Draw', ...((cls === 'SFD' || cls === 'SND') ? [o, 0] : [o]));
  console.log('==', g, [...emu.simq.log].join('\n   '));
}
