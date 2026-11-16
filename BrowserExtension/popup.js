"use strict";
const api=globalThis.browser||globalThis.chrome;
const $=id=>document.getElementById(id);
let current;
const display=error=>$("message").textContent=error.message||String(error);
async function check(){const reply=await api.runtime.sendMessage({type:"status"});if(!reply?.ok)throw Error(reply?.error||"Local app unavailable");display("Paired local app is ready.");}
$("pair").onclick=async()=>{try{const token=$("token").value.trim();if(token.length<32)throw Error("Copy the browser token from ShareSafe Settings.");await api.storage.local.set({browserToken:token});$("token").value="";await check();}catch(error){display(error);}};
$("check").onclick=()=>check().catch(display);
$("enable").onclick=async()=>{
  try{
    if(!current||new URL(current.url).protocol!=="https:")throw Error("Choose an HTTPS AI website.");
    const origin=new URL(current.url).origin;
    const granted=await api.permissions.request({origins:[origin+"/*"]});if(!granted)throw Error("Website permission was not granted.");
    const {enabledOrigins=[]}=await api.storage.local.get("enabledOrigins");await api.storage.local.set({enabledOrigins:Array.from(new Set([...enabledOrigins,origin]))});
    await api.scripting.executeScript({target:{tabId:current.id},files:["content.js"]});
    display(api.scripting.registerContentScripts?"Enabled. Reload this page if its editor was already open.":"Enabled for this tab. This browser does not expose persistent dynamic scripts; use this button again after opening a new tab.");
  }catch(error){display(error);}
};
$("disable").onclick=async()=>{try{if(!current)throw Error("Choose a website first.");const origin=new URL(current.url).origin;const {enabledOrigins=[]}=await api.storage.local.get("enabledOrigins");await api.storage.local.set({enabledOrigins:enabledOrigins.filter(x=>x!==origin)});await api.permissions.remove({origins:[origin+"/*"]});display("Custom website disabled. Known AI domains stay enabled by default.");}catch(error){display(error);}};
api.tabs.query({active:true,currentWindow:true}).then(tabs=>{current=tabs[0];$("origin").textContent=current?.url?new URL(current.url).origin:"No active website";}).catch(display);

// Inline vector marks keep actions recognizable without emoji or external assets.
for(const [id,path] of Object.entries({pair:"M12 3l9 4-2 9-7 6-7-6-2-9z M8 12l3 3 6-6",check:"M20 12a8 8 0 1 1-3-6 M20 3v6h-6",enable:"M12 4v16 M4 12h16",disable:"M5 12h14"})){
 const svg=document.createElementNS("http://www.w3.org/2000/svg","svg");svg.setAttribute("viewBox","0 0 24 24");svg.setAttribute("fill","none");svg.setAttribute("stroke","currentColor");svg.setAttribute("stroke-width","1.7");svg.setAttribute("stroke-linecap","round");svg.setAttribute("stroke-linejoin","round");svg.setAttribute("aria-hidden","true");const mark=document.createElementNS(svg.namespaceURI,"path");mark.setAttribute("d",path);svg.append(mark);$(id).prepend(svg);
}
