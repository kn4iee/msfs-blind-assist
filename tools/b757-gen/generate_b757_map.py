#!/usr/bin/env python3
"""
Generate the BlueBird 757-200 control map from the aircraft's cockpit behavior XML.

extract_controls.py (next to this file) expands the vendor's templates and classifies every interactive control;
this script turns those rows into the JSON the app embeds (MSFSBlindAssist/Resources/bbs757_control_map.json):
one entry per control the pilot can operate, with the L:var that holds its state, its positions and the control's
own click code as RPN, to be replayed through the MobiFlight calculator path.

    controls[]  node, section, panel, label, kind, state_var, positions[{value,label}],
                press, release, hold_ms, cw, ccw
                kind: toggle | switch | button | held | select | guard
                state_var: the L:var read back (null when the control keeps no L:var state: a button)
                press / release / cw / ccw: RPN, null when the control has none; a switch with no click code
                (the sim's own 3-state template) has all three null and is written through state_var
    status[]    key, panel, label, positions[{value,label}], announce: read-only L:vars worth speaking
                (APU state, master warning ...), listed in curation.json

What is derived and what is curated (curation.json):
  derived   kind, state var, positions (the clamps in the turn code, or the vendor's ANIMTIP texts when there is
            one per position), click code, hold time, section (node prefix), a label from the node name (a guard
            is named by what its tip says it covers).
  curated   which vendor banners and nodes are left out, the app's panel names, labels the node name cannot give or
            the vendor got wrong, position names no tooltip carries, the status rows. A curated label, position
            list or exclusion for a node the package does not have is an error (a stale entry never hides). The
            vendor's tooltip titles and toggle tips are never labels: the vendor copied them between controls.
  a toggle, guard or select that writes a value other than 0 or 1 to its state var stops the run until its
            positions are named in curation (A/T ARM writes 10).

press / release / cw / ccw are the cockpit's own click code, VERBATIM: the app replays exactly what a click in the
cockpit runs, so a slip the cockpit has stays a slip in the replay. Code that looks like a slip (a set without its
'L:', a closing brace nothing opened) is only reported, as suspected_slips; nothing is changed.

Dropped, and counted in the printed report: circuit breakers, CDU keys, levers, infinite knobs, finite knobs with
more than 12 positions, anything in an excluded banner or node, controls with no click code or whose state is not
an L:var, and the second row of a node the interior files repeat (the PW and RR variants).

Usage:  python -I tools/b757-gen/generate_b757_map.py [<package folder>] [--out <json>] [--curation <json>]
        With no folder the package is looked for in Community and Community2024 under
        %LOCALAPPDATA%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalCache\\Packages. Reads the package only.
Tests:  python -I tools/b757-gen/test_generate_b757_map.py   (stdlib unittest, no aircraft needed)
"""
import argparse
import collections
import json
import os
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
if str(HERE) not in sys.path:  # python -I puts neither the script's folder nor the current one on the path
    sys.path.insert(0, str(HERE))

import extract_controls  # noqa: E402

PACKAGE_NAME = "bluebirdsim-aircraft-757-200"
DEFAULT_OUT = HERE.parent.parent / "MSFSBlindAssist" / "Resources" / "bbs757_control_map.json"
DEFAULT_CURATION = HERE / "curation.json"

MAX_POSITIONS = 12
DEFAULT_HOLD_MS = 1500

# Section order in the map, and the node prefix that picks one (first match wins).
SECTIONS = ("Overhead", "Glareshield", "Forward panel", "Pedestal", "Other")
SECTION_PREFIXES = (
    ("B757_OVHD_", "Overhead"), ("BBS_OVHD_", "Overhead"),
    ("B757_MCP_", "Glareshield"),
    ("B757_MAIN_", "Forward panel"), ("B757_ADI_", "Forward panel"), ("B757_HSI_", "Forward panel"),
    ("B757_PED_", "Pedestal"),
)

