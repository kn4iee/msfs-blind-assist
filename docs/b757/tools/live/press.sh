#!/bin/bash
# usage: press.sh LVAR [holdms] [wait_s]
# WRITES TO THE SIM (owner's go-ahead first): snapshot, LVAR = 1, held holdms (250), LVAR = 0,
# wait wait_s (2), snapshot again, and print what changed (minus noise.txt).
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(git -C "$HERE" rev-parse --show-toplevel)"
T="$(mktemp -d)"
H=${2:-250}
"$HERE/snap.sh" "$T/before.json"
echo "(function(){SimVar.SetSimVarValue('L:$1','number',1); setTimeout(function(){SimVar.SetSimVarValue('L:$1','number',0);},$H); return 'pressed';})()" > "$T/press.js"
pwsh -File "$ROOT/tools/coherent-eval.ps1" -Title BBSTablet -ExprFile "$T/press.js" 2>&1 | tail -1
sleep "${3:-2}"
"$HERE/snap.sh" "$T/after.json"
python -I "$HERE/diff_snaps.py" "$T/before.json" "$T/after.json" "$HERE/noise.txt"
rm -rf "$T"
