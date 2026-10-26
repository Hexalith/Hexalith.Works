import datetime,json,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parent;now=datetime.datetime.now(datetime.UTC).strftime('%Y%m%dT%H%M%SZ');out=ROOT/'polls'/now;out.mkdir(parents=True)
endpoints={'ci-run':'repos/Hexalith/Hexalith.Works/actions/runs/40407181685','ci-jobs':'repos/Hexalith/Hexalith.Works/actions/runs/40407181685/jobs?per_page=100','ci-artifacts':'repos/Hexalith/Hexalith.Works/actions/runs/40407181685/artifacts?per_page=100','codeql-run':'repos/Hexalith/Hexalith.Works/actions/runs/40407181599'}
data={}
for name,path in endpoints.items():
 result=subprocess.run(['gh','api',path],capture_output=True,check=False)
 if result.returncode:raise RuntimeError(name+': '+result.stderr.decode())
 (out/(name+'.json')).write_bytes(result.stdout);data[name]=json.loads(result.stdout)
summary={'at':now,'ci':{k:data['ci-run'][k] for k in ['status','conclusion','head_sha']},'codeql':{k:data['codeql-run'][k] for k in ['status','conclusion']},'jobs':[{'id':j['id'],'name':j['name'],'status':j['status'],'conclusion':j['conclusion'],'activeSteps':[s['name'] for s in j['steps'] if s['status']=='in_progress'],'failedSteps':[s['name'] for s in j['steps'] if s.get('conclusion')=='failure']} for j in data['ci-jobs']['jobs']],'artifacts':[{'id':a['id'],'name':a['name'],'size':a['size_in_bytes'],'digest':a.get('digest')} for a in data['ci-artifacts']['artifacts']]}
(ROOT/'latest.json').write_text(json.dumps(summary,indent=2)+'\n');print(json.dumps(summary,indent=2))
