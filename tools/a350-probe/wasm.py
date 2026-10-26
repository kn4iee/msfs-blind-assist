import sys, struct, json, re, collections
path = sys.argv[1]; out = sys.argv[2]
data = open(path, 'rb').read()
assert data[:4] == b'\0asm', 'not wasm'
def leb(b, i):
    r = s = 0
    while True:
        x = b[i]; i += 1; r |= (x & 0x7f) << s; s += 7
        if not x & 0x80: return r, i
def name(b, i):
    n, i = leb(b, i); return b[i:i+n].decode('utf-8', 'replace'), i+n
i = 8; secs = []
while i < len(data):
    sid = data[i]; i += 1
    size, i = leb(data, i)
    secs.append((sid, i, size)); i += size
SN = {0:'custom',1:'type',2:'import',3:'function',4:'table',5:'memory',6:'global',7:'export',8:'start',9:'element',10:'code',11:'data',12:'datacount'}
res = {'sections': []}
imports, exports, customs = [], [], []
for sid, off, size in secs:
    label = SN.get(sid, sid)
    if sid == 0:
        cname, j = name(data, off); label += ':' + cname; customs.append((cname, off, size, j))
    res['sections'].append((label, size))
    if sid == 2:
        n, j = leb(data, off)
        for _ in range(n):
            mod, j = name(data, j); fld, j = name(data, j); kind = data[j]; j += 1
            if kind == 0: _, j = leb(data, j)
            elif kind == 1: j += 1; fl, j = leb(data, j); _, j = leb(data, j); 
            elif kind == 2:
                fl, j = leb(data, j); _, j = leb(data, j)
                if fl & 1: _, j = leb(data, j)
            elif kind == 3: j += 2
            imports.append((mod, fld, kind))
    if sid == 7:
        n, j = leb(data, off)
        for _ in range(n):
            nm, j = name(data, j); kind = data[j]; j += 1; idx, j = leb(data, j); exports.append((nm, kind))
res['imports'] = imports; res['exports'] = exports
# function names from name section
fnames = {}
for cname, off, size, j in customs:
    if cname == 'name':
        end = off + size
        while j < end:
            sub = data[j]; j += 1; ssz, j = leb(data, j); send = j + ssz
            if sub == 1:
                n, k = leb(data, j)
                for _ in range(n):
                    idx, k = leb(data, k); nm, k = name(data, k); fnames[idx] = nm
            j = send
res['n_function_names'] = len(fnames)
json.dump(res, open(out + '/wasm_summary.json', 'w'), indent=1)
open(out + '/wasm_fnames.txt', 'w', encoding='utf-8').write('\n'.join(fnames[k] for k in sorted(fnames)))
# strings from data section
strs = []
for sid, off, size in secs:
    if sid == 11:
        blob = data[off:off+size]
        for m in re.finditer(rb'[\x20-\x7e]{4,}', blob): strs.append(m.group().decode())
open(out + '/wasm_strings.txt', 'w', encoding='utf-8').write('\n'.join(strs))
print(json.dumps(res['sections'])); print('imports', len(imports), 'exports', len(exports), 'fnames', len(fnames), 'strings', len(strs))
c = collections.Counter(m for m, f, k in imports); print(c)
