import sys, re, json, collections
import os
if len(sys.argv) < 2:
    raise SystemExit('usage: python beh.py OUT_DIR   (writes a350_inputevents.json and a350_refs.json there)')
sys.path.insert(0, '.')
# The behaviour-file reader is the map generator's (tools/a350-gen), one copy for both.
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'a350-gen'))
from behavior_xml import Behavior
# The package as wdis finds it (package.py: A350_PACKAGE, else the sim's own UserCfg.opt).
from wdis import PACKAGE as PKG
F = os.path.join(PKG, 'SimObjects', 'Airplanes', 'A350', 'attachments', 'inibuilds', 'Function_Interior_A350', 'model',
                 'A350_Interior.behavior.xml')
txt = open(F, encoding='utf-8-sig').read()
head, sep, tail = txt.partition('<Strings')
head = re.sub(r'<[0-9][^ />]*/>', '<P/>', head)
b = Behavior(head + sep + tail)
print('components', len(b.components), 'mouserects', len(b.mouserects), 'inputevents', len(b.inputevents), 'strings', len(b.strings))
allcode = '\n'.join(b.strings.values())
refs = collections.Counter()
for m in re.finditer(r'\(>?([LBHKAOEM]):([^,)]+)', allcode):
    refs[(m.group(1), m.group(2).strip())] += 1
bykind = collections.defaultdict(list)
for (k, n), c in refs.items(): bykind[k].append(n)
for k in sorted(bykind): print(k, len(bykind[k]))
json.dump({k: sorted(set(v)) for k, v in bykind.items()}, open(sys.argv[1] + '/a350_refs.json', 'w'), indent=1)
# input event prefixes
pre = collections.Counter()
for ie in b.inputevents:
    i = ie['id'].replace('AIRLINER_', '')
    pre[i.split('_')[0]] += 1
print(pre.most_common(80))
json.dump(b.inputevents, open(sys.argv[1] + '/a350_inputevents.json', 'w'), indent=1)
