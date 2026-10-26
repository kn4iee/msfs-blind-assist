(function(){
  var out = [];
  var els = document.querySelectorAll('button, input, select, textarea, a, [onclick], [role=button], div, span');
  var seen = 0;
  for (var i = 0; i < els.length && out.length < 60; i++) {
    var e = els[i];
    var txt = (e.innerText || e.value || '').trim().replace(/\s+/g, ' ');
    if (!txt || txt.length > 40) continue;
    if (e.children.length > 0 && e.tagName !== 'BUTTON') continue;
    var r = e.getBoundingClientRect();
    if (r.width === 0) continue;
    out.push(e.tagName + (e.className ? '.' + String(e.className).split(' ').slice(0,2).join('.') : '') + ' "' + txt + '" ptr=' + getComputedStyle(e).cursor);
  }
  return out.join('\n');
})()
