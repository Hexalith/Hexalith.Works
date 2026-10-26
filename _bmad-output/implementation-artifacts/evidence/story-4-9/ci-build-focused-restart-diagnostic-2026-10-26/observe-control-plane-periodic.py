"""Bounded read-only diagnostic of the exact focused proof control plane; no parity credit."""
import datetime,hashlib,http.client,json,re,socket,subprocess,time,uuid
from pathlib import Path
D=Path(__file__).resolve().parent;RUN=D.parent;OUT=D/'control-plane-observer';OUT.mkdir(exist_ok=False)
L=json.loads((RUN/'full1/gate/ledger.json').read_text());PORTS=L['controlPlanePorts'];ROOTS=[RUN/'full1/workspace/platform/DaprComponents',RUN/'full1/gate/retained-works-source'];APPHOST=RUN/'full1/workspace/platform/apphost.cs'
P='com.microsoft.developer.usvc-dev.'
OWNER_KEYS=[P+'creatorProcessId',P+'creatorProcessStartTime',P+'group-version']
F='{"id":{{json .Id}},"name":{{json .Name}},"image":{{json .Config.Image}},"imageId":{{json .Image}},"status":{{json .State.Status}},"running":{{json .State.Running}},"exitCode":{{json .State.ExitCode}},"oomKilled":{{json .State.OOMKilled}},"startedAt":{{json .State.StartedAt}},"health":{{json (index .State "Health")}},"ports":{{json .NetworkSettings.Ports}},"mounts":{{json .Mounts}},"creatorPid":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.creatorProcessId")}},"creatorStart":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.creatorProcessStartTime")}},"group":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.group-version")}},"resourceName":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.name")}},"resourceUid":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.uid")}}}'
HF='{"id":{{json .Id}},"imageId":{{json .Image}},"owner":{{json (index .Config.Labels "hexalith.works.health-observer")}},"network":{{json .HostConfig.NetworkMode}},"privileged":{{json .HostConfig.Privileged}},"readOnly":{{json .HostConfig.ReadonlyRootfs}},"autoRemove":{{json .HostConfig.AutoRemove}},"mounts":{{json .Mounts}},"ports":{{json .HostConfig.PortBindings}}}'
SCOPE_ID='focus-'+uuid.uuid4().hex
scope={'status':'diagnostic-only; zero parity credit','helperSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'sentryAdmission':'exact owned read-only config mount plus Sentry name/image and nonempty DCP ownership labels','peerAdmission':'same exact credential mount and DCP creator PID/start/group as an already admitted Sentry; expected resource name and published control port','roots':[str(p) for p in ROOTS],'excluded':['environment values','credential contents','foreign resources'],'limits':{'seconds':1500,'observations':1200,'containers':20,'namespaceHelpers':120,'perContainerLogBytes':1048576,'pollSeconds':5},'namespaceHelper':'only if native tools unavailable; exact cached image from owned proof Redis; --rm, unique CID/name/label, shared target network, no host or target mounts; image-created anonymous scratch volume allowed and cleanup verified','aspireSkill':'/home/administrator/.agents/skills/aspire-monitoring/SKILL.md'}
(OUT/'scope.json').write_text(json.dumps(scope,indent=2)+'\n')
def now():return datetime.datetime.now(datetime.UTC).isoformat()
def call(args,timeout=5):
 try:
  r=subprocess.run(args,capture_output=True,timeout=timeout,check=False);return r.returncode,r.stdout,r.stderr
 except subprocess.TimeoutExpired:return -1,b'',b'bounded diagnostic timeout'
def put(file,obj):
 with (OUT/file).open('a') as f:f.write(json.dumps(obj,sort_keys=True)+'\n')
def inspect(cid):
 code,out,err=call(['docker','inspect','--type','container','--format',F,cid])
 try:return json.loads(out) if code==0 else None
 except json.JSONDecodeError:return None
def candidates(name,port,label=None):
 args=['docker','ps','--all','--filter','name='+name,'--filter','publish='+str(port),'--format','{{.ID}}']
 if label:args[3:3]=['--filter','label='+P+'creatorProcessId='+label]
 code,out,err=call(args);return out.decode().split() if code==0 else []
def ownership(s):return (s['creatorPid'],s['creatorStart'],s['group'])
def cred(s):return next((m['Source'] for m in s['mounts'] if m['Destination']=='/var/run/dapr/credentials'),None)
def events(cid,since):
 until=now();code,out,err=call(['docker','events','--since',since,'--until',until,'--filter','container='+cid,'--format','{{json .}}'])
 records=[]
 for line in out.decode(errors='replace').splitlines()[:100]:
  r=json.loads(line);a=r.get('Actor',{}).get('Attributes',{});r['Actor']['Attributes']={k:v for k,v in a.items() if k in ['name','image','signal',P+'creatorProcessId',P+'creatorProcessStartTime',P+'group-version',P+'uid',P+'name']};records.append(r)
 put('events.jsonl',{'at':until,'id':cid,'exitCode':code,'records':records,'stderr':err.decode(errors='replace')[:500]})
def probes(s):
 result=[]
 for target,bindings in (s['ports'] or {}).items():
  if target not in ['8080/tcp','50001/tcp','50005/tcp','50006/tcp']:continue
  for b in bindings or []:
   if b.get('HostIp') not in ['0.0.0.0','127.0.0.1','']:continue
   port=int(b['HostPort']);r={'target':target,'host':'127.0.0.1','port':port}
   try:
    if target=='8080/tcp':
     c=http.client.HTTPConnection('127.0.0.1',port,timeout=0.5)
     try:c.request('GET','/healthz');v=c.getresponse();r|={'httpStatus':v.status,'body':v.read(256).decode(errors='replace')}
     finally:c.close()
    else:
     with socket.create_connection(('127.0.0.1',port),timeout=0.5):r['tcpConnected']=True
   except Exception as e:r['errorType']=type(e).__name__
   result.append(r)
 return result
def status(out,err):
 text=(out+b'\n'+err).decode(errors='replace');m=re.findall(r'HTTP/\S+\s+(\d{3})|HTTP_CODE=(\d{3})',text)
 return {'httpStatuses':[int(a or b) for a,b in m],'stdout':out[:1024].decode(errors='replace'),'stderr':err[:1024].decode(errors='replace')}
helpers=0

def namespace_probe(s,info):
 global helpers
 if helpers>=120:return {'refused':'namespace helper budget exhausted'}
 name=next((x for x in Path(info['credential']).parts if re.fullmatch(r'works-sdk415-[0-9a-f]{32}',x)),None)
 if not name:return {'refused':'no owned proof Redis name in credential path'}
 fmt='{"id":{{json .Id}},"image":{{json .Image}},"label":{{json (index .Config.Labels "hexalith.works.sdk-fixture")}}}'
 code,out,err=call(['docker','inspect','--type','container','--format',fmt,name])
 if code:return {'refused':'owned Redis no longer inspectable'}
 redis=json.loads(out)
 if redis['label']!=name:return {'refused':'proof Redis label mismatch'}
 image=redis['image'];code,out,err=call(['docker','image','inspect','--format','{{json .Id}}',image])
 if code or json.loads(out)!=image:return {'refused':'exact proof image not cached'}
 helper_name='works-focus-health-'+uuid.uuid4().hex;cid=None;mounts=[];record={'at':now(),'target':s['id'],'proofRedis':redis,'name':helper_name,'owner':SCOPE_ID}
 args=['docker','create','--rm','--pull','never','--name',helper_name,'--label','hexalith.works.health-observer='+SCOPE_ID,'--network','container:'+s['id'],'--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true','--user','65534:65534','--entrypoint','/bin/busybox',image,'wget','-S','-O','-','-T','1','http://127.0.0.1:8080/healthz']
 record['createCommand']=args;helpers+=1
 try:
  code,out,err=call(args);record['createExit']=code
  # The unique name/label also recover an acknowledged daemon create if CLI response is lost.
  code,raw,err=call(['docker','inspect','--type','container','--format',HF,helper_name])
  if code:record['refused']='helper creation unavailable';return record
  h=json.loads(raw)
  if h['owner']!=SCOPE_ID:record['refused']='helper owner mismatch';return record
  cid=h['id'];mounts=h['mounts'];record['helper']=h
  assert h['imageId']==image and h['network']=='container:'+s['id'] and not h['privileged'] and h['readOnly'] and h['autoRemove'] and not h['ports']
  assert all(m['Type']=='volume' and m['Destination']=='/data' for m in mounts),'Unexpected helper mount'
  code,out,err=call(['docker','start','--attach',cid]);record['executionExit']=code;record['response']=status(out,err)
 finally:
  if cid:
   code,raw,err=call(['docker','inspect','--type','container','--format',HF,cid])
   if code==0 and json.loads(raw)['owner']==SCOPE_ID:record['cleanupExit']=call(['docker','rm','--force','--volumes',cid])[0]
   record['containerAbsent']=call(['docker','inspect','--type','container',cid])[0]!=0
   volumes=[]
   for mount in mounts:
    volume=mount['Name'];code,raw,err=call(['docker','volume','inspect',volume])
    if code==0:
     code,refs,err=call(['docker','ps','--all','--filter','volume='+volume,'--format','{{.ID}}'])
     if code==0 and not refs.strip():call(['docker','volume','rm',volume])
    volumes.append({'name':volume,'absent':call(['docker','volume','inspect',volume])[0]!=0})
   record['anonymousScratchVolumes']=volumes
  put('namespace-helpers.jsonl',record)
 return record

def internal(s,info):
 if not info.get('nativeUnavailable'):
  shell='if command -v wget >/dev/null 2>&1; then wget -S -O - -T 1 http://127.0.0.1:8080/healthz; elif command -v curl >/dev/null 2>&1; then curl --noproxy "*" --max-time 1 --silent --show-error --write-out "\\nHTTP_CODE=%{http_code}" http://127.0.0.1:8080/healthz; else exit 125; fi'
  args=['docker','exec',s['id'],'/bin/sh','-c',shell];code,out,err=call(args);r={'mode':'native existing tools','command':args,'exitCode':code,**status(out,err)}
  if r['httpStatuses']:return r
  text=(out+err).decode(errors='replace')
  if code in [125,126,127] or 'no such file' in text.lower() or 'not found' in text.lower():info['nativeUnavailable']=True;put('native-tool-limitations.jsonl',{'at':now(),'id':s['id'],**r})
  else:return r
 return {'mode':'read-only namespace helper','outcome':namespace_probe(s,info)}

def describe():
 args=['aspire','describe','--apphost',str(APPHOST),'--include-hidden','--format','Json','--non-interactive','--nologo'];code,out,err=call(args,timeout=8)
 record={'at':now(),'command':args,'exitCode':code,'resources':[]}
 try:
  data=json.loads(out)
  def visit(v):
   if isinstance(v,dict):
    name=v.get('name') or v.get('Name') or v.get('resourceName')
    if isinstance(name,str) and any(name.startswith(n) for n in ['dapr-sentry','dapr-placement-mtls','dapr-scheduler-mtls']):
     record['resources'].append({k:x for k,x in v.items() if k.lower() in ['name','resourcename','state','healthstatus','healthreports','endpoints','urls']})
    for x in v.values():visit(x)
   elif isinstance(v,list):
    for x in v:visit(x)
  visit(data)
 except (json.JSONDecodeError,UnicodeDecodeError):record['message']=out.decode(errors='replace')[:600]
 record['stderr']=err.decode(errors='replace')[:600];put('aspire-describe.jsonl',record)

seen={};known_sentry={};started=time.monotonic();observations=0;last_describe=0
while time.monotonic()-started<1500 and observations<1200:
 paths={str(p.resolve()) for root in ROOTS for p in root.rglob('sentry.yaml') if p.is_file()};active={}
 for cid in candidates('dapr-sentry',PORTS['sentry']):
  s=inspect(cid)
  if not s:continue
  config=[m for m in s['mounts'] if m['Destination']=='/var/run/dapr/config/sentry.yaml' and m['Source'] in paths and m['RW'] is False]
  if not config or 'sentry' not in s['image'] or not all(ownership(s)) or not cred(s):continue
  known_sentry[s['id']]={'credential':cred(s),'ownership':ownership(s),'configuration':config,'sentry':s['id']};active[s['id']]=(s,'sentry',known_sentry[s['id']])
 for origin in known_sentry.values():
  for name,kind in [('dapr-placement-mtls','placement'),('dapr-scheduler-mtls','scheduler')]:
   for cid in candidates(name,PORTS[kind],origin['ownership'][0]):
    s=inspect(cid)
    if s and ownership(s)==origin['ownership'] and cred(s)==origin['credential'] and name in s['name'] and ('daprio/dapr' in s['image'] or kind in s['image']):active[s['id']]=(s,kind,origin)
 for cid,(s,kind,origin) in active.items():
  if cid not in seen:
   if len(seen)>=20:continue
   seen[cid]={'credential':origin['credential'],'since':(datetime.datetime.now(datetime.UTC)-datetime.timedelta(seconds=15)).isoformat(),'lastInternal':0,'lastLogs':0,'logBytes':0,'lastLogDigest':None}
   put('admitted.jsonl',{'at':now(),'id':cid,'kind':kind,'name':s['name'],'creatorPid':s['creatorPid'],'creatorStart':s['creatorStart'],'group':s['group'],'resourceName':s['resourceName'],'resourceUid':s['resourceUid'],'credentialMount':origin['credential'],'admittedSentry':origin['sentry'],'exactConfiguration':origin['configuration']});events(cid,seen[cid]['since'])
  info=seen[cid];record={'at':now(),'id':cid,'kind':kind,'state':{k:v for k,v in s.items() if k!='mounts'},'published':probes(s)}
  if time.monotonic()-info['lastInternal']>=30:
   record['internal']=internal(s,info);info['lastInternal']=time.monotonic()
  put('observations.jsonl',record);observations+=1
  if time.monotonic()-info['lastLogs']>=20 and info['logBytes']<1048576:
   code,out,err=call(['docker','logs','--timestamps','--tail','100',cid]);data=out+b'\n'+err;digest=hashlib.sha256(data).hexdigest();info['lastLogs']=time.monotonic()
   if digest!=info['lastLogDigest']:
    remaining=1048576-info['logBytes'];out=out[:remaining];err=err[:max(0,remaining-len(out))];put('stdout.jsonl',{'at':now(),'id':cid,'kind':kind,'exitCode':code,'stdout':out.decode(errors='replace'),'stderr':err.decode(errors='replace'),'nativeWindowSha256':digest});info['logBytes']+=len(out)+len(err);info['lastLogDigest']=digest
 for cid,info in seen.items():
  if cid not in active and not info.get('gone'):events(cid,info['since']);info['gone']=now();put('disappearances.jsonl',{'at':now(),'id':cid})
 if time.monotonic()-last_describe>=20:describe();last_describe=time.monotonic()
 if (D/'finished.json').exists():break
 time.sleep(5)
for cid,info in seen.items():events(cid,info['since'])
(OUT/'summary.json').write_text(json.dumps({'endedAt':now(),'containers':len(seen),'observations':observations,'namespaceHelpers':helpers,'elapsedSeconds':time.monotonic()-started,'status':'diagnostic-only; no parity credit'},indent=2)+'\n')