# The extractor's kind -> the map's kind. A kind missing here is dropped.
KIND_MAP = {
    "toggle": "toggle",
    "multi-position switch": "switch",
    "rotary knob finite": "switch",  # only with 12 positions or fewer
    "momentary button": "button",
    "held button": "held",
    "select button": "select",
    "guard": "guard",
}
# Words the cockpit prints in capitals: they stay upper case in a label made from a node name.
ACRONYMS = frozenset((
    "APU IRS VHF HF ADF VOR DME EEC CVR EICAS CRT TCAS ILS GPS FMC CDU MCP EFIS ND PFD AT AP FD GND ENG ELEC HYD "
    "L R C BTL "
    # more initialisms the node names use: mode buttons, sides, engine makers
    "LNAV VNAV FLCH VS LOC APP CMD TOGA THR BCRS CLB CON CRZ SPD HDG DH WX WXR PTT RMI DDRMI ADI HSI EADI EHSI EFI "
    "CPT FO RR PW STBY PA ATC AM RAT FWD ET"
).split())
# Words that only name what kind of control a node is: dropped from the end of a label made from a node name.
ROLE_WORDS = frozenset(("BUTTON", "SWITCH", "KNOB", "SW", "CLICK", "TOGGLE", "BTN"))
# Vendor and area tokens dropped from the front of a node name.
VENDOR_TOKENS = frozenset(("B757", "BBS757", "BBS"))
AREA_TOKENS = frozenset(("OVHD", "PED", "MCP", "MAIN"))
# A tooltip that names a direction, not a position.
DIRECTION_WORDS = frozenset(("INC", "DEC", "INCREASE", "DECREASE", "CW", "CCW"))

CLAMP_STEP = re.compile(r"\d\s+[+-]\s+-?\d+(?:\.\d+)?\s+(?:min|max)\b")
STEP_UP = re.compile(r"(\d+(?:\.\d+)?)\s+\+\s+(-?\d+(?:\.\d+)?)\s+min\b")    # "<step> + <top> min"
STEP_DOWN = re.compile(r"(\d+(?:\.\d+)?)\s+-\s+(-?\d+(?:\.\d+)?)\s+max\b")  # "<step> - <bottom> max"
ANY_TOP = re.compile(r"(-?\d+(?:\.\d+)?)\s+min\b")
ANY_BOTTOM = re.compile(r"(-?\d+(?:\.\d+)?)\s+max\b")
CONSTANT_SET = re.compile(r"(?:^|\s)(-?\d+(?:\.\d+)?)\s*\(>L:")
ASSIGN = re.compile(r"(?:^|\s)(-?\d+(?:\.\d+)?)\s*\(>L:([^,()\s]+)")           # "10 (>L:X)": a constant written to X
FLIP = re.compile(r"\(L:([^,()\s]+)[^()]*\)\s*!\s*\(>L:\1\s*[,)]")              # "(L:X) ! (>L:X)": the var a click flips
MISSING_L = re.compile(r"\(>(?![A-Za-z]:)(?=[A-Za-z_])([^()]*)\)")  # "(>NAME, bool)": a set with no "L:" in front
RANGE_SPEC = re.compile(r"range\s+(-?\d+(?:\.\d+)?)\.\.(-?\d+(?:\.\d+)?)\s+step\s+(\d+(?:\.\d+)?)$")

CURATION_KEYS = {"exclude_banners": list, "exclude_nodes": list, "panels": dict, "node_panels": list, "labels": dict,
                 "positions": dict, "spelling": dict, "status": list}


class CurationError(Exception):
    """curation.json does not fit the package or its own schema."""


# ---- words -----------------------------------------------------------------------------------------------------
def title_word(word: str, spelling=None) -> str:
    key = word.upper()
    if spelling and key in spelling:
        return spelling[key]
    if key in ACRONYMS:
        return key
    if len(word) <= 3 and any(not ch.isalpha() for ch in word):  # F/O, A/P, N1, 1
        return key
    return word.capitalize()


def title_case(text: str, spelling=None) -> str:
    return " ".join(title_word(w, spelling) for w in text.split())


def humanize(node: str, spelling=None, keep_role=False) -> str:
    """A label from a node name: the vendor and area prefix and the 'BUTTON' style suffix go, acronyms stay upper,
    the rest is Title Case. The cockpit's own words, never expanded. keep_role keeps the suffix ("Mic L Light
    Knob"), for telling apart two controls whose names differ only there."""
    tokens = [t for t in re.split(r"[_\-]+", node) if t]
    while len(tokens) > 1 and tokens[0].upper() in VENDOR_TOKENS:
        tokens.pop(0)
    if len(tokens) > 1 and tokens[0].upper() in AREA_TOKENS:
        tokens.pop(0)
    while not keep_role and len(tokens) > 1 and tokens[-1].upper() in ROLE_WORDS:
        tokens.pop()
    return " ".join(title_word(t, spelling) for t in tokens)


