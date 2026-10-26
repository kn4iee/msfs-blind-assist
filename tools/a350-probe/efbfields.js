(function(){
  var out = [document.querySelector('.page-title') ? 'TITLE: ' + document.querySelector('.page-title').innerText : 'no title'];
  var els = document.querySelectorAll('input, select, textarea, button');
  for (var i = 0; i < els.length; i++) {
    var e = els[i]; var r = e.getBoundingClientRect(); if (!r.width) continue;
    var lab = '';
    var p = e.parentElement; for (var k = 0; k < 3 && p && !lab; k++) { var t = (p.innerText||'').trim().split('\n')[0]; if (t && t.length < 40) lab = t; p = p.parentElement; }
    var v = e.tagName === 'SELECT' ? (e.options[e.selectedIndex] ? e.options[e.selectedIndex].text : '') + ' of [' + [].map.call(e.options, function(o){return o.text;}).slice(0,8).join('|') + ']' : (e.value || e.innerText || '').trim();
    out.push(e.tagName + (e.type ? ':' + e.type : '') + ' label="' + lab + '" value="' + v.slice(0,80) + '"' + (e.disabled ? ' DISABLED' : ''));
  }
  return out.join('\n');
})()
