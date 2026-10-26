"""Fresh canonical full against unchanged final sources plus the accepted child-only Docker pin."""
import copy,datetime,hashlib,importlib.util,json,os,subprocess,sys
from pathlib import Path
RUN=Path(__file__).resolve().parent;D=RUN/'full2';D.mkdir(exist_ok=False);WORKS=RUN/'works';PLATFORM=RUN/'full1/workspace/platform';QUALIFIED=RUN/'cp-health-environment-1/qualified'
sys.dont_write_bytecode=True;sys.path.insert(0,str(QUALIFIED));from configure_scope import create_scope
spec=importlib.util.spec_from_file_location('final_full2_coordinator',WORKS/'scripts/verify-platform-parity-ci.py');coordinator=importlib.util.module_from_spec(spec);spec.loader.exec_module(coordinator)
record=json.loads((RUN/'full1/bootstrap.json').read_text());record.update(profile='full',status='running-final-frozen-source-with-scoped-health-port-environment',artifactsRoot=str(D),environmentExperiment='exact owned CP auto-health ports pinned below Linux ephemeral range')
(D/'platform-source-binding.json').write_bytes((RUN/'full1/platform-source-binding.json').read_bytes());(D/'bootstrap.json').write_text(json.dumps(record,indent=2)+'\n')
# Reusing source roots avoids another clone; required source/runtime/archive closure still runs.
sys.path.insert(0,str(PLATFORM/'eng'));import verify_works_host
old=json.loads((RUN/'full1/gate/ledger.json').read_text());verify_works_host.require_unchanged_inputs(copy.deepcopy(old),WORKS,PLATFORM,D/'platform-source-binding.json',tuple(old['controlPlanePortSelection']['excludedEphemeralRange']),tuple(tuple(x) for x in old['controlPlanePortSelection']['excludedReservedRanges']))
binding=json.loads((RUN/'cp-health-environment-1/qualified-helper-binding.json').read_text());before={key:hashlib.sha256(Path(value['path']).read_bytes()).hexdigest() for key,value in binding['assets'].items()};assert all(before[k]==v['sha256'] for k,v in binding['assets'].items())
(D/'helper-hashes-before.json').write_text(json.dumps(before,indent=2)+'\n')
config,scope=create_scope(D/'environment',[PLATFORM/'DaprComponents',D/'gate/retained-works-source'])
pins=json.loads((RUN/'full1/full-invocation.json').read_text())['environmentPins'];environment=os.environ.copy()|pins|{'PATH':str(QUALIFIED/'bin')+os.pathsep+os.environ['PATH'],'STORY49_CP_HEALTH_SCOPE':str(config)}
credential=subprocess.run(['gh','auth','token'],capture_output=True,check=False)
if credential.returncode or not credential.stdout.strip():raise RuntimeError('Existing gh credential unavailable; credential output not recorded')
environment['GITHUB_TOKEN']=credential.stdout.decode().strip();credential=None
command=['bash',str(PLATFORM/'eng/verify-works-host.sh'),'--profile','full','--works-root',str(WORKS),'--runtime-sdk-root',str(RUN/'full1/workspace/Hexalith.EventStore'),'--platform-source-binding',str(D/'platform-source-binding.json'),'--artifacts-path',str(D/'gate')]
invocation={'scope':'fresh canonical full; same frozen final sources, distinct scoped environment input','command':command,'sourceCapture':str(RUN/'inputs.json'),'sourceCaptureSha256':hashlib.sha256((RUN/'inputs.json').read_bytes()).hexdigest(),'worksRevision':json.loads((RUN/'inputs.json').read_text())['worksRevision'],'sourceRoots':{'works':str(WORKS),'platform':str(PLATFORM),'runtimeSdk':str(RUN/'full1/workspace/Hexalith.EventStore')},'environmentPins':pins,'pathPrefix':str(QUALIFIED/'bin'),'remainingPathUnchanged':True,'scopeConfiguration':str(config),'scopeSha256':hashlib.sha256(config.read_bytes()).hexdigest(),'selectedSentryConfigurationRoots':scope['sentryConfigRoots'],'helperHashes':before,'strictInputClosureBefore':True,'canonicalProcessBudgetsSeconds':1800,'canonicalStartupBudgetSeconds':180,'authenticationPresent':True,'authenticationSource':'existing gh credential in memory; value never recorded','authenticationPolicy':'official GIT_API only; redirects drop auth; owner strips token before live runtime','extraHelperContainers':0,'observation':'read-only exact-owned Docker/DCP only'}
(D/'full-invocation.json').write_text(json.dumps(invocation,indent=2)+'\n');started=datetime.datetime.now(datetime.UTC).isoformat()
result=subprocess.run(command,env=environment);environment.pop('GITHUB_TOKEN',None)
after={key:hashlib.sha256(Path(value['path']).read_bytes()).hexdigest() for key,value in binding['assets'].items()};(D/'helper-hashes-after.json').write_text(json.dumps(after,indent=2)+'\n');unchanged=before==after
if result.returncode==0:
 snapshot=coordinator.load_snapshot_helper(WORKS) if hasattr(coordinator,'load_snapshot_helper') else None
 # The original source manifest is authoritative and unchanged-source checks run in the owning gate.
 coordinator.require_unchanged_coordinators(record['coordinatorSha256'])
 record.update(status='passed-disposable-synthetic-parity-and-rollback',profile='full')
(D/'bootstrap.json').write_text(json.dumps(record,indent=2)+'\n')
(D/'driver-finished.json').write_text(json.dumps({'startedAt':started,'finishedAt':datetime.datetime.now(datetime.UTC).isoformat(),'exitCode':result.returncode,'helperBytesUnchanged':unchanged,'scope':'fresh full with recorded environment; parent audit/hosted/reviews pending'},indent=2)+'\n')
raise SystemExit(result.returncode if result.returncode else 0 if unchanged else 1)
