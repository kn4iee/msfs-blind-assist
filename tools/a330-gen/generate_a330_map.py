"""Generates MSFSBlindAssist/Resources/a330_control_map.json (the iniBuilds A330, MSFS 2024) from
tools/a330-gen/curation.json. The generator holds no control data: it loads the curation file, checks it
(see README.md) and writes the map.

The aircraft is a streamed, ENCRYPTED package, so nothing here is read from its files. Every control
is one of the aircraft's own FS2024 input events (SimConnect EnumerateInputEvents, 649 of them), and
every read-back and position in the curation file was MEASURED live on 2026-10-16 by a differential
probe: set the input event, snapshot all 1,936 INI_* L:vars the panel-state file names, record what
moved, put it back. The results live in the curation file; the probe's raw output is not kept.

Conventions the probe found (do not "correct" them by intuition):
  * a push-button's INPUT is the opposite of its L:var: input 0 = pushed in (on/auto, light off) and
    the L:var reads 1 (PACK 1, the batteries, generators, bleeds, hydraulic pumps, PRIMs/SECs);
    superseded 2026-11-27 for the 73 push-buttons that toggle ("mode": "toggle" with "press"): each
    is pressed by ONE set of its press value, and its input event's own value says nothing about its
    state (README.md);
  * a THREE-position switch numbers from the TOP: 0 = the top position (strobe ON, no-smoking ON,
    nose light T.O., annunciator light TEST); a TWO-position switch has 1 = the top (beacon ON);
  * a momentary key (ECAM page keys, master warning/caution, calls, agents) ignores a raw value: it
    is pressed with the event's own _Push / _Release actions ("mode": "push");
  * where a switch moves no L:var at all (probe heat, the centre tank pumps, the wipers...), its read
    is the input event's own value ("B:EVENT"): the app subscribes to it.

The measurement history (what was and was not probed, the later ground and flight measurements) is in
README.md.

Run:  python tools/a330-gen/generate_a330_map.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
CURATION = os.path.join(HERE, "curation.json")
KEYS = os.path.join(HERE, "keys.txt")
CATALOGUE = os.path.join(HERE, "data", "input_events.tsv")
OUT = os.path.join(HERE, "..", "..", "MSFSBlindAssist", "Resources", "a330_control_map.json")

# True while plan 04 is open: the curation may then hold a `not_yet_offered` list (events the catalogue
# has that no control or left_out entry accounts for yet). The plan's Task 5 empties the list and
# removes this allowance.
PLAN_04_OPEN = True

UNPROVEN = os.path.join(HERE, "unproven.txt")

# Fields of a curation control the map carries after every other field, in this order, when present.
EMITTED_LAST = ("guard", "state", "unit", "pressesToCycle", "proof")
# Fields the generator places itself, in the map's order; every other field of a control follows them.
STRUCTURAL = {"key", "section", "panel", "kind", "mode", "read", "positions"} | set(EMITTED_LAST)


class CurationError(ValueError):
    """The curation file (or the catalogue it is checked against) is wrong; the message names why."""


def _no_repeated_key(pairs):
    seen = {}
    for k, v in pairs:
        if k in seen:
            raise CurationError(f"the curation repeats the key {k!r} in one object")
        seen[k] = v
    return seen


def load_curation(path=CURATION):
    with open(path, encoding="utf-8") as f:
        return json.load(f, object_pairs_hook=_no_repeated_key)


def load_keys(path=KEYS):
    with open(path, encoding="utf-8") as f:
        return [line.strip() for line in f if line.strip()]


def load_catalogue(path=CATALOGUE):
    """The simulator's input-event catalogue: a first line with the date and the simulator's version,
    then `name<TAB>hash` lines. Returns (header, [names]). The catalogue is committed, so a missing one
    is an error: the map is never generated without its check."""
    if not os.path.exists(path):
        raise CurationError(f"{path} is missing: the input-event catalogue is committed, and the map is never "
                            f"generated without checking it")
    with open(path, encoding="utf-8") as f:
        lines = [line.rstrip("\r\n") for line in f]
    if not lines or not lines[0].strip():
        raise CurationError(f"{path}: the first line must give the capture's date and the simulator's version")
    names = []
    for n, line in enumerate(lines[1:], start=2):
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) != 2 or not parts[0] or not parts[1]:
            raise CurationError(f"{path} line {n}: expected name<TAB>hash, got {line!r}")
        names.append(parts[0])
    return lines[0], names


def control_event(entry_id, entry):
    """A control's input event is its id in the curation's `controls`; a control with no event
    (a read-out) is identified by its own key."""
    return None if entry_id == entry["key"] else entry_id


def check_catalogue(curation, catalogue_names):
    """Every input event in the catalogue is a control or left out with a reason; nothing else is."""
    controls = curation["controls"]
    events = {control_event(i, e) for i, e in controls.items()} - {None}
    left_out = curation.get("left_out", {})
    not_yet = curation.get("not_yet_offered", [])
    for ev, why in left_out.items():
        if ev in events:
            raise CurationError(f"{ev} is both a control ({controls[ev]['key']}) and left out: pick one")
        if not isinstance(why, str) or not why.strip():
            raise CurationError(f"left_out {ev} has no reason")
    for ev in not_yet:
        if ev in events:
            raise CurationError(f"{ev} is both a control ({controls[ev]['key']}) and in not_yet_offered: pick one")
        if ev in left_out:
            raise CurationError(f"{ev} is both left out and in not_yet_offered: pick one")
    if not_yet and not PLAN_04_OPEN:
        raise CurationError(f"not_yet_offered is not allowed once plan 04 is closed ({len(not_yet)} events: {sorted(not_yet)[:3]}...)")
    known = set(catalogue_names)
    for ev in sorted(events):
        if ev not in known:
            raise CurationError(f"control {controls[ev]['key']} names the input event {ev}, which the catalogue lacks")
    for ev in sorted(set(left_out) | set(not_yet)):
        if ev not in known:
            raise CurationError(f"{ev} (left_out or not_yet_offered) is not in the catalogue")
    accounted = events | set(left_out) | set(not_yet)
    for ev in catalogue_names:
        if ev not in accounted:
            raise CurationError(f"input event {ev} is neither a control nor left out")


def check_keys(curation, known_keys):
    """No key the map has ever had disappears, unless it is retired with a reason."""
    now = {e["key"] for e in curation["controls"].values()}
    retired = curation.get("retired_keys", {})
    for k in known_keys:
        if k not in now and k not in retired:
            raise CurationError(f"key {k} is in keys.txt but no control carries it (retire it in retired_keys, with the commit and reason, or restore it)")
    for k, why in retired.items():
        if k in now:
            raise CurationError(f"key {k} is retired but a control still carries it")
        if not why:
            raise CurationError(f"retired key {k} has no reason")


def check_curation(curation):
    """The checks that need no catalogue: unique keys, labels, panels."""
    controls = curation["controls"]
    keys = [e["key"] for e in controls.values()]
    dup = sorted({k for k in keys if keys.count(k) > 1})
    if dup:
        raise CurationError(f"duplicate keys: {dup}")
    name_over = curation.get("name_overrides", {})
    pos_over = curation.get("position_overrides", {})
    by_key = {e["key"]: e for e in controls.values()}
    for k in name_over:
        if k not in by_key:
            raise CurationError(f"name_overrides names {k}, which no control carries")
    for k, names in pos_over.items():
        if k not in by_key:
            raise CurationError(f"position_overrides names {k}, which no control carries")
        have = {p["name"] for p in by_key[k].get("positions", [])}
        for n in names:
            if n not in have:
                raise CurationError(f"position_overrides names position {n!r} of {k}, which the control does not have")
    for entry_id, e in controls.items():
        k = e["key"]
        if "release" in e:
            raise CurationError(f"control {k} has a release value: a push-button that toggles is pressed by ONE set of "
                                f"its press value, and a second set is a second press (README.md, 2026-11-27)")
        if k not in name_over or not name_over[k].get("label"):
            raise CurationError(f"control {k} has no label (no name override, and no aircraft string is available yet)")
        for p in e.get("positions", []):
            if p["name"] not in pos_over.get(k, {}) or not pos_over[k][p["name"]].get("label"):
                raise CurationError(f"control {k} position {p['name']!r} has no label")
    sections = curation["panel_order"]
    own = set(curation.get("own_panels", []))
    used = {(e["section"], e["panel"]) for e in controls.values()}
    for section, panels in sections.items():
        for p in panels:
            if (section, p) not in used and p not in own:
                raise CurationError(f"empty panel {section}/{p}")
    for section, panel in sorted(used):
        if panel not in sections.get(section, []):
            raise CurationError(f"a control sits in {section}/{panel}, which panel_order does not list")


def build_controls(curation):
    name_over = curation["name_overrides"]
    pos_over = curation["position_overrides"]
    out = []
    for entry_id, e in curation["controls"].items():
        k = e["key"]
        c = {"key": k, "section": e["section"], "panel": e["panel"], "label": name_over[k]["label"],
             "kind": e["kind"], "event": control_event(entry_id, e), "mode": e["mode"]}
        if "read" in e:
            c["read"] = e["read"]
        if e.get("positions"):
            c["positions"] = [{"value": p["value"], "input": p["input"], "label": pos_over[k][p["name"]]["label"],
                               "proven": p["proven"]}
                              for p in e["positions"]]
        for x, v in e.items():
            if x not in STRUCTURAL:
                c[x] = v
        for x in EMITTED_LAST:
            if x in e:
                c[x] = e[x]
        out.append(c)
    return out


def invariant_number(v):
    """A number as text, the same on every PC: no culture, no trailing .0 on a whole number."""
    return str(int(v)) if float(v).is_integer() else repr(float(v))


def build_unproven(curation):
    """The offered positions whose names no indication has proven, one line each: key, value, label."""
    pos_over = curation["position_overrides"]
    rows = []
    for e in curation["controls"].values():
        for p in e.get("positions", []):
            if not p["proven"]:
                rows.append((e["key"], p["value"], pos_over[e["key"]][p["name"]]["label"]))
    rows.sort(key=lambda r: (r[0], r[1]))
    head = ("# Positions the map offers whose names no indication has proven yet (generated from curation.json by "
            "generate_a330_map.py; do not edit): key<TAB>value<TAB>label\n")
    return head + "".join(f"{k}\t{invariant_number(v)}\t{lab}\n" for k, v, lab in rows)


def build_doc(curation):
    sections = [{"name": n, "panels": p} for n, p in curation["panel_order"].items()]
    return {"version": 1, "sections": sections, "controls": build_controls(curation)}


def main():
    curation = load_curation()
    check_curation(curation)
    check_keys(curation, load_keys())
    check_catalogue(curation, load_catalogue(CATALOGUE)[1])
    doc = build_doc(curation)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
        f.write("\n")
    with open(UNPROVEN, "w", encoding="utf-8", newline="\n") as f:
        f.write(build_unproven(curation))
    print(f"wrote {len(doc['controls'])} controls to {os.path.normpath(OUT)}")


if __name__ == "__main__":
    try:
        main()
    except CurationError as ex:
        raise SystemExit(f"curation error: {ex}")
