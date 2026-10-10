"""Authenticated loopback bridge for the native app and scoped browser client."""
from __future__ import annotations
import argparse,base64,hashlib,hmac,io,json,math,os,re,secrets,signal,sqlite3,threading,time
from contextlib import closing
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit
from PIL import Image,ImageOps
from .core import Policy,GraphIndex,LocalModel,analyze,sanitize,optimize_prompt,MAX_TEXT
from .files import ingest,export_text,redact_image,boxes_for_findings,MAX_BYTES
from .workspace import Workspaces
from .project import export_project

def image_asset(name,data,ocr):
    image=ImageOps.exif_transpose(Image.open(io.BytesIO(data))).convert('RGB');image.load()
    if image.width*image.height>16_000_000:raise ValueError('Image exceeds 16 million pixels')
    text='';words=[];warnings=['Review the complete image and add any missing rectangles. OCR can miss content.']
    if ocr is not None:
        if not isinstance(ocr,dict) or not isinstance(ocr.get('text'),str) or len(ocr['text'])>MAX_TEXT:raise ValueError('Invalid native OCR result')
        text=ocr['text'];raw=ocr.get('words',[])
        if not isinstance(raw,list) or len(raw)>20000:raise ValueError('Invalid OCR word inventory')
        for word in raw:
            if not isinstance(word,dict):raise ValueError('Invalid OCR word')
            s,e=word.get('start'),word.get('end')
            if type(s)!=int or type(e)!=int or not 0<=s<e<=len(text) or text[s:e]!=word.get('text'):raise ValueError('OCR offsets do not match source')
            geometry=[word.get(k) for k in ('x','y','width','height')]
            if any(type(v) not in (int,float) or not math.isfinite(v) for v in geometry):raise ValueError('Invalid OCR geometry')
            x,y,w,h=geometry
            if x<0 or y<0 or w<=0 or h<=0 or x+w>image.width+1 or y+h>image.height+1:raise ValueError('OCR rectangle outside image')
            words.append(word)
    else:warnings.append('Native OCR unavailable. Manual rectangle review is required.')
    return {'text':text,'kind':'image','image':image,'words':words,'warnings':warnings,'name':name}

class State:
    def __init__(self,data_dir,native_token,browser_token,port=8766,model_port=11434,docker=None):
        if not all(isinstance(t,str) and len(t)>=32 for t in (native_token,browser_token)) or native_token==browser_token:raise ValueError('Use two distinct strong pairing tokens')
        if model_port not in (11434,11435):raise ValueError('Only the supported local model ports are allowed')
        LocalModel.URL=f'http://127.0.0.1:{model_port}'
        self.data_dir=Path(data_dir);self.data_dir.mkdir(parents=True,exist_ok=True,mode=0o700)
        self.native_token=native_token;self.browser_token=browser_token;self.port=port
        self.sessions={};self.lock=threading.RLock();self.workspaces=Workspaces(self.data_dir,docker)
        self.inline_policy=Policy.parse({'actions':{'CREDENTIAL':'mask'},'use_model':False})
        self.audit_path=self.data_dir/'audit.sqlite3'
        with closing(sqlite3.connect(self.audit_path)) as db,db:db.execute('CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY, at TEXT, event TEXT, details TEXT)')
    def audit(self,event,**details):
        with closing(sqlite3.connect(self.audit_path)) as db,db:
            db.execute('INSERT INTO events(at,event,details) VALUES(?,?,?)',(time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),event,json.dumps(details)))
    def put(self,result,asset):
        with self.lock:
            now=time.monotonic();self.sessions={k:v for k,v in self.sessions.items() if now-v['created']<1800}
            if len(self.sessions)>=10:self.sessions.pop(next(iter(self.sessions)))
            sid=secrets.token_hex(16);self.sessions[sid]={'created':now,'result':result,'asset':asset};return sid
    def get(self,sid):
        if not isinstance(sid,str):raise ValueError('Analyze content first')
        with self.lock:
            r=self.sessions.get(sid)
            if not r or time.monotonic()-r['created']>=1800:
                self.sessions.pop(sid,None);raise ValueError('Review expired. Analyze again')
            return r

