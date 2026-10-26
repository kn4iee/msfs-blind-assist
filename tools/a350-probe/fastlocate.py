# READ-ONLY fast locator: instead of reading all ~23 GB of the sim's memory, list the memory blocks
# (no reading), keep only the huge reserved blocks shaped like a WASM linear memory, and check the
# A350's marker text at its known offset inside each one (a few tiny reads).
import collections, time
import memscan

ANCHORS = ['INI_MCDU{}_ENTER', 'INI_MCDU_OPTION', 'INI_FMC_PAGE1']


def fast_locate(h, min_span=1 << 32):
    t0 = time.time()
    span = collections.defaultdict(lambda: [None, 0])  # allocation base -> [lowest addr, highest end]
    nreg = 0
    for base, size, state, prot, typ, abase in memscan.regions(h):
        nreg += 1
        if state == 0x10000 or not abase:  # MEM_FREE
            continue
        s = span[abase]
        s[0] = base if s[0] is None else min(s[0], base)
        s[1] = max(s[1], base + size)
    t_enum = time.time() - t0
    lin = [memscan.linear_addr_of(a) for a in ANCHORS]
    cands = [(a, e - a) for a, (lo, e) in span.items() if e - a >= min_span]
    hits = []
    for a, sz in cands:
        if all((memscan.read(h, a + la, len(s) + 1) or b'') == s.encode() + b'\0' for s, la in zip(ANCHORS, lin)):
            hits.append(a)
    return hits, nreg, len(cands), t_enum, time.time() - t0


if __name__ == '__main__':
    import json, os
    pid = memscan.find_pid()
    h = memscan.open_proc(pid)
    hits, nreg, ncand, t_enum, t_all = fast_locate(h)
    print('regions %d, big blocks checked %d, listing %.2fs, total %.2fs' % (nreg, ncand, t_enum, t_all))
    print('A350 found at', [hex(x) for x in hits])
    if hits:
        # membase.json is what memread/objscan/pagedump/pointer read; refresh it every session.
        json.dump({'pid': pid, 'bases': hits}, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'membase.json'), 'w'))
        print('wrote membase.json')
