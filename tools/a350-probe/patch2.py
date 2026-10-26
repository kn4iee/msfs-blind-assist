# Scratch module for whole-cockpit draw emulation (A350 1.2.7+). Re-finds everything by name.
# Hooks: nvgText (0x7E57: str,end,ctx,xbits,ybits), nvgFillColor (0x7E58: ctx,colorPtr),
#        nvgSave (0x7E59: ctx), nvgRestore (0x7E5A: ctx). All report via the unused WASI path_readlink import.
import sys, wdis
from patchtext import uleb, sleb, find_by_name, hook_import_index, patch_code, add_exports

def call_hook(argbytes, magic):
    return argbytes + b'\x41' + sleb(magic) + b'\x10' + uleb(hook_import_index()) + b'\x1a'

Z = b'\x41\x00'
HOOKS = {
    'nvgText': call_hook(b'\x20\x03\x20\x04\x20\x00\x20\x01\xbc\x20\x02\xbc', 0x7E57),
    'nvgFillColor': call_hook(b'\x20\x00\x20\x01' + Z + Z + Z, 0x7E58),
    'nvgSave': call_hook(b'\x20\x00' + Z + Z + Z + Z, 0x7E59),
    'nvgRestore': call_hook(b'\x20\x00' + Z + Z + Z + Z, 0x7E5A),
    'nvgTranslate': call_hook(b'\x20\x00\x20\x01\xbc\x20\x02\xbc' + Z + Z, 0x7E5B),
    'nvgScale': call_hook(b'\x20\x00\x20\x01\xbc\x20\x02\xbc' + Z + Z, 0x7E5C),
    'nvgRotate': call_hook(b'\x20\x00\x20\x01\xbc' + Z + Z + Z, 0x7E5D),
    'nvgResetTransform': call_hook(b'\x20\x00' + Z + Z + Z + Z, 0x7E5E),
    'nvgReset': call_hook(b'\x20\x00' + Z + Z + Z + Z, 0x7E5F),
    'nvgTransform': call_hook(b'\x20\x00' + Z + Z + Z + Z, 0x7E60),
}
# DrawFrame(ctx, x, y, w, h, const FsColor&): selection boxes (ECAM cursor, MFD highlight).
HOOKS2 = {'DrawFrame(NVGcontext*, float, float, float, float, FsColor const&)':
          call_hook(b'\x20\x00\x20\x01\xbc\x20\x02\xbc\x20\x03\xbc\x20\x04\xbc', 0x7E61)
          + call_hook(b'\x20\x00\x20\x05' + Z + Z + Z, 0x7E62),
          'DrawWideFrame(NVGcontext*, float, float, float, float, float, FsColor const&)':
          call_hook(b'\x20\x00\x20\x01\xbc\x20\x02\xbc\x20\x03\xbc\x20\x04\xbc', 0x7E61)
          + call_hook(b'\x20\x00\x20\x06\x41\x01' + Z + Z, 0x7E62)}
EXPORTS = ['CTR::Draw()', 'EFIS::Draw()', 'FCU::Draw()', 'HUD::Draw()', 'ISIS::Draw()', 'RMP::Draw()',
           'BAT::Draw()', 'MFD::Draw()', 'FMGS::Draw()', 'SFD::Draw(int)', 'SND::Draw(int)', 'Systems::Draw()',
           'nvgBeginFrame', 'nvgEndFrame']

# UI element draws: report (this, ctx, class id) so texts can be tied to clickable elements and rectangles.
import re as _re
UI_DRAWS = sorted(v for k, v in wdis.fn.items() if _re.match(r'^[A-Za-z_]+::draw\(NVGcontext\*, bool, int\)$', v))
for _i, _nm in enumerate(UI_DRAWS):
    HOOKS2[_nm] = call_hook(b'\x20\x00\x20\x01\x41' + sleb(_i) + Z + Z, 0x7E65)
import json as _json
_json.dump(UI_DRAWS, open('ui_draws.json', 'w'))

def build(out):
    data = wdis.data
    ex = []
    for nm in EXPORTS:
        ex.append(('emu_' + nm.split('(')[0].replace('::', '_'), find_by_name(nm)))
    data = add_exports(data, ex)
    data = patch_code(data, {find_by_name(k): v for k, v in list(HOOKS.items()) + list(HOOKS2.items())})
    open(out, 'wb').write(data)
    return len(data), ex

if __name__ == '__main__':
    print(build(sys.argv[1] if len(sys.argv) > 1 else 'a350-all.wasm'))

