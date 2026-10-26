#!/bin/bash
# usage: efb-tap.sh x,y [x,y ...]   (EFB-local points, 1.5 s apart)
# TAPS BLUEBIRD'S EFB (owner's go-ahead first; a toggle tile is tapped only after its state is read, [B757-2]).
# Injects the app's EFB agent into the BBSNavigraphMap view first, then taps through the tablet's own handlers.
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(git -C "$HERE" rev-parse --show-toplevel)"
T="$(mktemp -d)"
pre=(-PreFile "$ROOT/MSFSBlindAssist/Resources/coherent-bbs757-efb-agent.js")
for p in "$@"; do
  x=${p%,*}; y=${p#*,}
  echo "JSON.stringify(window.__MSFSBA_BBS757EFB.click($x,$y))" > "$T/tap.js"
  pwsh -File "$ROOT/tools/coherent-eval.ps1" -Title BBSNavigraphMap "${pre[@]}" -ExprFile "$T/tap.js" 2>&1 | tail -1
  pre=()
  sleep 1.5
done
rm -rf "$T"
