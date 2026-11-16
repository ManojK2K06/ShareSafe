import test_bridge as fixture
import unittest

class InlinePolicyTests(unittest.TestCase):
    setUpClass=classmethod(fixture.BridgeTests.setUpClass.__func__)
    tearDownClass=classmethod(fixture.BridgeTests.tearDownClass.__func__)
    request=fixture.BridgeTests.request
    def tearDown(self):self.request('/v1/inline/policy',{})
    def test_browser_cannot_update_policy(self):self.assertEqual(self.request('/v1/inline/policy',{'replacements':{}},token='b'*64)[0],403)
    def test_quick_glossary_applies_to_browser(self):
        self.assertEqual(self.request('/v1/inline/policy',{'replacements':{'PrivateWorkflow':'generic workflow'}})[0],200)
        status,response,_=self.request('/v1/browser/review',{'text':'Explain PrivateWorkflow with api_key=fixture-secret-123'},token='b'*64)
        self.assertEqual(status,200);self.assertNotIn('PrivateWorkflow',response['text']);self.assertNotIn('fixture-secret',response['text']);self.assertIn('generic workflow',response['text'])
    def test_clear_all_invalidates_review(self):
        response=self.request('/v1/analyze',{'text':'Public text'})[1];self.assertEqual(self.request('/v1/clear',{})[0],200)
        self.assertEqual(self.request('/v1/export',{'session_id':response['session_id']})[0],400)
    def test_environment_block_action_applies_inline(self):
        self.assertEqual(self.request('/v1/inline/policy',{'actions':{'CREDENTIAL':'block'}})[0],200)
        status,response,_=self.request('/v1/browser/review',{'text':'api_key=fixture-secret-123'},token='b'*64)
        self.assertEqual(status,400);self.assertNotIn('text',response)
    def test_environment_bad_action_rejected(self):
        self.assertEqual(self.request('/v1/inline/policy',{'actions':{'EMAIL':'unknown'}})[0],400)
    def test_browser_cannot_stop_engine(self):
        self.assertEqual(self.request('/v1/shutdown',{},token='b'*64)[0],403)
        self.assertEqual(self.request('/v1/health')[0],200)
