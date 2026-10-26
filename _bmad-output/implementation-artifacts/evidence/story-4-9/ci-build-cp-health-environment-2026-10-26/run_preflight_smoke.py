"""Run the exact retained readiness fact with only a child-scoped Docker PATH environment pin."""
import copy,datetime,hashlib,json,os,shutil,sys
from pathlib import Path
from configure_scope import create_scope
ROOT=Path(__file__).resolve().parent;RUN=ROOT.parent;D=ROOT/'preflight-smoke-6';D.mkdir(exist_ok=False)
L=json.loads((RUN/'full1/gate/ledger.json').read_text());P=RUN/'full1/workspace/platform';W=RUN/'works'
sys.path.insert(0,str(P/'eng'));import verify_works_host,isolated_daprd
from works_verifier_command import run_owned

def closure():verify_works_host.require_unchanged_inputs(copy.deepcopy(L),W,P,RUN/'full1/platform-source-binding.json',tuple(L['controlPlanePortSelection']['excludedEphemeralRange']),tuple(tuple(x) for x in L['controlPlanePortSelection']['excludedReservedRanges']))
closure();launcher=D/'isolated-daprd';(launcher/'.dapr/bin').mkdir(parents=True)
for relative in ['binding.json','.dapr/bin/daprd']:shutil.copy2(RUN/'full1/gate/isolated-daprd-retained-preflight'/relative,launcher/relative)
config,scope=create_scope(D,[RUN/'full1/workspace/platform/DaprComponents',RUN/'full1/gate/retained-works-source'])
pins=json.loads((RUN/'full1/full-invocation.json').read_text())['environmentPins'];env=os.environ.copy()|pins|L['runtimeEnvironment']|{'WORKS_PLATFORM_HOST_EVIDENCE':str(D/'host-starts.jsonl'),'WORKS_SDK_PROOF_EVIDENCE_PATH':str(D/'persisted-states.jsonl'),'WORKS_PLATFORM_DAPRD_RUNTIME_PATH':str(launcher),'STORY49_CP_HEALTH_SCOPE':str(config),'PATH':str(ROOT/'bin')+os.pathsep+os.environ['PATH']}
env.pop('GITHUB_TOKEN',None);env.pop('GH_TOKEN',None)
command=list(next(c['command'] for c in L['commands'] if c['name']=='retained-host-preflight'));command[command.index('-result-xml')+1]=str(D/'result.xml')
paths=set(L['runtimeArtifacts'])|set(L['consumer']['artifacts'])|set(L['platformAdapterAssemblies'])|{L['isolatedRuntime']['runtime'],*[str(launcher/n) for n in ['binding.json','.dapr/bin/daprd']]};before={p:hashlib.sha256(Path(p).read_bytes()).hexdigest() for p in sorted(paths)};(D/'runtime-before.json').write_text(json.dumps(before,indent=2)+'\n')
record={'scope':'adapter native smoke only; no full qualification','startedAt':datetime.datetime.now(datetime.UTC).isoformat(),'command':command,'cwd':str(W),'timeoutSeconds':1800,'startupBudgetSeconds':180,'sourceRuntimeArchiveClosureBefore':True,'runtimeFiles':len(before),'pathPrefix':str(ROOT/'bin'),'remainingPathUnchanged':True,'originalPathSha256':hashlib.sha256(os.environ['PATH'].encode()).hexdigest(),'scopeSha256':hashlib.sha256(config.read_bytes()).hexdigest(),'adapterSha256':hashlib.sha256((ROOT/'docker_health_adapter.py').read_bytes()).hexdigest(),'shimSha256':hashlib.sha256((ROOT/'bin/docker').read_bytes()).hexdigest()};(D/'command.json').write_text(json.dumps(record,indent=2)+'\n')
# Start the separately bounded read-only observer before the selected fact.
s=Path(RUN/'observe-control-plane.py').read_text().replace("RUN=Path(__file__).resolve().parent;OUT=RUN/'read-only-observer'","RUN=Path(__file__).resolve().parent;SOURCE_RUN=RUN.parent.parent;OUT=RUN/'read-only-observer'")
s=s.replace("ROOTS=[RUN/'full1/","ROOTS=[SOURCE_RUN/'full1/").replace(",RUN/'full1/gate/retained-works-source'",",SOURCE_RUN/'full1/gate/retained-works-source'").replace("APPHOST=RUN/'full1/","APPHOST=SOURCE_RUN/'full1/").replace("(RUN/'full1/gate/ledger.json')","(SOURCE_RUN/'full1/gate/ledger.json')").replace("if L.get('failure') or L.get('status')=='passed-disposable-synthetic-parity-and-rollback':break","if (RUN/'finished.json').exists():break").replace('10800','1900').replace('6000','1200');(D/'observe-control-plane.py').write_text(s)
import subprocess
observer_output=(D/'observer-driver.log').open('w');observer=subprocess.Popen([sys.executable,str(D/'observe-control-plane.py')],stdout=observer_output,stderr=subprocess.STDOUT)
try:
 with (D/'result.log').open('w') as output:result=run_owned(command,cwd=W,env=env,stdout=output,timeout=1800)
 record['exitCode']=result.returncode
except BaseException as error:record.update(errorType=type(error).__name__,error=str(error))
finally:
 after={p:hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).is_file() else None for p in before};(D/'runtime-after.json').write_text(json.dumps(after,indent=2)+'\n');record['runtimeBytesUnchanged']=after==before
 try:closure();record['sourceRuntimeArchiveClosureAfter']=True
 except Exception as error:record.update(sourceRuntimeArchiveClosureAfter=False,closureError=str(error))
 record['finishedAt']=datetime.datetime.now(datetime.UTC).isoformat();(D/'finished.json').write_text(json.dumps(record,indent=2)+'\n')
 observer.wait(timeout=20);observer_output.close()
print(json.dumps(record,indent=2))
raise SystemExit(0 if record.get('exitCode')==0 and record['runtimeBytesUnchanged'] and record.get('sourceRuntimeArchiveClosureAfter') else 1)
