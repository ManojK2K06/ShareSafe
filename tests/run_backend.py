"""Write measured test results, including failures and skipped checks."""
import datetime,json,sys,unittest
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'backend'))
class Results(unittest.TextTestResult):
    def __init__(self,*args,**kwargs):super().__init__(*args,**kwargs);self.checks=[]
    def addSuccess(self,test):super().addSuccess(test);self.checks.append({'name':test.id(),'status':'passed'})
    def addSkip(self,test,reason):super().addSkip(test,reason);self.checks.append({'name':test.id(),'status':'skipped','reason':reason})
    def addFailure(self,test,error):super().addFailure(test,error);self.checks.append({'name':test.id(),'status':'failed'})
    def addError(self,test,error):super().addError(test,error);self.checks.append({'name':test.id(),'status':'error'})
suite=unittest.defaultTestLoader.discover(str(root/'backend/tests'))
result=unittest.TextTestRunner(verbosity=1,resultclass=Results).run(suite)
report={'date':datetime.datetime.now(datetime.timezone.utc).isoformat(),'tests_run':result.testsRun,'passed':sum(c['status']=='passed' for c in result.checks),'skipped':len(result.skipped),'failures':len(result.failures),'errors':len(result.errors),'checks':result.checks,'scope':'Portable engine, real loopback HTTP, file exports and container configuration; no macOS GUI or actual Docker isolation claim'}
destination=root/'tests/results/backend.json';destination.parent.mkdir(exist_ok=True);destination.write_text(json.dumps(report,indent=2),encoding='utf-8')
raise SystemExit(0 if result.wasSuccessful() else 1)
