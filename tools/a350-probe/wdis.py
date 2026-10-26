# Minimal WASM (MVP + bulk-memory) disassembler for inspecting single functions by name.
import sys, struct
import os
# Set A350_PACKAGE to your inibuilds-aircraft-a350 folder (in your Community folder).
PACKAGE = os.environ.get('A350_PACKAGE') or os.path.join(os.environ.get('LOCALAPPDATA', ''), r'Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community\inibuilds-aircraft-a350')
WASM = os.path.join(PACKAGE, 'SimObjects/Airplanes/A350/attachments/inibuilds/Function_Interior_A350/panel/inibuilds-A350.wasm')
if not os.path.exists(WASM):
    raise SystemExit('A350 module not found at %s. Set A350_PACKAGE to your inibuilds-aircraft-a350 folder.' % WASM)
data = open(WASM, 'rb').read()


def leb(b, i):
    r = s = 0
    while True:
        x = b[i]; i += 1; r |= (x & 0x7f) << s; s += 7
        if not x & 0x80:
            return r, i


def sleb(b, i, bits=64):
    r = s = 0
    while True:
        x = b[i]; i += 1; r |= (x & 0x7f) << s; s += 7
        if not x & 0x80:
            if x & 0x40:
                r -= 1 << s
            return r, i


def name(b, i):
    n, i = leb(b, i)
    return b[i:i + n].decode('utf-8', 'replace'), i + n


secs = {}
i = 8
while i < len(data):
    sid = data[i]; i += 1
    size, i = leb(data, i)
    secs.setdefault(sid, []).append((i, size))
    i += size

# types
off, _ = secs[1][0]
n, j = leb(data, off)
types = []
for _ in range(n):
    assert data[j] == 0x60
    j += 1
    np_, j = leb(data, j); ps = list(data[j:j + np_]); j += np_
    nr, j = leb(data, j); rs = list(data[j:j + nr]); j += nr
    types.append((ps, rs))

# imports
off, _ = secs[2][0]
n, j = leb(data, off)
fimports = []
for _ in range(n):
    mod, j = name(data, j); fld, j = name(data, j); kind = data[j]; j += 1
    if kind == 0:
        t, j = leb(data, j); fimports.append((fld, t))
    elif kind == 1:
        j += 1; fl, j = leb(data, j); _, j = leb(data, j)
    elif kind == 2:
        fl, j = leb(data, j); _, j = leb(data, j)
        if fl & 1:
            _, j = leb(data, j)
    elif kind == 3:
        j += 2
nimp = len(fimports)

# function types
off, _ = secs[3][0]
n, j = leb(data, off)
ftypes = [t for _, t in fimports]
for _ in range(n):
    t, j = leb(data, j); ftypes.append(t)

# globals (initial values)
globs = []
if 6 in secs:
    off, _ = secs[6][0]
    n, j = leb(data, off)
    for _ in range(n):
        vt = data[j]; mut = data[j + 1]; j += 2
        op = data[j]; j += 1
        if op == 0x41:
            v, j = sleb(data, j, 32)
        elif op == 0x42:
            v, j = sleb(data, j, 64)
        elif op == 0x43:
            v = struct.unpack('<f', data[j:j + 4])[0]; j += 4
        elif op == 0x44:
            v = struct.unpack('<d', data[j:j + 8])[0]; j += 8
        elif op == 0x23:
            v, j = leb(data, j); v = ('global', v)
        else:
            v = None
        assert data[j] == 0x0b
        j += 1
        globs.append((vt, mut, v))

# names
fn = {}
for off, size in secs.get(0, []):
    cname, j = name(data, off)
    if cname != 'name':
        continue
    end = off + size
    while j < end:
        sub = data[j]; j += 1
        ssz, j = leb(data, j); send = j + ssz
        if sub == 1:
            cnt, k = leb(data, j)
            for _ in range(cnt):
                idx, k = leb(data, k); nm, k = name(data, k); fn[idx] = nm
        j = send
for k, (fld, t) in enumerate(fimports):
    fn.setdefault(k, 'import:' + fld)

# data segments
off, _ = secs[11][0]
n, j = leb(data, off)
segs = []
for _ in range(n):
    flag, j = leb(data, j)
    base = None
    if flag == 0:
        assert data[j] == 0x41
        base, j = sleb(data, j + 1, 32)
        assert data[j] == 0x0b
        j += 1
    ln, j = leb(data, j)
    segs.append((base, data[j:j + ln]))
    j += ln


