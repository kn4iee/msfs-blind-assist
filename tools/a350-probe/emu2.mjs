// Whole-cockpit draw emulator (A350 1.2.7+). Runs any WASM gauge's own Draw() on a COPY of the A350's
// memory and captures every text it draws, with position and fill colour. Never touches the sim.
// usage: node emu2.mjs <dump.bin> <Gauge>[#n] ...   e.g.  node emu2.mjs linmem0.bin CTR EFIS#0 EFIS#1 FCU
import fs from 'fs';

const here = (f) => new URL('./' + f, import.meta.url);
const sigs = JSON.parse(fs.readFileSync(here('importsigs.json')));
const VT = JSON.parse(fs.readFileSync(here('vtables.json')));

export let EXTRA_MB = +(process.env.EMU_EXTRA_MB || 64);
const COLORS = [
  ['green', [0, 1, 0]], ['amber', [1, 0.6, 0]], ['cyan', [0, 1, 1]], ['magenta', [1, 0, 1]],
  ['white', [1, 1, 1]], ['red', [1, 0, 0]], ['yellow', [1, 1, 0]], ['grey', [0.5, 0.5, 0.5]], ['black', [0, 0, 0]],
];
export function colorName(c) {
  if (!c) return '?';
  let best = '?', bd = 1e9;
  for (const [n, [r, g, b]] of COLORS) { const d = (c[0] - r) ** 2 + (c[1] - g) ** 2 + (c[2] - b) ** 2; if (d < bd) { bd = d; best = n; } }
  return best;
}

