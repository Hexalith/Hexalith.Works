import json,subprocess,sys,time
from pathlib import Path
R=Path(__file__).resolve().parent;previous=None
for _ in range(160):
 p=subprocess.run([sys.executable,str(R/'poll.py')],capture_output=True,check=False)
 if p.returncode:
  print('Read-only poll failed: '+p.stderr.decode(errors='replace')[:500],flush=True);raise SystemExit(p.returncode)
 d=json.loads(p.stdout);signature=json.dumps({k:v for k,v in d.items() if k!='at'},sort_keys=True)
 if signature!=previous:
  with (R/'status-events.jsonl').open('a') as f:f.write(json.dumps(d,sort_keys=True)+'\n')
  print(json.dumps(d),flush=True);previous=signature
 if d['ci']['status']=='completed' or any(j['conclusion']=='failure' for j in d['jobs']):break
 time.sleep(45)
