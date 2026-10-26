"""One conditional namespace helper per repeatedly unreachable exact-owned target; diagnostic only."""
import concurrent.futures,datetime,hashlib,http.client,json,re,subprocess,time,uuid
from pathlib import Path
D=Path(__file__).resolve().parent;OUT=D/'failure-internal';OUT.mkdir(exist_ok=False);OWNER='story49-recovery-'+uuid.uuid4().hex
PREFIX='com.microsoft.developer.usvc-dev.'
F='{"id":{{json .Id}},"running":{{json .State.Running}},"ports":{{json .NetworkSettings.Ports}},"mounts":{{json .Mounts}},"creatorPid":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.creatorProcessId")}},"creatorStart":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.creatorProcessStartTime")}},"group":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.group-version")}},"resourceUid":{{json (index .Config.Labels "com.microsoft.developer.usvc-dev.uid")}}}'
HF='{"id":{{json .Id}},"imageId":{{json .Image}},"owner":{{json (index .Config.Labels "hexalith.works.health-observer")}},"network":{{json .HostConfig.NetworkMode}},"privileged":{{json .HostConfig.Privileged}},"readOnly":{{json .HostConfig.ReadonlyRootfs}},"autoRemove":{{json .HostConfig.AutoRemove}},"mounts":{{json .Mounts}},"ports":{{json .HostConfig.PortBindings}}}'
def now():return datetime.datetime.now(datetime.UTC).isoformat()
def put(name,record):
 with (OUT/name).open('a') as f:f.write(json.dumps(record,sort_keys=True)+'\n')
def call(args,timeout=5):
 try:
  r=subprocess.run(args,capture_output=True,timeout=timeout,check=False);return r.returncode,r.stdout,r.stderr
 except subprocess.TimeoutExpired:return -1,b'',b'bounded diagnostic timeout'
def rows(path):
 try:return [json.loads(x) for x in path.read_text().splitlines()]
 except (FileNotFoundError,json.JSONDecodeError):return []
def inspect(cid,fmt=F):
 code,out,err=call(['docker','inspect','--type','container','--format',fmt,cid]);return json.loads(out) if code==0 else None

def target(a):
 s=inspect(a['id'])
 if not s or not s['running']:return None
 assert s['id']==a['id'] and s['resourceUid']==a['resourceUid']
 assert all(s[k]==a[k] for k in ['creatorPid','creatorStart','group'])
 assert any(m['Source']==a['credentialMount'] and m['Destination']=='/var/run/dapr/credentials' for m in s['mounts'])
 return s

def http(host,port,kind):
 r={'startedAt':now(),'kind':kind,'host':host,'port':port,'path':'/healthz'}
 try:
  c=http.client.HTTPConnection(host,port,timeout=1)
  try:c.request('GET','/healthz');v=c.getresponse();r.update(httpStatus=v.status,body=v.read(256).decode(errors='replace'))
  finally:c.close()
 except Exception as error:r['errorType']=type(error).__name__
 r['finishedAt']=now();return r

def socket_rows(ports):
 result=[]
 for port in ports:
  command=['ss','-tan','( sport = :'+str(port)+' or dport = :'+str(port)+' )'];code,out,err=call(command)
  result.append({'at':now(),'command':command,'exitCode':code,'stdout':out[:16000].decode(errors='replace'),'stderr':err[:1000].decode(errors='replace'),'processDetailsRequested':False})
 return result

def windows_health(port):
 command=['/mnt/c/Windows/System32/curl.exe','--noproxy','*','--max-time','2','--silent','--show-error','--write-out','HTTP_CODE=%{http_code}','http://127.0.0.1:'+str(port)+'/healthz'];r={'startedAt':now(),'kind':'windows-loopback','command':command}
 try:
  code,out,err=call(command,3);r.update(exitCode=code,stdout=out[:1000].decode(errors='replace'),stderr=err[:1000].decode(errors='replace'))
 except OSError as error:r.update(errorType=type(error).__name__,error=str(error))
 r['finishedAt']=now();return r