def labels_from_tips(texts, spelling=None):
    """Position labels from the vendor's tooltips: the words every tooltip starts with (and ends with) are dropped
    ("BATTERY OFF" / "BATTERY ON" -> Off / On), never a whole label."""
    words = [t.split() for t in texts]
    if len(words) == 2 and all(words) and [w[-1].upper() for w in words] == ["OFF", "ON"]:
        return ["Off", "On"]  # "L ENG HYD PUMP OFF" / "L ENG HYD PUMPS ON": the vendor's wording drifts, the state does not
    shortest = min((len(w) for w in words), default=0)
    lead = 0
    while lead < shortest - 1 and len({w[lead].upper() for w in words}) == 1:
        lead += 1
    trail = 0
    while trail < shortest - lead - 1 and len({w[-1 - trail].upper() for w in words}) == 1:
        trail += 1
    return [title_case(" ".join(w[lead:len(w) - trail]), spelling) for w in words]


# ---- positions -------------------------------------------------------------------------------------------------
def number(x):
    return int(x) if float(x) == int(x) else float(f"{x:.10g}")


def tip_pairs(row):
    """[(index, text)] from a row's ANIMTIP texts; plain strings count from 0."""
    out = []
    for i, t in enumerate(row.get("_tips") or []):
        out.append((int(t[0]), str(t[1]).strip()) if isinstance(t, (tuple, list)) else (i, str(t).strip()))
    return out


def turn_code(row) -> str:
    clicks = row.get("_clicks") or {}
    return " ".join(code for slot in ("left", "cw", "ccw", "other") for _, code in clicks.get(slot, []))


def position_values(row):
    """The values a control can take, from the extractor's positions cell ('3', 'range 0..2 step 1'), or None
    when they cannot be listed (more than 12, or none known)."""
    spec = str(row.get("positions") or "").strip()
    m = RANGE_SPEC.match(spec)
    if m:
        lo, hi, step = float(m.group(1)), float(m.group(2)), float(m.group(3))
        span = (hi - lo) / step if step > 0 else -1
        if span < 1 or abs(span - round(span)) > 1e-6:
            return None
        n = int(round(span)) + 1
        return [number(lo + i * step) for i in range(n)] if n <= MAX_POSITIONS else None
    if not spec.isdigit():
        return None
    n = int(spec)
    if n < 2 or n > MAX_POSITIONS:
        return None
    code = turn_code(row)
    constants = sorted({number(c) for c in CONSTANT_SET.findall(code)})
    if len(constants) == n:  # a knob whose turns assign constants: those are its values
        return constants
    up, down = STEP_UP.findall(code), STEP_DOWN.findall(code)
    if up and down and float(up[0][0]) == float(down[0][0]) > 0:  # "<step> + <top> min" and "<step> - <bottom> max"
        step, top, bottom = float(up[0][0]), max(float(t) for _, t in up), min(float(b) for _, b in down)
        if abs((top - bottom) / step + 1 - n) < 1e-6:
            return [number(bottom + i * step) for i in range(n)]
    lows = [float(x) for x in ANY_BOTTOM.findall(code)]
    highs = [float(x) for x in ANY_TOP.findall(code)]
    lo = min(lows) if lows else 0.0
    if highs and abs((max(highs) - lo) + 1 - n) > 1e-6:  # the clamps do not describe n positions: count from 0
        lo = 0.0
    return [number(lo + i) for i in range(n)]


def usable_tips(tips, values) -> bool:
    """The tooltips name the positions when there is one per value, in order, none is a direction hint or a
    localization key, and the words they leave are all different."""
    if not tips or len(tips) != len(values) or [i for i, _ in tips] != list(range(len(values))):
        return False
    for _, text in tips:
        if not text or text.startswith(("TT:", "@")) or text.split()[-1].upper() in DIRECTION_WORDS \
                or text.upper() in DIRECTION_WORDS:
            return False
    return True


