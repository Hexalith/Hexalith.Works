import copy,hashlib,json,os,shutil,sys
from pathlib import Path
run=Path('/tmp/story49-group2-current-20261026T182217Z');diag=run/'focused-restart-1';diag.mkdir(exist_ok=False)
ledger=json.loads((run/'full1/gate/ledger.json').read_text());platform=run/'full1/workspace/platform';works=run/'works'
sys.path.insert(0,str(platform/'eng'));import verify_works_host
check=copy.deepcopy(ledger)
verify_works_host.require_unchanged_inputs(check,works,platform,run/'full1/platform-source-binding.json',tuple(ledger['controlPlanePortSelection']['excludedEphemeralRange']),[tuple(x) for x in ledger['controlPlanePortSelection']['excludedReservedRanges']])
launcher=diag/'isolated-daprd';(launcher/'.dapr/bin').mkdir(parents=True)
for relative in ['binding.json','.dapr/bin/daprd']:
 shutil.copy2(run/'full1/gate/isolated-daprd'/relative,launcher/relative)
 assert (launcher/relative).read_bytes()==(run/'full1/gate/isolated-daprd'/relative).read_bytes()
pins=json.loads((run/'full1/full-invocation.json').read_text())['environmentPins']
env=os.environ.copy()|pins|ledger['runtimeEnvironment']|{'WORKS_PLATFORM_HOST_EVIDENCE':str(diag/'host-starts.jsonl'),'WORKS_SDK_PROOF_EVIDENCE_PATH':str(diag/'persisted-states.jsonl'),'WORKS_PLATFORM_DAPRD_RUNTIME_PATH':str(launcher)}
(diag/'runtime-environment.json').write_text(json.dumps(pins|ledger['runtimeEnvironment']|{k:env[k] for k in ['WORKS_PLATFORM_HOST_EVIDENCE','WORKS_SDK_PROOF_EVIDENCE_PATH','WORKS_PLATFORM_DAPRD_RUNTIME_PATH']},indent=2)+'\n')
paths=set(ledger['runtimeArtifacts'])|set(ledger['consumer']['artifacts'])|set(ledger['platformAdapterAssemblies'])
paths|={str(run/'full1/gate/isolated-daprd'/x) for x in ['binding.json','.dapr/bin/daprd']}
paths|={str(launcher/x) for x in ['binding.json','.dapr/bin/daprd']}
paths.add(ledger['isolatedRuntime']['runtime'])
hashes={path:hashlib.sha256(Path(path).read_bytes()).hexdigest() for path in sorted(paths)}
(diag/'runtime-before.json').write_text(json.dumps(hashes,indent=2)+'\n')
command=[str(Path(ledger['consumer']['root'])/'Hexalith.Works.IntegrationTests'),'-noLogo','-explicit','on','-method','Hexalith.Works.IntegrationTests.WorksSdkRecoveryPipelineSmokeTests.SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart','-result-xml',str(diag/'result.xml')]
(diag/'command.json').write_text(json.dumps({'scope':'focused diagnostic only; no full acceptance credit','command':command,'cwd':str(works),'timeoutSeconds':1800,'sourceRuntimeInputClosureBefore':True,'runtimeFiles':len(hashes),'launcherPolicy':'identical shim/binding copies; only diagnostic evidence paths differ; canonical runtime bytes and proof deadlines unchanged','skill':'/home/administrator/.agents/skills/aspire-monitoring/SKILL.md'},indent=2)+'\n')
print('Focused preparation strict closure passed;',len(hashes),'runtime/launcher inputs bound.')
