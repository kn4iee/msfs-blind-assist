(function(T, nth){
  var els=[].filter.call(document.querySelectorAll('button, input[type=button], input[type=submit], .is-button'),function(e){ return ((e.value||e.innerText||'').trim()===T) && e.getBoundingClientRect().width; });
  var e=els[nth||0]; if(!e) return 'NOT FOUND '+T+' ('+els.length+')';
  var r=e.getBoundingClientRect(), o={bubbles:true,cancelable:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2,button:0,view:window};
  ['mousedown','mouseup','click'].forEach(function(n){e.dispatchEvent(new MouseEvent(n,o))});
  return 'clicked '+T+' of '+els.length;
})
