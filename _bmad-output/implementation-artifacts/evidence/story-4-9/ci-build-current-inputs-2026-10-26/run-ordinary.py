import datetime,hashlib,json,os,subprocess
from pathlib import Path
run=Path('/tmp/story49-group2-current-20261026T182217Z');works=run/'works'
results=[]
environment=os.environ.copy()|{'HexalithBuildsRoot':str(works/'references/Hexalith.Builds'),'DOTNET_USE_POLLING_FILE_WATCHER':'1'}
for project in ['Hexalith.Works.UnitTests','Hexalith.Works.PropertyTests','Hexalith.Works.IntegrationTests']:
 command=['dotnet',str(works/'tests'/project/'bin/Release/net10.0'/(project+'.dll')),'-noLogo','-result-xml',str(run/'ordinary'/(project+'.xml'))]
 log=run/'ordinary'/(project+'.log')
 record={'project':project,'command':command,'cwd':str(works),'startedAtUtc':datetime.datetime.now(datetime.UTC).isoformat()}
 with log.open('w') as output:r=subprocess.run(command,cwd=works,env=environment,stdout=output,stderr=subprocess.STDOUT,timeout=1800,check=False)
 record|={'exitCode':r.returncode,'finishedAtUtc':datetime.datetime.now(datetime.UTC).isoformat(),'logSha256':hashlib.sha256(log.read_bytes()).hexdigest()}
 results.append(record);(run/'ordinary/results.json').write_text(json.dumps(results,indent=2)+'\n')
 print(project,r.returncode,flush=True)
expected=json.loads((run/'ordinary-runtime-before.json').read_text())
after={project:{str(p.relative_to(works/'tests'/project/'bin/Release/net10.0')):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((works/'tests'/project/'bin/Release/net10.0').rglob('*')) if p.is_file()} for project in expected}
(run/'ordinary-runtime-after.json').write_text(json.dumps(after,indent=2)+'\n')
assert after==expected,'Already-built ordinary runtime bytes changed during execution'
raise SystemExit(0 if all(r['exitCode']==0 for r in results) else 1)
