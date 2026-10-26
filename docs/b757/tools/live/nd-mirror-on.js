(function(){
 var src=null; var imgs=document.querySelectorAll('img');
 for(var i=0;i<imgs.length;i++){var r=imgs[i].getBoundingClientRect(); if(Math.round(r.width)===1397){src=imgs[i].getAttribute('src');}}
 if(!src) return 'no efb img';
 var old=document.getElementById('msfsba_mirror'); if(old) old.parentNode.removeChild(old);
 var rects=[[21,8,697,640],[21,735,704,859],[17,1609,849,689],[892,1609,849,689],[1630,735,704,859],[1631,8,697,640],[2671,1224,1293,156]];
 var host=document.createElement('div'); host.id='msfsba_mirror';
 for(var k=0;k<rects.length;k++){var q=rects[k]; var im=document.createElement('img'); im.src=src; im.style.cssText='position:absolute;left:'+q[0]+'px;top:'+q[1]+'px;width:'+q[2]+'px;height:'+q[3]+'px;z-index:99999;pointer-events:none;'; host.appendChild(im);}
 document.body.appendChild(host); return 'mirror on '+src;
})()
