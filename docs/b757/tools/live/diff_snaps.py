import json, sys
a = json.loads(open(sys.argv[1]).read().strip()); b = json.loads(open(sys.argv[2]).read().strip())
noise = set(open(sys.argv[3]).read().split())
for k in sorted(set(a) | set(b)):
    if a.get(k) != b.get(k) and k not in noise:
        print(f"{k}: {a.get(k)} -> {b.get(k)}")
