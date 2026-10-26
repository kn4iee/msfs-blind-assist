import wdis, struct, json, re
# Build a flat image of initial memory from data segments.
mem = {}
segs = [(b, blob) for b, blob in wdis.segs if b is not None]
def u32(a):
    for b, blob in segs:
        if b <= a and a + 4 <= b + len(blob):
            return struct.unpack_from('<I', blob, a - b)[0]
    return None
def demangle_simple(s):
    m = re.match(r'^(\d+)([A-Za-z_]\w*)$', s)
    if m and int(m.group(1)) == len(m.group(2)): return m.group(2)
    return None
vt = {}
for b, blob in segs:
    for off in range(8, len(blob) - 4, 4):
        a = b + off
        if struct.unpack_from('<I', blob, off - 8)[0] != 0: continue
        T = struct.unpack_from('<I', blob, off - 4)[0]
        if not (1024 <= T < 1280000): continue
        np_ = u32(T + 4)
        if np_ is None: continue
        s = wdis.cstr(np_)
        if not s: continue
        nm = demangle_simple(s)
        if nm:
            vt[a] = nm
json.dump({str(k): v for k, v in vt.items()}, open('vtables.json', 'w'))
print('vtables found', len(vt))
names = sorted(set(vt.values()))
print([n for n in names if re.search(r'UI|TextBox|Button|Label|Element|FMSpage|Dropdown|Notepad|^FMS_Init$|^FMC_FPLAN$|FMGS$|^MFD$', n)])
