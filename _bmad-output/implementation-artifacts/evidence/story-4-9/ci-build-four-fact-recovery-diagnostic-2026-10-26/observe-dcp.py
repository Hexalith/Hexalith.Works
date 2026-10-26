"""Read-only exact owned DCP status/endpoint diagnostic; never persists env or credentials."""
import datetime,hashlib,http.client,json,re,subprocess,time,urllib.parse
from pathlib import Path
D=Path(__file__).resolve().parent;OUT=D/'dcp-observer';OUT.mkdir(exist_ok=False)
ALLOWED_STATUS={'containerId','containerName','state','healthStatus','startupTimestamp','finishTimestamp','exitCode','effectiveAddress','effectivePort','proxyProcessPid','proxylessEndpointName','error','message'}
NAMES=['dapr-sentry','dapr-placement-mtls','dapr-scheduler-mtls']
def now():return datetime.datetime.now(datetime.UTC).isoformat()
def put(name,record):
 with (OUT/name).open('a') as f:f.write(json.dumps(record,sort_keys=True)+'\n')
def call(args):
 try:
  r=subprocess.run(args,capture_output=True,timeout=5,check=False);return r.returncode,r.stdout,r.stderr
 except subprocess.TimeoutExpired:return -1,b'',b'bounded command timeout'
def probe(host,port):
 r={'host':host,'port':port,'path':'/healthz'}
 try:
  c=http.client.HTTPConnection(host,port,timeout=0.5)
  try:c.request('GET','/healthz');v=c.getresponse();r|={'httpStatus':v.status,'body':v.read(256).decode(errors='replace')}
  finally:c.close()
 except Exception as e:r['errorType']=type(e).__name__
 return r
(OUT/'scope.json').write_text(json.dumps({'helperSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'status':'diagnostic-only; zero parity credit','admission':'DCP creator PID/start/group from exact owned Docker control-plane admission; --kubeconfig from that exact process, loopback API only; container API UID must match admitted resource UID','excluded':['spec.env','status.effectiveEnv','createFiles','PEM contents','kubeconfig/client credential contents'],'limits':{'seconds':1900,'pollSeconds':5,'maximumSnapshots':1400}},indent=2)+'\n')
started=time.monotonic();snapshots=0;known={};logged=set()
while time.monotonic()-started<1900 and snapshots<1400:
 try:admissions=[json.loads(x) for x in (D/'read-only-observer/admitted.jsonl').read_text().splitlines()]
 except FileNotFoundError:admissions=[]
 groups={}
 for a in admissions:groups.setdefault(a['creatorPid'],[]).append(a)
 for pid,admitted in groups.items():
  try:
   args=[x.decode() for x in (Path('/proc')/pid/'cmdline').read_bytes().split(b'\0') if x];exe=Path(args[0])
   if exe.name!='dcp' or '--kubeconfig' not in args:continue
   config=Path(args[args.index('--kubeconfig')+1])
   if not config.is_file():continue
   process_start=(Path('/proc')/pid/'stat').read_text().rsplit(')',1)[1].split()[19]
   if pid in known and (known[pid]['config']!=str(config) or known[pid]['processStartTicks']!=process_start):continue
  except (FileNotFoundError,ProcessLookupError,PermissionError,IndexError):continue
  base=['kubectl','--kubeconfig',str(config),'--request-timeout=2s','--cache-dir',str(D/'private-kube-cache')]
  if pid not in known:
   code,out,err=call(base+['config','view','--minify','-o','jsonpath={.clusters[0].cluster.server}']);server=out.decode()
   if code or urllib.parse.urlsplit(server).hostname not in ['127.0.0.1','localhost','::1']:continue
   known[pid]={'server':server,'config':str(config),'processStartTicks':process_start};put('admitted-apis.jsonl',{'at':now(),'creatorPid':pid,'creatorStart':admitted[0]['creatorStart'],'group':admitted[0]['group'],'server':server,'kubeconfigPath':str(config),'credentialContentsStored':False})
  wanted={a['resourceName']:a for a in admitted};snapshot={'at':now(),'creatorPid':pid,'server':known[pid]['server'],'containers':[],'services':[],'endpoints':[]}
  matched_uid=False
  for kind in ['containers','services','endpoints']:
   if kind!='containers' and not matched_uid:
    snapshot['refusal']='No admitted container UID matched this exact creator API; no service/proxy reads performed'
    break
   command=base+['get',kind+'.usvc-dev.developer.microsoft.com','-o','json'];code,out,err=call(command)
   if code:
    snapshot[kind+'ReadError']={'exitCode':code,'stderr':err[:500].decode(errors='replace')};continue
   try:objects=json.loads(out).get('items',[])
   except json.JSONDecodeError:continue
   for obj in objects:
    name=obj['metadata']['name'];status=obj.get('status',{});spec=obj.get('spec',{});meta={k:obj['metadata'].get(k) for k in ['name','uid']}
    if kind=='containers':
     if name not in wanted or obj['metadata']['uid']!=wanted[name]['resourceUid']:continue
     matched_uid=True
     snapshot[kind].append({'metadata':meta,'status':{k:v for k,v in status.items() if k in ALLOWED_STATUS}})
    elif kind=='services':
     if not any(name==n+'-health' or name==n+'-grpc' for n in NAMES):continue
     record={'metadata':meta,'spec':{k:v for k,v in spec.items() if k in ['address','port','protocol','addressAllocationMode']},'status':{k:v for k,v in status.items() if k in ALLOWED_STATUS}}
     if name.endswith('-health') and status.get('effectivePort'):
      host=status.get('effectiveAddress');port=int(status['effectivePort'])
      if host in ['localhost','127.0.0.1','::1']:record['proxyHealth']=[probe(h,port) for h in ([host,'127.0.0.1','::1'] if host=='localhost' else [host])]
     log=config.parent/('resource-service-'+obj['metadata']['uid']+'.log')
     if log.is_file():
      raw=log.read_bytes()[-65536:];digest=hashlib.sha256(raw).hexdigest()
      if (str(log),digest) not in logged:
       lines=raw.decode(errors='replace').splitlines()[-60:];safe=[l for l in lines if not re.search(r'environment|effectiveEnv|-----BEGIN|authorization|bearer|password|token|private.key',l,re.I)]
       put('service-logs.jsonl',{'at':now(),'source':str(log),'nativeTailSha256':digest,'redactedLines':len(lines)-len(safe),'service':name,'lines':safe});logged.add((str(log),digest))
     snapshot[kind].append(record)
    else:
     service=spec.get('serviceName') or spec.get('service')
     if isinstance(service,dict):service=service.get('name')
     if not any(n in name or (isinstance(service,str) and n in service) for n in NAMES):continue
     snapshot[kind].append({'metadata':meta,'spec':{k:v for k,v in spec.items() if k in ['serviceName','service','address','port','protocol','targetPort','containerPort']},'status':{k:v for k,v in status.items() if k in ALLOWED_STATUS}})
  put('snapshots.jsonl',snapshot);snapshots+=1
 try:owner_ledger=json.loads((D.parent/'full1/gate/ledger.json').read_text())
 except (FileNotFoundError,json.JSONDecodeError):owner_ledger={}
 if (D/'finished.json').exists():break
 time.sleep(5)
(OUT/'summary.json').write_text(json.dumps({'endedAt':now(),'snapshots':snapshots,'ownedApis':len(known),'status':'diagnostic-only; zero parity credit'},indent=2)+'\n')
