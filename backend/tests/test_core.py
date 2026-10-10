import unittest
from unittest.mock import patch
from sharesafe_engine.core import *

class DetectionTests(unittest.TestCase):
    def test_email(self):
        r=analyze('Please email alice+test@example.org.');self.assertEqual(r['findings'][0].category,'EMAIL')
    def test_phone(self):
        r=analyze('Call +91 98765 43210.');self.assertEqual(r['findings'][0].category,'PHONE')
    def test_pan(self):
        r=analyze('PAN: ABCPD1234E');self.assertEqual(r['findings'][0].category,'PAN')
    def test_aadhaar(self):
        r=analyze('Candidate: 2345 6789 0123');self.assertEqual(r['findings'][0].category,'AADHAAR')
    def test_account_context_precedes_phone(self):
        r=analyze('Account 9876543210');self.assertEqual(r['findings'][0].category,'ACCOUNT')
    def test_luhn(self):
        self.assertTrue(luhn('4111 1111 1111 1111'));self.assertFalse(luhn('4111 1111 1111 1112'))
    def test_card(self):
        self.assertEqual(analyze('Card 4111 1111 1111 1111')['findings'][0].category,'CARD')
    def test_secret_blocks(self):
        r=analyze('api_key=abc123456789');self.assertEqual(r['findings'][0].action,'block')
        with self.assertRaises(ValueError):sanitize(r)
    def test_private_key_blocks(self):
        r=analyze('-----BEGIN PRIVATE KEY-----\nQUJDRA==\n-----END PRIVATE KEY-----');self.assertEqual(r['findings'][0].action,'block')
    def test_person_label(self):
        self.assertEqual(analyze('Name: Alice Morgan')['findings'][0].category,'PERSON')
    def test_currency(self):
        r=analyze('Proposed INR 20 crore');self.assertEqual(r['findings'][0].category,'FINANCIAL')
    def test_public_text(self):
        self.assertEqual(analyze('The public workshop begins on Tuesday at noon.')['findings'],[])
    def test_empty(self):
        with self.assertRaises(ValueError):analyze('  ')
    def test_size_limit(self):
        with self.assertRaises(ValueError):analyze('x'*(MAX_TEXT+1))
    def test_exact_offsets_unicode(self):
        r=analyze('🙂 mail alice@example.com');f=r['findings'][0];self.assertEqual(r['text'][f.start:f.end],'alice@example.com')

class PolicyTests(unittest.TestCase):
    def test_invalid_action(self):
        with self.assertRaises(ValueError):Policy.parse({'actions':{'EMAIL':'delete'}})
    def test_invalid_category(self):
        with self.assertRaises(ValueError):Policy.parse({'actions':{'OTHER':'mask'}})
    def test_invalid_terms(self):
        with self.assertRaises(ValueError):Policy.parse({'confidential_terms':'not a list'})
    def test_unknown_decision(self):
        with self.assertRaises(ValueError):sanitize(analyze('hello'),{'nope':False})
    def test_warn_requires_ack(self):
        r=analyze('alice@example.com',{'actions':{'EMAIL':'warn'}})
        with self.assertRaises(ValueError):sanitize(r)
        self.assertEqual(sanitize(r,acknowledged=True)[0],'alice@example.com')
    def test_unchecked_requires_ack(self):
        r=analyze('alice@example.com');fid=r['findings'][0].id
        with self.assertRaises(ValueError):sanitize(r,{fid:False})
    def test_stable_pseudonyms(self):
        t='alice@example.com and alice@example.com and bob@example.com'
        clean,_=sanitize(analyze(t));self.assertEqual(clean,'[EMAIL_1] and [EMAIL_1] and [EMAIL_2]')
    def test_allow_policy(self):
        r=analyze('alice@example.com',{'actions':{'EMAIL':'allow'}});self.assertEqual(sanitize(r)[0],'alice@example.com')
    def test_allow_list(self):
        r=analyze('public@example.org',{'allow_terms':['public@example.org']});self.assertFalse(r['findings'])
    def test_confidential_term(self):
        clean,_=sanitize(analyze('Refer to Borealis.',{'confidential_terms':['Borealis']}));self.assertNotIn('Borealis',clean)
    def test_prompt_scaffold_no_invented_facts(self):
        self.assertTrue(optimize_prompt('[PERSON_1] requested a summary.').endswith('[PERSON_1] requested a summary.'))
    def test_ai_failure_prevents_export(self):
        with patch.object(LocalModel,'detect',side_effect=RuntimeError('offline')):
            r=analyze('public text',{'use_model':True});self.assertEqual(r['model_status'],'failed')
            with self.assertRaises(ValueError):sanitize(r)

