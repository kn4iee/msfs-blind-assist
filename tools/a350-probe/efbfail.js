(function(T){
  var b=[].filter.call(document.querySelectorAll('button.failure-button'),function(e){return e.innerText.trim()===T})[0];
  if(!b) return 'NOT FOUND '+T;
  b.scrollIntoView(); var r=b.getBoundingClientRect(), o={bubbles:true,cancelable:true,clientX:r.left+5,clientY:r.top+5,button:0,view:window};
  ['mousedown','mouseup','click'].forEach(function(n){b.dispatchEvent(new MouseEvent(n,o))});
  return T+' -> '+(/bg-danger/.test(b.className)?'ACTIVE':'off');
})
