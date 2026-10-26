import sys, re, json, collections
sys.path.insert(0, '.')
from behavior_xml import Behavior
import os
# Set A350_PACKAGE to your inibuilds-aircraft-a350 folder (in your Community folder).
from wdis import PACKAGE as PKG
F = PKG + r'\SimObjects\Airplanes\A350\attachments\inibuilds\Function_Interior_A350\model\A350_Interior.behavior.xml'
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
