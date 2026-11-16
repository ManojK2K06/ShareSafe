"""Offline verification for restricted hosts. HTTP handlers run in memory, not TCP.

Run run_backend.py and browser.mjs separately on an unrestricted Windows desktop.
This runner never labels in-process handler checks as real-network integration.
"""
import datetime,io,json,os,subprocess,sys,tempfile,threading,unittest
from email.message import Message
from pathlib import Path
root=Path(__file__).resolve().parents[1]
temporary=root/'tests/tmp/restricted';temporary.mkdir(parents=True,exist_ok=True)
tempfile.tempdir=str(temporary)
sys.path[:0]=[str(root/'backend'),str(root/'backend/tests')]
import test_bridge,test_inline,test_workspace
from sharesafe_engine.bridge import State,make_handler,watch_parent

class MemoryTransport:
    @classmethod
    def setUpClass(cls):
        cls.temp=tempfile.TemporaryDirectory();cls.state=State(cls.temp.name,'n'*64,'b'*64,port=8766)
    @classmethod
    def tearDownClass(cls):cls.state.workspaces.close();cls.temp.cleanup()
    def request(self,path,body=None,token='n'*64,headers=None,method=None):
        handler=object.__new__(make_handler(self.state))
        handler.path=path;handler.request_version='HTTP/1.1';handler.requestline='synthetic request'
        handler.command=method or ('POST' if body is not None else 'GET')
        payload=json.dumps(body).encode() if body is not None else b''
        handler.headers=Message()
        values={'Host':'127.0.0.1:8766','Authorization':'Bearer '+token,'Content-Type':'application/json','Content-Length':str(len(payload))}
        values.update(headers or {})
        for name,value in values.items():handler.headers[name]=value
        handler.rfile=io.BytesIO(payload);handler.wfile=io.BytesIO()
        getattr(handler,'do_'+handler.command)()
        head,_,content=handler.wfile.getvalue().partition(b'\r\n\r\n')
        lines=head.decode().split('\r\n');status=int(lines[0].split()[1])
        response_headers=dict(line.split(': ',1) for line in lines[1:] if ': ' in line)
        return status,json.loads(content) if content else {},response_headers

class HandlerTests(MemoryTransport,test_bridge.BridgeTests):pass
class InlineTests(MemoryTransport,test_inline.InlinePolicyTests):pass
class WindowsLifecycleTests(unittest.TestCase):
    @unittest.skipUnless(os.name=='nt','Windows process handle test')
    def test_parent_handle_signals_shutdown(self):
        parent=subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)'])
        event=threading.Event();watcher=threading.Thread(target=watch_parent,args=(parent.pid,event.set),daemon=True)
        try:
            watcher.start();self.assertFalse(event.wait(.15));parent.terminate();parent.wait(5);self.assertTrue(event.wait(5))
        finally:
            if parent.poll() is None:parent.kill();parent.wait()

class Results(unittest.TextTestResult):
    def __init__(self,*args,**kwargs):super().__init__(*args,**kwargs);self.checks=[]
    def addSuccess(self,test):super().addSuccess(test);self.checks.append({'name':test.id(),'status':'passed'})
    def addSkip(self,test,reason):super().addSkip(test,reason);self.checks.append({'name':test.id(),'status':'skipped','reason':reason})
    def addError(self,test,error):super().addError(test,error);self.checks.append({'name':test.id(),'status':'error'})
    def addFailure(self,test,error):super().addFailure(test,error);self.checks.append({'name':test.id(),'status':'failed'})

suite=unittest.TestSuite()
for module in ('test_core','test_files','test_project','test_workspace'):
    suite.addTests(unittest.defaultTestLoader.loadTestsFromName(module))
suite.addTests(unittest.defaultTestLoader.loadTestsFromTestCase(test_bridge.OCRTests))
for cls in (HandlerTests,InlineTests,WindowsLifecycleTests):suite.addTests(unittest.defaultTestLoader.loadTestsFromTestCase(cls))
# Capability-probe hard links without changing the product's link rejection logic.
probe=temporary/'link-probe.txt';probe.write_text('synthetic')
try:os.link(probe,temporary/'hard-link-probe.txt')
except OSError:
    test_workspace.WorkspaceTests.test_hard_link_quarantines=unittest.skip('Managed host forbids hard-link creation')(test_workspace.WorkspaceTests.test_hard_link_quarantines)
result=unittest.TextTestRunner(verbosity=1,resultclass=Results).run(suite)
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Portable engine, in-memory HTTP handlers (NO TCP claim), Windows parent lifecycle, workspace argument and export checks; NO actual Docker claim',
        'tests_run':result.testsRun,'passed':sum(x['status']=='passed' for x in result.checks),'skipped':len(result.skipped),'failures':len(result.failures),'errors':len(result.errors),'checks':result.checks}
(root/'tests/results/windows-restricted.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
raise SystemExit(0 if result.wasSuccessful() else 1)
