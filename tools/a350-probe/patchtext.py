# Build a SCRATCH copy of the A350 module for draw emulation:
#   1. nvgText first reports (string, end, ctx, x, y) through an otherwise-unused import
#      (WASI path_readlink, import 18) tagged with a magic number, then runs as normal.
#   2. A few internal drawing functions are exported so the emulator can call them.
# The installed aircraft file is only READ. Output goes to the path given (default: a350-texthook.wasm).
# Every function is found by its name in the module's name section (find_by_name), never by index, so an A350 update
# needs no edit here. This is the first, text-only scratch module (1.2.6, October 2026); patch2.py, which the README's
# once-per-version steps run, builds the whole-cockpit one from these helpers.
import sys
import wdis

MAGIC = 0x7E57
HOOK_IMPORT_NAME = 'path_readlink'   # 6 x i32 -> i32; never called while drawing


def uleb(n):
    out = bytearray()
    while True:
        b = n & 0x7f; n >>= 7
        out.append(b | (0x80 if n else 0))
        if not n:
            return bytes(out)


def sleb(n):
    out = bytearray()
    while True:
        b = n & 0x7f; n >>= 7
        done = (n == 0 and not b & 0x40) or (n == -1 and b & 0x40)
        out.append(b | (0 if done else 0x80))
        if done:
            return bytes(out)


def find_by_name(name):
    hits = [k for k, v in wdis.fn.items() if v == name and k >= wdis.nimp]
    assert len(hits) == 1, (name, hits)
    return hits[0]


def hook_import_index():
    hits = [i for i, (fld, _t) in enumerate(wdis.fimports) if fld == HOOK_IMPORT_NAME]
    assert len(hits) == 1
    return hits[0]


def text_hook():
    return (b'\x20\x03' b'\x20\x04' b'\x20\x00'          # string, end, ctx
            b'\x20\x01\xbc' b'\x20\x02\xbc'               # x, y as raw float bits
            b'\x41' + sleb(MAGIC) +                       # magic
            b'\x10' + uleb(hook_import_index()) + b'\x1a')  # call hook; drop


def walk_sections(b):
    i = 8; out = []
    while i < len(b):
        sid = b[i]; size, k = wdis.leb(b, i + 1)
        out.append((sid, i, k, k + size)); i = k + size
    return out


def patch_code(data, funcs_to_hook):
    """Prepend hook bytes to the given functions' bodies. Returns new module bytes."""
    (code_off, code_size), = [(cs, ce - cs) for sid, hs, cs, ce in walk_sections(data) if sid == 10]
    n, j = wdis.leb(data, code_off)
    new = bytearray()
    for fi in range(n):
        sz, k = wdis.leb(data, j)
        es, bs, be = j, k, k + sz
        j = be
        fidx = fi + wdis.nimp
        if fidx not in funcs_to_hook:
            new += data[es:be]
            continue
        p = bs
        ngroups, p = wdis.leb(data, p)          # skip the locals declaration
        for _ in range(ngroups):
            _, p = wdis.leb(data, p); p += 1
        body = data[bs:p] + funcs_to_hook[fidx] + data[p:be]
        new += uleb(len(body)) + body
    section = uleb(n) + bytes(new)
    hdr_start = code_off - 1 - len(uleb(code_size))
    assert data[hdr_start] == 10
    return data[:hdr_start] + bytes([10]) + uleb(len(section)) + section + data[code_off + code_size:]


def add_exports(data, extra):
    """extra: list of (name, func_index). Appends function exports to the export section."""
    for sid, hs, cs, ce in walk_sections(data):
        if sid == 7:
            n, j = wdis.leb(data, cs)
            entries = data[j:ce]
            for name, idx in extra:
                nb = name.encode()
                entries += uleb(len(nb)) + nb + b'\x00' + uleb(idx)
            content = uleb(n + len(extra)) + entries
            return data[:hs] + b'\x07' + uleb(len(content)) + content + data[ce:]
    raise SystemExit('no export section')


def build(out_path):
    data = wdis.data
    data = add_exports(data, [
        ('emu_FMGS_Draw', find_by_name('FMGS::Draw()')),
        ('emu_FMGS_DrawFMS_A350', find_by_name('FMGS::DrawFMS_A350(NVGcontext*, bool)')),
        ('emu_MFD_Draw', find_by_name('MFD::Draw()')),
        ('emu_nvgBeginFrame', find_by_name('nvgBeginFrame')),
        ('emu_nvgEndFrame', find_by_name('nvgEndFrame')),
    ])
    data = patch_code(data, {find_by_name('nvgText'): text_hook()})
    open(out_path, 'wb').write(data)
    return len(data)


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else 'a350-texthook.wasm'
    print('wrote patched scratch module', out, 'bytes', build(out))
