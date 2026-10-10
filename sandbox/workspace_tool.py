"""A tool process INSIDE the restricted container; never run this on the host."""
import json,os,subprocess,sys
from pathlib import Path
ROOT=Path('/workspace')

def path(value):
    if not isinstance(value,str) or not value or '\0' in value or '\\' in value:raise ValueError('Invalid relative path')
    p=Path(value)
    if p.is_absolute() or '..' in p.parts:raise ValueError('Only workspace-relative paths are allowed')
    target=ROOT/p
    for ancestor in [target,*target.parents]:
        if ancestor==ROOT.parent:break
        if ancestor.is_symlink():raise ValueError('Links are not accepted')
    if ROOT.resolve() not in target.resolve().parents:raise ValueError('Path escaped workspace')
    return target

def execute(action):
    tool=action.get('tool')
    if tool=='read':
        target=path(action.get('path'))
        if target.stat().st_size>200000:raise ValueError('File is too large')
        return {'text':target.read_text(encoding='utf-8')}
    if tool=='write':
        target=path(action.get('path'));text=action.get('text')
        if not isinstance(text,str) or len(text)>200000:raise ValueError('Invalid write')
        target.parent.mkdir(parents=True,exist_ok=True)
        fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_TRUNC|getattr(os,'O_NOFOLLOW',0),0o600)
        with os.fdopen(fd,'w',encoding='utf-8') as f:f.write(text)
        return {'written':True,'path':action['path']}
    if tool=='run':
        argv=action.get('argv')
        if not isinstance(argv,list) or not 1<=len(argv)<=64 or any(not isinstance(s,str) or not s or '\0' in s or len(s)>4000 for s in argv):raise ValueError('Use a command argument array')
        result=subprocess.run(argv,cwd=ROOT,capture_output=True,timeout=20,env={'PATH':'/usr/local/bin:/usr/bin:/bin','HOME':'/tmp','PYTHONDONTWRITEBYTECODE':'1'})
        return {'exit_code':result.returncode,'stdout':result.stdout[:12000].decode(errors='replace'),'stderr':result.stderr[:12000].decode(errors='replace')}
    raise ValueError('Unsupported tool')

if __name__=='__main__':
    try:
        data=sys.stdin.buffer.read(1300001)
        if len(data)>1300000:raise ValueError('Tool request exceeded its size limit')
        print(json.dumps(execute(json.loads(data))))
    except Exception as exc:
        print(json.dumps({'error':str(exc)}));sys.exit(1)
