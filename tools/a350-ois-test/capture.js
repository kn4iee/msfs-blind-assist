// Captures the visible OIS page as a fixture: run through tools/a350-probe/ceval.mjs (see README.md). Never keeps the
// owner's credentials: a text field in a row labelled as an ID, key, logon, password, token or PIN, and every password
// field, is saved blank, and a login code shown as "Code: ..." is saved as 0AAA000A (secrets.test.js checks).
(function(){
  var SECRET_ROW=/USER ID|\bID\b|API KEY|\bKEY\b|LOGON|PASSWORD|TOKEN|SECRET|\bPIN\b/;
  function secret(e){if(/^password$/i.test(e.type||''))return true;var row=e.closest&&e.closest('.option-group');
    var label=row&&row.querySelector('.option-group-label');return !!label&&SECRET_ROW.test((label.textContent||'').toUpperCase());}
  function vis(e){var r=e.getBoundingClientRect();return r.width>0&&r.height>0;}
  var src=document.body.querySelectorAll('*');
  for(var i=0;i<src.length;i++)src[i].setAttribute('data-cap-v',vis(src[i])?'1':'0');
  for(var i=src.length-1;i>=0;i--){var e=src[i];var k=e.getAttribute('data-cap-v')==='1'||e.getAttribute('data-cap-k')==='1';
    if(k&&e.parentElement)e.parentElement.setAttribute('data-cap-k','1');}
  for(var i=0;i<src.length;i++){var e=src[i];if(e.getAttribute('data-cap-v')==='0'&&e.parentElement&&e.parentElement.getAttribute('data-cap-v')==='1'&&e.outerHTML.length<3000){var d=e.querySelectorAll('*');e.setAttribute('data-cap-k','1');for(var q=0;q<d.length;q++)d[q].setAttribute('data-cap-k','1');}}
  var c=document.body.cloneNode(true);
  for(var i=0;i<src.length;i++){src[i].removeAttribute('data-cap-v');src[i].removeAttribute('data-cap-k');}
  var kill=c.querySelectorAll('script,style,svg,canvas,link');
  for(var k=0;k<kill.length;k++)if(kill[k].parentNode)kill[k].parentNode.removeChild(kill[k]);
  function keep(e){return e.getAttribute('data-cap-v')==='1'||e.getAttribute('data-cap-k')==='1';}
  var all=c.querySelectorAll('[data-cap-v]');
  for(var j=0;j<all.length;j++){var e=all[j];if(!e.parentNode||!e.parentElement)continue;
    if(!keep(e)&&e.parentElement.getAttribute('data-cap-v')!=null&&!keep(e.parentElement))e.parentNode.removeChild(e);}
  all=c.querySelectorAll('[data-cap-v]');
  for(var j=0;j<all.length;j++){var e=all[j];if(e.getAttribute('data-cap-v')==='0')e.setAttribute('data-vis','0');
    e.removeAttribute('data-cap-v');e.removeAttribute('data-cap-k');
    e.removeAttribute('data-a350-ois-idx');e.removeAttribute('data-a350-ois-own');if(!/position:\s*fixed|background-color|opacity|width:\s*[\d.]+%/.test(e.getAttribute('style')||''))e.removeAttribute('style');
    if(e.tagName==='INPUT'&&e.value&&!secret(e))e.setAttribute('value',e.value);
    if(e.tagName==='INPUT'&&secret(e))e.removeAttribute('value');
    if(e.tagName==='INPUT'&&e.checked)e.setAttribute('checked','');}
  return c.innerHTML.replace(/\n\s*\n/g,'\n').replace(/(Code:\s*(?:<[^>]*>\s*)*)[0-9A-Z]{6,12}(<)/g,'$10AAA000A$2');
})()
