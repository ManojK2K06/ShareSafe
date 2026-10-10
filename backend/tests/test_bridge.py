import base64,io,json,tempfile,threading,unittest
from http.client import HTTPConnection
from http.server import ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch
from PIL import Image
from sharesafe_engine.bridge import State,make_handler,image_asset,extension_origin

class BridgeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp=tempfile.TemporaryDirectory();cls.state=State(Path(cls.temp.name),'n'*64,'b'*64,port=0)
        cls.server=ThreadingHTTPServer(('127.0.0.1',0),make_handler(cls.state));cls.port=cls.server.server_port;cls.state.port=cls.port
        cls.thread=threading.Thread(target=cls.server.serve_forever,daemon=True);cls.thread.start()
    @classmethod
    def tearDownClass(cls):cls.server.shutdown();cls.server.server_close();cls.thread.join();cls.temp.cleanup()
    def request(self,path,body=None,token='n'*64,headers=None,method=None):
        connection=HTTPConnection('127.0.0.1',self.port,timeout=10)
        h={'Authorization':'Bearer '+token,'Content-Type':'application/json'};h.update(headers or {})
        connection.request(method or ('POST' if body is not None else 'GET'),path,json.dumps(body) if body is not None else None,h)
        response=connection.getresponse();data=response.read();connection.close()
        return response.status,json.loads(data) if data else {},dict(response.headers)
    def analyze(self,text='Contact alice@example.org',policy=None):
        status,r,_=self.request('/v1/analyze',{'text':text,'policy':policy or {}});self.assertEqual(status,200);return r
    def test_health_requires_token(self):self.assertEqual(self.request('/v1/health',token='')[0],403)
    def test_token_scopes(self):
        self.assertEqual(self.request('/v1/health',token='b'*64)[0],403)
        self.assertEqual(self.request('/v1/browser/status',token='b'*64)[0],200)
        self.assertEqual(self.request('/v1/browser/status')[0],403)
        for route in ['/v1/analyze','/v1/workspace/run','/v1/project/prepare','/v1/export']:
            self.assertEqual(self.request(route,{},token='b'*64)[0],403)
    def test_bad_host(self):self.assertEqual(self.request('/v1/health',headers={'Host':'attacker.invalid'})[0],403)
    def test_website_origin_denied(self):self.assertEqual(self.request('/v1/browser/review',{'text':'hello'},token='b'*64,headers={'Origin':'https://chatgpt.com'})[0],403)
    def test_preflight_website_denied(self):self.assertEqual(self.request('/v1/browser/review',method='OPTIONS',headers={'Origin':'https://evil.example'})[0],403)
    def test_extension_cors(self):
        origin='chrome-extension://'+'a'*32
        status,_,h=self.request('/v1/browser/review',method='OPTIONS',headers={'Origin':origin});self.assertEqual(status,204);self.assertEqual(h['Access-Control-Allow-Origin'],origin)
    def test_no_cache(self):self.assertEqual(self.request('/v1/health')[2]['Cache-Control'],'no-store')
    def test_inline_masks_credential(self):
        status,r,_=self.request('/v1/browser/review',{'text':'api_key=fixture-secret-928'},token='b'*64);self.assertEqual(status,200);self.assertNotIn('fixture-secret',r['text']);self.assertEqual(r['mode'],'local_rules_and_graph')
    def test_native_credential_blocks(self):
        r=self.analyze('api_key=fixture-secret-928');self.assertTrue(r['blocked']);self.assertEqual(self.request('/v1/export',{'session_id':r['session_id']})[0],400)
    def test_retained_requires_ack(self):
        r=self.analyze();decisions={f['id']:False for f in r['findings']}
        self.assertEqual(self.request('/v1/export',{'session_id':r['session_id'],'decisions':decisions})[0],400)
        status,out,_=self.request('/v1/export',{'session_id':r['session_id'],'decisions':decisions,'acknowledged':True});self.assertEqual(status,200);self.assertIn(b'alice@example.org',base64.b64decode(out['data']))
    def test_clean_export_and_audit_no_raw_text(self):
        r=self.analyze('Contact audit.person@example.org');status,out,_=self.request('/v1/export',{'session_id':r['session_id']});self.assertEqual(status,200);self.assertNotIn(b'audit.person@example.org',base64.b64decode(out['data']))
        events=self.request('/v1/audit')[1];self.assertNotIn('audit.person@example.org',json.dumps(events))
    def test_workflow_mapping(self):
        r=self.analyze('Private Workflow then public review.',{'replacements':{'Private Workflow':'generic workflow'}})
        out=self.request('/v1/preview',{'session_id':r['session_id']})[1];self.assertEqual(out['text'],'generic workflow then public review.')
    def test_forgotten_review_cannot_export(self):
        r=self.analyze();self.request('/v1/forget',{'session_id':r['session_id']});self.assertEqual(self.request('/v1/export',{'session_id':r['session_id']})[0],400)
    def test_expired_session(self):
        r=self.analyze();self.state.sessions[r['session_id']]['created']-=1801;self.assertEqual(self.request('/v1/preview',{'session_id':r['session_id']})[0],400)
    def test_invalid_text_type(self):self.assertEqual(self.request('/v1/analyze',{'text':[]})[0],400)
    def test_no_remote_model_on_inline(self):
        with patch('sharesafe_engine.core.LocalModel.request',side_effect=AssertionError('Network forbidden')):
            self.assertEqual(self.request('/v1/browser/review',{'text':'Public draft.'},token='b'*64)[0],200)
    def test_requested_model_failure_stops_export(self):
        with patch('sharesafe_engine.core.LocalModel.detect',side_effect=RuntimeError('Offline')):
            r=self.analyze('Public draft.',{'use_model':True});self.assertEqual(r['model_status'],'failed')
            self.assertEqual(self.request('/v1/export',{'session_id':r['session_id']})[0],400)
    def test_graph_retrieves_source(self):
        r=self.analyze('Project Orion is confidential. Project Orion includes module Vega.')
        response=self.request('/v1/query',{'session_id':r['session_id'],'question':'Orion'})[1];self.assertTrue(response['evidence']);self.assertIn('Orion',response['evidence'][0]['quote'])
    def test_native_image_review_gate_and_pixels(self):
        image=Image.new('RGB',(60,40),'white');buffer=io.BytesIO();image.save(buffer,format='PNG')
        status,r,_=self.request('/v1/analyze',{'name':'input.png','file':base64.b64encode(buffer.getvalue()).decode()});self.assertEqual(status,200)
        body={'session_id':r['session_id'],'boxes':[{'x':5,'y':5,'width':20,'height':10}]}
        self.assertEqual(self.request('/v1/export',body)[0],400)
        body['image_reviewed']=True;status,out,_=self.request('/v1/export',body);self.assertEqual(status,200)
        output=Image.open(io.BytesIO(base64.b64decode(out['data'])));self.assertEqual(output.getpixel((10,10)),(0,0,0));self.assertEqual(output.getpixel((40,20)),(255,255,255))

class OCRTests(unittest.TestCase):
    def image(self):
        b=io.BytesIO();Image.new('RGB',(100,50),'white').save(b,format='PNG');return b.getvalue()
    def test_offsets_validated(self):
        with self.assertRaises(ValueError):image_asset('x.png',self.image(),{'text':'abc','words':[{'text':'ab','start':0,'end':3,'x':1,'y':1,'width':5,'height':5}]})
    def test_geometry_validated(self):
        with self.assertRaises(ValueError):image_asset('x.png',self.image(),{'text':'abc','words':[{'text':'abc','start':0,'end':3,'x':float('nan'),'y':1,'width':5,'height':5}]})
    def test_native_unicode_scalar_offsets(self):
        asset=image_asset('x.png',self.image(),{'text':'🙂 email','words':[{'text':'email','start':2,'end':7,'x':1,'y':1,'width':5,'height':5}]});self.assertEqual(asset['words'][0]['text'],'email')
    def test_origin_parser(self):
        self.assertTrue(extension_origin('safari-web-extension://'+'A'*36));self.assertFalse(extension_origin('chrome-extension://'+'a'*32+'/evil'));self.assertFalse(extension_origin('https://example.org'))

if __name__=='__main__':unittest.main()
