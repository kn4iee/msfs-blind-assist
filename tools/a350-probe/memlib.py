import struct, json
VT = {int(k): v for k, v in json.load(open('vtables.json')).items()}
class Mem:
    def __init__(s, path):
        buf = open(path, 'rb').read(); s.regs = []; i = 0
        while i < len(buf):
            lin, ln = struct.unpack_from('<II', buf, i); s.regs.append((lin, buf[i + 8:i + 8 + ln])); i += 8 + ln
    def rd(s, a, n):
        for lin, b in s.regs:
            if lin <= a and a + n <= lin + len(b): return b[a - lin:a - lin + n]
    def u32(s, a): b = s.rd(a, 4); return struct.unpack('<I', b)[0] if b else None
    def u8(s, a): b = s.rd(a, 1); return b[0] if b else None
    def f32(s, a): b = s.rd(a, 4); return struct.unpack('<f', b)[0] if b else None
    def f64(s, a): b = s.rd(a, 8); return struct.unpack('<d', b)[0] if b else None
    def cls(s, obj): return VT.get(s.u32(obj) or 0)
    def string(s, a):
        b = s.rd(a, 12)
        if not b: return None
        if b[11] & 0x80:
            p, n, c = struct.unpack('<III', b)
            if n > 4096: return None
            t = s.rd(p, n)
        else:
            n = b[11]; t = b[:n] if n <= 10 else None
        try: return t.decode('latin1') if t is not None else None
        except: return None
def find_mfd(m):
    """The live MFD object: first word is an MFD vtable (heap address changes every sim start)."""
    vts = {a for a, c in VT.items() if c == 'MFD'}
    for lin, b in m.regs:
        for o in range(0, len(b) - 4, 4):
            if struct.unpack_from('<I', b, o)[0] in vts: return lin + o