def namespace(a,trigger):
 record={'at':now(),'target':a['id'],'kind':a['kind'],'owner':OWNER,'trigger':trigger};cid=None;mounts=[]
 s=target(a)
 if not s:record['refused']='Target no longer running';put('attempts.jsonl',record);return
 name=next((x for x in Path(a['credentialMount']).parts if re.fullmatch(r'works-sdk415-[0-9a-f]{32}',x)),None)
 if not name:record['refused']='No exact proof Redis identity in admitted credential path';put('attempts.jsonl',record);return
 fmt='{"id":{{json .Id}},"image":{{json .Image}},"label":{{json (index .Config.Labels "hexalith.works.sdk-fixture")}}}'
 redis=inspect(name,fmt)
 if not redis or redis['label']!=name:record['refused']='Exact proof Redis unavailable or owner label mismatch';put('attempts.jsonl',record);return
 image=redis['image'];code,out,err=call(['docker','image','inspect','--format','{"id":{{json .Id}},"repoDigests":{{json .RepoDigests}},"volumes":{{json .Config.Volumes}}}',image])
 if code or json.loads(out)['id']!=image:record['refused']='Exact proof image not cached';put('attempts.jsonl',record);return
 record.update(proofRedis=redis,cachedImage=json.loads(out),targetBefore=s)
 proxies=[]
 for snapshot in reversed(rows(D/'dcp-observer/snapshots.jsonl')):
  if any(c.get('status',{}).get('containerId')==a['id'] and c.get('metadata',{}).get('uid')==a['resourceUid'] for c in snapshot.get('containers',[])):
   wanted={'sentry':'dapr-sentry-health','placement':'dapr-placement-mtls-health','scheduler':'dapr-scheduler-mtls-health'}[a['kind']]
   record['dcpBefore']=snapshot
   for service in snapshot.get('services',[]):
    host=service.get('status',{}).get('effectiveAddress');port=service.get('status',{}).get('effectivePort')
    if service['metadata']['name']==wanted and host in ['localhost','127.0.0.1','::1'] and port:proxies.append((host,int(port),'dcp-proxy'))
   break
 endpoints=[('127.0.0.1',int(b['HostPort']),'docker-published') for port,bindings in (s['ports'] or {}).items() if port=='8080/tcp' for b in (bindings or []) if b.get('HostIp') in ['127.0.0.1','0.0.0.0','']]+proxies
 published_ports=sorted({port for host,port,kind in endpoints if kind=='docker-published'});record['socketRowsBefore']=socket_rows(published_ports)
 endpoints += [('172.21.192.1',port,'windows-host-gateway') for port in published_ports]
 helper='works-recovery-health-'+uuid.uuid4().hex
 command=['docker','create','--rm','--pull','never','--name',helper,'--label','hexalith.works.health-observer='+OWNER,'--network','container:'+a['id'],'--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true','--user','65534:65534','--entrypoint','/bin/busybox',image,'wget','-S','-O','-','-T','1','http://127.0.0.1:8080/healthz'];record['createCommand']=command
 try:
  code,out,err=call(command);record.update(createExit=code,createStdout=out.decode(errors='replace')[:1000],createStderr=err.decode(errors='replace')[:1000])
  h=inspect(helper,HF)
  if not h:record['refused']='Helper create unavailable';return
  assert h['owner']==OWNER
  cid=h['id'];mounts=h['mounts'];record['helper']=h
  assert h['imageId']==image and h['network']=='container:'+a['id'] and h['readOnly'] and h['autoRemove'] and not h['privileged'] and not h['ports']
  assert all(m['Type']=='volume' and m['Destination']=='/data' for m in mounts)
  assert target(a),'Owned target ended before simultaneous sampling'
  def internal():
   began=now();args=['docker','start','--attach',cid];code,out,err=call(args,8);text=(out+b'\n'+err).decode(errors='replace');return {'startedAt':began,'finishedAt':now(),'command':args,'exitCode':code,'httpStatuses':[int(x) for x in re.findall(r'HTTP/\S+\s+(\d{3})',text)],'stdout':out[:2000].decode(errors='replace'),'stderr':err[:2000].decode(errors='replace')}
  with concurrent.futures.ThreadPoolExecutor(max_workers=1+len(endpoints)+len(published_ports)) as pool:
   inner=pool.submit(internal);outer=[pool.submit(http,*e) for e in endpoints];windows=[pool.submit(windows_health,port) for port in published_ports];record['internal']=inner.result();record['simultaneousExternal']=[f.result() for f in outer]+[f.result() for f in windows]
  record['targetAfter']=target(a);record['socketRowsAfter']=socket_rows(published_ports)
 except Exception as error:record.update(errorType=type(error).__name__,error=str(error))
 finally:
  if cid:
   h=inspect(cid,HF)
   if h and h['owner']==OWNER:record['cleanupExit']=call(['docker','rm','--force','--volumes',cid])[0]
   record['containerAbsent']=inspect(cid,HF) is None;volumes=[]
   for m in mounts:
    v=m['Name'];code,out,err=call(['docker','volume','inspect',v])
    if code==0:
     code,refs,err=call(['docker','ps','--all','--filter','volume='+v,'--format','{{.ID}}'])
     if code==0 and not refs.strip():call(['docker','volume','rm',v])
    volumes.append({'name':v,'absent':call(['docker','volume','inspect',v])[0]!=0})
   record['anonymousScratchVolumes']=volumes
  record['finishedAt']=now();put('attempts.jsonl',record)

scope={'status':'four-fact sequence diagnostic only; no full credit','helperSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'owner':OWNER,'trigger':'three consecutive unique public health observations refusing/timing out while exact owned container remains Running','limits':{'seconds':1900,'helpersPerTarget':1,'maximumTargets':6},'helperPolicy':'exact cached image from same owned proof Redis, --rm, unique CID/name/owner, network container:<exact-owned-ID>, readonly root, no privileges/capabilities, no host/target mounts or published ports; only image-declared anonymous /data volume; exact cleanup verified','targetOperations':'read-only inspect/HTTP; no exec or mutation','simultaneous':'namespace wget and published/DCP HTTP reads run concurrently; timestamps retained'}
(OUT/'scope.json').write_text(json.dumps(scope,indent=2)+'\n')
started=time.monotonic();last={};streak={};attempted=set()
while time.monotonic()-started<1900 and not (D/'finished.json').exists():
 admissions={a['id']:a for a in rows(D/'read-only-observer/admitted.jsonl')};latest={o['id']:o for o in rows(D/'read-only-observer/observations.jsonl')}
 for cid,o in latest.items():
  if cid in attempted or cid not in admissions or last.get(cid)==o['at']:continue
  last[cid]=o['at'];bad=o['state'].get('running') is True and bool(o.get('published')) and any(v.get('errorType') in ['ConnectionRefusedError','TimeoutError','ConnectionResetError','RemoteDisconnected'] for v in o['published'])
  streak[cid]=streak.get(cid,0)+1 if bad else 0
  if streak[cid]>=3 and len(attempted)<6:
   attempted.add(cid);namespace(admissions[cid],{'consecutiveFailedSamples':streak[cid],'observation':o})
 time.sleep(0.5)
(OUT/'summary.json').write_text(json.dumps({'finishedAt':now(),'attemptedTargets':len(attempted),'targetIds':sorted(attempted),'extraHelpersMaximumPerTarget':1,'owner':OWNER,'scope':'diagnostic only'},indent=2)+'\n')
