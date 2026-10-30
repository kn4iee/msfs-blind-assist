// Captures the FAP's visible page as a fixture: run through tools/a350-probe/ceval.mjs (see README.md). The FAP keeps every page in
// the DOM; a page it hides is dropped whole, and every style stays, as the page's state is in its paint (a door's colour, a tank's bar).
(function(){
  var src=document.querySelectorAll('#iniFAP, #iniFAP *');
  function vis(e){var r=e.getBoundingClientRect();return r.width>0&&r.height>0;}
  for(var i=0;i<src.length;i++)src[i].setAttribute('data-cap-v',vis(src[i])?'1':'0');
  var c=document.getElementById('iniFAP').cloneNode(true);
  for(var i=0;i<src.length;i++)src[i].removeAttribute('data-cap-v');
  var kill=c.querySelectorAll('script,style,svg,canvas,link,.hiddenPage,#login');
  for(var k=0;k<kill.length;k++)if(kill[k].parentNode)kill[k].parentNode.removeChild(kill[k]);
  var all=c.querySelectorAll('[data-cap-v]');
  for(var j=0;j<all.length;j++){var e=all[j];
    if(e.getAttribute('data-cap-v')==='0'&&!e.classList.contains('visiblePage'))e.setAttribute('data-vis','0');
    e.removeAttribute('data-cap-v');e.removeAttribute('data-a350-fap-idx');e.removeAttribute('data-a350-fap-own');
    if(e.tagName==='IMG'){var s=e.getAttribute('src')||'';e.setAttribute('src',s.replace(/^.*\//,''));}}
  c.removeAttribute('data-cap-v');
  var toasts=document.querySelectorAll('.notie-container'),t='';
  for(var q=0;q<toasts.length;q++)t+=toasts[q].outerHTML;
  return c.outerHTML.replace(/\n\s*\n/g,'\n')+t;
})()
