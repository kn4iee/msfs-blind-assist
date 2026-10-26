"""Generate snap.js: one JavaScript expression that reads every BlueBird L:var in one Coherent call.

usage: python -I make_snap_js.py <BBS752.wasm> <lvars.tsv> <out.js>

The names are the printable ASCII strings (5+ characters) of BlueBird's systems module that look like one of its
variable prefixes, plus the first column of docs/b757/evidence/package/lvars.tsv, sorted and de-duplicated.
The module is only read as bytes, never run. The expression is ES5 (the Coherent view is an old Chromium): it
returns JSON.stringify of a name -> value map, and a name that throws is left out of the map.
"""
import argparse
import json
import re
import sys
from pathlib import Path

# The prefixes BlueBird's own L:vars use (docs/b757/evidence/wasm-findings.md).
WASM_NAME = re.compile(r"^(B757|BBS|BBS757|FMC|ENGINE|GO_AROUND|OVHD)_[A-Za-z0-9_]+$")
PRINTABLE_RUN = re.compile(rb"[\x20-\x7e]{5,}")

# One line, ES5: var, no arrow functions. The names array is spliced in at NAMES.
EXPRESSION = (
    "(function(){var n=NAMES;var o={};"
    "for(var i=0;i<n.length;i++){try{o[n[i]]=SimVar.GetSimVarValue(\"L:\"+n[i],\"number\");}catch(e){}}"
    "return JSON.stringify(o);})()"
)


def wasm_names(path: Path) -> set[str]:
    data = path.read_bytes()
    names = set()
    for run in PRINTABLE_RUN.finditer(data):
        text = run.group().decode("ascii")
        if WASM_NAME.match(text):
            names.add(text)
    return names


def tsv_names(path: Path) -> set[str]:
    names = set()
    for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines()):
        name = line.split("\t", 1)[0].strip()
        if not name or (number == 0 and name == "name"):
            continue
        names.add(name)
    return names


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate snap.js, the one-call read of every BlueBird L:var.")
    parser.add_argument("wasm", type=Path, help="BBS752.wasm (read as bytes only)")
    parser.add_argument("tsv", type=Path, help="docs/b757/evidence/package/lvars.tsv (header: name, refs, files)")
    parser.add_argument("out", type=Path, help="the JavaScript file to write (snap.js)")
    args = parser.parse_args()

    for path in (args.wasm, args.tsv):
        if not path.is_file():
            parser.error(f"not a file: {path}")

    from_wasm = wasm_names(args.wasm)
    from_tsv = tsv_names(args.tsv)
    names = sorted(from_wasm | from_tsv)
    if not from_wasm:
        print(f"warning: no BlueBird variable names in {args.wasm}; is it the right module?", file=sys.stderr)
    if not names:
        print("error: no names at all, nothing to write", file=sys.stderr)
        return 1

    # json.dumps gives an ASCII, correctly escaped JavaScript array literal.
    expression = EXPRESSION.replace("NAMES", json.dumps(names))
    args.out.write_text(expression, encoding="utf-8", newline="")
    print(
        f"{len(names)} names ({len(from_wasm)} from the module, {len(from_tsv)} from the tsv, "
        f"{len(from_wasm & from_tsv)} in both) -> {args.out}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
