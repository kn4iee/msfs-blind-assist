import struct, sys, time, json, ctypes
import memscan, fastdump
cfg = json.load(open('membase.json')); h = memscan.open_proc(cfg['pid']); base = cfg['bases'][0]
regs = fastdump.alloc_regions(h, base)
total = sum(s for _, s in regs)
buf = bytearray(total); cb = (ctypes.c_char * total).from_buffer(buf)
got = ctypes.c_size_t()
def snap():
    off = 0
    for rb, size in regs:
        memscan.k32.ReadProcessMemory(h, ctypes.c_void_p(rb), ctypes.byref(cb, off), size, ctypes.byref(got)); off += size
def save(p):
    with open(p, 'wb') as f:
        off = 0
        for rb, size in regs:
            f.write(struct.pack('<II', rb - base, size)); f.write(buf[off:off + size]); off += size
for p in sys.argv[1:]:
    t = time.perf_counter(); snap(); dt = time.perf_counter() - t; save(p); print(p, '%.1f ms' % (dt * 1000), total / 1e6, 'MB')
