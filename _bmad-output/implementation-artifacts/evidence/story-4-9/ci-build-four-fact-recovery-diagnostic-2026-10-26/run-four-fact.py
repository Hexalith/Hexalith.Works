import copy,datetime,hashlib,json,os,subprocess,sys
from pathlib import Path
D=Path(__file__).resolve().parent;RUN=D.parent;L=json.loads((RUN/'full1/gate/ledger.json').read_text());P=RUN/'full1/workspace/platform';W=RUN/'works'
sys.path.insert(0,str(P/'eng'));import verify_works_host
from works_verifier_command import run_owned
config=json.loads((D/'command.json').read_text());env=os.environ.copy()|json.loads((D/'runtime-environment.json').read_text())
env.pop('GITHUB_TOKEN',None);env.pop('GH_TOKEN',None)
record={'startedAt':datetime.datetime.now(datetime.UTC).isoformat(),'scope':'four-fact recovery sequence diagnostic only; no complete/full parity credit','command':config['command'],'cwd':str(W)}
try:
 with (D/'result.log').open('w') as output:
  result=run_owned(config['command'],cwd=W,env=env,stdout=output,timeout=1800)
 record['exitCode']=result.returncode
except BaseException as e:record['errorType']=type(e).__name__;record['error']=str(e)
finally:
 before=json.loads((D/'runtime-before.json').read_text());after={p:hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).is_file() else None for p in before}
 (D/'runtime-after.json').write_text(json.dumps(after,indent=2)+'\n');record['runtimeLauncherBytesUnchanged']=before==after
 record['changedRuntimeFiles']=[p for p in before if before[p]!=after[p]]
 try:
  verify_works_host.require_unchanged_inputs(copy.deepcopy(L),W,P,RUN/'full1/platform-source-binding.json',tuple(L['controlPlanePortSelection']['excludedEphemeralRange']),tuple(tuple(x) for x in L['controlPlanePortSelection']['excludedReservedRanges']))
  record['sourceRuntimeArchiveInputClosureAfter']=True
 except Exception as e:record['sourceRuntimeArchiveInputClosureAfter']=False;record['closureError']=str(e)
 record['finishedAt']=datetime.datetime.now(datetime.UTC).isoformat();(D/'finished.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record,indent=2))
raise SystemExit(0 if record.get('exitCode')==0 and record.get('runtimeLauncherBytesUnchanged') and record.get('sourceRuntimeArchiveInputClosureAfter') else 1)
