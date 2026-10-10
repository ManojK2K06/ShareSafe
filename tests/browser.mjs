import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn} from 'node:child_process';
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
import vm from 'node:vm';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.SHARESAFE_PLAYWRIGHT||'playwright');
const results=[];const record=(name)=>results.push({name,passed:true});
const tmp=path.join(root,'tests','tmp','browser');fs.mkdirSync(tmp,{recursive:true});
const config=path.join(tmp,'config.json');fs.writeFileSync(config,JSON.stringify({native_token:'n'.repeat(64),browser_token:'b'.repeat(64),port:8766,model_port:11434}));
const engine=spawn(process.env.SHARESAFE_PYTHON||'python3',[path.join(root,'backend','bridge_entry.py'),'--config',config,'--data-dir',path.join(tmp,'data')],{env:{...process.env,PYTHONPATH:[path.join(root,'backend'),process.env.PYTHONPATH].filter(Boolean).join(path.delimiter)},stdio:['ignore','pipe','pipe']});
let browser;
try {
  for(let i=0;i<60;i++){try{const response=await fetch('http://127.0.0.1:8766/v1/health',{headers:{Authorization:'Bearer '+'n'.repeat(64)}});if(response.ok)break;}catch{}await new Promise(r=>setTimeout(r,100));}
  browser=await chromium.launch({headless:true,...(process.env.SHARESAFE_BROWSER?{executablePath:process.env.SHARESAFE_BROWSER}:{})});
  const context=await browser.newContext();let failure=false,delay=0;
  await context.exposeBinding('__testReview',async(_,message)=>{
    if(delay)await new Promise(r=>setTimeout(r,delay));if(failure)return {ok:false,error:'Local service unavailable'};
    const response=await fetch('http://127.0.0.1:8766/v1/browser/review',{method:'POST',headers:{Authorization:'Bearer '+'b'.repeat(64),'Content-Type':'application/json'},body:JSON.stringify({text:message.text})});
    const result=await response.json();return response.ok?{ok:true,result}:{ok:false,error:result.error};
  });
  await context.addInitScript(()=>{
    // Keep the real closed shadow root; this test-only reference allows inspection.
    const original=Element.prototype.attachShadow;Element.prototype.attachShadow=function(options){const root=original.call(this,options);if(this.id==='sharesafe-prompt-review')window.__testRoot=root;return root;};
    window.chrome={runtime:{sendMessage:message=>window.__testReview(message)},storage:{local:{get:async()=>({enabledOrigins:[]})},onChanged:{addListener:()=>{}}}};
  });
  await context.addInitScript({path:path.join(root,'BrowserExtension','content.js')});
  await context.route('https://chatgpt.com/**',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><html><body><form><textarea id="prompt" aria-label="Prompt"></textarea><button type="submit" aria-label="Send message">Send</button><input id="attachment" type="file"></form><script>window.sent=0;window.uploads=0;document.querySelector("form").addEventListener("submit",e=>{e.preventDefault();if(document.querySelector("textarea").value.trim())window.sent++});document.querySelector("textarea").addEventListener("keydown",e=>{if(e.key==="Enter"&&!e.shiftKey){e.preventDefault();if(e.target.value.trim())window.sent++}});document.querySelector("input").addEventListener("change",()=>window.uploads++);</script></body></html>'}));
  const page=await context.newPage();await page.goto('https://chatgpt.com/fixture');
  const prompt=page.locator('#prompt');
  const panelText=()=>page.evaluate(()=>window.__testRoot?.textContent||'');
  const button=async(label)=>{
    await page.waitForFunction(label=>Array.from(window.__testRoot?.querySelectorAll('button')||[]).some(b=>b.textContent===label&&!b.disabled),label);
    const box=await page.evaluate(label=>{const b=Array.from(window.__testRoot.querySelectorAll('button')).find(b=>b.textContent===label);const r=b.getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2};},label);await page.mouse.click(box.x,box.y);
  };
  const state=()=>page.evaluate(()=>({sent:window.sent,uploads:window.uploads}));
  await prompt.fill('Contact alice@example.org with api_key=fixture-secret-432');await prompt.press('Enter');
  assert.equal((await state()).sent,0);assert.match(await panelText(),/Clean this prompt/);record('Enter pauses before the page sends');
  await button('Review locally');await page.waitForFunction(()=>window.__testRoot?.querySelector('textarea'));
  const clean=await page.evaluate(()=>window.__testRoot.querySelector('textarea').value);assert(!clean.includes('alice@example.org'));assert(!clean.includes('fixture-secret'));record('Real engine cleans email and credential in preview');
  await button('Use prepared draft');await page.locator('#sharesafe-prompt-review').waitFor({state:'detached'});assert.equal(await prompt.inputValue(),clean);record('Explicit replacement updates the actual editor');
  await prompt.press('Enter');assert.equal((await state()).sent,1);record('Approved draft sends only on the next user action');
  await prompt.fill('New draft for bob@example.org');await prompt.press('Enter');assert.equal((await state()).sent,1);record('Edited draft needs a fresh review');
  await button('Cancel');await prompt.press('Shift+Enter');assert.equal((await state()).sent,1);assert.equal(await page.locator('#sharesafe-prompt-review').count(),0);record('Shift Enter remains a newline');
  await prompt.fill('Changed while scan runs: user@example.org');await prompt.press('Enter');delay=300;
  await button('Review locally');await prompt.fill('This newer draft stays untouched.');await page.waitForFunction(()=>window.__testRoot?.textContent.includes('prompt changed'));assert.equal(await prompt.inputValue(),'This newer draft stays untouched.');record('Draft changes during review invalidate the result');delay=0;
  await button('Cancel');failure=true;await prompt.fill('Offline draft user@example.org');await prompt.press('Enter');await button('Review locally');await page.waitForFunction(()=>window.__testRoot?.textContent.includes('send remains paused'));
  assert.equal((await state()).sent,1);record('Disconnected app does not silently approve sending');
  const ack=await page.evaluate(()=>{const r=window.__testRoot.querySelector('input').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2};});await page.mouse.click(ack.x,ack.y);await button('Keep original once');await prompt.press('Enter');assert.equal((await state()).sent,2);record('Original bypass requires explicit acknowledgement');failure=false;
  await prompt.fill('Review carol@example.org');await page.getByRole('button',{name:'Send message'}).click();assert.equal((await state()).sent,2);await button('Review locally');await page.waitForFunction(()=>window.__testRoot?.querySelector('textarea'));await button('Use prepared draft');await page.locator('#sharesafe-prompt-review').waitFor({state:'detached'});await page.getByRole('button',{name:'Send message'}).click();assert.equal((await state()).sent,3);record('Approved button click and its form submission are one send');
  await prompt.fill('');await page.mouse.click(650,400);await page.keyboard.press('Enter');assert.equal(await page.locator('#sharesafe-prompt-review').count(),0);record('Enter outside the editor is not intercepted');
  await page.locator('#attachment').setInputFiles({name:'original.txt',mimeType:'text/plain',buffer:Buffer.from('private fixture')});assert.equal((await state()).uploads,0);assert.match(await panelText(),/attachment first/);record('Original attachment is paused for separate review');
  const chooser=page.waitForEvent('filechooser');await button('I have a reviewed copy — choose it');await (await chooser).setFiles({name:'reviewed.txt',mimeType:'text/plain',buffer:Buffer.from('public reviewed fixture')});assert.equal((await state()).uploads,1);record('Explicit reviewed attachment selection can proceed');
  await context.close();
  // Background permission tests use a mock transport, separate from real-engine DOM checks.
  let listener,fetches=[];const settings={browserToken:'b'.repeat(64),enabledOrigins:['https://custom.example']};
  const background={console,URL,AbortController,setTimeout,clearTimeout,fetch:async(url,options)=>{fetches.push({url,options});return{ok:true,json:async()=>({text:'[EMAIL_1]',findings:[]})};},chrome:{runtime:{id:'test-extension',onMessage:{addListener:f=>listener=f},onInstalled:{addListener:()=>{}},onStartup:{addListener:()=>{}}},storage:{local:{get:async()=>settings},onChanged:{addListener:()=>{}}}}};background.globalThis=background;
  vm.runInNewContext(fs.readFileSync(path.join(root,'BrowserExtension','background.js'),'utf8'),background);
  const send=(message,sender)=>new Promise(resolve=>listener(message,sender,resolve));
  const sender={id:'test-extension',tab:{id:1},url:'https://chatgpt.com/'};
  assert((await send({type:'review',text:'fixture'},sender)).ok);assert.equal(fetches.at(-1).url,'http://127.0.0.1:8766/v1/browser/review');record('Background transport is restricted to loopback (mock transport)');
  assert(!(await send({type:'review',text:'fixture'},{...sender,id:'foreign'})).ok);record('Foreign extension sender rejected');
  assert(!(await send({type:'review',text:'fixture'},{...sender,url:'https://not-enabled.example'})).ok);record('Unapproved custom domain rejected');
  assert((await send({type:'review',text:'fixture'},{...sender,url:'https://custom.example/chat'})).ok);record('Approved custom AI domain accepted');
  assert(!(await send({type:'status'},sender)).ok);record('Content scripts cannot impersonate extension settings');
  assert(!(await send({type:'review',text:'x'.repeat(200001)},sender)).ok);record('Oversized prompt rejected before transport');
  const report={date:new Date().toISOString(),browser:await browser.version(),domTransport:'real local engine',backgroundTransport:'mock permission tests',passed:results.length,checks:results};
  fs.writeFileSync(path.join(root,'tests','results','browser.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
} finally {if(browser)await browser.close();engine.kill();}