def cstr(addr):
    for base, blob in segs:
        if base is not None and base <= addr < base + len(blob):
            k = addr - base
            e = blob.find(b'\0', k)
            s = blob[k:e]
            if 2 <= len(s) < 120 and all(32 <= c < 127 for c in s):
                return s.decode()
    return None


# code bodies
off, _ = secs[10][0]
n, j = leb(data, off)
bodies = []
for _ in range(n):
    bsz, j = leb(data, j); bodies.append((j, bsz)); j += bsz

LOADS = {0x28: 'i32.load', 0x29: 'i64.load', 0x2a: 'f32.load', 0x2b: 'f64.load', 0x2c: 'i32.load8_s',
         0x2d: 'i32.load8_u', 0x2e: 'i32.load16_s', 0x2f: 'i32.load16_u', 0x30: 'i64.load8_s',
         0x31: 'i64.load8_u', 0x32: 'i64.load16_s', 0x33: 'i64.load16_u', 0x34: 'i64.load32_s',
         0x35: 'i64.load32_u', 0x36: 'i32.store', 0x37: 'i64.store', 0x38: 'f32.store', 0x39: 'f64.store',
         0x3a: 'i32.store8', 0x3b: 'i32.store16', 0x3c: 'i64.store8', 0x3d: 'i64.store16', 0x3e: 'i64.store32'}
NUM = {0x45: 'i32.eqz', 0x46: 'i32.eq', 0x47: 'i32.ne', 0x48: 'i32.lt_s', 0x49: 'i32.lt_u', 0x4a: 'i32.gt_s',
       0x4b: 'i32.gt_u', 0x4c: 'i32.le_s', 0x4d: 'i32.le_u', 0x4e: 'i32.ge_s', 0x4f: 'i32.ge_u', 0x50: 'i64.eqz',
       0x51: 'i64.eq', 0x52: 'i64.ne', 0x5b: 'f32.eq', 0x5c: 'f32.ne', 0x5d: 'f32.lt', 0x5e: 'f32.gt',
       0x5f: 'f32.le', 0x60: 'f32.ge', 0x61: 'f64.eq', 0x62: 'f64.ne', 0x63: 'f64.lt', 0x64: 'f64.gt',
       0x65: 'f64.le', 0x66: 'f64.ge', 0x67: 'i32.clz', 0x6a: 'i32.add', 0x6b: 'i32.sub', 0x6c: 'i32.mul',
       0x6d: 'i32.div_s', 0x6e: 'i32.div_u', 0x6f: 'i32.rem_s', 0x70: 'i32.rem_u', 0x71: 'i32.and',
       0x72: 'i32.or', 0x73: 'i32.xor', 0x74: 'i32.shl', 0x75: 'i32.shr_s', 0x76: 'i32.shr_u',
       0x7c: 'i64.add', 0x7d: 'i64.sub', 0x7e: 'i64.mul', 0x83: 'i64.and', 0x84: 'i64.or', 0x86: 'i64.shl',
       0x88: 'i64.shr_u', 0x92: 'f32.add', 0x93: 'f32.sub', 0x94: 'f32.mul', 0x95: 'f32.div', 0xa0: 'f64.add',
       0xa1: 'f64.sub', 0xa2: 'f64.mul', 0xa3: 'f64.div', 0xa7: 'i32.wrap_i64', 0xa8: 'i32.trunc_f32_s',
       0xab: 'i32.trunc_f64_s', 0xac: 'i64.extend_i32_s', 0xad: 'i64.extend_i32_u', 0xb2: 'f32.convert_i32_s',
       0xb7: 'f64.convert_i32_s', 0xbb: 'f64.promote_f32', 0xb6: 'f32.demote_f64', 0xc0: 'i32.extend8_s',
       0xc1: 'i32.extend16_s'}


