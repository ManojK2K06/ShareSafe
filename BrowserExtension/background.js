"use strict";
const api = globalThis.browser || globalThis.chrome;
const known = new Set(["chatgpt.com", "chat.openai.com", "claude.ai", "gemini.google.com", "chat.deepseek.com", "chat.z.ai", "z.ai"]);
const endpoint = "http://127.0.0.1:8766";

async function config() { return api.storage.local.get(["browserToken", "enabledOrigins"]); }
async function allowedSender(sender) {
  if (sender.id !== api.runtime.id || !sender.tab || !sender.url) return false;
  const url = new URL(sender.url);
  const settings = await config();
  return url.protocol === "https:" && (known.has(url.hostname) || (settings.enabledOrigins || []).includes(url.origin));
}
async function local(path, body) {
  const settings = await config();
  if (typeof settings.browserToken !== "string" || settings.browserToken.length < 32) throw Error("Open ShareSafe and pair the extension in its settings.");
  const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), 30000);
  try {
    const response = await fetch(endpoint + path, {
      method: body ? "POST" : "GET", headers: {"Authorization": "Bearer " + settings.browserToken, "Content-Type": "application/json"},
      body: body ? JSON.stringify(body) : undefined, signal:controller.signal, cache:"no-store", redirect:"error", credentials:"omit"
    });
    const result = await response.json(); if (!response.ok) throw Error(result.error || "Local review failed."); return result;
  } finally { clearTimeout(timeout); }
}
async function handle(message,sender) {
  if (!message || typeof message !== "object") throw Error("Invalid request.");
  if (message.type === "status" && sender.id === api.runtime.id && !sender.tab) return local("/v1/browser/status");
  if (message.type !== "review" || !await allowedSender(sender)) throw Error("This page has not been enabled for ShareSafe.");
  if (typeof message.text !== "string" || !message.text.trim() || message.text.length > 200000) throw Error("Enter a prompt of up to 200,000 characters.");
  return local("/v1/browser/review", {text: message.text});
}
api.runtime.onMessage.addListener((message,sender,reply) => {
  handle(message,sender).then(result=>reply({ok:true,result}),error=>reply({ok:false,error:error.message})); return true;
});
// Dynamic scripts are persistent where supported. Safari compatibility is checked by the popup.
async function restore() {
  if (!api.scripting?.registerContentScripts || !api.scripting?.getRegisteredContentScripts) return;
  const {enabledOrigins=[]} = await config();
  const scripts = await api.scripting.getRegisteredContentScripts();
  const desired = new Set(enabledOrigins.map(origin=>"sharesafe-"+Array.from(origin).map(c=>c.charCodeAt(0).toString(16)).join("")));
  const stale = scripts.filter(s=>s.id.startsWith("sharesafe-")&&!desired.has(s.id)).map(s=>s.id);
  if(stale.length) await api.scripting.unregisterContentScripts({ids:stale});
  for(const origin of enabledOrigins) {
    if(!/^https:\/\/[^/]+$/.test(origin)) continue;
    if(!await api.permissions.contains({origins:[origin+"/*"]})) continue;
    const id="sharesafe-"+Array.from(origin).map(c=>c.charCodeAt(0).toString(16)).join("");
    if(!scripts.some(s=>s.id===id)) await api.scripting.registerContentScripts([{id,matches:[origin+"/*"],js:["content.js"],runAt:"document_start",persistAcrossSessions:true}]);
  }
}
api.runtime.onInstalled.addListener(()=>restore().catch(()=>{}));
api.runtime.onStartup?.addListener(()=>restore().catch(()=>{}));
api.storage.onChanged.addListener((changes,area)=>{if(area==="local"&&changes.enabledOrigins) restore().catch(()=>{});});
