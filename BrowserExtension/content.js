(() => {
  "use strict";
  if(globalThis.__sharesafeInstalled) return; globalThis.__sharesafeInstalled=true;
  const api=globalThis.browser||globalThis.chrome;
  let panel=null,approval=null,lastEditor=null,allowFileUntil=0,enabled=true,passSubmit=null;
  const known=new Set(["chatgpt.com","chat.openai.com","claude.ai","gemini.google.com","chat.deepseek.com","chat.z.ai","z.ai"]);
  const isEditor=e=>e instanceof HTMLElement && (e.matches("textarea")||e.isContentEditable||e.matches('[role="textbox"]')) && !e.matches('[type="password"], [role="searchbox"]') && !e.closest('[role="search"]') && !e.disabled;
  const editor=e=>e.composedPath().find(isEditor);
  const text=e=>e instanceof HTMLTextAreaElement||e instanceof HTMLInputElement?e.value:e.innerText;
  const remove=()=>{panel?.remove();panel=null;};
  function ui(title) {
    remove(); const host=document.createElement("div");host.id="sharesafe-prompt-review";
    host.style.cssText="all:initial;position:fixed;right:22px;bottom:22px;z-index:2147483647";
    const root=host.attachShadow({mode:"closed"}); const style=document.createElement("style");
    style.textContent="*{box-sizing:border-box}section{font:14px system-ui;color:#182a31;background:#fff;border:1px solid #b9ccd2;border-radius:18px;padding:20px;width:390px;box-shadow:0 12px 60px #0004}h2{font-size:19px;margin:0 0 10px}p{line-height:1.5}button{font:inherit;padding:9px 12px;border:1px solid #b9ccd2;border-radius:9px;background:#f4f7f8;color:#132b31;cursor:pointer;margin:4px 5px 0 0}button.primary{background:#12675d;color:white;border-color:#12675d}button:disabled{opacity:.5;cursor:default}textarea{resize:vertical;width:100%;height:140px;padding:10px;color:#132b31;background:#f5f8f8;border:1px solid #c4d3d6;border-radius:8px;font:13px ui-monospace,monospace}small{display:block;color:#5b6b71;margin-top:12px}label{display:block;line-height:1.5;margin:8px 0}";
    const section=document.createElement("section");section.setAttribute("role","dialog");section.setAttribute("aria-label",title);
    const heading=document.createElement("h2");heading.textContent=title;section.append(heading);root.append(style,section);(document.body||document.documentElement).append(host);panel=host; return section;
  }
  function paragraph(parent,value){const p=document.createElement("p");p.textContent=value;parent.append(p);return p;}
  function button(parent,label,action,primary=false){const b=document.createElement("button");b.textContent=label;if(primary)b.className="primary";b.addEventListener("click",action);parent.append(b);return b;}
  function stillCurrent(e,source){return e.isConnected&&text(e)===source;}
  function approve(e,source){approval={element:e,text:source,expires:Date.now()+30000};remove();e.focus();}
  function consume(e){if(approval&&approval.element===e&&approval.text===text(e)&&approval.expires>Date.now()){approval=null;return true;}approval=null;return false;}
  async function apply(e,source,clean,message) {
    if(!stillCurrent(e,source)){message.textContent="The prompt changed. Cancel this review and review the latest draft.";return;}
    e.focus();
    if(e instanceof HTMLTextAreaElement||e instanceof HTMLInputElement){const prototype=e instanceof HTMLTextAreaElement?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(prototype,"value").set.call(e,clean);e.dispatchEvent(new InputEvent("input",{bubbles:true,inputType:"insertReplacementText",data:clean}));}
    else {const selection=window.getSelection();const range=document.createRange();range.selectNodeContents(e);selection.removeAllRanges();selection.addRange(range);if(!document.execCommand("insertText",false,clean)){message.textContent="This editor cannot be replaced. Copy the prepared draft and paste it yourself.";return;}}
    await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
    if(text(e)!==clean){message.textContent="The editor did not accept the replacement. Copy the prepared draft and paste it yourself.";return;}
    approve(e,clean);
  }
  function review(e) {
    const source=text(e);if(!source?.trim())return;
    const section=ui("Clean this prompt?");const message=paragraph(section,"Review locally before sending this draft. Nothing is sent to a model provider by ShareSafe.");
    const cleanButton=button(section,"Review locally",async()=>{
      cleanButton.disabled=true;message.textContent="Checking with the paired local app…";
      try {
        const reply=await api.runtime.sendMessage({type:"review",text:source});
        if(!reply?.ok)throw Error(reply?.error||"Local service unavailable.");
        const clean=reply.result.text;if(typeof clean!=="string")throw Error("Invalid review output.");
        if(!stillCurrent(e,source))throw Error("The prompt changed. Cancel and review the latest draft.");
        message.textContent=`${reply.result.findings.length} supported findings. Review the prepared draft, then replace it and press Send again.`;
        const preview=document.createElement("textarea");preview.readOnly=true;preview.value=clean;section.insertBefore(preview,cleanButton);
        cleanButton.remove();button(section,"Use prepared draft",()=>apply(e,source,clean,message),true);
        button(section,"Copy prepared",async()=>{try{await navigator.clipboard.writeText(clean);message.textContent="Prepared draft copied. Paste it into the composer and review again.";}catch{preview.focus();preview.select();message.textContent="Press Copy to copy the selected draft.";}});
      } catch(error) {message.textContent=error.message+" Your send remains paused.";cleanButton.disabled=false;}
    },true);
    const label=document.createElement("label"),ack=document.createElement("input");ack.type="checkbox";label.append(ack,document.createTextNode(" I reviewed this original draft and accept its privacy risks"));section.append(label);
    const original=button(section,"Keep original once",()=>{if(stillCurrent(e,source))approve(e,source);else message.textContent="The draft changed. Review it again.";});original.disabled=true;ack.addEventListener("change",()=>original.disabled=!ack.checked);
    button(section,"Cancel",remove);const small=document.createElement("small");small.textContent="No automatic sending. Unknown sensitive information may remain. Attachments require a separately reviewed copy.";section.append(small);
  }
  function block(event,e){
    if(!enabled||!e||!text(e)?.trim())return;
    if(event.type==="submit"&&passSubmit&&passSubmit.element===e&&passSubmit.text===text(e)&&passSubmit.expires>Date.now()){passSubmit=null;return;}
    if(consume(e)){if(event.type!=="submit")passSubmit={element:e,text:text(e),expires:Date.now()+250};return;}
    passSubmit=null;event.preventDefault();event.stopImmediatePropagation();review(e);
  }
  document.addEventListener("focusin",event=>{const candidate=event.composedPath().find(isEditor);if(candidate)lastEditor=candidate;},true);
  document.addEventListener("input",event=>{if(event.composedPath().some(isEditor))approval=null;},true);
  document.addEventListener("keydown",event=>{if(event.key==="Enter"&&!event.shiftKey&&!event.ctrlKey&&!event.altKey&&!event.metaKey&&!event.isComposing)block(event,editor(event));},true);
  document.addEventListener("submit",event=>block(event,event.target.querySelector("textarea,[contenteditable='true'],[role='textbox']")||lastEditor),true);
  document.addEventListener("click",event=>{
    const target=event.composedPath().find(e=>e instanceof HTMLElement&&e.matches("button,[role='button'],input[type='submit']"));if(!target)return;
    const description=[target.getAttribute("aria-label"),target.getAttribute("title"),target.getAttribute("data-testid"),target.textContent].filter(Boolean).join(" ").trim();
    if(/(?:^|\b)(?:send|submit|send-button|send message|发送|發送)(?:\b|$)/i.test(description)||target.matches("input[type='submit']"))block(event,lastEditor||document.querySelector("textarea,[contenteditable='true'],[role='textbox']"));
  },true);
  document.addEventListener("change",event=>{
    const input=event.composedPath().find(e=>e instanceof HTMLInputElement&&e.type==="file");if(!enabled||!input||!input.files?.length)return;
    if(allowFileUntil>Date.now()){allowFileUntil=0;return;}
    event.preventDefault();event.stopImmediatePropagation();input.value="";
    const section=ui("Review this attachment first");paragraph(section,"Open the file in ShareSafe, analyze it, inspect the complete result and export a reviewed copy. Original documents are not cleaned by this extension.");
    button(section,"I have a reviewed copy — choose it",()=>{allowFileUntil=Date.now()+30000;remove();input.click();},true);button(section,"Cancel",remove);
  },true);
  function update(settings){enabled=known.has(location.hostname)||(settings.enabledOrigins||[]).includes(location.origin);if(!enabled){remove();approval=null;}}
  api.storage.local.get("enabledOrigins").then(update).catch(()=>{});
  api.storage.onChanged.addListener((changes,area)=>{if(area==="local"&&changes.enabledOrigins)update({enabledOrigins:changes.enabledOrigins.newValue});});
})();