def serialized(result,sid,asset):
    data={'session_id':sid,'text':result['text'],'findings':[f.serialize(result['text']) for f in result['findings']],
          'graph':result['graph'].public(),'warnings':result['warnings']+asset['warnings'],
          'model_status':result['model_status'],'duration_ms':result['duration_ms'],'kind':asset['kind'],
          'blocked':result['model_status']=='failed' or any(f.action=='block' for f in result['findings'])}
    if asset['kind']=='image':
        out=io.BytesIO();asset['image'].save(out,format='PNG')
        data.update(image=base64.b64encode(out.getvalue()).decode(),width=asset['image'].width,height=asset['image'].height,
                    boxes=boxes_for_findings(result['findings'],asset['words']))
    return data

def extension_origin(origin):
    parsed=urlsplit(origin)
    return parsed.scheme in {'chrome-extension','moz-extension','safari-web-extension'} and bool(re.fullmatch(r'[A-Za-z0-9-]{16,100}',parsed.netloc)) and parsed.path in ('','/')

def make_handler(state):
    class Handler(BaseHTTPRequestHandler):
        server_version='ShareSafeMac/0.1'
        def setup(self):
            super().setup();self.connection.settimeout(10)
        def log_message(self,*args):pass
        def reject(self):
            # Drain a small rejected POST body before closing. Otherwise TCP can
            # reset the response while the local client is still sending it.
            try:length=int(self.headers.get('Content-Length','0'))
            except ValueError:length=0
            if 0<length<=65536:self.rfile.read(length)
            self.reply({'error':'Pairing required'},403)
        def host_ok(self):return self.headers.get('Host') in {f'127.0.0.1:{state.port}',f'localhost:{state.port}'}
        def authorized(self,browser=False):
            value=self.headers.get('Authorization','')
            expected=state.browser_token if browser else state.native_token
            return hmac.compare_digest(value,'Bearer '+expected)
        def reply(self,obj,status=200):
            data=json.dumps(obj,ensure_ascii=False).encode()
            self.send_response(status);self.send_header('Content-Type','application/json; charset=utf-8')
            self.send_header('Cache-Control','no-store');self.send_header('X-Content-Type-Options','nosniff')
            origin=self.headers.get('Origin','')
            if extension_origin(origin):self.send_header('Access-Control-Allow-Origin',origin);self.send_header('Vary','Origin')
            self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)
        def do_OPTIONS(self):
            if not self.host_ok() or not extension_origin(self.headers.get('Origin','')):return self.reply({'error':'Origin not permitted'},403)
            self.send_response(204);self.send_header('Access-Control-Allow-Origin',self.headers['Origin']);self.send_header('Vary','Origin')
            self.send_header('Access-Control-Allow-Headers','Authorization, Content-Type')
            self.send_header('Access-Control-Allow-Methods','GET, POST');self.end_headers()
        def do_GET(self):
            path=urlsplit(self.path).path;browser=path.startswith('/v1/browser/')
            if not self.host_ok() or not self.authorized(browser):return self.reject()
            if path in {'/v1/health','/v1/browser/status'}:return self.reply({'ready':True,'mode':'local','categories':list(Policy().actions)})
            if path=='/v1/status':
                try:models=LocalModel(timeout=3).status().get('models',[]);model_status='ready' if models else 'no_models'
                except Exception:models=[];model_status='offline'
                return self.reply({'model_status':model_status,'models':[m['name'] for m in models],'docker':state.workspaces.docker_status(),'default_actions':Policy().actions})
            if path=='/v1/workspaces':return self.reply({'workspaces':state.workspaces.list()})
            if path=='/v1/audit':
                with closing(sqlite3.connect(state.audit_path)) as db:rows=db.execute('SELECT at,event,details FROM events ORDER BY id DESC LIMIT 100').fetchall()
                return self.reply({'events':[{'at':r[0],'event':r[1],'details':json.loads(r[2])} for r in rows]})
            return self.reply({'error':'Not found'},404)
        def do_POST(self):
            path=urlsplit(self.path).path;browser=path=='/v1/browser/review'
            if not self.host_ok() or not self.authorized(browser):return self.reject()
            origin=self.headers.get('Origin','')
            if origin and not extension_origin(origin):return self.reply({'error':'Origin not permitted'},403)
            if self.headers.get('Content-Type','').split(';')[0]!='application/json':return self.reply({'error':'JSON required'},415)
            try:
                length=int(self.headers.get('Content-Length','0'))
                if not 0<length<17*1024*1024:return self.reply({'error':'Request size exceeded'},413)
                body=json.loads(self.rfile.read(length))
                if not isinstance(body,dict):raise ValueError('JSON object required')
                if browser:
                    # Inline prompt cleaning masks credentials. Native review keeps its configurable default block.
                    result=analyze(body.get('text',''),state.inline_policy)
                    clean,report=sanitize(result)
                    state.audit('browser_review',findings=len(result['findings']),redacted=report['redacted'])
                    return self.reply({'text':clean,'findings':[f.serialize(result['text']) for f in result['findings']],
                                       'source_hash':result['sha256'],'report':report,'mode':'local_rules_and_graph'})
                if path=='/v1/inline/policy':
                    state.inline_policy=Policy.parse({'actions':{'CREDENTIAL':'mask'},'use_model':False,
                        'replacements':body.get('replacements',{}),'confidential_terms':body.get('confidential_terms',[]),
                        'allow_terms':body.get('allow_terms',[])})
                    state.audit('inline_policy',glossary_terms=len(state.inline_policy.replacements))
                    return self.reply({'ok':True})
                if path=='/v1/clear':
                    with state.lock:state.sessions.clear()
                    with state.workspaces.lock:state.workspaces.plans.clear()
                    return self.reply({'ok':True})
                if path=='/v1/analyze':
                    policy=Policy.parse(body.get('policy'))
                    if 'file' in body:
                        data=base64.b64decode(body['file'],validate=True)
                        if not data or len(data)>MAX_BYTES:raise ValueError('File exceeds 12 MiB')
                        name=body.get('name','input.txt')
                        if not isinstance(name,str):raise ValueError('Invalid file name')
                        asset=image_asset(name,data,body.get('ocr')) if Path(name).suffix.lower() in {'.png','.jpg','.jpeg','.webp'} else ingest(name,data,ocr=False)
                        text=asset['text']
                    else:text=body.get('text','');asset={'text':text,'kind':'text','warnings':[]}
                    if not isinstance(text,str):raise ValueError('Text must be a string')
                    if not text.strip() and asset['kind']=='image':
                        result={'text':'','findings':[],'graph':GraphIndex(''),'policy':policy,'warnings':[],
                                'model_status':'not_applicable','duration_ms':0,'sha256':hashlib.sha256(data).hexdigest()}
                    else:result=analyze(text,policy)
                    sid=state.put(result,asset);state.audit('analyze',findings=len(result['findings']),kind=asset['kind'],model=result['model_status'])
                    return self.reply(serialized(result,sid,asset))
                if path=='/v1/forget':
                    with state.lock:state.sessions.pop(body.get('session_id'),None)
                    return self.reply({'ok':True})
                if path=='/v1/project/prepare':
                    result=state.workspaces.prepare(body.get('source',''),body.get('glossary'),body.get('prompt','Review the prepared project.'))
                    state.audit('project_prepare',included_files=result['report']['included_files'],excluded_files=result['report']['excluded_files'])
                    return self.reply(result)
                if path=='/v1/project/forget':state.workspaces.forget(body.get('project_id'));return self.reply({'ok':True})
                if path=='/v1/project/export':
                    data,report=export_project(state.workspaces.plan(body.get('project_id')),body.get('reviewed'))
                    return self.reply({'data':base64.b64encode(data).decode(),'name':'sharesafe-agent-workspace.zip','report':report})
                if path=='/v1/workspace/create':
                    result=state.workspaces.create(body.get('project_id'),body.get('reviewed'));state.audit('sandbox_create',files=len(result['files']))
                    return self.reply(result)
                if path=='/v1/workspace/verify':return self.reply(state.workspaces.verify(body.get('workspace_id')))
                if path=='/v1/workspace/run':
                    result=state.workspaces.run(body.get('workspace_id'),body.get('argv'));state.audit('sandbox_run',exit_code=result['exit_code'],verified=result['verification']['passed'])
                    return self.reply(result)
                if path=='/v1/workspace/agent':
                    model=Policy.parse({'model':body.get('model','qwen2.5:3b')}).model
                    result=state.workspaces.local_agent(body.get('workspace_id'),model);state.audit('local_agent',steps=len(result['steps']),verified=result['verification']['passed'])
                    return self.reply(result)
                if path=='/v1/workspace/delete':state.workspaces.remove(body.get('workspace_id'),body.get('confirmed'));return self.reply({'ok':True})
                if path not in {'/v1/query','/v1/preview','/v1/export'}:return self.reply({'error':'Not found'},404)
                record=state.get(body.get('session_id'));result=record['result'];asset=record['asset']
                if path=='/v1/query':
                    question=body.get('question','')
                    if not isinstance(question,str) or not 1<=len(question)<=1000:raise ValueError('Enter a question of 1 to 1000 characters')
                    chunks=result['graph'].retrieve(question)
                    evidence=LocalModel(result['policy'].model).answer(question,chunks) if body.get('use_model') is True else [{'source':c['id'],'quote':c['text']} for c in chunks]
                    return self.reply({'evidence':evidence,'abstained':not evidence})
                clean,report=sanitize(result,body.get('decisions'),body.get('acknowledged') is True)
                if body.get('optimize') is True:clean=optimize_prompt(clean)
                if path=='/v1/preview':return self.reply({'text':clean,'report':report})
                if asset['kind']=='image':
                    if body.get('image_reviewed') is not True:raise ValueError('Review the complete image before exporting')
                    decisions=body.get('decisions',{});selected={f.id for f in result['findings'] if decisions.get(f.id,True) and f.action in {'mask','generalize'}}
                    boxes=[b for b in boxes_for_findings(result['findings'],asset['words']) if b['finding_id'] in selected]
                    manual=body.get('boxes',[])
                    if not isinstance(manual,list) or len(manual)>300:raise ValueError('At most 300 manual rectangles')
                    data=redact_image(asset['image'],boxes+manual);name='sanitized.png'
                else:data,_,name=export_text(clean,body.get('format','txt'))
                state.audit('export',redacted=report['redacted'],retained_risks=report['retained_risks'])
                return self.reply({'data':base64.b64encode(data).decode(),'name':name,'report':report})
            except (ValueError,TypeError,KeyError,base64.binascii.Error) as exc:return self.reply({'error':str(exc)},400)
            except Exception:return self.reply({'error':'Local operation failed. No output was approved; check dependencies and retry.'},500)
    return Handler

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--config',type=Path,required=True);parser.add_argument('--data-dir',type=Path,required=True)
    parser.add_argument('--parent-pid',type=int)
    args=parser.parse_args();os.umask(0o077)
    config=json.loads(args.config.read_text());port=config.get('port',8766)
    if type(port)!=int or not 1024<=port<=65535:raise ValueError('Invalid local service port')
    state=State(args.data_dir,config['native_token'],config['browser_token'],port,config.get('model_port',11434))
    server=ThreadingHTTPServer(('127.0.0.1',port),make_handler(state))
    def stop(*_):threading.Thread(target=server.shutdown,daemon=True).start()
    signal.signal(signal.SIGTERM,stop)
    if args.parent_pid:
        if os.name!='posix':raise ValueError('Parent monitoring is supported by the packaged macOS service')
        def watch_parent():
            while True:
                time.sleep(2)
                try:os.kill(args.parent_pid,0)
                except ProcessLookupError:server.shutdown();return
                except PermissionError:continue
        threading.Thread(target=watch_parent,daemon=True).start()
    try:server.serve_forever()
    finally:state.workspaces.close();server.server_close()
