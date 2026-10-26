# usage: wclick.sh <view needle> <guid> <x> <y>
node ceval.mjs "$1" -e "(function(){var g=$2,x=$3,y=$4;Coherent.call('WASM_MOUSE_MOVE',g,x,y,0);Coherent.call('WASM_MOUSE_DOWN',g,x,y,0);setTimeout(function(){Coherent.call('WASM_MOUSE_UP',g,x,y,0);Coherent.call('WASM_CLICK',g,x,y,0);},120);return 'clicked '+x+','+y})()"