def positions_for(row, spelling=None):
    """[{value, label}] for a control row: buttons have none, toggles and guards two, switches as many as the
    clamps say. Labels come from the vendor's tooltips when those name the positions, else 'Position <n>'."""
    kind = row["kind"]
    if kind in ("momentary button", "held button"):
        return []
    if kind in ("toggle", "guard", "select button"):
        values, fallback = [0, 1], (["Closed", "Open"] if kind == "guard" else ["Off", "On"])
    else:
        values = position_values(row)
        if values is None:
            return []
        fallback = [f"Position {v}" for v in values]
    tips = tip_pairs(row)
    if usable_tips(tips, values):
        labels = labels_from_tips([t for _, t in tips], spelling)
        if len({l.lower() for l in labels}) == len(labels) and all(labels):
            return [{"value": v, "label": l} for v, l in zip(values, labels)]
    return [{"value": v, "label": l} for v, l in zip(values, fallback)]


# ---- one control -----------------------------------------------------------------------------------------------
def section_of(node: str) -> str:
    for prefix, section in SECTION_PREFIXES:
        if node.startswith(prefix):
            return section
    return "Other"


def panel_of(row, cur):
    """(app panel name, section): a node rule first, then the banner table, else the banner in Title Case."""
    node, banner = row["node"], row["panel"]
    for rule in cur["node_panels"]:
        if ("banner" not in rule or rule["banner"] == banner) and ("prefix" not in rule or node.startswith(rule["prefix"])):
            return rule["panel"], rule.get("section") or section_of(node)
    entry = cur["panels"].get(banner)
    if entry is None:
        return title_case(banner, cur["spelling"]), section_of(node)
    if isinstance(entry, str):
        return entry, section_of(node)
    return entry["name"], entry.get("section") or section_of(node)


def state_var_of(row):
    for s in row.get("_state") or []:
        if s.startswith("L:"):
            return s[2:].split(" ")[0]
    return None


def join_codes(items):
    """A node can carry several interaction templates; a click reaches all of them, so their codes run together."""
    return " ".join(code for _, code in items) or None


def click_fields(row, out_kind):
    clicks = row["_clicks"]
    press, release = join_codes(clicks.get("left", [])), join_codes(clicks.get("release", []))
    cw, ccw = join_codes(clicks.get("cw", [])), join_codes(clicks.get("ccw", []))
    if out_kind == "switch" and not (cw or ccw) and press:
        right = join_codes([(l, c) for l, c in clicks.get("other", []) if l == "RightSingle"])
        if right and CLAMP_STEP.search(press):  # left click steps up, right click steps down, both clamped
            press, cw, ccw = None, press, right
    return press, release, cw, ccw


def suspected_slips(code):
    """What looks like a slip in a click code, as words; the code itself is never changed. Two things no RPN parser
    accepts: a set without its variable type, '(>B757_X_ON, bool)', and a closing brace nothing opened."""
    if not code:
        return []
    found = []
    if MISSING_L.search(code):
        found.append("set without a variable type")
    if code.count("}") > code.count("{"):
        found.append("closing brace nothing opened")
    return found


def hold_ms_of(row) -> int:
    try:
        seconds = float(str((row.get("_scope") or {}).get("MIN_HELD_DURATION", "")).strip())
    except ValueError:
        return DEFAULT_HOLD_MS
    return int(round(seconds * 1000)) if seconds > 0 else DEFAULT_HOLD_MS


def expand_positions(entry, values, node):
    """A curated position list: names in value order (one per value the control has), or explicit {value,label}s."""
    if all(isinstance(p, dict) for p in entry):
        return [{"value": p["value"], "label": p["label"]} for p in entry]
    if values is None or len(entry) != len(values):
        raise CurationError(f"positions for {node}: {len(entry)} names but the control has "
                            f"{'no listable positions' if values is None else str(len(values)) + ' positions'}")
    return [{"value": v, "label": l} for v, l in zip(values, entry)]


def tidy(text: str, spelling=None) -> str:
    """The vendor's wording, repaired: ALL CAPS becomes Title Case, mixed case is left as written."""
    text = " ".join(text.split())
    return title_case(text, spelling) if text == text.upper() else " ".join(
        (spelling or {}).get(w.upper(), w) for w in text.split())


