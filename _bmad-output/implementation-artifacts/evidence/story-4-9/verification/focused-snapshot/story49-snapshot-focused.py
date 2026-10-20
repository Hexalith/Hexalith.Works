from pathlib import Path
import datetime, json, os, subprocess, sys
platform=Path('/tmp/story49-platform-immutable-v6');sys.path.insert(0,str(platform/'eng'))
import verify_works_host as gate, isolated_daprd, retained_works_host, works_host_snapshot
prep=Path('/tmp/story49-snapshot-focused-build-v6');manifest=Path('/tmp/story49-platform-immutable-v6.json');base=Path('/tmp/story49-snapshot-focused-20261020');base.mkdir()
p=json.loads((prep/'ledger.json').read_text());assert p['status']=='preparation-passed; parity-incomplete';snapshot=works_host_snapshot.verify(manifest,platform)
works=Path('/home/administrator/projects/hexalith/works');works_before=gate.canonical_source_snapshot(works)
runtime=Path(p['runtimeOutput']);assembly=Path(p['host']['assembly']);retained=Path(p['retainedHostSource']['assembly']);executable=prep/'consumer/bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests'
gate.require_identical_runtime(assembly.parent/'Hexalith.Platform.Works.Runtime.dll',executable.parent/'Hexalith.Platform.Works.Runtime.dll')
old=json.loads(Path('/tmp/story49-post-cutover-20261020/ledger.json').read_text())['runtimeEnvironment']
for lane,method in [('retained','Hexalith.Works.IntegrationTests.WorksPlatformRetainedHostPreflightTests.ArchivedTopologyReachesHealthyReadiness'),('callback','Hexalith.Works.IntegrationTests.WorksSdkRecoveryPipelineSmokeTests.SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery')]:
 root=base/lane;root.mkdir();ports=gate.select_control_plane_ports();shim=root/'isolated-daprd';binding=isolated_daprd.prepare(shim,Path.home()/'.dapr/bin/daprd',ports['sentry'])
 env=os.environ.copy();env.update(old);env.update({'HexalithBuildsRoot':'/tmp/story416-builds-b435f2e-public-source','WORKS_PLATFORM_APPHOST_ASSEMBLY':str(assembly),'WORKS_PLATFORM_APPHOST_SHA256':gate.sha256(assembly),'WORKS_PLATFORM_ROOT':str(platform),'WORKS_RETAINED_HOST_ROOT':p['retainedHostSource']['root'],'WORKS_RETAINED_HOST_ASSEMBLY':str(retained),'WORKS_RETAINED_HOST_SHA256':gate.sha256(retained),'ArtifactsPath':str(runtime),'WORKS_PLATFORM_RUNTIME_ARTIFACTS':str(runtime),'WORKS_PLATFORM_HOST_EVIDENCE':str(root/'host-starts.jsonl'),'WORKS_SDK_PROOF_EVIDENCE_PATH':str(root/'persisted-states.jsonl'),'WORKS_PLATFORM_DAPRD_RUNTIME_PATH':str(shim),**{'WORKS_PLATFORM_'+k.upper()+'_PORT':str(v) for k,v in ports.items()}})
 command=[str(executable),'-noLogo','-explicit','on','-method',method,'-result-xml',str(root/'result.xml')]
 record={'command':command,'started':datetime.datetime.now(datetime.timezone.utc).isoformat(),'controlPlanePorts':ports,'isolatedRuntime':binding,'snapshot':snapshot};(root/'command-result.json').write_text(json.dumps(record,indent=2)+'\n');print(lane,ports,flush=True)
 try:
  with (root/'result.log').open('w') as out:result=subprocess.run(command,cwd=works,env=env,stdout=out,stderr=subprocess.STDOUT,timeout=480)
  record['exitCode']=result.returncode
  if result.returncode:raise ValueError(lane+' behaviour failed')
  record['test']=gate.require_passed_tests(root/'result.xml',[method.split('.')[-1]])
  if lane=='retained':
   starts=gate.verify_host_records(root/'host-starts.jsonl',assembly,platform,'3.126.0',runtime_artifacts=p['runtimeArtifacts'],control_plane_ports=ports,retained_assembly=retained,retained_preflight=True)
  else:
   starts=[json.loads(line) for line in (root/'host-starts.jsonl').read_text().splitlines() if line];assert len(starts)==1
   for start in starts:
    assert start['role']=='platform-target' and start['assembly']==str(assembly) and start['sha256'].lower()==gate.sha256(assembly)
    assert start['controlPlanePorts']==ports and start['dependencyBuildsSuppressed'] is True
    gate.require_platform_hosting(start,'works',assembly.parent/'Hexalith.Platform.Works.Runtime.dll')
    gate.require_platform_composition(start,gate.platform_composition_literals(platform/'src/Hexalith.Platform.Composition/WorksPreviewModel.cs'))
    for module in start['runtimeModules']:
     assert module['configuration']=='Release' and p['runtimeArtifacts'][module['entry']]==module['entrySha256'].lower()
     for sdk in module['sdkAssemblies']:assert sdk['version']=='3.126.0.0' and gate.sha256(sdk['path'])==sdk['sha256'].lower()
  record['isolatedRuntimeEvidence']=isolated_daprd.verify(shim,binding,starts)
  assert all(gate.sha256(path)==digest for path,digest in p['runtimeArtifacts'].items())
  assert all(gate.sha256(path)==digest for path,digest in p['platformAdapterAssemblies'].items())
  assert works_before==gate.canonical_source_snapshot(works)
  record['sourceUnchanged']=retained_works_host.verify(p['retainedHostSource']);record['snapshotFinal']=works_host_snapshot.verify(manifest,platform)
  assert record['snapshotFinal']['manifestSha256']==snapshot['manifestSha256'];record['status']='passed-focused-binding-and-startup'
 except Exception as error:
  record['status']='failed';record['failure']=type(error).__name__+': '+str(error);raise
 finally:
  record['finished']=datetime.datetime.now(datetime.timezone.utc).isoformat();(root/'command-result.json').write_text(json.dumps(record,indent=2)+'\n');print(lane,record.get('status'),record.get('failure'),flush=True)
