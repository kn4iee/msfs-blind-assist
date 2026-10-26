"""What changed between two L:var snapshots, minus the variables that drift on their own.

usage: python -I diff_snaps.py <a.json> <b.json> [--noise <file>] [--learn-noise]

Prints "NAME: old -> new" for every name whose value differs, leaving out the names in the noise file (one name per
line). With --learn-noise (needs --noise), the two snapshots must be taken with nothing done between them: every
name that differs is added to the noise file, which is written sorted and de-duplicated, and nothing else is printed.
"""
import argparse
import json
import sys
from pathlib import Path

MISSING = "(missing)"


def load_snapshot(path: Path) -> dict:
    try:
        # utf-8-sig: tolerate a byte order mark; strip(): tolerate a trailing newline.
        data = json.loads(path.read_text(encoding="utf-8-sig").strip())
    except (OSError, ValueError) as error:
        sys.exit(f"diff_snaps.py: cannot read a snapshot from {path}: {error}")
    if not isinstance(data, dict):
        sys.exit(f"diff_snaps.py: {path} is not a name -> value map")
    return data


def load_noise(path: Path) -> set:
    return set(path.read_text(encoding="utf-8-sig").split())


def show(snapshot: dict, name: str) -> str:
    return json.dumps(snapshot[name]) if name in snapshot else MISSING


def main() -> int:
    parser = argparse.ArgumentParser(description="Diff two L:var snapshots.")
    parser.add_argument("a", type=Path, help="the earlier snapshot (JSON)")
    parser.add_argument("b", type=Path, help="the later snapshot (JSON)")
    parser.add_argument("--noise", type=Path, metavar="FILE", help="names to leave out, one per line")
    parser.add_argument(
        "--learn-noise",
        action="store_true",
        help="add every name that differs to the noise file (two snapshots with nothing done between them)",
    )
    args = parser.parse_args()
    if args.learn_noise and args.noise is None:
        parser.error("--learn-noise needs --noise <file>")

    a = load_snapshot(args.a)
    b = load_snapshot(args.b)
    # A name in only one snapshot differs, even from a null (JSON for a NaN) in the other.
    absent = object()
    differing = sorted(name for name in set(a) | set(b) if a.get(name, absent) != b.get(name, absent))

    if args.learn_noise:
        known = load_noise(args.noise) if args.noise.is_file() else set()
        added = [name for name in differing if name not in known]
        merged = sorted(known | set(differing))
        args.noise.write_text("".join(f"{name}\n" for name in merged), encoding="utf-8", newline="\n")
        print(f"added {len(added)} names to {args.noise} ({len(merged)} in total)")
        return 0

    if args.noise is not None and not args.noise.is_file():
        parser.error(f"no noise file at {args.noise} (--learn-noise creates one)")
    noise = load_noise(args.noise) if args.noise else set()
    for name in differing:
        if name not in noise:
            print(f"{name}: {show(a, name)} -> {show(b, name)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
