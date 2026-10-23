"""Survey an installed MSFS aircraft package, read only, for everything MSFSBA can read or drive: variants, displays,
L:vars, B: input events, H:/K: events, cockpit tooltips, local endpoints, WASM modules and manuals.

Usage:  python -I survey_package.py <package-folder> <output-folder>
        (the package folder is the one holding manifest.json and layout.json, e.g. Community\\<vendor>-aircraft-757)
Needs:  nothing running. Reads the package's files only; never executes, imports or writes inside the package.
Output (in <output-folder>, every table tab-separated and sorted):
  summary.md          manifest, file counts by type, what is encrypted, the headline counts below
  variants.tsv        every aircraft.cfg: title, atc_model, icao_type_designator, ui_variation, ui_type
  displays.tsv        every panel.cfg gauge: HTML instrument path or WASM module + gauge name, size
  lvars.tsv           L:var name, references, files (behavior XML, JS, WASM strings)
  input-events.tsv    B: input event IDs (compiled *.behavior.xml) with their tooltip keys, plus B: references
  events.tsv          H: and K: events referenced, with counts
  tooltips.tsv        source behavior XML: template, node, tooltip text or key, resolved through the package locPak
  templates.tsv       behavior templates used, with counts (shows whose template library the cockpit is built on)
  endpoints.tsv       URLs, ports, WebSocket and Coherent calls found in JS/HTML/JSON/cfg
  wasm-strings/<module>.txt  identifier-like strings from each WASM module (L:/H: names, client data, CDU hints)
  documents.tsv       PDFs and other manuals shipped in the package
The aircraft's own files are the vendor's: never commit them. These outputs are lists of names, fine to commit as
evidence after a read.
"""
import collections
import json
import pathlib
import re
import sys

TEXT_EXT = {".xml", ".js", ".mjs", ".html", ".htm", ".json", ".cfg", ".css", ".txt", ".ini", ".flt"}
LVAR_RE = re.compile(r"\bL:([A-Za-z_][A-Za-z0-9_#:.\-]*[A-Za-z0-9_#])")
BVAR_RE = re.compile(r"\bB:([A-Za-z_][A-Za-z0-9_#]*)")
HEVT_RE = re.compile(r"\bH:([A-Za-z_][A-Za-z0-9_#.:\-]*[A-Za-z0-9_#])")
KEVT_RE = re.compile(r"\bK:([A-Z_][A-Z0-9_#]*)")
URL_RE = re.compile(r"""(?:https?|wss?)://[^\s"'`<>)]+|(?:localhost|127\.0\.0\.1):\d{2,5}""")
CALL_RE = re.compile(r"""\b(Coherent\.(?:call|on|trigger)|RegisterViewListener|new\s+WebSocket|fetch)\s*\(\s*["'`]?([^"'`),]{0,80})""")
INPUT_EVENT_RE = re.compile(r'<InputEvent\b[^>]*\bID="([^"]+)"[^>]*>(.*?)</InputEvent>', re.S)
TT_DESC_RE = re.compile(r"<TTDescription>([^<]*)</TTDescription>")
USE_TEMPLATE_RE = re.compile(r'<UseTemplate\s+Name="([^"]+)"\s*>(.*?)</UseTemplate>', re.S)
TEMPLATE_NAME_RE = re.compile(r'<UseTemplate\s+Name="([^"]+)"')
TAG_RE = r"<{0}>([^<]*)</{0}>"
WASM_STR_RE = re.compile(rb"[\x20-\x7e]{6,}")
WASM_KEEP_RE = re.compile(r"\b[LHKB]:[A-Za-z_]|^[A-Z][A-Z0-9_]{5,}$|^[A-Za-z0-9_]+\.[A-Za-z0-9_.]+$|ClientData|CDU|FMC|MCDU|EICAS|EFB|http|ws://|localhost|127\.0\.0\.1|8320|winwing|wingflex|mobiflight", re.I)
CFG_KEYS = ("title", "atc_model", "atc_type", "icao_type_designator", "icao_manufacturer", "icao_model",
            "ui_manufacturer", "ui_type", "ui_variation", "ui_typerole", "atc_id")


