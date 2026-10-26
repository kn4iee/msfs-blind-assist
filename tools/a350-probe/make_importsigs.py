# Writes importsigs.json (the A350 module's import signatures), used by the emulators. Run once per A350 version.
import json
import wdis

sigs = {}
for fld, t in wdis.fimports:
    ps, rs = wdis.types[t]
    sigs[fld] = {'params': ps, 'results': rs}
json.dump(sigs, open('importsigs.json', 'w'))
print(len(sigs), 'import signatures written to importsigs.json')
