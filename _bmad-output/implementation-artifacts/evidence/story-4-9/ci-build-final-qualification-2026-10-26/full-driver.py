"""Run the unchanged owning full gate on this fixed snapshot; credentials stay only in memory."""
import importlib.util,json,subprocess,sys
from pathlib import Path
RUN=Path(__file__).resolve().parent;WORKS=RUN/'works';ARTIFACTS=RUN/'full1'
spec=importlib.util.spec_from_file_location('final_works_coordinator',WORKS/'scripts/verify-platform-parity-ci.py');coordinator=importlib.util.module_from_spec(spec);spec.loader.exec_module(coordinator)
command,environment,record,snapshot=coordinator.bootstrap(WORKS,ARTIFACTS)
command[command.index('--profile')+1]='full'
credential=subprocess.run(['gh','auth','token'],capture_output=True,check=False)
if credential.returncode or not credential.stdout.strip():raise RuntimeError('Existing gh credential unavailable; no credential output recorded')
environment['GITHUB_TOKEN']=credential.stdout.decode().strip();credential=None
(ARTIFACTS/'full-invocation.json').write_text(json.dumps({'command':command,'environmentPins':{k:v for k,v in environment.items() if k.startswith('Hexalith') or k=='DOTNET_USE_POLLING_FILE_WATCHER'},'authenticationPresent':True,'authenticationSource':'existing gh credential in memory; value never recorded','authenticationPolicy':'owning official GIT_API fetch only; stripped on redirects and before live runtime','profile':'full'},indent=2)+'\n')
result=subprocess.run(command,env=environment)
environment.pop('GITHUB_TOKEN',None)
if result.returncode:raise SystemExit(result.returncode)
if snapshot.origin_files(Path(record['platformOrigin']))!=record['platformSourceFiles']:raise ValueError('Selected Platform source changed during full qualification')
coordinator.require_unchanged_coordinators(record['coordinatorSha256']);record['status']='passed-disposable-synthetic-parity-and-rollback';record['profile']='full'
(ARTIFACTS/'bootstrap.json').write_text(json.dumps(record,indent=2)+'\n')
