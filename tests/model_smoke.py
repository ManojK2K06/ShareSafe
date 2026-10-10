"""Actual local model requests; absence/failure is an error, not a mocked pass."""
import datetime,json,os,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'backend'))
from sharesafe_engine.core import LocalModel,analyze,sanitize
port=int(os.environ.get('SHARESAFE_MODEL_PORT','11434'))
if port not in (11434,11435):raise SystemExit('Only the supported loopback model ports are permitted')
LocalModel.URL=f'http://127.0.0.1:{port}'
model=os.environ.get('SHARESAFE_MODEL','qwen2.5:3b')
text='Project Orion is confidential. Project Orion supports the public workshop. Contact alice@example.org.'
result=analyze(text,{'use_model':True,'model':model})
assert result['model_status']=='completed',result['warnings']
assert all(text[f.start:f.end] for f in result['findings'])
evidence=LocalModel(model).answer('Which project is confidential?',result['graph'].retrieve('Orion confidential'))
assert evidence,'The real model abstained on a supported evidence question'
assert all(item['quote'] in text for item in evidence)
assert result['graph'].retrieve('Galactic telescopes on Neptune')==[]
clean,report=sanitize(result);assert 'alice@example.org' not in clean;assert 'Orion' not in clean
output={'date':datetime.datetime.now(datetime.timezone.utc).isoformat(),'model':model,'endpoint':LocalModel.URL,'passed':4,'checks':['Actual model analysis completed with grounded spans','Actual model answer matched source evidence','Unknown retrieval question abstained','Reviewed output removed supported private values'],'agent_container_tested':False}
(root/'tests/results/model.json').write_text(json.dumps(output,indent=2),encoding='utf-8');print(json.dumps(output,indent=2))