def guard_label(row, out_kind, spelling=None):
    """A guard's single animation tip names what it covers ("Battery", "Left Generator Drive Disconnect"); its node
    name is only a number. Else None. No other vendor wording is used as a label: the tooltip titles and the
    toggles' tip pairs were copied between controls (every window heat button is titled 'Anti-ice Window', the center
    fuel pumps 'R Pump Aft'), so they are candidates for curation.json, never labels."""
    tips = tip_pairs(row)
    if out_kind == "guard" and len(tips) == 1 and tips[0][1] and not tips[0][1].startswith(("TT:", "@")):
        name = tidy(tips[0][1], spelling)
        return name if name.lower().endswith("guard") else f"{name} Guard"
    return None


def state_var_for(row, out_kind, press):
    """The L:var read back. A guard's own animation may read the switch under it, so a guard's state is the var its
    click flips."""
    if out_kind == "guard" and press:
        m = FLIP.search(press)
        if m:
            return m.group(1)
    return state_var_of(row)


def convert_row(row, cur):
    """(control, None) or (None, why it is left out)."""
    kind, node = row["kind"], row["node"]
    if row["panel"] in cur["exclude_banners"]:
        return None, "excluded banner"
    if node in cur["exclude_nodes"]:
        return None, "excluded node"
    if kind not in KIND_MAP:
        return None, kind
    out_kind = KIND_MAP[kind]
    values = None
    if out_kind == "switch":
        values = position_values(row)
        if values is None:
            spec = str(row.get("positions") or "").strip()
            return None, f"{kind} with more than 12 positions" if spec else f"{kind} without positions"
    press, release, cw, ccw = click_fields(row, out_kind)
    suspected = [f"{name}: {what}" for name, code in (("press", press), ("release", release), ("cw", cw), ("ccw", ccw))
                 for what in suspected_slips(code)]
    state_var = state_var_for(row, out_kind, press)
    if not (press or cw or ccw or (out_kind == "switch" and state_var)):
        return None, "no click code"
    if out_kind not in ("button", "held") and state_var is None:
        return None, "state is not an L:var"
    spelling = cur["spelling"]
    if node in cur["positions"]:
        if values is None and out_kind in ("toggle", "guard", "select"):
            values = [0, 1]
        positions = expand_positions(cur["positions"][node], values, node)
    else:
        if out_kind in ("toggle", "guard", "select") and press:
            odd = sorted({v for v, name in ASSIGN.findall(press) if name == state_var} - {"0", "1"})
            if odd:  # not a 0/1 switch: only the pilot's cockpit knows which value is which
                raise CurationError(f"{node} writes {', '.join(odd)} to its state var {state_var}: "
                                    f"name its positions under 'positions' in curation")
        positions = positions_for(row, spelling)
    panel, section = panel_of(row, cur)
    control = {
        "node": node, "section": section, "panel": panel,
        "label": cur["labels"].get(node) or guard_label(row, out_kind, spelling) or humanize(node, spelling),
        "kind": out_kind, "state_var": state_var, "positions": positions,
        "press": press, "release": release, "hold_ms": hold_ms_of(row) if out_kind == "held" else 0,
        "cw": cw, "ccw": ccw,
    }
    if suspected:
        control["_suspected"] = suspected  # build_map moves it into the report
    return control, None


def unique_labels(controls) -> None:
    """Inside one panel no two controls share a label: the second 'Light' becomes 'Light 2', the third 'Light 3'.
    The list is in map order and is changed in place."""
    reserved = collections.defaultdict(set)
    for c in controls:
        reserved[(c["section"], c["panel"])].add(c["label"])
    seen = collections.defaultdict(set)
    for c in controls:
        key = (c["section"], c["panel"])
        label = c["label"]
        if label in seen[key]:
            n = 2
            while f"{label} {n}" in reserved[key] or f"{label} {n}" in seen[key]:
                n += 1
            label = c["label"] = f"{label} {n}"
            reserved[key].add(label)
        seen[key].add(label)


