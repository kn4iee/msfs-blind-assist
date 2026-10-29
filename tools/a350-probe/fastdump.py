# READ-ONLY fast copy: list ONLY the A350 allocation's regions (walk from its base), then read them back-to-back.
# fastdump2.py is the copy to take (it checks membase.json and every read); this file keeps alloc_regions for it.
import struct, sys, time, json, ctypes
import memscan

def alloc_regions(h, base):
    out = []; a = base; mbi = memscan.MBI()
    while True:
        if not memscan.k32.VirtualQueryEx(h, ctypes.c_void_p(a), ctypes.byref(mbi), ctypes.sizeof(mbi)): break
        if (mbi.AllocationBase or 0) != base: break
        if mbi.State == memscan.MEM_COMMIT and not (mbi.Protect & (memscan.PAGE_GUARD | memscan.PAGE_NOACCESS)):
            out.append((mbi.BaseAddress, mbi.RegionSize))
        a = mbi.BaseAddress + mbi.RegionSize
    return out

def dump(path, h, base, regs):
    t0 = time.perf_counter()
    blobs = [(rb - base, memscan.read(h, rb, size)) for rb, size in regs]
    t1 = time.perf_counter()
    with open(path, 'wb') as f:
        for lin, b in blobs:
            if b: f.write(struct.pack('<II', lin, len(b))); f.write(b)
    return t1 - t0

if __name__ == '__main__':
    cfg = json.load(open('membase.json')); h = memscan.open_proc(cfg['pid']); base = cfg['bases'][0]
    t = time.perf_counter(); regs = alloc_regions(h, base); tl = time.perf_counter() - t
    for p in sys.argv[1:]:
        print(p, 'regions', len(regs), 'list %.1f ms' % (tl * 1000), 'read %.1f ms' % (dump(p, h, base, regs) * 1000))
