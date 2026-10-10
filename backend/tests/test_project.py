import base64
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from sharesafe_engine.project import prepare_project, export_project, folder_entries, safe_path


def entry(path,text): return {'path':path,'data':text.encode()}
def contents(plan):
    payload,_=export_project(plan,True)
    with zipfile.ZipFile(io.BytesIO(payload)) as z:
        return {n:z.read(n).decode() for n in z.namelist()}


class ProjectTests(unittest.TestCase):
    def test_real_fixture_secret_and_prompt_removed_from_actual_zip(self):
        plan=prepare_project([entry('main.py','API_KEY = "fixture-secret-3928"\nEMAIL = "private.person@example.com"\n')],
                             prompt='Use fixture-secret-3928 and contact private.person@example.com.')
        dump='\n'.join(k+'\n'+v for k,v in contents(plan).items())
        self.assertNotIn('fixture-secret-3928',dump);self.assertNotIn('private.person@example.com',dump)
        self.assertTrue(plan['report']['known_values_absent']);self.assertEqual(plan['report']['network_calls'],0)
    def test_source_files_not_modified_and_replica_python_actually_runs(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'main.py').write_text('CLIENT = "PrivateClient"\nassert CLIENT\nprint("replica-executed")\n')
            before=hashlib.sha256((root/'main.py').read_bytes()).hexdigest()
            plan=prepare_project(folder_entries(root),{'PrivateClient':'GenericClient'})
            copy=root.parent/(root.name+'-prepared.py')
            try:
                copy.write_text(plan['files'][0]['text'])
                run=subprocess.run([sys.executable,'-I',str(copy)],capture_output=True,text=True,timeout=10)
                self.assertEqual(run.returncode,0);self.assertEqual(run.stdout.strip(),'replica-executed')
                self.assertEqual(before,hashlib.sha256((root/'main.py').read_bytes()).hexdigest())
            finally: copy.unlink(missing_ok=True)
    def test_cross_file_explicit_alias_propagation(self):
        plan=prepare_project([entry('a.md','Project Atlas is confidential.'),entry('b.md','Project Atlas is also called Nimbus.'),entry('c.py','PROJECT = "Nimbus"\n')])
        dump='\n'.join(contents(plan).values())
        self.assertNotIn('Project Atlas',dump);self.assertNotIn('Nimbus',dump)
    def test_consistent_replacement_across_files_and_prompt(self):
        plan=prepare_project([entry('a.py','CLIENT="PrivateClient"'),entry('b.json','{"client":"PrivateClient"}')],{'PrivateClient':'GenericClient'},'Review PrivateClient')
        self.assertIn('GenericClient',plan['files'][0]['text']);self.assertIn('GenericClient',plan['files'][1]['text']);self.assertIn('GenericClient',plan['prompt'])
    def test_sensitive_path_and_import_are_renamed_consistently(self):
        plan=prepare_project([entry('PrivateClient.py','VALUE=1'),entry('main.py','from PrivateClient import VALUE\n')],{'PrivateClient':'GenericClient'})
        self.assertEqual(plan['files'][0]['path'],'GenericClient.py');self.assertIn('from GenericClient import',plan['files'][1]['text'])
    def test_email_in_filename_is_removed(self):
        plan=prepare_project([entry('private.person@example.com.md','public text')])
        self.assertNotIn('private.person@',plan['files'][0]['path'])
    def test_private_files_history_agent_config_binaries_excluded(self):
        paths=['.env','.git/config','a.pem','a.png','AGENTS.md','.codex/config.toml','node_modules/package/a.js','.antigravity/settings.json']
        plan=prepare_project([entry('main.py','VALUE=1')]+[entry(p,'fixture-secret-leak') for p in paths])
        self.assertEqual(plan['report']['excluded_files'],len(paths));self.assertEqual(plan['report']['included_files'],1)
        self.assertNotIn('fixture-secret-leak','\n'.join(contents(plan).values()))
    def test_unsupported_file_content_need_not_be_read(self):
        plan=prepare_project([entry('main.py','VALUE=1'),{'path':'big.png','omitted':True}])
        self.assertEqual(plan['report']['excluded_files'],1)
    def test_binary_masquerading_as_text_excluded(self):
        plan=prepare_project([entry('a.py','VALUE=1'),{'path':'b.py','data':b'hello\x00binary'}])
        self.assertEqual(plan['excluded'][0]['reason'],'binary_content')
    def test_invalid_utf8_excluded(self):
        plan=prepare_project([entry('a.py','VALUE=1'),{'path':'b.py','data':b'\xff\xfe'}])
        self.assertEqual(plan['excluded'][0]['reason'],'not_utf8')
    def test_links_fail_closed_without_following(self):
        plan=prepare_project([entry('a.py','VALUE=1'),{'path':'linked.py','link':True}])
        self.assertEqual(plan['excluded'][0]['reason'],'link_or_reparse_point')
    def test_traversal_absolute_ads_and_windows_devices_rejected(self):
        for p in ['../outside.py','/a.py','C:/a.py','a\\b.py','a.py:secret','x/../a.py','a//b.py','CON.py','x./a.py']:
            with self.subTest(path=p),self.assertRaises(ValueError): safe_path(p)
    def test_duplicate_case_paths_rejected(self):
        with self.assertRaises(ValueError):prepare_project([entry('Main.py','X=1'),entry('main.py','X=2')])
    def test_renamed_path_collision_rejected(self):
        with self.assertRaises(ValueError):prepare_project([entry('Alpha.py','X=1'),entry('Bravo.py','X=2')],{'Alpha':'GenericName','Bravo':'GenericName'})
    def test_generated_filename_collision_rejected(self):
        with self.assertRaises(ValueError):prepare_project([entry('SHARESAFE_MANIFEST.json','{}')])
    def test_review_gate_cannot_be_bypassed_with_truthy_value(self):
        plan=prepare_project([entry('a.py','X=1')])
        for value in [False,None,1,'yes']:
            with self.subTest(value=value),self.assertRaises(ValueError):export_project(plan,value)
    def test_private_mapping_and_original_paths_not_in_manifest(self):
        plan=prepare_project([entry('PrivateClient.py','X="fixture-private@example.com"')],{'PrivateClient':'GenericClient'})
        manifest=contents(plan)['agent-workspace/SHARESAFE_MANIFEST.json']
        self.assertNotIn('PrivateClient',manifest);self.assertNotIn('fixture-private@example.com',manifest);self.assertNotIn('mappings',manifest)
    def test_actual_export_rescanned_after_plan_tampering(self):
        plan=prepare_project([entry('a.py','X="private@example.com"')]);plan['files'][0]['text']='private@example.com'
        with self.assertRaises(ValueError):export_project(plan,True)
    def test_new_detectable_value_in_export_is_also_blocked(self):
        plan=prepare_project([entry('a.py','X=1')]);plan['files'][0]['text']='X="new-private@example.com"'
        with self.assertRaises(ValueError):export_project(plan,True)
    def test_python_syntax_damage_blocks_preparation(self):
        with self.assertRaises(ValueError):prepare_project([entry('a.py','class PrivateClient:\n    pass\n')],{'PrivateClient':'GenericClient','class':'GenericKeyword'})
    def test_duplicate_json_keys_after_replacement_blocked(self):
        with self.assertRaises(ValueError):prepare_project([entry('a.json','{"Alpha":1,"Bravo":2}')],{'Alpha':'GenericName','Bravo':'GenericName'})
    def test_unparseable_input_reported_without_syntax_assurance(self):
        plan=prepare_project([entry('a.py','not valid python =')]);self.assertEqual(plan['files'][0]['syntax'],'input_not_parseable')
    def test_unknown_language_has_no_false_syntax_claim(self):
        plan=prepare_project([entry('a.ts','const value = 1;')]);self.assertEqual(plan['files'][0]['syntax'],'not_checked')
    def test_all_excluded_folder_rejected(self):
        with self.assertRaises(ValueError):prepare_project([entry('.env','PASSWORD=x')])
    def test_local_paths_and_endpoint_values_removed(self):
        plan=prepare_project([entry('a.py','PATH = "C:\\Users\\PrivateUser\\secret.txt"\nURL="https://private.example.com/api"\n')])
        dump='\n'.join(contents(plan).values());self.assertNotIn('PrivateUser',dump);self.assertNotIn('private.example.com',dump)
    def test_environment_lookup_kept_as_code(self):
        plan=prepare_project([entry('a.js','const api_key = process.env.MY_API_KEY;')])
        self.assertIn('process.env.MY_API_KEY',plan['files'][0]['text'])
    def test_secret_assignment_variants_detected(self):
        plan=prepare_project([entry('a.json','{"DB_PASSWORD":"fixture-password-111","client_secret":"fixture-secret-222"}')])
        dump='\n'.join(contents(plan).values());self.assertNotIn('fixture-password-111',dump);self.assertNotIn('fixture-secret-222',dump)
    def test_generated_readme_is_access_guidance_not_sandbox_claim(self):
        plan=prepare_project([entry('a.py','X=1')]);self.assertFalse(plan['report']['os_agent_isolation_enforced'])
        self.assertIn('not an OS',contents(plan)['agent-workspace/SHARESAFE_AGENT_README.md'])
    def test_no_model_or_network_invocation(self):
        with patch('sharesafe_engine.core.LocalModel.request',side_effect=AssertionError('Network forbidden')):
            plan=prepare_project([entry('a.py','EMAIL="private@example.com"')]);export_project(plan,True)
    def test_empty_utf8_file_preserved(self):
        plan=prepare_project([entry('__init__.py','')]);self.assertEqual(plan['files'][0]['text'],'')
    def test_size_limit_blocks_preparation(self):
        with self.assertRaises(ValueError):prepare_project([entry('big.py','x'*200001)])
    def test_invalid_glossary_replacement_rejected(self):
        for replacement in ['../outside','Generic Client','Generic"Client','x']:
            with self.subTest(replacement=replacement),self.assertRaises(ValueError):prepare_project([entry('a.py','X=1')],{'PrivateClient':replacement})
    def test_export_has_no_original_filesystem_metadata(self):
        plan=prepare_project([entry('a.py','X=1')]);payload,_=export_project(plan,True)
        with zipfile.ZipFile(io.BytesIO(payload)) as z:
            self.assertTrue(all(i.filename.startswith('agent-workspace/') and not i.is_dir() for i in z.infolist()))
            self.assertTrue(all((i.external_attr>>16)&0o170000==0o100000 for i in z.infolist()))

if __name__=='__main__':unittest.main()