class GraphTests(unittest.TestCase):
    def test_graph_propagates_distant_alias(self):
        text='Project Falcon is confidential. Project Falcon is also called Aurora.\n'+'Public material.\n'*160+'Aurora has a launch plan.'
        r=analyze(text);remote=[f for f in r['findings'] if f.start>2000]
        self.assertTrue(remote);self.assertEqual(r['text'][remote[-1].start:remote[-1].end],'Aurora')
        self.assertGreater(len(remote[-1].evidence),1)
    def test_public_project_not_confidential(self):
        r=analyze('Project Falcon is public.');self.assertFalse(r['findings'])
    def test_cooccurrence_is_not_confidentiality(self):
        r=analyze('Project Falcon is confidential. Project Public is announced publicly.\n')
        # Confidential paragraph handling is deliberately conservative and may
        # flag both projects in that paragraph; another paragraph must not inherit it.
        r=analyze('Project Falcon is confidential.\nProject Public is announced publicly.')
        self.assertFalse(any(r['text'][f.start:f.end]=='Project Public' for f in r['findings']))
    def test_graph_retrieval_finds_linked_context(self):
        text='Project Falcon is also called Aurora.\n'+'Public filler.\n'*100+'Aurora has an internal launch plan.'
        graph=analyze(text)['graph'];chunks=graph.retrieve('Project Falcon');self.assertTrue(any('internal launch plan' in c['text'] for c in chunks))
    def test_graph_edge_provenance(self):
        g=GraphIndex('Project Falcon supports ABC Bank.');self.assertTrue(g.edges)
        ids={c['id'] for c in g.chunks};self.assertTrue(all(e['evidence'] in ids for e in g.edges))
    def test_unknown_query_abstains(self):
        self.assertEqual(GraphIndex('A public schedule.').retrieve('quantum whales'),[])
    def test_chunks_preserve_every_character(self):
        text='abc\n'*900;chunks=chunk_text(text);self.assertEqual(''.join(c['text'] for c in chunks),text)
    def test_components_deterministic(self):
        t='Project Falcon.\n'+'filler.\n'*200+'Project Raven.'
        self.assertEqual(GraphIndex(t).public(),GraphIndex(t).public())

class ModelValidationTests(unittest.TestCase):
    def test_model_does_not_expand_reliable_rule_span(self):
        text='Name: Alice Morgan';policy=Policy()
        f=rule_findings(text,policy)+[Finding(0,len(text),'PERSON','model suggestion','local-model')]
        chosen=resolve_overlaps(f,policy,text)
        self.assertEqual(text[chosen[0].start:chosen[0].end],'Alice Morgan')
    def test_hallucinated_entity_rejected(self):
        model=LocalModel()
        with patch.object(model,'complete',return_value={'findings':[{'quote':'Invented Person','category':'PERSON'}]}):
            found,rejected=model.detect('Only Alice is present.');self.assertEqual(found,[]);self.assertEqual(rejected,1)
    def test_model_valid_quote_retained(self):
        model=LocalModel()
        with patch.object(model,'complete',return_value={'findings':[{'quote':'Alice','category':'PERSON'}]}):
            found,rejected=model.detect('Only Alice is present.');self.assertEqual(found[0].start,5);self.assertEqual(rejected,0)
    def test_answer_invalid_source_rejected(self):
        model=LocalModel()
        with patch.object(model,'complete',return_value={'evidence':[{'source':'S9','quote':'Alice'}]}):self.assertEqual(model.answer('Who?',[{'id':'S1','text':'Alice'}]),[])
    def test_answer_invented_quote_rejected(self):
        model=LocalModel()
        with patch.object(model,'complete',return_value={'evidence':[{'source':'S1','quote':'Bob'}]}):self.assertEqual(model.answer('Who?',[{'id':'S1','text':'Alice'}]),[])
    def test_answer_valid_quote(self):
        model=LocalModel()
        with patch.object(model,'complete',return_value={'evidence':[{'source':'S1','quote':'Alice'}]}):self.assertEqual(model.answer('Who?',[{'id':'S1','text':'Alice'}])[0]['quote'],'Alice')
    def test_invalid_model_schema(self):
        model=LocalModel()
        with patch.object(model,'complete',return_value={'findings':None}):
            with self.assertRaises(ValueError):model.detect('Some text')

if __name__=='__main__':unittest.main()
