(function(T, nth){
  var els = document.querySelectorAll('*'), hits = [];
  for (var i = 0; i < els.length; i++) { var e = els[i]; var r = e.getBoundingClientRect(); if (!r.width) continue;
    var t = (e.innerText || '').trim().replace(/\s+/g,' '); if (t === T) hits.push(e); }
  // deepest match
  hits = hits.filter(function(e){ return !hits.some(function(o){ return o !== e && e.contains(o); }); });
  var e = hits[nth || 0]; if (!e) return 'NOT FOUND ' + T + ' (' + hits.length + ')';
  var r = e.getBoundingClientRect(), x = r.left + r.width/2, y = r.top + r.height/2;
  var o = {bubbles:true, cancelable:true, clientX:x, clientY:y, button:0, view:window};
  ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(function(n){ try { e.dispatchEvent(n.indexOf('pointer')===0 && window.PointerEvent ? new PointerEvent(n,o) : new MouseEvent(n,o)); } catch(x) { e.dispatchEvent(new MouseEvent(n.replace('pointer','mouse'),o)); } });
  return 'clicked ' + e.tagName + '.' + e.className + ' of ' + hits.length;
})