def tell_apart(keyed, rows_by_node, cur) -> None:
    """Two controls in one panel whose labels (made from node names) differ only by the BUTTON / KNOB / SWITCH suffix
    keep that word on the later one ('Mic L Light' and 'Mic L Light Knob') before unique_labels would number it.
    A curated label or a guard's own name is never touched."""
    spelling = cur["spelling"]
    taken = collections.defaultdict(set)
    for _, c in keyed:
        taken[(c["section"], c["panel"])].add(c["label"])
    seen = collections.defaultdict(set)
    for _, c in keyed:
        key = (c["section"], c["panel"])
        row = rows_by_node[c["node"]]
        auto = c["node"] not in cur["labels"] and guard_label(row, c["kind"], spelling) is None
        if auto and c["label"] in seen[key]:
            longer = humanize(c["node"], spelling, keep_role=True)
            if longer not in taken[key]:
                c["label"] = longer
                taken[key].add(longer)
        seen[key].add(c["label"])


def status_row(entry):
    for field in ("key", "label", "panel", "announce"):
        if field not in entry:
            raise CurationError(f"status entry {entry!r} has no {field}")
    positions = entry.get("positions") or []
    if not all(isinstance(p, dict) for p in positions):
        positions = [{"value": i, "label": p} for i, p in enumerate(positions)]
    return {"key": entry["key"], "panel": entry["panel"], "label": entry["label"], "positions": positions,
            "announce": bool(entry["announce"])}


def build_map(rows, cur, aircraft, version):
    """(the map, a report of what was dropped and which curation entries did nothing)."""
    keyed, seen = [], set()
    dropped = collections.Counter()
    suspected = {}
    unmapped = set()
    for index, row in enumerate(rows):
        control, why = convert_row(row, cur)
        if why:
            dropped[why] += 1
        elif control["node"] in seen:
            dropped["duplicate node"] += 1
        else:
            seen.add(control["node"])
            if row["panel"] not in cur["panels"]:
                unmapped.add(row["panel"])
            if "_suspected" in control:
                suspected[control["node"]] = control.pop("_suspected")
            keyed.append(((SECTIONS.index(control["section"]), control["panel"], row.get("line", 0), index), control))
    keyed.sort(key=lambda kc: kc[0])
    tell_apart(keyed, {r["node"]: r for r in reversed(rows)}, cur)
    controls = [c for _, c in keyed]
    unique_labels(controls)

    in_package = {r["node"] for r in rows}
    for table in ("labels", "positions", "exclude_nodes"):
        stale = sorted(n for n in cur[table] if n not in in_package)
        if stale:
            raise CurationError(f"curation {table} name nodes the package does not have: {', '.join(stale)}")
    kept = {c["node"] for c in controls}
    banners = {r["panel"] for r in rows}
    report = {
        "dropped": dropped,
        "suspected_slips": suspected,
        "overrides_on_dropped": sorted({n for t in ("labels", "positions") for n in cur[t] if n not in kept}),
        "unused_banners": sorted((set(cur["exclude_banners"]) | set(cur["panels"])) - banners),
        "unmapped_banners": sorted(unmapped),
    }
    status = [status_row(s) for s in cur["status"]]
    return {"aircraft": aircraft, "package_version": version, "controls": controls, "status": status}, report


def dumps(map_) -> str:
    return json.dumps(map_, indent=1, ensure_ascii=False) + "\n"


# ---- curation and package --------------------------------------------------------------------------------------
def load_curation(path) -> dict:
    with open(path, encoding="utf-8-sig") as fh:
        data = json.load(fh)
    unknown = sorted(k for k in data if k not in CURATION_KEYS and not k.startswith("_"))
    if unknown:
        raise CurationError(f"{path}: unknown key(s) {', '.join(unknown)} (known: {', '.join(CURATION_KEYS)})")
    cur = {k: kind() for k, kind in CURATION_KEYS.items()}
    for key, kind in CURATION_KEYS.items():
        if key in data:
            if not isinstance(data[key], kind):
                raise CurationError(f"{path}: {key} must be a {kind.__name__}")
            cur[key] = data[key]
    cur["spelling"] = {k.upper(): v for k, v in cur["spelling"].items()}
    for banner, entry in cur["panels"].items():
        if not isinstance(entry, str) and not (isinstance(entry, dict) and "name" in entry):
            raise CurationError(f"{path}: panels[{banner!r}] is a name or {{name, section}}")
        if isinstance(entry, dict) and entry.get("section", "Other") not in SECTIONS:
            raise CurationError(f"{path}: panels[{banner!r}] names an unknown section")
    for rule in cur["node_panels"]:
        if "panel" not in rule or not ({"banner", "prefix"} & set(rule)):
            raise CurationError(f"{path}: node_panels rule {rule!r} needs a panel and a banner or prefix")
        if rule.get("section", "Other") not in SECTIONS:
            raise CurationError(f"{path}: node_panels rule {rule!r} names an unknown section")
    return cur


