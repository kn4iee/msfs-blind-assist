(function(label, val){
  var els=document.querySelectorAll('input'), t=null;
  for (var i=0;i<els.length;i++){ var e=els[i], p=e.parentElement, lab='';
    for (var k=0;k<3&&p&&!lab;k++){ var s=(p.innerText||'').trim().split('\n')[0]; if (s&&s.length<40) lab=s; p=p.parentElement; }
    if (lab===label && e.getBoundingClientRect().width) { t=e; break; } }
  if(!t) return 'no field '+label;
  t.focus(); t.dispatchEvent(new FocusEvent('focus')); 
  var set=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set; set.call(t,val);
  t.dispatchEvent(new Event('input',{bubbles:true})); t.dispatchEvent(new Event('change',{bubbles:true}));
  t.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',keyCode:13,bubbles:true})); t.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',keyCode:13,bubbles:true}));
  t.blur(); t.dispatchEvent(new FocusEvent('blur'));
  return 'set '+label+' -> '+t.value;
})
