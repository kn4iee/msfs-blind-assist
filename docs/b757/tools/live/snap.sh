#!/bin/bash
# usage: snap.sh out.json
# Reads every BlueBird L:var named in snap.js in one call in the BBSTablet view and writes the JSON map.
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(git -C "$HERE" rev-parse --show-toplevel)"
pwsh -File "$ROOT/tools/coherent-eval.ps1" -Title BBSTablet -ExprFile "$HERE/snap.js" 2>&1 | tail -1 > "$1"