def package_roots(local_app_data=None):
    base = pathlib.Path(local_app_data or os.environ.get("LOCALAPPDATA", ""), "Packages",
                        "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages")
    return [base / "Community", base / "Community2024"]


def find_package(local_app_data=None):
    """The installed package folder, or None. Community first, then Community2024."""
    if not (local_app_data or os.environ.get("LOCALAPPDATA")):
        return None
    for root in package_roots(local_app_data):
        candidate = root / PACKAGE_NAME
        if (candidate / "manifest.json").is_file():
            return candidate
    return None


def package_version(pkg: pathlib.Path) -> str:
    try:
        return str(json.loads(extract_controls.read_text(pkg / "manifest.json")).get("package_version", "?"))
    except (OSError, ValueError):
        return "?"


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description="Generate the BlueBird 757-200 control map.")
    ap.add_argument("package", nargs="?", help="package folder (the one holding manifest.json). Omit to look in "
                                               "Community and Community2024 under %%LOCALAPPDATA%%.")
    ap.add_argument("--out", default=str(DEFAULT_OUT), help="the JSON to write (default: the app's Resources)")
    ap.add_argument("--curation", default=str(DEFAULT_CURATION))
    args = ap.parse_args(argv)

    pkg = pathlib.Path(args.package).resolve() if args.package else find_package()
    if pkg is None:
        sys.exit(f"{PACKAGE_NAME} not found under %LOCALAPPDATA%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\"
                 f"LocalCache\\Packages\\Community or Community2024: pass the package folder")
    if not (pkg / "manifest.json").is_file():
        sys.exit(f"{pkg} has no manifest.json: pass the package folder itself")

    rows, _lights, summary = extract_controls.extract(pkg)
    if summary["errors"]:
        print("files that did not parse: " + "; ".join(summary["errors"]), file=sys.stderr)
    if not rows:
        sys.exit(f"no controls found in {pkg}")
    try:
        cur = load_curation(args.curation)
        map_, report = build_map(rows, cur, pkg.name, package_version(pkg))
    except CurationError as e:
        sys.exit(f"curation: {e}")

    out = pathlib.Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", encoding="utf-8", newline="\n") as fh:
        fh.write(dumps(map_))

    controls = map_["controls"]
    print(f"wrote {out}")
    print(f"  package          : {pkg.name} {map_['package_version']}")
    print(f"  extracted rows   : {len(rows)}")
    print(f"  controls: {len(controls)}")
    for kind, n in sorted(collections.Counter(c["kind"] for c in controls).items()):
        print(f"    {kind:8s} {n}")
    print(f"  status rows      : {len(map_['status'])}")
    print("  dropped:")
    for why, n in sorted(report["dropped"].items(), key=lambda kv: (-kv[1], kv[0])):
        print(f"    {n:5d}  {why}")
    panels = collections.Counter((c["section"], c["panel"]) for c in controls)
    print(f"  panels           : {len(panels)}")
    for (section, panel), n in sorted(panels.items(), key=lambda kv: (SECTIONS.index(kv[0][0]), kv[0][1])):
        print(f"    {section} / {panel}: {n}")
    if report["suspected_slips"]:
        print(f"  suspected slips in the vendor's click code, replayed verbatim: {len(report['suspected_slips'])} controls")
        for node, what in report["suspected_slips"].items():
            print(f"    {node}: {'; '.join(what)}")
    for node in report["overrides_on_dropped"]:
        print(f"note: curation names {node}, which the rules leave out", file=sys.stderr)
    for banner in report["unused_banners"]:
        print(f"note: curation names the banner {banner!r}, which no control uses", file=sys.stderr)
    for banner in report["unmapped_banners"]:
        print(f"note: the banner {banner!r} has no panel name in curation (its Title Case is used unless a node "
              f"rule applies)", file=sys.stderr)


if __name__ == "__main__":
    main()
