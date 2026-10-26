(function(idx, text){
  var vis=[].filter.call(document.querySelectorAll('input[type=text],input[type=number]'),function(e){return e.getBoundingClientRect().width});
  var t=vis[idx]; if(!t) return 'no input '+idx;
  var r=t.getBoundingClientRect(), o={bubbles:true,cancelable:true,clientX:r.left+5,clientY:r.top+5,button:0,view:window};
  ['mousedown','mouseup','click'].forEach(function(n){t.dispatchEvent(new MouseEvent(n,o))});
  t.focus();
  var set=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set; set.call(t,''); t.dispatchEvent(new Event('input',{bubbles:true}));
  for (var i=0;i<text.length;i++){ var c=text[i], code=c.charCodeAt(0);
    t.dispatchEvent(new KeyboardEvent('keydown',{key:c,keyCode:code,which:code,bubbles:true}));
    t.dispatchEvent(new KeyboardEvent('keypress',{key:c,keyCode:code,charCode:code,which:code,bubbles:true}));
    set.call(t,t.value+c); t.dispatchEvent(new Event('input',{bubbles:true}));
    t.dispatchEvent(new KeyboardEvent('keyup',{key:c,keyCode:code,which:code,bubbles:true})); }
  t.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',keyCode:13,which:13,bubbles:true}));
  t.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',keyCode:13,which:13,bubbles:true}));
  t.dispatchEvent(new Event('change',{bubbles:true})); t.blur();
  return 'typed '+text+' into #'+idx+' -> '+t.value;
})
