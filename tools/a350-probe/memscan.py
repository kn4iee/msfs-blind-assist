# READ-ONLY: locate the iniBuilds A350 WASM linear memory inside FlightSimulator2024.exe and read from it.
# Never writes to the process. Uses OpenProcess(QUERY_INFORMATION | VM_READ) + ReadProcessMemory only.
import ctypes, ctypes.wintypes as wt, sys, time, json, os
import wdis

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010
MEM_COMMIT = 0x1000
MEM_PRIVATE = 0x20000
MEM_MAPPED = 0x40000
PAGE_NOACCESS = 0x01
PAGE_GUARD = 0x100


class MBI(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_ulonglong), ("AllocationBase", ctypes.c_ulonglong),
                ("AllocationProtect", wt.DWORD), ("PartitionId", wt.WORD), ("_pad", wt.WORD),
                ("RegionSize", ctypes.c_ulonglong), ("State", wt.DWORD), ("Protect", wt.DWORD),
                ("Type", wt.DWORD), ("_pad2", wt.DWORD)]


k32.OpenProcess.restype = wt.HANDLE
k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
k32.VirtualQueryEx.restype = ctypes.c_size_t
k32.VirtualQueryEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.POINTER(MBI), ctypes.c_size_t]
k32.ReadProcessMemory.restype = wt.BOOL
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


SIM_EXES = ('FlightSimulator2024.exe', 'FlightSimulator.exe')   # MSFS 2024, then 2020, as A350MemoryReader.ProcessNames


def find_pid(exe=None):
    """The sim's process id (MSFS 2024 first, then 2020), or None when no sim runs."""
    import subprocess
    for name in (exe,) if exe else SIM_EXES:
        out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq ' + name, '/FO', 'CSV', '/NH'], capture_output=True, text=True).stdout
        for line in out.splitlines():
            parts = [p.strip('"') for p in line.split('","')]
            if parts and parts[0].lower() == name.lower():
                return int(parts[1])
    return None


def sim_pid():
    """find_pid, or an exit with a sentence when no sim runs (a None pid crashed in ctypes, tools review 2026-10-30)."""
    pid = find_pid()
    if pid is None:
        sys.exit('the sim is not running (' + ' or '.join(SIM_EXES) + ')')
    return pid


def open_proc(pid):
    h = k32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
    if not h:
        raise OSError('OpenProcess failed, error %d' % ctypes.get_last_error())
    return h


def read(h, addr, size):
    buf = ctypes.create_string_buffer(size)
    got = ctypes.c_size_t(0)
    ok = k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, size, ctypes.byref(got))
    if not ok:
        return None
    return buf.raw[:got.value]


def regions(h):
    addr = 0
    mbi = MBI()
    while addr < 0x7FFFFFFFFFFF:
        r = k32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi))
        if not r:
            break
        yield mbi.BaseAddress, mbi.RegionSize, mbi.State, mbi.Protect, mbi.Type, mbi.AllocationBase
        addr = mbi.BaseAddress + mbi.RegionSize


def linear_addr_of(s):
    needle = s.encode() + b'\0'
    for base, blob in wdis.segs:
        if base is None:
            continue
        k = blob.find(b'\0' + needle)
        if k >= 0:
            return base + k + 1
    return None


# The A350's anchor names, 1.2.7's (1.2.6's INI_MCDU{}_TACT_MODE and INI_FMC{}_MCDU no longer exist), each at a fixed
# linear address in its memory: a memory holding all three is the A350's. One list for every script.
ANCHORS = ['INI_MCDU{}_ENTER', 'INI_MCDU_OPTION', 'INI_FMC_PAGE1']


def anchored(h, base):
    """Whether the A350's anchor names sit at their offsets from base."""
    return all((read(h, base + la, len(s) + 1) or b'') == s.encode() + b'\0'
               for s, la in zip(ANCHORS, [linear_addr_of(a) for a in ANCHORS]))


def alloc_regions(h, base):
    """The committed, readable regions of the allocation at base: the A350's memory alone [A350-2]."""
    out = []; a = base; mbi = MBI()
    while True:
        if not k32.VirtualQueryEx(h, ctypes.c_void_p(a), ctypes.byref(mbi), ctypes.sizeof(mbi)): break
        if (mbi.AllocationBase or 0) != base: break
        if mbi.State == MEM_COMMIT and not (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)):
            out.append((mbi.BaseAddress, mbi.RegionSize))
        a = mbi.BaseAddress + mbi.RegionSize
    return out


def locate(h, verbose=True):
    # The slow whole-process scan, for when fastlocate.py's large-reservation rule finds nothing.
    anchors = ANCHORS
    lin = [linear_addr_of(a) for a in anchors]
    assert all(lin), lin
    needle = anchors[0].encode() + b'\0'
    found = []
    t0 = time.time(); scanned = 0
    for base, size, state, prot, typ, abase in regions(h):
        if state != MEM_COMMIT or typ not in (MEM_PRIVATE, MEM_MAPPED):
            continue
        if prot & PAGE_GUARD or prot & PAGE_NOACCESS or prot == 0:
            continue
        CH = 64 << 20
        off = 0
        while off < size:
            n = min(CH + 64, size - off)
            blob = read(h, base + off, n)
            scanned += n
            if blob:
                p = blob.find(needle)
                while p >= 0:
                    hit = base + off + p
                    cand = hit - lin[0]
                    ok = anchored(h, cand)
                    found.append((hex(cand), ok, hex(base), size, hex(typ)))
                    p = blob.find(needle, p + 1)
            off += CH
    if verbose:
        print('scanned %.1f GB in %.1fs' % (scanned / 1e9, time.time() - t0))
    return found


if __name__ == '__main__':
    print('slow: this reads every committed region of the sim, in 64 MB pieces (minutes, and many GB read);'
          ' fastlocate.py finds the A350 in about a second: use it first')
    pid = sim_pid()
    print('pid', pid)
    h = open_proc(pid)
    hits = locate(h)
    for x in hits:
        print('candidate base', x)
    good = [int(x[0], 16) for x in hits if x[1]]
    if good:
        json.dump({'pid': pid, 'bases': good}, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'membase.json'), 'w'))