def read_text(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def load_loc(pkg: pathlib.Path) -> dict:
    """The package's en-US strings (locPak is JSON), so TT: keys and @ keys read as words."""
    for name in ("en-US.locPak", "en-GB.locPak"):
        p = pkg / name
        if p.is_file():
            try:
                return json.loads(read_text(p)).get("LocalisationPackage", {}).get("Strings", {})
            except (ValueError, AttributeError):
                pass
    return {}


def resolve(text: str, loc: dict) -> str:
    key = text.strip()
    for prefix in ("TT:", "@"):
        if key.startswith(prefix):
            key = key[len(prefix):]
    return loc.get(key, "")


def rel(path: pathlib.Path, pkg: pathlib.Path) -> str:
    return path.relative_to(pkg).as_posix()


def write_tsv(path: pathlib.Path, header: list, rows: list) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(header) + "\n")
        for row in sorted(rows, key=lambda r: [str(c).lower() for c in r]):
            f.write("\t".join(str(c).replace("\t", " ").replace("\n", " ") for c in row) + "\n")


def parse_cfg(text: str) -> list:
    """[(section, key, value)] from an MSFS .cfg, comments stripped, quotes removed."""
    out, section = [], ""
    for line in text.splitlines():
        line = line.split(";", 1)[0].strip()
        if line.startswith("[") and "]" in line:
            section = line[1:line.index("]")]
        elif "=" in line:
            key, value = line.split("=", 1)
            out.append((section, key.strip().lower(), value.strip().strip('"')))
    return out


