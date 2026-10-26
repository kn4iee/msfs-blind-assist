#!/bin/bash
# usage: cdu.sh CDU [KEY ...]   (CDU 1 = captain, 2 = first officer)
# Presses each key 700 ms apart through the app's tablet agent (MSFSBA must not hold the BBSTablet view, [DBG-9]),
# then prints the 14 rows: row number | text | colour codes. With no keys it only reads.
# Keys are WRITES TO THE SIM (owner's go-ahead first). Needs the agent installed: run MSFSBA once with the 757
# loaded and close it, or the read answers "noagent".
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(git -C "$HERE" rev-parse --show-toplevel)"
T="$(mktemp -d)"
CDU=$1; shift
for k in "$@"; do
  echo "window.__MSFSBA_BBS757.press($CDU,'$k')" > "$T/press.js"
  pwsh -File "$ROOT/tools/coherent-eval.ps1" -Title BBSTablet -ExprFile "$T/press.js" 2>&1 | tail -1
  sleep 0.7
done
[ $# -gt 0 ] && sleep 1.2
cat > "$T/read.js" <<EOF
(function(){var A=window.__MSFSBA_BBS757; if(!A) return 'noagent'; var r=A.scrape($CDU,''); if(!r.frame) return 'noframe ready='+r.ready; var d=JSON.parse(r.frame).Data; var out=[]; for(var i=0;i<14;i++){var s='',cs=''; for(var j=0;j<24;j++){var c=d[i*24+j]; s+= (c&&c.length)?c[0]:' '; cs += (c&&c.length)?(c[1]||'.'):' ';} out.push(('0'+i).slice(-2)+'|'+s+'|'+cs);} return out.join('\n');})()
EOF
pwsh -File "$ROOT/tools/coherent-eval.ps1" -Title BBSTablet -ExprFile "$T/read.js" 2>&1 | tail -14
rm -rf "$T"
