"""Read-only exact-owned observation; no exec, creation, helpers, mutation or parity credit."""
import datetime,hashlib,http.client,json,subprocess,time
from pathlib import Path
RUN=Path(__file__).resolve().parent;SOURCE_RUN=RUN.parent.parent;OUT=RUN/'read-only-observer';OUT.mkdir(exist_ok=False)
ROOTS=[SOURCE_RUN/'full1/workspace/platform/DaprComponents',SOURCE_RUN/'full1/gate/retained-works-source'];APPHOST=SOURCE_RUN/'full1/workspace/platform/apphost.cs'
F='{"id":{{json .Id}},"name":{{json .Name}},"image":{{json .Config.Image}},"status":{{json .State.Status}},"running":{{json .State.Running}},"exitCode":{{json .State.ExitCode}},"oomKilled":{{json .State.OOMKilled}},"startedAt":{{json .State.StartedAt}},"health":{{json (index .State "Health")}},"ports":{{json .NetworkSettings.Ports}},"mounts":{{json .Mounts}},"creatorPid":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.creatorProcessId")}},"creatorStart":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.creatorProcessStartTime")}},"group":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.group-version")}},"resourceName":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.name")}},"resourceUid":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.uid")}}}'
(OUT/'scope.json').write_text(json.dumps({'helperSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'status':'read-only diagnostic; no parity credit','dockerCommandAllowlist':['ps','inspect','logs','events'],'namespaceHelpers':0,'containerExec':False,'admission':'Sentry exact owned read-only config source; peers exact shared credential mount and DCP creator PID/start/group labels','roots':[str(x) for x in ROOTS],'limits':{'seconds':1900,'containers':60,'observations':1200,'pollSeconds':5,'logBytesPerContainer':1048576},'excluded':['environments','credential contents','foreign resources']},indent=2)+'\n')
def now():return datetime.datetime.now(datetime.UTC).isoformat()
def put(name,obj):
 with (OUT/name).open('a') as f:f.write(json.dumps(obj,sort_keys=True)+'\n')
def call(args,timeout=5):
 if args[0]=='docker' and args[1] not in ['ps','inspect','logs','events']:raise ValueError('Read-only observer refused a Docker mutation/exec')
 try:r=subprocess.run(args,capture_output=True,timeout=timeout,check=False);return r.returncode,r.stdout,r.stderr
 except subprocess.TimeoutExpired:return -1,b'',b'bounded read-only observation timeout'
def inspect(cid):
 code,out,err=call(['docker','inspect','--type','container','--format',F,cid])
 try:return json.loads(out) if code==0 else None
 except json.JSONDecodeError:return None
def candidates(name,port=None,pid=None):
 args=['docker','ps','--all','--filter','name='+name,'--format','{{.ID}}']
 if port:args[3:3]=['--filter','publish='+str(port)]
 if pid:args[3:3]=['--filter','label=com.microsoft.developer.usvc-dev.creatorProcessId='+pid]
 code,out,err=call(args);return out.decode().split() if code==0 else []
def owner(s):return (s['creatorPid'],s['creatorStart'],s['group'])
def cred(s):return next((m['Source'] for m in s['mounts'] if m['Destination']=='/var/run/dapr/credentials'),None)
def events(cid,since):
 until=now();code,out,err=call(['docker','events','--since',since,'--until',until,'--filter','container='+cid,'--format','{{json .}}']);records=[]
 for line in out.decode(errors='replace').splitlines()[:100]:
  r=json.loads(line);a=r.get('Actor',{}).get('Attributes',{});r['Actor']['Attributes']={k:v for k,v in a.items() if k in ['name','image','signal','com.microsoft.developer.usvc-dev.creatorProcessId','com.microsoft.developer.usvc-dev.creatorProcessStartTime','com.microsoft.developer.usvc-dev.group-version','com.microsoft.developer.usvc-dev.name','com.microsoft.developer.usvc-dev.uid']};records.append(r)
 put('events.jsonl',{'at':until,'id':cid,'exitCode':code,'records':records,'stderr':err[:500].decode(errors='replace')})
def health(s):
 result=[]
 for target,bindings in (s['ports'] or {}).items():
  if target!='8080/tcp':continue
  for b in bindings or []:
   if b.get('HostIp') not in ['0.0.0.0','127.0.0.1','']:continue
   port=int(b['HostPort']);r={'target':target,'host':'127.0.0.1','port':port}
   try:
    c=http.client.HTTPConnection('127.0.0.1',port,timeout=0.5)
    try:c.request('GET','/healthz');v=c.getresponse();r|={'httpStatus':v.status,'body':v.read(256).decode(errors='replace')}
    finally:c.close()
   except Exception as e:r['errorType']=type(e).__name__
   result.append(r)
 return result
def describe():
 args=['aspire','describe','--apphost',str(APPHOST),'--include-hidden','--format','Json','--non-interactive','--nologo'];code,out,err=call(args,8);r={'at':now(),'command':args,'exitCode':code,'resources':[]}
 try:
  data=json.loads(out)
  def visit(v):
   if isinstance(v,dict):
    name=v.get('name') or v.get('Name') or v.get('resourceName')
    if isinstance(name,str) and any(name.startswith(n) for n in ['dapr-sentry','dapr-placement-mtls','dapr-scheduler-mtls']):r['resources'].append({k:x for k,x in v.items() if k.lower() in ['name','resourcename','state','healthstatus','healthreports','endpoints','urls']})
    for x in v.values():visit(x)
   elif isinstance(v,list):
    for x in v:visit(x)
  visit(data)
 except (json.JSONDecodeError,UnicodeDecodeError):r['message']=out[:600].decode(errors='replace')
 r['stderr']=err[:600].decode(errors='replace');put('aspire-describe.jsonl',r)
seen={};sentries={};started=time.monotonic();count=0;last_describe=0
while time.monotonic()-started<1900 and count<1200:
 try:L=json.loads((SOURCE_RUN/'full1/gate/ledger.json').read_text())
 except (FileNotFoundError,json.JSONDecodeError):L={}
 ports=L.get('controlPlanePorts');active={}
 if ports:
  paths={str(p.resolve()) for root in ROOTS if root.exists() for p in root.rglob('sentry.yaml') if p.is_file()}
  for cid in candidates('dapr-sentry',ports['sentry']):
   s=inspect(cid)
   if not s:continue
   config=[m for m in s['mounts'] if m['Destination']=='/var/run/dapr/config/sentry.yaml' and m['Source'] in paths and m['RW'] is False]
   if not config or not all(owner(s)) or not cred(s) or 'sentry' not in s['image']:continue
   sentries[s['id']]={'credential':cred(s),'ownership':owner(s),'configuration':config,'sentry':s['id']};active[s['id']]=(s,'sentry',sentries[s['id']])
  for origin in sentries.values():
   for name,kind in [('dapr-placement-mtls','placement'),('dapr-scheduler-mtls','scheduler')]:
    for cid in candidates(name,pid=origin['ownership'][0]):
     s=inspect(cid)
     if s and owner(s)==origin['ownership'] and cred(s)==origin['credential'] and name in s['name'] and ('daprio/dapr' in s['image'] or kind in s['image']):active[s['id']]=(s,kind,origin)
  for cid,(s,kind,origin) in active.items():
   if cid not in seen:
    if len(seen)>=60:continue
    seen[cid]={'since':(datetime.datetime.now(datetime.UTC)-datetime.timedelta(seconds=15)).isoformat(),'lastLogs':0,'logBytes':0,'digest':None}
    put('admitted.jsonl',{'at':now(),'id':cid,'kind':kind,'name':s['name'],'creatorPid':s['creatorPid'],'creatorStart':s['creatorStart'],'group':s['group'],'resourceName':s['resourceName'],'resourceUid':s['resourceUid'],'credentialMount':origin['credential'],'admittedSentry':origin['sentry'],'exactConfiguration':origin['configuration']});events(cid,seen[cid]['since'])
   info=seen[cid];put('observations.jsonl',{'at':now(),'id':cid,'kind':kind,'state':{k:v for k,v in s.items() if k!='mounts'},'published':health(s)});count+=1
   if time.monotonic()-info['lastLogs']>=20 and info['logBytes']<1048576:
    code,out,err=call(['docker','logs','--timestamps','--tail','100',cid]);digest=hashlib.sha256(out+b'\n'+err).hexdigest();info['lastLogs']=time.monotonic()
    if digest!=info['digest']:
     left=1048576-info['logBytes'];out=out[:left];err=err[:max(0,left-len(out))];put('stdout.jsonl',{'at':now(),'id':cid,'kind':kind,'exitCode':code,'stdout':out.decode(errors='replace'),'stderr':err.decode(errors='replace'),'nativeWindowSha256':digest});info['logBytes']+=len(out)+len(err);info['digest']=digest
  for cid,info in seen.items():
   if cid not in active and not info.get('gone'):events(cid,info['since']);info['gone']=now();put('disappearances.jsonl',{'at':now(),'id':cid})
  if time.monotonic()-last_describe>=30:describe();last_describe=time.monotonic()
 if (RUN/'finished.json').exists():break
 time.sleep(5)
for cid,info in seen.items():events(cid,info['since'])
(OUT/'summary.json').write_text(json.dumps({'endedAt':now(),'containers':len(seen),'observations':count,'extraHelperContainers':0,'containerExec':0,'status':'read-only diagnostic; no parity credit'},indent=2)+'\n')