def main(pkg: pathlib.Path, out: pathlib.Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    (out / "wasm-strings").mkdir(exist_ok=True)
    loc = load_loc(pkg)
    files = [p for p in pkg.rglob("*") if p.is_file()]
    by_ext = collections.Counter(p.suffix.lower() or "(none)" for p in files)

    refs = {kind: collections.defaultdict(lambda: [0, set()]) for kind in ("L", "B", "H", "K")}
    variants, displays, input_events, tooltips, endpoints, documents = [], [], [], [], [], []
    templates = collections.Counter()
    wasm_rows = []

    def note(kind: str, name: str, where: str) -> None:
        entry = refs[kind][name]
        entry[0] += 1
        entry[1].add(where)

    for path in files:
        ext = path.suffix.lower()
        where = rel(path, pkg)
        if ext == ".pdf" or (ext in (".txt", ".rtf", ".docx") and "doc" in where.lower()):
            documents.append((where, path.stat().st_size))
        if ext == ".wasm":
            data = path.read_bytes()
            kept = sorted({s.decode("ascii") for s in WASM_STR_RE.findall(data)
                           if WASM_KEEP_RE.search(s.decode("ascii"))})
            (out / "wasm-strings" / (path.stem + ".txt")).write_text("\n".join(kept) + "\n", encoding="utf-8")
            wasm_rows.append((where, len(data), len(kept)))
            for s in kept:
                for m in LVAR_RE.finditer(s):
                    note("L", m.group(1), where)
                for m in HEVT_RE.finditer(s):
                    note("H", m.group(1), where)
            continue
        if ext not in TEXT_EXT and not path.name.endswith(".locPak"):
            continue
        if path.name.endswith(".locPak"):
            continue
        text = read_text(path)
        for kind, rx in (("L", LVAR_RE), ("B", BVAR_RE), ("H", HEVT_RE), ("K", KEVT_RE)):
            for m in rx.finditer(text):
                note(kind, m.group(1), where)
        if ext in (".js", ".mjs", ".html", ".htm", ".json", ".cfg"):
            for m in URL_RE.finditer(text):
                endpoints.append(("url", m.group(0), where))
            for m in CALL_RE.finditer(text):
                arg = m.group(2).strip()
                if arg and not arg.startswith(("_0x", "this[")):  # skip minified/obfuscated arguments
                    endpoints.append((re.sub(r"\s+", " ", m.group(1)), arg, where))
        if path.name.lower() == "aircraft.cfg":
            cfg = parse_cfg(text)
            sections = sorted({s for s, _, _ in cfg})
            for section in sections:
                values = {k: v for s, k, v in cfg if s == section and k in CFG_KEYS}
                if values:
                    variants.append((where, section) + tuple(values.get(k, "") for k in CFG_KEYS))
        if path.name.lower() == "panel.cfg":
            cfg = parse_cfg(text)
            for section, key, value in cfg:
                if key.startswith(("htmlgauge", "gauge")) or key in ("size_mm", "texture", "pixel_size"):
                    kind = "wasm" if "wasm_module=" in value or value.endswith(".wasm") else "html" if "html" in value.lower() else ""
                    displays.append((where, section, key, kind, value))
        if ext == ".xml":
            for m in INPUT_EVENT_RE.finditer(text):
                descs = TT_DESC_RE.findall(m.group(2))
                desc = descs[0] if descs else ""
                input_events.append((m.group(1), desc, resolve(desc, loc), where))
            for name in TEMPLATE_NAME_RE.findall(text):
                templates[name] += 1
            for m in USE_TEMPLATE_RE.finditer(text):
                body = m.group(2)
                fields = {}
                for tag in ("NODE_ID", "ANIM_NAME", "PART_ID", "TOOLTIPID", "TOOLTIP_TITLE", "TT_DESCRIPTION_ID",
                            "TOOLTIP_DESCRIPTION", "INPUT_EVENT_ID_SOURCE", "ID"):
                    t = re.search(TAG_RE.format(tag), body)
                    if t:
                        fields[tag] = t.group(1).strip()
                tip = fields.get("TOOLTIPID") or fields.get("TOOLTIP_TITLE") or fields.get("TT_DESCRIPTION_ID") or fields.get("TOOLTIP_DESCRIPTION", "")
                node = fields.get("NODE_ID") or fields.get("PART_ID") or fields.get("ANIM_NAME") or fields.get("ID", "")
                if tip or node:
                    tooltips.append((m.group(1), node, tip, resolve(tip, loc), where))

    write_tsv(out / "variants.tsv", ["file", "section"] + list(CFG_KEYS), variants)
    write_tsv(out / "displays.tsv", ["file", "section", "key", "kind", "value"], displays)
    write_tsv(out / "tooltips.tsv", ["template", "node", "tooltip", "tooltip_text", "file"], tooltips)
    write_tsv(out / "templates.tsv", ["template", "uses"], list(templates.items()))
    write_tsv(out / "endpoints.tsv", ["kind", "value", "file"], sorted(set(endpoints)))
    write_tsv(out / "documents.tsv", ["file", "bytes"], documents)
    write_tsv(out / "lvars.tsv", ["name", "refs", "files"],
              [(n, c, "; ".join(sorted(w))[:400]) for n, (c, w) in refs["L"].items()])
    defined = collections.defaultdict(set)
    for ie_id, key, text, where in input_events:
        defined[(ie_id, key, text)].add(where)
    b_rows = [(i, k, t, "; ".join(sorted(w))[:300]) for (i, k, t), w in defined.items()]
    b_rows += [(n, "", "", "ref: " + "; ".join(sorted(w))[:300]) for n, (c, w) in refs["B"].items()]
    write_tsv(out / "input-events.tsv", ["id", "tooltip_key", "tooltip_text", "files"], b_rows)
    write_tsv(out / "events.tsv", ["kind", "name", "refs", "files"],
              [(k, n, c, "; ".join(sorted(w))[:300]) for k in ("H", "K") for n, (c, w) in refs[k].items()])

    manifest = {}
    if (pkg / "manifest.json").is_file():
        try:
            manifest = json.loads(read_text(pkg / "manifest.json"))
        except ValueError:
            pass
    encrypted = by_ext.get(".fsarchive", 0) + by_ext.get(".spb", 0)
    unique_ie = sorted({row[0] for row in input_events})
    lines = [
        f"# Package survey: {pkg.name}",
        "",
        f"- Title: {manifest.get('title', '?')} · creator: {manifest.get('creator', '?')} · version: "
        f"{manifest.get('package_version', '?')} · minimum game version: {manifest.get('minimum_game_version', '?')}",
        f"- Files: {len(files)}; by type: " + ", ".join(f"{e} {n}" for e, n in by_ext.most_common(20)),
        f"- Encrypted or binary-only containers (.fsarchive/.spb): {encrypted}",
        f"- Variants (aircraft.cfg sections with a title or model): {len(variants)}",
        f"- Displays: {sum(1 for d in displays if d[3] == 'html')} HTML gauges, {sum(1 for d in displays if d[3] == 'wasm')} WASM gauges",
        f"- WASM modules: " + (", ".join(f"{w} ({b} bytes, {k} strings kept)" for w, b, k in wasm_rows) or "none"),
        f"- L:vars named: {len(refs['L'])}; B: input event IDs defined: {len(unique_ie)}, B: names referenced: {len(refs['B'])}",
        f"- H: events: {len(refs['H'])}; K: events: {len(refs['K'])}",
        f"- Tooltipped templates: {len(tooltips)}; distinct templates: {len(templates)}; localization strings: {len(loc)}",
        f"- Endpoints and calls: {len(set(endpoints))}; documents: {len(documents)}",
        "",
        "Template families (top 15): " + ", ".join(f"{n} ({c})" for n, c in templates.most_common(15)),
    ]
    (out / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))


if __name__ == "__main__":
    sys.stdout.reconfigure(errors="replace")
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    package = pathlib.Path(sys.argv[1]).resolve()
    if not (package / "layout.json").is_file() and not (package / "manifest.json").is_file():
        sys.exit(f"{package} has no manifest.json or layout.json: pass the package folder itself")
    main(package, pathlib.Path(sys.argv[2]).resolve())