export async function createEmulator(modulePath = here('a350-all.wasm')) {
  const t0 = performance.now();
  const mod = await WebAssembly.compile(fs.readFileSync(modulePath));
  const calls = new Map();
  let texts = [], frames = [], elements = [];
  const UI = JSON.parse(fs.readFileSync(here('ui_draws.json'))).map(s => s.split('::')[0]);
  let mem;
  const fill = new Map(), stack = new Map(), xf = new Map();
  const f32 = (u) => { const b = new DataView(new ArrayBuffer(4)); b.setUint32(0, u >>> 0, true); return b.getFloat32(0, true); };
  const imports = {};
  // Optional answers for calls the draw code makes to the sim (aircraft vars, named vars, calculator code).
  const answers = { avar: () => null, lvarById: () => null };
  const simq = { avars: new Map(), units: new Map(), log: new Set() };
  for (const imp of WebAssembly.Module.imports(mod)) {
    imports[imp.module] ??= {};
    const res = (sigs[imp.name] || { results: [] }).results;
    const zero = res.length === 0 ? undefined : res[0] === 126 ? 0n : 0;
    if (imp.name === 'path_readlink') {
      imports[imp.module][imp.name] = (a, b, c, d, e, magic) => {
        const dv = new DataView(mem.buffer);
        const X = (ctx) => xf.get(ctx) || [1, 0, 0, 1, 0, 0];
        const mul = (ctx, m) => { const t = X(ctx); xf.set(ctx, [m[0] * t[0] + m[1] * t[2], m[0] * t[1] + m[1] * t[3], m[2] * t[0] + m[3] * t[2], m[2] * t[1] + m[3] * t[3], m[4] * t[0] + m[5] * t[2] + t[4], m[4] * t[1] + m[5] * t[3] + t[5]]); };
        if (magic === 0x7E57) {
          const u8 = new Uint8Array(mem.buffer);
          let end = b >>> 0; if (!end) { end = a; while (u8[end]) end++; }
          const lx = f32(d), ly = f32(e), t = X(c);
          texts.push({ text: Buffer.from(u8.subarray(a >>> 0, end)).toString('latin1'), x: lx * t[0] + ly * t[2] + t[4], y: lx * t[1] + ly * t[3] + t[5], lx, ly, ctx: c, color: fill.get(c) });
        } else if (magic === 0x7E58) {
          const p = b >>> 0; fill.set(a, [dv.getFloat32(p, true), dv.getFloat32(p + 4, true), dv.getFloat32(p + 8, true), dv.getFloat32(p + 12, true)]);
        } else if (magic === 0x7E59) {
          if (!stack.has(a)) stack.set(a, []); stack.get(a).push([fill.get(a), X(a)]);
        } else if (magic === 0x7E5A) {
          const s = stack.get(a); if (s && s.length) { const [f, m] = s.pop(); fill.set(a, f); xf.set(a, m); }
        } else if (magic === 0x7E5B) { mul(a, [1, 0, 0, 1, f32(b), f32(c)]);
        } else if (magic === 0x7E5C) { mul(a, [f32(b), 0, 0, f32(c), 0, 0]);
        } else if (magic === 0x7E5D) { const r = f32(b), cs = Math.cos(r), sn = Math.sin(r); mul(a, [cs, sn, -sn, cs, 0, 0]);
        } else if (magic === 0x7E5E || magic === 0x7E5F) { xf.set(a, [1, 0, 0, 1, 0, 0]);
        } else if (magic === 0x7E61) { const t = X(a), x = f32(b), y = f32(c); frames.push({ x: x * t[0] + y * t[2] + t[4], y: x * t[1] + y * t[3] + t[5], w: f32(d), h: f32(e) });
        } else if (magic === 0x7E62) { const p = b >>> 0, f = frames[frames.length - 1]; if (f) { f.wide = !!c; } if (f) f.color = [dv.getFloat32(p, true), dv.getFloat32(p + 4, true), dv.getFloat32(p + 8, true), dv.getFloat32(p + 12, true)];
        } else if (magic === 0x7E65) {
          const el = a >>> 0, t = X(b);
          const r = [dv.getFloat32(el + 4, true), dv.getFloat32(el + 8, true), dv.getFloat32(el + 12, true), dv.getFloat32(el + 16, true)];
          elements.push({ ptr: el, cls: UI[c], rect: r, x: r[0] * t[0] + r[1] * t[2] + t[4], y: r[0] * t[1] + r[1] * t[3] + t[5], textStart: texts.length });
        } else if (magic === 0x7E60) { calls.set('nvgTransform', (calls.get('nvgTransform') || 0) + 1);
        } else { calls.set(imp.name, (calls.get(imp.name) || 0) + 1); }
        return 0;
      };
    } else if (imp.name === 'get_aircraft_var_enum' || imp.name === 'get_units_enum' || imp.name === 'fsVarsGetUnitId' || imp.name === 'fsVarsGetAircraftVarId') {
      // Name -> id: hand out our own ids and remember the names, so value requests can be answered.
      imports[imp.module][imp.name] = (p) => { const nm = cstr(p); const tbl = imp.name.includes('nit') ? simq.units : simq.avars; if (!tbl.has(nm)) tbl.set(nm, tbl.size + 1); return tbl.get(nm); };
    } else if (imp.name === 'aircraft_varget') {
      imports[imp.module][imp.name] = (v, u, i) => { const key = name(simq.avars, v) + ':' + i + '|' + name(simq.units, u); simq.log.add('A ' + key); return answers.avar(key) ?? 0; };
    } else if (imp.name === 'fsVarsNamedVarGet') {
      imports[imp.module][imp.name] = (id, u, out) => { simq.log.add('L#' + id); const v = answers.lvarById(id); if (v != null && out) new DataView(mem.buffer).setFloat64(out >>> 0, v, true); };
    } else if (imp.name === 'execute_calculator_code') {
      imports[imp.module][imp.name] = (p) => { simq.log.add('CALC ' + cstr(p)); return 0; };
    } else {
      imports[imp.module][imp.name] = () => { calls.set(imp.name, (calls.get(imp.name) || 0) + 1); return zero; };
    }
  }
  function cstr(p) { const u8 = new Uint8Array(mem.buffer); let e = p >>> 0; while (u8[e]) e++; return Buffer.from(u8.subarray(p >>> 0, e)).toString('latin1'); }
  function name(tbl, id) { for (const [k, v] of tbl) if (v === id) return k; return '?' + id; }
  const inst = await WebAssembly.instantiate(mod, imports);
  mem = inst.exports.memory;
  const compileMs = performance.now() - t0;
  let image = null;

  function load(dumpPath) {
    const buf = fs.readFileSync(dumpPath);
    let maxEnd = 0;
    for (let i = 0; i < buf.length;) { const lin = buf.readUInt32LE(i), len = buf.readUInt32LE(i + 4); maxEnd = Math.max(maxEnd, lin + len); i += 8 + len; }
    const need = Math.ceil((maxEnd + (EXTRA_MB << 20)) / 65536), have = mem.buffer.byteLength / 65536;
    if (need > have) mem.grow(need - have);
    image = buf;
    restore();
  }
  // Put the copy back exactly as captured (a draw may change state in the copy).
  function restore() {
    const u8 = new Uint8Array(mem.buffer);
    for (let i = 0; i < image.length;) { const lin = image.readUInt32LE(i), len = image.readUInt32LE(i + 4); u8.set(image.subarray(i + 8, i + 8 + len), lin); i += 8 + len; }
  }

  // CustomVariable objects: {u32 id, std::string name, f64 cachedValue@+16}. Answer L:var reads from the copy.
  let cvars = null;
  function scanCustomVars() {
    cvars = new Map();
    const d = new DataView(mem.buffer), u8 = new Uint8Array(mem.buffer);
    for (let i = 0; i < image.length;) {
      const lin = image.readUInt32LE(i), len = image.readUInt32LE(i + 4);
      for (let o = 0; o + 24 <= len; o += 4) {
        const a = lin + o, id = d.getUint32(a, true);
        if (id < 1 || id > 65535) continue;
        const s = a + 4, longf = u8[s + 11] & 0x80;
        let p, n;
        if (longf) { p = d.getUint32(s, true); n = d.getUint32(s + 4, true); if (n < 4 || n > 80 || p < 1024 || p + n > u8.length) continue; }
        else { p = s; n = u8[s + 11]; if (n < 4 || n > 10) continue; }
        if (u8[p] !== 73 && !(u8[p] >= 65 && u8[p] <= 90)) continue;   // starts with a capital letter
        let ok = true; for (let k = 0; k < n; k++) { const c = u8[p + k]; if (!((c >= 48 && c <= 57) || (c >= 65 && c <= 90) || c === 95 || c === 58)) { ok = false; break; } }
        if (!ok) continue;
        if (!cvars.has(id)) cvars.set(id, { name: Buffer.from(u8.subarray(p, p + n)).toString('latin1'), addr: a });
      }
      i += 8 + len;
    }
    answers.lvarById = (id) => { const c = cvars.get(id); return c ? new DataView(mem.buffer).getFloat64(c.addr + 16, true) : null; };
    return cvars;
  }

  function findObjects(cls) {
    const vts = Object.entries(VT).filter(([, c]) => c === cls).map(([a]) => +a);
    const out = [];
    for (let i = 0; i < image.length;) {
      const lin = image.readUInt32LE(i), len = image.readUInt32LE(i + 4);
      for (let o = 0; o + 4 <= len; o += 4) if (vts.includes(image.readUInt32LE(i + 8 + o))) out.push(lin + o);
      i += 8 + len;
    }
    return out;
  }

  function draw(exportName, ...args) {
    texts = []; frames = []; elements = []; fill.clear(); stack.clear(); xf.clear();
    let error = null;
    const t = performance.now();
    try { inst.exports[exportName](...args); } catch (e) { error = e; }
    return { texts, frames, elements, error, ms: performance.now() - t };
  }

  return { inst, mem, load, restore, findObjects, scanCustomVars, draw, calls, compileMs, answers, simq, dv: () => new DataView(mem.buffer) };
}

