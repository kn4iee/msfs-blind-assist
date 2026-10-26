import wdis, struct
TABLE = 920832
_seg = [s for s in wdis.segs if s[0] is not None and s[0] <= TABLE < s[0] + len(s[1])][0]
def varname(e):
    if not 0 <= e < 1939: return None
    v = struct.unpack_from('<I', _seg[1], TABLE - _seg[0] + 4*e)[0]
    return wdis.cstr(v)
NAMES = {e: varname(e) for e in range(1939)}
BYNAME = {v: k for k, v in NAMES.items() if v}
