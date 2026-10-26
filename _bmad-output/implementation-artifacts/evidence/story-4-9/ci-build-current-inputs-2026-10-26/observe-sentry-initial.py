"""Read-only owned-Sentry diagnostics. Never supplies canonical parity credit."""
import datetime
import hashlib
import http.client
import json
import socket
import subprocess
import time
from pathlib import Path

RUN=Path('/tmp/story49-group2-current-20261026T182217Z')
OUT=RUN/'sentry-observer'
OUT.mkdir(exist_ok=False)
ROOTS=[RUN/'full1/workspace/platform/DaprComponents',RUN/'full1/gate/retained-works-source']
FORMAT='{"id":{{json .Id}},"name":{{json .Name}},"image":{{json .Config.Image}},"status":{{json .State.Status}},"running":{{json .State.Running}},"exitCode":{{json .State.ExitCode}},"oomKilled":{{json .State.OOMKilled}},"startedAt":{{json .State.StartedAt}},"finishedAt":{{json .State.FinishedAt}},"health":{{if .State.Health}}{{json .State.Health}}{{else}}null{{end}},"ports":{{json .NetworkSettings.Ports}},"mounts":{{json .Mounts}}}'
scope={'status':'diagnostic-only; zero parity credit','helperSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'admission':'Docker volume filter and exact read-only Sentry configuration bind source under these owned roots, plus Sentry image/name; never admit by image alone','roots':[str(p) for p in ROOTS],'excluded':['container environment','credential file contents','foreign containers'],'limits':{'pollSeconds':10,'maximumSeconds':5400,'maximumContainers':20,'maximumObservations':1000,'perContainerLogBytes':1048576,'dockerCommandSeconds':5,'httpTimeoutSeconds':0.5,'tcpTimeoutSeconds':0.5}}
(OUT/'scope.json').write_text(json.dumps(scope,indent=2)+'\n')

def now():return datetime.datetime.now(datetime.UTC).isoformat()
def command(args):
 try:
  r=subprocess.run(args,capture_output=True,timeout=5,check=False)
  return r.returncode,r.stdout,r.stderr
 except subprocess.TimeoutExpired:return -1,b'',b'bounded diagnostic command timeout'
def write(name,value):
 with (OUT/name).open('a') as f:f.write(json.dumps(value,sort_keys=True)+'\n')
def configs():
 return {str(p.resolve()) for root in ROOTS if root.exists() for p in root.rglob('sentry.yaml') if p.is_file()}
def events(cid,since):
 until=now();code,out,err=command(['docker','events','--since',since,'--until',until,'--filter','container='+cid,'--format','{{json .}}'])
 write('events.jsonl',{'observedAtUtc':until,'container':cid,'since':since,'exitCode':code,'records':out.decode(errors='replace').splitlines()[:100],'stderr':err.decode(errors='replace')[:500]})

seen={};started=time.monotonic();count=0
while time.monotonic()-started<5400 and count<1000:
 paths=configs();candidates=set()
 for path in paths:
  code,out,err=command(['docker','ps','--all','--filter','volume='+path,'--format','{{.ID}}'])
  if code==0:candidates.update(out.decode().split())
 active=set()
 for candidate in sorted(candidates):
  code,out,err=command(['docker','inspect','--type','container','--format',FORMAT,candidate])
  if code:continue
  try:state=json.loads(out)
  except json.JSONDecodeError:continue
  matches=[m for m in state['mounts'] if m.get('Destination')=='/var/run/dapr/config/sentry.yaml' and m.get('Source') in paths and m.get('RW') is False]
  if not matches or 'sentry' not in state['image'] or 'dapr-sentry' not in state['name']:continue
  cid=state['id'];active.add(cid)
  if cid not in seen:
   if len(seen)>=20:continue
   stamp=now();seen[cid]={'firstObservedAtUtc':stamp,'eventSince':(datetime.datetime.now(datetime.UTC)-datetime.timedelta(seconds=15)).isoformat(),'lastLogs':0,'logBytes':0,'lastLogDigest':None}
   write('admitted.jsonl',{'observedAtUtc':stamp,'id':cid,'name':state['name'],'image':state['image'],'matchingConfiguration':matches})
   events(cid,seen[cid]['eventSince'])
  record={'observedAtUtc':now(),'container':cid,'state':{k:v for k,v in state.items() if k!='mounts'},'configuration':matches,'probes':[]}
  for target,bindings in (state.get('ports') or {}).items():
   if target not in ['8080/tcp','50001/tcp']:continue
   for binding in bindings or []:
    if binding.get('HostIp') not in ['0.0.0.0','127.0.0.1','']:continue
    port=int(binding['HostPort']);probe={'target':target,'host':'127.0.0.1','port':port}
    try:
     if target=='8080/tcp':
      connection=http.client.HTTPConnection('127.0.0.1',port,timeout=0.5)
      try:
       connection.request('GET','/healthz');response=connection.getresponse();probe|={'status':response.status,'body':response.read(256).decode(errors='replace')}
      finally:connection.close()
     else:
      with socket.create_connection(('127.0.0.1',port),timeout=0.5):probe['tcpConnected']=True
    except Exception as e:probe['errorType']=type(e).__name__
    record['probes'].append(probe)
  write('observations.jsonl',record);count+=1
  info=seen[cid]
  if time.monotonic()-info['lastLogs']>=20 and info['logBytes']<1048576:
   code,out,err=command(['docker','logs','--timestamps','--tail','80',cid]);info['lastLogs']=time.monotonic()
   data=out+b'\n'+err;digest=hashlib.sha256(data).hexdigest()
   if digest!=info['lastLogDigest']:
    remaining=1048576-info['logBytes'];out=out[:remaining];err=err[:max(0,remaining-len(out))]
    write('startup-stdout.jsonl',{'observedAtUtc':now(),'container':cid,'exitCode':code,'stdout':out.decode(errors='replace'),'stderr':err.decode(errors='replace'),'nativeWindowSha256':digest})
    info['logBytes']+=len(out)+len(err);info['lastLogDigest']=digest
 for cid,info in seen.items():
  if cid not in active and not info.get('disappearanceObserved'):
   events(cid,info['eventSince']);info['disappearanceObserved']=now()
   write('disappearances.jsonl',{'observedAtUtc':now(),'container':cid,'scope':'no longer returned by exact owned config bind filter; not a removal-caller conclusion'})
 try:ledger=json.loads((RUN/'full1/gate/ledger.json').read_text())
 except (FileNotFoundError,json.JSONDecodeError):ledger={}
 if ledger.get('status')=='passed-disposable-synthetic-parity-and-rollback' or ledger.get('failure'):break
 time.sleep(10)
for cid,info in seen.items():events(cid,info['eventSince'])
(OUT/'summary.json').write_text(json.dumps({'endedAtUtc':now(),'containers':len(seen),'observations':count,'elapsedSeconds':time.monotonic()-started,'status':'diagnostic-only; zero parity credit'},indent=2)+'\n')