export function toLines(texts, rowH = 12, withColor = true) {
  const rows = texts.filter(s => s.text.trim()).sort((a, b) => Math.round(a.y / rowH) - Math.round(b.y / rowH) || a.x - b.x);
  const lines = []; let line = [], ly = null;
  for (const s of rows) {
    const r = Math.round(s.y / rowH);
    if (ly !== null && r !== ly) { lines.push(line.join(' | ')); line = []; }
    line.push(withColor ? `${s.text.trim()}{${colorName(s.color)}}` : s.text.trim()); ly = r;
  }
  if (line.length) lines.push(line.join(' | '));
  return lines;
}

if (import.meta.url === `file:///${process.argv[1].replace(/\\/g, '/')}`) {
  const [dump, ...gauges] = process.argv.slice(2);
  const emu = await createEmulator();
  emu.load(dump);
  console.log('compile ms', emu.compileMs.toFixed(0));
  for (const g of gauges) {
    const [cls, nStr] = g.split('#');
    const objs = emu.findObjects(cls);
    const n = +(nStr || 0);
    if (!objs.length) { console.log(`== ${g}: no object`); continue; }
    emu.restore();
    const args = (cls === 'SFD' || cls === 'SND') ? [objs[n], 0] : [objs[n]];
    const r = emu.draw('emu_' + cls + '_Draw', ...args);
    console.log(`== ${g} @${objs[n]} (${objs.length} objs) ${r.texts.length} texts ${r.ms.toFixed(1)} ms${r.error ? ' ERROR ' + r.error.message : ''}`);
    for (const l of toLines(r.texts)) console.log('  ' + l);
  }
  console.log('stubbed calls:', JSON.stringify([...emu.calls]));
}
