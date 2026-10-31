import fs from 'node:fs';import path from 'node:path';import assert from 'node:assert/strict';import {pathToFileURL} from 'node:url';import crypto from 'node:crypto';
const out=path.resolve('outputs/Test106-UI-Polish');
const game=process.env.CURBSIDE_GAME_UI||'C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Content/Game/UI';
const js=fs.readFileSync(path.join(game,'index.js'),'utf8');
function nativeFunction(name){const start=js.indexOf('function '+name+'(');assert(start>=0,'Native function unavailable: '+name);const body=js.indexOf('{',start);let depth=1,end=body+1;while(depth&&end<js.length){if(js[end]==='{')depth++;else if(js[end]==='}')depth--;end++;}assert.equal(depth,0);return js.slice(start,end);}
function exported(name){const match=js.match(new RegExp('get '+name+'\\(\\)\\{return (\\w+)\\}'));assert(match,'Installed native UI export missing: '+name);return match[1];}
const names={transparency:exported('applyInterfaceTransparency'),text:exported('applyTextScale'),scaling:exported('applyInterfaceScalingEnabled'),style:exported('applyInterfaceStyle')};
const opacityFunction=nativeFunction(names.transparency),curve=opacityFunction.match(/\.75\*(\w+)\(/)?.[1],contrastVariable=opacityFunction.match(/(\w+)\.higherContrast/)?.[1],contrastClass=js.match(/higherContrast:"([^"]+)"/)?.[1];assert(curve&&contrastVariable&&contrastClass,'Native setting contract changed; inspect before testing');
const applyCode=`const ${contrastVariable}={higherContrast:${JSON.stringify(contrastClass)}};${[...new Set(Object.values(names).concat(curve))].map(nativeFunction).join('\n')};${names.style}(settings.style);${names.transparency}(settings.transparency);${names.text}(settings.text);${names.scaling}(settings.scaling);`;
const input=fs.readFileSync(path.join(out,'markings.html'),'utf8').replace('font-size:1px!important;','');
const preview=path.join(out,'settings-verification.html');fs.writeFileSync(preview,input);
const {chromium}=await import(pathToFileURL(process.env.CURBSIDE_PLAYWRIGHT||'C:/Users/Trevor/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.mjs'));
const browser=await chromium.launch({headless:true,executablePath:process.env.CURBSIDE_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',args:['--allow-file-access-from-files']});
const results=[];
try{const page=await browser.newPage({viewport:{width:1280,height:720}});await page.goto(pathToFileURL(preview).href);await page.evaluate(()=>document.fonts.ready);
for(const style of ['bright-blue','dark-grey-orange'])for(const transparency of [0,.5,1])for(const text of [.85,1,1.25])for(const scaling of [false,true]){
 const settings={style,transparency,text,scaling};
 const actual=await page.evaluate(({code,settings})=>{new Function('settings',code)(settings);
 const panel=document.querySelector('section'),header=panel.querySelector('header'),content=panel.querySelector('.content_AD7'),footer=[...panel.querySelectorAll('div')].find(e=>e.className.startsWith('footer_')),leave=[...panel.querySelectorAll('button')].find(b=>b.textContent==='Leave active tool'),label=panel.querySelector('img').nextElementSibling;
 const reference=document.createElement('div');reference.className='content_XD5';reference.style.fontSize='var(--fontSizeS)';reference.style.fontFamily='var(--fontFamily)';document.body.append(reference);
 const native=getComputedStyle(reference),p=getComputedStyle(panel),c=getComputedStyle(content),h=getComputedStyle(header),f=getComputedStyle(footer);const result={background:c.backgroundColor,referenceBackground:native.backgroundColor,blur:c.backdropFilter,referenceBlur:native.backdropFilter,font:getComputedStyle(label).fontFamily,referenceFont:native.fontFamily,fontSize:getComputedStyle(label).fontSize,referenceFontSize:native.fontSize,normalOpacity:document.documentElement.style.getPropertyValue('--panelOpacityNormal'),darkOpacity:document.documentElement.style.getPropertyValue('--panelOpacityDark'),textScale:document.documentElement.style.getPropertyValue('--fontScale'),textScaleChange:document.documentElement.style.getPropertyValue('--fontScaleChange'),rootFontSize:getComputedStyle(document.documentElement).fontSize,panelOpacity:p.opacity,textOpacity:getComputedStyle(label).opacity,iconOpacity:getComputedStyle(panel.querySelector('img')).opacity,headerBackground:h.backgroundColor,footerBackground:f.backgroundColor,width:panel.getBoundingClientRect().width,bottom:panel.getBoundingClientRect().bottom,horizontalOverflow:panel.scrollWidth>panel.clientWidth+1};reference.remove();return result;},{code:applyCode,settings});
 assert.equal(actual.background,actual.referenceBackground);assert.equal(actual.blur,actual.referenceBlur);assert.equal(actual.font,actual.referenceFont);assert.equal(actual.fontSize,actual.referenceFontSize);assert.equal(Number(actual.textScale),text);assert(Math.abs(Number(actual.textScaleChange)-(text-1))<1e-9);assert.equal(actual.panelOpacity,'1');assert.equal(actual.textOpacity,'1');assert.equal(actual.iconOpacity,'1');assert.equal(actual.footerBackground,actual.headerBackground);assert(!actual.horizontalOverflow);assert(actual.bottom<=720);
 if(transparency===0){assert.equal(Number(actual.normalOpacity),1);assert.equal(actual.blur,'none');}
 else assert(Number(actual.normalOpacity)<1);
 const rootPx=parseFloat(actual.rootFontSize);assert(Math.abs(actual.width-Math.min(384*rootPx,1280-24*rootPx))<1.1);
 // The selected native upgrade and footer action remain reachable through their scroll areas.
 for(const label of ['White line','Leave active tool']){const button=page.getByRole('button',{name:label,exact:true});await button.scrollIntoViewIfNeeded();const b=await button.boundingBox();assert(b&&b.y>=0&&b.y+b.height<=720,'Unreachable: '+label);}
 results.push({settings,...actual});
}
fs.writeFileSync(path.join(out,'player-settings-results.json'),JSON.stringify({scope:'Desktop verification using installed native setting functions and CSS. Not in-game Cohtml or controller certification.',gameUiSha256:crypto.createHash('sha256').update(js).digest('hex'),cases:results.length,results},null,2));
console.log('PASS: '+results.length+' player-setting combinations; native transparency/blur, text family/size, UI scaling, live updates, readable text/icon opacity and reachable controls.');
}finally{await browser.close();}
