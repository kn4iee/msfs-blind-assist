import { createEmulator, toLines } from './emu2.mjs';
const SDPAGE = 1451120;
const emu = await createEmulator(); emu.load(process.argv[2]);
const ctr = emu.findObjects('CTR')[0];
console.log('current SD page', emu.dv().getInt32(SDPAGE, true));
for (const p of process.argv.slice(3).map(Number)) {
  emu.restore(); emu.dv().setInt32(SDPAGE, p, true);
  const r = emu.draw('emu_CTR_Draw', ctr);
  const wdSet = globalThis.wdSet ??= null; const sd = r.texts;
  console.log(`===== SD page ${p}: ${sd.length} texts ${r.error ? 'ERR ' + r.error.message : ''}`);
  for (const l of toLines(sd)) console.log('  ' + l);
}
