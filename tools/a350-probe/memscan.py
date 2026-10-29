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


def find_pid(exe='FlightSimulator2024.exe'):
    import subprocess
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq ' + exe, '/FO', 'CSV', '/NH'], capture_output=True, text=True).stdout
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if parts and parts[0].lower() == exe.lower():
            return int(parts[1])
    return None


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


def locate(h, verbose=True):
    # The slow whole-process scan, for when fastlocate.py's large-reservation rule finds nothing. The anchors are
    # 1.2.7's (1.2.6's INI_MCDU{}_TACT_MODE and INI_FMC{}_MCDU no longer exist): fastlocate.py's, one list.
    from fastlocate import ANCHORS as anchors
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
                    ok = all((read(h, cand + la, len(a) + 1) or b'') == a.encode() + b'\0' for a, la in zip(anchors[1:], lin[1:]))
                    found.append((hex(cand), ok, hex(base), size, hex(typ)))
                    p = blob.find(needle, p + 1)
            off += CH
    if verbose:
        print('scanned %.1f GB in %.1fs' % (scanned / 1e9, time.time() - t0))
    return found


if __name__ == '__main__':
    print('slow: this reads every committed region of the sim, in 64 MB pieces (minutes, and many GB read);'
          ' fastlocate.py finds the A350 in about a second: use it first')
    pid = find_pid()
    print('pid', pid)
    h = open_proc(pid)
    hits = locate(h)
    for x in hits:
        print('candidate base', x)
    good = [int(x[0], 16) for x in hits if x[1]]
    if good:
        json.dump({'pid': pid, 'bases': good}, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'membase.json'), 'w'))
