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