def walk(fidx):
    """Yield (pos, opname, immediate) for every instruction of a function body."""
    start, size = bodies[fidx - nimp]
    j = start; end = start + size
    nloc, j = leb(data, j)
    for _ in range(nloc):
        c, j = leb(data, j); j += 1
    while j < end:
        op = data[j]; pos = j; j += 1
        imm = None
        if op in (0x02, 0x03, 0x04):
            j += 1
        elif op in (0x0c, 0x0d):
            imm, j = leb(data, j)
        elif op == 0x0e:
            cnt, j = leb(data, j); ls = []
            for _ in range(cnt + 1):
                l, j = leb(data, j); ls.append(l)
            imm = ls
        elif op == 0x10:
            imm, j = leb(data, j)
        elif op == 0x11:
            t, j = leb(data, j); tb, j = leb(data, j); imm = t
        elif op in (0x20, 0x21, 0x22, 0x23, 0x24):
            imm, j = leb(data, j)
        elif op in LOADS:
            a, j = leb(data, j); o, j = leb(data, j); imm = o
        elif op in (0x3f, 0x40):
            j += 1
        elif op == 0x41:
            imm, j = sleb(data, j, 32)
        elif op == 0x42:
            imm, j = sleb(data, j, 64)
        elif op == 0x43:
            imm = struct.unpack('<f', data[j:j + 4])[0]; j += 4
        elif op == 0x44:
            imm = struct.unpack('<d', data[j:j + 8])[0]; j += 8
        elif op == 0xfc:
            sub, j = leb(data, j)
            if sub == 8:
                _, j = leb(data, j); j += 1
            elif sub == 9:
                _, j = leb(data, j)
            elif sub == 10:
                j += 2
            elif sub == 11:
                j += 1
            imm = sub
        yield pos, op, imm


def disasm(fidx, out, limit=6000):
    start, size = bodies[fidx - nimp]
    ps, rs = types[ftypes[fidx]]
    out.append(';; func %d %s  params=%s results=%s' % (fidx, fn.get(fidx, '?')[:160], ps, rs))
    depth = 0; count = 0
    for pos, op, imm in walk(fidx):
        if count >= limit:
            out.append('  ... (truncated)'); break
        count += 1
        if op in (0x02, 0x03, 0x04):
            out.append('  ' * depth + {0x02: 'block', 0x03: 'loop', 0x04: 'if'}[op]); depth += 1; continue
        if op == 0x05:
            out.append('  ' * (depth - 1) + 'else'); continue
        if op == 0x0b:
            depth -= 1; out.append('  ' * max(depth, 0) + 'end'); continue
        if op == 0x0c: txt = 'br %d' % imm
        elif op == 0x0d: txt = 'br_if %d' % imm
        elif op == 0x0e: txt = 'br_table %s' % imm
        elif op == 0x0f: txt = 'return'
        elif op == 0x00: txt = 'unreachable'
        elif op == 0x01: txt = 'nop'
        elif op == 0x10: txt = 'call %d <%s>' % (imm, fn.get(imm, '?')[:140])
        elif op == 0x11: txt = 'call_indirect type%d %s' % (imm, types[imm])
        elif op == 0x1a: txt = 'drop'
        elif op == 0x1b: txt = 'select'
        elif op in (0x20, 0x21, 0x22, 0x23, 0x24):
            txt = ['local.get', 'local.set', 'local.tee', 'global.get', 'global.set'][op - 0x20] + ' %d' % imm
        elif op in LOADS: txt = '%s offset=%d' % (LOADS[op], imm)
        elif op in (0x3f, 0x40): txt = 'memory.size' if op == 0x3f else 'memory.grow'
        elif op == 0x41:
            s = cstr(imm) if imm > 1000 else None
            txt = 'i32.const %d' % imm + ('   ; "%s"' % s if s else '')
        elif op == 0x42: txt = 'i64.const %d' % imm
        elif op in (0x43, 0x44): txt = ('f32' if op == 0x43 else 'f64') + '.const %g' % imm
        elif op == 0xfc: txt = {8: 'memory.init', 9: 'data.drop', 10: 'memory.copy', 11: 'memory.fill'}.get(imm, 'fc.%d' % imm)
        elif 0x45 <= op <= 0xc4: txt = NUM.get(op, 'num_0x%02x' % op)
        else: txt = 'UNKNOWN_0x%02x' % op
        out.append('  ' * depth + txt)


def find(prefix):
    return sorted(k for k, v in fn.items() if v.startswith(prefix) and k >= nimp)


def callers_of(target_idx):
    res = []
    for fi in range(len(bodies)):
        f = fi + nimp
        for pos, op, imm in walk(f):
            if op == 0x10 and imm == target_idx:
                res.append(f); break
    return res


if __name__ == '__main__':
    out = []
    for pref in sys.argv[1:]:
        for k in find(pref)[:3]:
            disasm(k, out); out.append('')
    print('\n'.join(out))
