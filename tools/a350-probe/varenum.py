import wdis, struct
# The VariablesEnum name table (1.2.7): enum e's name pointer at TABLE + 4e, its value at 1343616 + 8e (the i32.const
# in Variables::GetInt). Not 920832, an entry 88 enums on (sim 2026-10-30: INI_V1 is 358, INI_VREF_SPEED 382).
TABLE = 920480
_seg = [s for s in wdis.segs if s[0] is not None and s[0] <= TABLE < s[0] + len(s[1])][0]
def varname(e):
    if not 0 <= e < 1939: return None
    v = struct.unpack_from('<I', _seg[1], TABLE - _seg[0] + 4*e)[0]
    return wdis.cstr(v)
NAMES = {e: varname(e) for e in range(1939)}
BYNAME = {v: k for k, v in NAMES.items() if v}
