"""Run on a Mac with the built image. Never substitutes mocks for container checks."""
import json,os,tempfile
from pathlib import Path
from sharesafe_engine.workspace import Workspaces
if os.environ.get('SHARESAFE_DOCKER_TEST')!='1':raise SystemExit('Set SHARESAFE_DOCKER_TEST=1 to run actual container checks.')
with tempfile.TemporaryDirectory() as folder:
    root=Path(folder);source=root/'source';source.mkdir();(source/'main.py').write_text('API_KEY="fixture-secret-111"\nprint("public")\n')
    workspace=Workspaces(root/'storage')
    if not workspace.docker_status()['ready']:raise SystemExit('Docker daemon or sharesafe-agent:local is missing. Build it first; checks have NOT passed.')
    wid=workspace.create(workspace.prepare(str(source))['project_id'],True)['workspace_id']
    os.environ['SHARESAFE_HOST_SECRET']='private-host-fixture'
    checks='''import os,socket,pathlib,json
assert os.geteuid()!=0
assert os.statvfs('/').f_flag & os.ST_RDONLY
assert 'SHARESAFE_HOST_SECRET' not in os.environ
assert not pathlib.Path('/var/run/docker.sock').exists()
assert not pathlib.Path(SOURCE).exists()
assert list(pathlib.Path('/sys/class/net').iterdir())==[pathlib.Path('/sys/class/net/lo')]
status=pathlib.Path('/proc/self/status').read_text()
assert 'CapEff:\\t0000000000000000' in status
assert 'NoNewPrivs:\\t1' in status
try:
 socket.create_connection(('1.1.1.1',443),timeout=1)
except OSError: pass
else: raise AssertionError('Network escaped')
pathlib.Path('/workspace/public.txt').write_text('verified public output')
print(json.dumps({'passed':True,'checks':9}))
'''.replace('SOURCE',repr(str(source)))
    result=workspace.run(wid,['python','-c',checks]);assert result['exit_code']==0,result;assert result['verification']['passed'],result
    assert (source/'main.py').read_text().startswith('API_KEY="fixture-secret-111"')
    print(json.dumps({'actual_container_checks':10,'result':result},indent=2))
