import copy,importlib.util,json,os,socket,tempfile,unittest
from types import SimpleNamespace
from pathlib import Path
from unittest import mock
import docker_health_adapter as a
class AdapterTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
  self.scope={'images':{repo+':'+version:'sha256:'+('a'*64) for repo in ['daprio/sentry','daprio/dapr'] for version in ['1.18.3','1.18.7']},'sentryConfigRoots':['/tmp/owned-platform'],'ownerPid':51,'ownerStartTicks':'123','portRange':[20000,20100],'windowsExcludedRanges':[],'allocationState':str(self.root/'allocated.json')}
 def command(self,kind='dapr-placement-mtls',version='1.18.7'):
  entry,repo=a.KINDS[kind];name=kind+'-abcdefgh';prefix=a.LABEL
  result=['create','--name',name,'--mount','type=bind,src=/tmp/works-sdk415-'+('a'*32)+'/certificates,target=/var/run/dapr/credentials'+('' if kind=='dapr-sentry' else ',readonly'),'-p','127.0.0.1:65533:50005/tcp','-p','127.0.0.1::8080/tcp','-e','SENSITIVE=never-log-this','--label','opaque=unrelated-private-value']
  for k,v in {'name':name,'uid':'88888888-1111-2222-3333-444444444444','group-version':'usvc-dev.developer.microsoft.com/v1','creatorProcessId':'50','creatorProcessStartTime':'owned-start'}.items():result+=['--label',prefix+k+'='+v]
  if kind=='dapr-sentry':result+=['--mount','type=bind,src=/tmp/owned-platform/DaprComponents/sentry.yaml,target=/var/run/dapr/config/sentry.yaml,readonly']
  return result+['--user','1000:1000','--entrypoint',entry,repo+':'+version,'--healthz-listen-address=0.0.0.0','--tls-enabled']
 def test_six_selected_variants_change_one_value_and_preserve_grpc_env_and_command(self):
  for kind in a.KINDS:
   for version in ['1.18.3','1.18.7']:
    with self.subTest(kind=kind,version=version):
     original=self.command(kind,version);identity=a.shape(original,self.scope);self.assertIsNotNone(identity);effective=a.rewrite(original,identity,23456)
     self.assertEqual([i for i,(x,y) in enumerate(zip(original,effective)) if x!=y],[identity['publishIndex']]);self.assertEqual(effective[identity['publishIndex']],'127.0.0.1:23456:8080/tcp');self.assertEqual(len(original),len(effective));self.assertIn('127.0.0.1:65533:50005/tcp',effective);self.assertIn('SENSITIVE=never-log-this',effective)
 def test_native_uppercase_tcp_publish_is_admitted_without_changing_protocol(self):
  args=[v.replace('/tcp','/TCP') if v.startswith('127.0.0.1:') else v for v in self.command()]
  candidate=a.shape(args,self.scope);self.assertIsNotNone(candidate)
  changed=a.rewrite(args,candidate,23456);self.assertEqual(changed[candidate['publishIndex']],'127.0.0.1:23456:8080/TCP')
 def test_foreign_and_ambiguous_requests_are_not_admitted(self):
  original=self.command();cases=[]
  def change(before,after):return [after if x==before else x for x in original]
  cases.extend([change('create','run'),change('dapr-placement-mtls-abcdefgh','foreign-abcdefgh'),change('daprio/dapr:1.18.7','foreign/dapr:1.18.7'),change('./placement','./scheduler'),change('127.0.0.1::8080/tcp','0.0.0.0::8080/tcp'),change('127.0.0.1::8080/tcp','127.0.0.1:23456:8080/tcp'),change('127.0.0.1::8080/tcp','127.0.0.1::8080/udp'),change(a.LABEL+'creatorProcessId=50',a.LABEL+'creatorProcessId=not-a-pid'),change(a.LABEL+'uid=88888888-1111-2222-3333-444444444444',a.LABEL+'uid=invalid'),change(a.LABEL+'group-version=usvc-dev.developer.microsoft.com/v1',a.LABEL+'group-version=foreign'),change(a.LABEL+'name=dapr-placement-mtls-abcdefgh',a.LABEL+'name=another-name')])
  mount=original[original.index('--mount')+1];cases.extend([change(mount,mount.replace('/tmp/works-sdk415-','/tmp/foreign-')),change(mount,mount.replace(',readonly','')),change(mount,mount.replace('credentials','other')),original[:1]+['--unsupported-value','x']+original[1:],original[:1]+['-p','127.0.0.1::8080/tcp']+original[1:]])
  for index,args in enumerate(cases):
   with self.subTest(case=index):self.assertIsNone(a.shape(args,self.scope))
 def test_sentry_requires_exact_readonly_owned_configuration_mount(self):
  args=self.command('dapr-sentry');mount=[x for x in args if 'target=/var/run/dapr/config/sentry.yaml' in x][0]
  for replacement in [mount.replace(',readonly',''),mount.replace('/tmp/owned-platform/','/tmp/foreign-platform/'),mount.replace('sentry.yaml','other.yaml')]:
   with self.subTest(replacement=replacement):self.assertIsNone(a.shape([replacement if x==mount else x for x in args],self.scope))
 def test_no_config_execs_all_original_arguments_without_interception(self):
  args=['inspect','--format','SENSITIVE=never-log-this','foreign-id'];stop=RuntimeError('exec boundary')
  with mock.patch.dict(os.environ,{},clear=True),mock.patch.object(a.sys,'argv',['docker',*args]),mock.patch.object(a.os,'execv',side_effect=stop) as execute:
   with self.assertRaisesRegex(RuntimeError,'exec boundary'):a.main()
  execute.assert_called_once_with('/usr/bin/docker',['/usr/bin/docker',*args])
 def test_ownership_refuses_reused_root_and_non_descendant_creator(self):
  candidate=a.shape(self.command(),self.scope)
  with mock.patch.object(a,'stat',return_value={'ppid':1,'startTicks':'wrong'}):self.assertFalse(a.ownership(candidate,self.scope))
  with mock.patch.object(a,'stat',return_value={'ppid':1,'startTicks':'123'}):self.assertFalse(a.ownership(candidate,self.scope))
 def test_owned_ancestry_positive_then_non_dcp_and_foreign_uid_refusals(self):
  candidate=a.shape(self.command(),self.scope);chain={100:{'ppid':50,'startTicks':'100'},50:{'ppid':51,'startTicks':'50'},51:{'ppid':1,'startTicks':'123'}}
  for exe,uid,expected in [('dcp',os.getuid(),True),('python3',os.getuid(),False),('dcp',os.getuid()+1,False)]:
   with self.subTest(executable=exe,uid=uid),mock.patch.object(a.os,'getpid',return_value=100),mock.patch.object(a,'stat',side_effect=lambda pid:chain[pid]),mock.patch.object(a.Path,'resolve',return_value=Path('/owned/'+exe)),mock.patch.object(a.Path,'stat',return_value=SimpleNamespace(st_uid=uid)):
    self.assertEqual(a.ownership(candidate,self.scope),expected)
 def test_native_identity_positive_and_wrong_image_label_and_pre_scope_refusals(self):
  candidate=a.shape(self.command(),self.scope);self.scope.update(realDocker='/owned/docker',createdAtEpoch=1700000000)
  expected=self.scope['images'][candidate['image']]
  for image,label,created,accepted in [(expected,candidate['proofName'],'2030-01-01T00:00:00Z',True),('sha256:wrong',candidate['proofName'],'2030-01-01T00:00:00Z',False),(expected,'foreign-proof','2030-01-01T00:00:00Z',False),(expected,candidate['proofName'],'2000-01-01T00:00:00Z',False)]:
   redis={'id':'exact-proof-id','label':label,'created':created};responses=[SimpleNamespace(returncode=0,stdout=(image+'\n').encode()),SimpleNamespace(returncode=0,stdout=json.dumps(redis).encode())]
   with self.subTest(image=image,label=label,created=created),mock.patch.object(a.subprocess,'run',side_effect=responses) as run:
    result=a.native_identity(candidate,self.scope);self.assertEqual(result is not None,accepted);self.assertEqual(run.call_count,1 if image!=expected else 2)
 def test_detached_controller_requires_actual_api_ancestor_and_owned_server_monitor(self):
  candidate=a.shape(self.command(),self.scope);self.scope['ownerStartTicks']='100'
  chain={100:{'ppid':50,'startTicks':'140'},50:{'ppid':60,'startTicks':'130'},60:{'ppid':1,'startTicks':'120'},75:{'ppid':51,'startTicks':'110'},51:{'ppid':1,'startTicks':'100'},76:{'ppid':1,'startTicks':'110'},61:{'ppid':1,'startTicks':'120'}}
  for api,monitor,config,expected in [('60','75','/owned/kubeconfig',True),('61','75','/owned/kubeconfig',False),('60','76','/owned/kubeconfig',False),('60','75','/foreign/kubeconfig',False)]:
   commands={'/proc/50/cmdline':['dcp','run-controllers','--monitor',api,'--kubeconfig','/owned/kubeconfig'],'/proc/60/cmdline':['dcp','start-apiserver','--monitor',monitor,'--kubeconfig',config]}
   def raw(path):return b'\0'.join(x.encode() for x in commands[str(path)])+b'\0'
   with self.subTest(api=api,monitor=monitor,config=config),mock.patch.object(a.os,'getpid',return_value=100),mock.patch.object(a,'stat',side_effect=lambda pid:chain[pid]),mock.patch.object(a.Path,'resolve',return_value=Path('/owned/dcp')),mock.patch.object(a.Path,'stat',return_value=SimpleNamespace(st_uid=os.getuid())),mock.patch.object(a.Path,'read_bytes',autospec=True,side_effect=raw):
    self.assertEqual(a.ownership(candidate,self.scope),expected)
 def test_scope_factory_binds_explicit_selected_configuration_roots(self):
  import configure_scope
  config,scope=configure_scope.create_scope(self.root/'scope-output',['/tmp/selected/full2/platform/DaprComponents','/tmp/selected/full2/retained-source'])
  self.assertEqual(scope['sentryConfigRoots'],['/tmp/selected/full2/platform/DaprComponents','/tmp/selected/full2/retained-source']);self.assertEqual(scope['ownerPid'],os.getpid());self.assertTrue(config.is_file())
 def test_allocator_excludes_busy_reserved_and_windows_ports_and_never_reuses(self):
  held=socket.socket();held.bind(('127.0.0.1',20001));self.addCleanup(held.close);self.scope['windowsExcludedRanges']=[[20002,20002]]
  with mock.patch.object(a.random.SystemRandom,'randrange',side_effect=[20001,20002,20003]):port,minimum=a.allocate(self.scope)
  self.assertEqual(port,20003);self.assertLess(port,minimum)
  with mock.patch.object(a.random.SystemRandom,'randrange',side_effect=[20003,20004]):second,_=a.allocate(self.scope)
  self.assertEqual(second,20004);self.assertEqual(json.loads(Path(self.scope['allocationState']).read_text()),[20003,20004])
 def test_rewrite_guard_remains_active_under_python_optimization(self):
  args=self.command();identity=a.shape(args,self.scope)
  with self.assertRaisesRegex(RuntimeError,'exactly one'):a.rewrite(['127.0.0.1::8080/tcp'],{'publishIndex':0},'')
if __name__=='__main__':unittest.main(verbosity=2)
