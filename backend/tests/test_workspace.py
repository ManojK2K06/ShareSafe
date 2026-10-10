import json,os,subprocess,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from sharesafe_engine.workspace import Workspaces,bounded_process,suspicious,SAFE_ENV

class WorkspaceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();root=Path(self.temp.name);self.source=root/'original';self.source.mkdir()
        (self.source/'main.py').write_text('API_KEY="fixture-secret-9328"\nEMAIL="person@example.org"\nprint("public")\n')
        (self.source/'.env').write_text('private-token-111')
        self.manager=Workspaces(root/'storage',docker='/usr/bin/docker')
    def tearDown(self):self.temp.cleanup()
    def prepared(self):return self.manager.prepare(str(self.source))
    def create(self):return self.manager.create(self.prepared()['project_id'],True)['workspace_id']
    def test_prepare_source_unchanged(self):
        before=(self.source/'main.py').read_bytes();r=self.prepared();self.assertEqual(before,(self.source/'main.py').read_bytes());self.assertNotIn('fixture-secret',json.dumps(r['files']));self.assertNotIn('person@example.org',json.dumps(r['files']));self.assertEqual(r['report']['excluded_files'],1)
    def test_review_required(self):
        r=self.prepared()
        for value in (False,1,'yes',None):
            with self.assertRaises(ValueError):self.manager.create(r['project_id'],value)
    def test_materialized_copy_verified_private_map_absent(self):
        wid=self.create();r=self.manager.verify(wid);self.assertTrue(r['passed']);dump=''.join((self.manager.root/wid/f).read_text() for f in r['files']);self.assertNotIn('fixture-secret',dump);self.assertNotIn('private-token',dump)
        self.assertIn('fixture-secret', (self.manager.registry/(wid+'.json')).read_text());self.assertNotIn('private-registry',r['files'])
    def test_plans_expire(self):
        r=self.prepared();self.manager.plans[r['project_id']]['created']-=1801
        with self.assertRaises(ValueError):self.manager.create(r['project_id'],True)
    def test_private_storage_not_source(self):
        with self.assertRaises(ValueError):self.manager.prepare(str(self.manager.root))
    def test_relative_source_rejected(self):
        with self.assertRaises(ValueError):self.manager.prepare('.')
    def test_residual_original_value_quarantines(self):
        wid=self.create();(self.manager.root/wid/'new.txt').write_text('fixture-secret-9328');r=self.manager.verify(wid);self.assertFalse(r['passed']);self.assertTrue(self.manager.list()[0]['quarantined'])
        with patch('sharesafe_engine.workspace.subprocess.Popen',side_effect=AssertionError('Must not execute')):
            with self.assertRaises(ValueError):self.manager.run(wid,['python','main.py'])
    def test_new_secret_quarantines(self):
        wid=self.create();(self.manager.root/wid/'new.txt').write_text('github_pat_'+'a'*30);self.assertFalse(self.manager.verify(wid)['passed'])
    def test_binary_quarantines(self):
        wid=self.create();(self.manager.root/wid/'binary.bin').write_bytes(b'\x00\xff');self.assertFalse(self.manager.verify(wid)['passed'])
    def test_soft_link_quarantines(self):
        wid=self.create()
        try:(self.manager.root/wid/'link.txt').symlink_to(self.source/'main.py')
        except OSError as error:self.skipTest(str(error))
        self.assertFalse(self.manager.verify(wid)['passed'])
    def test_hard_link_quarantines(self):
        wid=self.create();os.link(self.source/'main.py',self.manager.root/wid/'link.py');self.assertFalse(self.manager.verify(wid)['passed'])
    def test_command_isolation_flags_and_only_one_mount(self):
        wid=self.create();args=self.manager.arguments(wid,['python','main.py'])
        for item in ['none','--read-only','ALL','no-new-privileges','64','512m','--user','--cpus']:self.assertIn(item,args)
        self.assertEqual(args.count('--mount'),1);mount=args[args.index('--mount')+1];self.assertIn(str(self.manager.root/wid),mount);self.assertNotIn(str(self.source),mount)
        self.assertNotIn('--privileged',args);self.assertNotIn('--env-file',args);self.assertNotIn('DOCKER_HOST',SAFE_ENV);self.assertEqual(args[-2:],['python','main.py'])
    def test_local_docker_socket_explicit(self):
        args=self.manager.arguments(self.create(),['python','--version']);self.assertEqual(args[1],'--host');self.assertTrue(args[2].startswith(('unix://','npipe://')))
    def test_invalid_argv_and_id(self):
        wid=self.create()
        for argv in ['sh -c x',[],[''],['x\0y']]:
            with self.assertRaises(ValueError):self.manager.arguments(wid,argv)
        with self.assertRaises(ValueError):self.manager.record('../original')
    def test_no_host_shell_fallback(self):
        self.manager.docker=None
        with self.assertRaises(ValueError):self.manager.arguments(self.create(),['python','--version'])
    def test_deletion_confirms_and_preserves_source(self):
        wid=self.create()
        with self.assertRaises(ValueError):self.manager.remove(wid,False)
        self.manager.remove(wid,True);self.assertTrue((self.source/'main.py').exists());self.assertFalse((self.manager.root/wid).exists())
    def test_concurrent_execution_rejected(self):
        wid=self.create();self.manager.acquire(wid)
        with self.assertRaises(ValueError):self.manager.acquire(wid)
        with self.assertRaises(ValueError):self.manager.remove(wid,True)
        self.manager.release(wid)
    def test_unknown_private_term_requires_glossary_review(self):
        (self.source/'main.py').write_text('VALUE="CedarCustomer"\n')
        r=self.manager.prepare(str(self.source),{'CedarCustomer':'ExampleCustomer'},'Review CedarCustomer.')
        self.assertNotIn('CedarCustomer',json.dumps(r['files']));self.assertNotIn('CedarCustomer',r['prompt'])
    def test_generic_values_not_sensitive(self):self.assertFalse(suspicious('A public guide to Python.'))
    def test_extended_credentials(self):
        for value in ['xoxb-'+('a'*20),'github_pat_'+('a'*30),'eyJ'+('a'*12)+'.'+('b'*12)+'.'+('c'*12)]:self.assertTrue(suspicious(value))
    def test_bounded_output_stops_flooding(self):
        code,data=bounded_process([sys.executable,'-c','import sys;sys.stdout.buffer.write(b"a"*1000000)'],5,env=dict(os.environ))
        self.assertLess(len(data),25000);self.assertIn(b'Output limit',data)
    def test_bounded_timeout(self):
        code,_=bounded_process([sys.executable,'-c','import time;time.sleep(10)'],0.15,env=dict(os.environ));self.assertEqual(code,124)
    def test_agent_rejects_absolute_tool_path(self):
        wid=self.create()
        with patch('sharesafe_engine.workspace.subprocess.run',return_value=subprocess.CompletedProcess([],0,b'container',b'')) as docker, patch('sharesafe_engine.workspace.LocalModel.complete',return_value={'tool':'read','path':'/Users/private.txt'}), patch('sharesafe_engine.workspace.bounded_process') as tool:
            with self.assertRaises(ValueError):self.manager.local_agent(wid)
            tool.assert_not_called();self.assertFalse(self.manager.running);self.assertIn('rm',docker.call_args.args[0])
    def test_agent_rejects_sensitive_write_before_tool(self):
        wid=self.create()
        with patch('sharesafe_engine.workspace.subprocess.run',return_value=subprocess.CompletedProcess([],0,b'container',b'')), patch('sharesafe_engine.workspace.LocalModel.complete',return_value={'tool':'write','path':'new.py','text':'API_KEY="new-private-secret-123"'}), patch('sharesafe_engine.workspace.bounded_process') as tool:
            with self.assertRaises(ValueError):self.manager.local_agent(wid)
            tool.assert_not_called();self.assertFalse((self.manager.root/wid/'new.py').exists())
    def test_agent_withholds_sensitive_tool_output_from_model(self):
        wid=self.create()
        with patch('sharesafe_engine.workspace.subprocess.run',return_value=subprocess.CompletedProcess([],0,b'container',b'')), patch('sharesafe_engine.workspace.LocalModel.complete',side_effect=[{'tool':'read','path':'main.py'},{'tool':'finish','summary':'Review complete.'}]) as model, patch('sharesafe_engine.workspace.bounded_process',return_value=(0,b'fixture-secret-9328')) as tool:
            result=self.manager.local_agent(wid);self.assertTrue(result['completed']);self.assertNotIn('fixture-secret-9328',model.call_args.args[1]);self.assertIn('withheld',model.call_args.args[1]);self.assertIn('exec',tool.call_args.args[0]);self.assertIn('/usr/local/lib/sharesafe/workspace_tool.py',tool.call_args.args[0])
    def test_shutdown_cleans_tracked_containers_and_rejects_jobs(self):
        self.manager.containers['workspace']='sharesafe-test'
        with patch('sharesafe_engine.workspace.subprocess.run') as docker:
            self.manager.close();self.assertEqual(docker.call_args.args[0][-3:],['rm','--force','sharesafe-test'])
        with self.assertRaises(ValueError):self.manager.acquire('new-workspace')
    def test_swift_and_plist_source_included_and_cleaned(self):
        (self.source/'main.py').unlink();(self.source/'App.swift').write_text('let apiKey = "fixture-swift-secret-123"\nprint("public")\n');(self.source/'Info.plist').write_text('<plist><dict><key>Name</key><string>Example</string></dict></plist>')
        response=self.prepared();paths={f['path'] for f in response['files']};self.assertEqual(paths,{'App.swift','Info.plist'});self.assertNotIn('fixture-swift-secret',json.dumps(response['files']))
    def test_root_execution_rejected(self):
        wid=self.create()
        with patch('sharesafe_engine.workspace.os.getuid',return_value=0,create=True):
            with self.assertRaises(ValueError):self.manager.arguments(wid,['python','--version'])

if __name__=='__main__':unittest.main()
