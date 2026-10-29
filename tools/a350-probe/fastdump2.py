# READ-ONLY copy of the A350's memory for offline draws (A350Probe draw, draw.mjs): lists ONLY the A350 allocation's
# regions, then reads them back-to-back into one buffer [A350-2]. About 8 ms on a fresh A350 (66 MB); a grown heap
# takes longer (about 650 ms at 495 MB) and spans frames, so a copy can be torn: take several and keep one that draws.
# membase.json (fastlocate.py, every sim session) is checked first: the sim must be the same process and the A350's
# anchors must still sit at its base, or the copy would be empty or another module's (tools review 2026-10-30).
# usage: python fastdump2.py c1.bin [c2.bin ...]
import struct, sys, time, json, ctypes, os
import memscan

try:
    cfg = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'membase.json')))
except FileNotFoundError:
    sys.exit('no membase.json: run fastlocate.py first (once per sim session)')
if memscan.sim_pid() != cfg['pid']:
    sys.exit('membase.json is from another sim session: run fastlocate.py first')
h = memscan.open_proc(cfg['pid'])
base = cfg['bases'][0]
if not memscan.anchored(h, base):
    sys.exit("the A350's anchors are not at membase.json's base (the aircraft reloaded?): run fastlocate.py again")
regs = memscan.alloc_regions(h, base)
total = sum(s for _, s in regs)
buf = bytearray(total)
cb = (ctypes.c_char * total).from_buffer(buf)
got = ctypes.c_size_t()


def snap():
    """Reads every region into the buffer; returns the regions whose read failed or came back short."""
    off = 0
    short = []
    for rb, size in regs:
        ok = memscan.k32.ReadProcessMemory(h, ctypes.c_void_p(rb), ctypes.byref(cb, off), size, ctypes.byref(got))
        if not ok or got.value != size:
            short.append((rb, got.value if ok else 0, size))
        off += size
    return short


def save(p):
    with open(p, 'wb') as f:
        off = 0
        for rb, size in regs:
            f.write(struct.pack('<II', rb - base, size))
            f.write(buf[off:off + size])
            off += size


for p in sys.argv[1:]:
    t = time.perf_counter()
    short = snap()
    dt = time.perf_counter() - t
    if short:
        rb, n, size = short[0]
        print(p, 'NOT SAVED: %d region(s) read short (the A350 reloaded or its memory moved?); first at %s, %d of %d bytes'
              % (len(short), hex(rb), n, size))
        continue
    save(p)
    print(p, '%.1f ms' % (dt * 1000), total / 1e6, 'MB')
