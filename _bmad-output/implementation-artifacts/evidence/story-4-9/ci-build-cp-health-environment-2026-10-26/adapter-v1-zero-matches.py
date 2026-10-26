"""Task-local Docker Desktop experiment: pin only exact owned CP auto-health publishes."""
import datetime,fcntl,hashlib,json,os,random,re,socket,subprocess,sys,uuid
from pathlib import Path
LABEL='com.microsoft.developer.usvc-dev.'
VALUES={'--name','--network','--mount','--label','-p','--publish','-e','--env','--env-file','--pull','--entrypoint','--user','--workdir','--hostname','--add-host','--restart','--health-cmd','--health-interval','--health-timeout','--health-retries','--health-start-period','--health-start-interval'}
BOOLS={'--rm','--init','--read-only','-i','-t','-it'}
KINDS={'dapr-sentry':('/sentry','daprio/sentry'),'dapr-placement-mtls':('./placement','daprio/dapr'),'dapr-scheduler-mtls':('./scheduler','daprio/dapr')}
def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def stat(pid):
 data=(Path('/proc')/str(pid)/'stat').read_text().rsplit(')',1)[1].split();return {'ppid':int(data[1]),'startTicks':data[19]}
def parse(arguments):
 if not arguments or arguments[0]!='create':return None
 values={};i=1
 while i<len(arguments):
  token=arguments[i]
  if token in VALUES:
   if i+1==len(arguments):return None
   values.setdefault(token,[]).append((i+1,arguments[i+1]));i+=2
  elif token in BOOLS:i+=1
  elif token.startswith('--restart='):i+=1
  elif token.startswith('-'):return None
  else:return {'values':values,'image':token,'imageIndex':i}
 return None

def single(values,key):
 items=values.get(key,[]);return items[0][1] if len(items)==1 else None

def shape(arguments,scope):
 parsed=parse(arguments)
 if not parsed:return None
 v=parsed['values'];name=single(v,'--name');entry=single(v,'--entrypoint');image=parsed['image']
 if not name or image not in scope['images']:return None
 kind=next((k for k,(e,repo) in KINDS.items() if re.fullmatch(re.escape(k)+r'-[a-z0-9]{8}',name) and e==entry and image.startswith(repo+':')),None)
 if not kind:return None
 labels={}
 for _,item in v.get('--label',[]):
  key,sep,value=item.partition('=')
  if not sep or key in labels:return None
  labels[key]=value
 if labels.get(LABEL+'name')!=name or labels.get(LABEL+'group-version')!='usvc-dev.developer.microsoft.com/v1':return None
 try:uuid.UUID(labels[LABEL+'uid']);creator=int(labels[LABEL+'creatorProcessId'])
 except (KeyError,ValueError):return None
 if creator<=1 or not labels.get(LABEL+'creatorProcessStartTime'):return None
 publishes=v.get('-p',[])+v.get('--publish',[])
 auto=[(index,value) for index,value in publishes if value=='127.0.0.1::8080/tcp']
 if len(auto)!=1 or sum(value.rsplit(':',1)[-1] in ('8080','8080/tcp') for _,value in publishes)!=1:return None
 mounts=[]
 for _,item in v.get('--mount',[]):
  fields={}
  for field in item.split(','):
   key,sep,value=field.partition('=')
   if key in fields:return None
   fields[key]=value if sep else True
  mounts.append(fields)
 credentials=[m for m in mounts if m.get('type')=='bind' and m.get('target')=='/var/run/dapr/credentials']
 if len(credentials)!=1:return None
 credential=credentials[0];source=credential.get('src');readonly=credential.get('readonly',False) in (True,'true')
 if not isinstance(source,str) or readonly!=(kind!='dapr-sentry'):return None
 path=Path(source)
 if not path.is_absolute() or str(path.resolve())!=source:return None
 proof=next((p for p in path.parents if re.fullmatch(r'works-sdk415-[a-f0-9]{32}',p.name)),None)
 if proof is None or proof.parent!=Path('/tmp') or path.name not in ['certificates'] and not re.fullmatch(r'dapr-credentials-[a-fA-F0-9]{32}',path.name):return None
 if kind=='dapr-sentry':
  configs=[m for m in mounts if m.get('type')=='bind' and m.get('target')=='/var/run/dapr/config/sentry.yaml' and m.get('readonly') in (True,'true')]
  if len(configs)!=1:return None
  config=Path(str(configs[0].get('src','')))
  if not config.is_absolute() or str(config.resolve())!=str(config) or config.name!='sentry.yaml':return None
  if not any(config.is_relative_to(Path(root)) for root in scope['sentryConfigRoots']):return None
 return {'kind':kind,'name':name,'entrypoint':entry,'image':image,'publishIndex':auto[0][0],'originalPublish':auto[0][1],'creatorPid':creator,'creatorStartLabel':labels[LABEL+'creatorProcessStartTime'],'resourceUid':labels[LABEL+'uid'],'credentialSource':source,'proofName':proof.name}

def ownership(candidate,scope):
 try:
  if stat(scope['ownerPid'])['startTicks']!=scope['ownerStartTicks']:return False
  p=os.getpid();ancestors=[]
  while p>1 and len(ancestors)<64:
   ancestors.append(p)
   if p==scope['ownerPid']:break
   p=stat(p)['ppid']
  if not ancestors or ancestors[-1]!=scope['ownerPid'] or candidate['creatorPid'] not in ancestors:return False
  creator=candidate['creatorPid']
  if Path('/proc',str(creator),'exe').resolve().name!='dcp':return False
  if Path(candidate['credentialSource']).stat().st_uid!=os.getuid():return False
  return True
 except (FileNotFoundError,ProcessLookupError,PermissionError):return False

def native_identity(candidate,scope):
 p=subprocess.run([scope['realDocker'],'image','inspect','--format','{{.Id}}',candidate['image']],capture_output=True,timeout=5,check=False)
 if p.returncode or p.stdout.decode().strip()!=scope['images'][candidate['image']]:return None
 fmt='{"id":{{json .Id}},"created":{{json .Created}},"label":{{json (index .Config.Labels "hexalith.works.sdk-fixture")}}}'
 p=subprocess.run([scope['realDocker'],'inspect','--type','container','--format',fmt,candidate['proofName']],capture_output=True,timeout=5,check=False)
 if p.returncode:return None
 redis=json.loads(p.stdout)
 if redis['label']!=candidate['proofName']:return None
 created=datetime.datetime.fromisoformat(redis['created'].replace('Z','+00:00')).timestamp()
 if created<scope['createdAtEpoch']-2:return None
 return redis

def allocate(scope):
 minimum=int(Path('/proc/sys/net/ipv4/ip_local_port_range').read_text().split()[0]);lower,upper=scope['portRange'];upper=min(upper,minimum-1)
 if not 1024<=lower<=upper:raise RuntimeError('No below-ephemeral scope port range')
 path=Path(scope['allocationState']);path.parent.mkdir(parents=True,exist_ok=True)
 with path.open('a+') as handle:
  fcntl.flock(handle,fcntl.LOCK_EX);handle.seek(0);text=handle.read();reserved=set(json.loads(text)) if text else set()
  for _ in range(512):
   port=random.SystemRandom().randrange(lower,upper+1)
   if port in reserved or any(a<=port<=b for a,b in scope['windowsExcludedRanges']):continue
   try:
    with socket.socket(socket.AF_INET,socket.SOCK_STREAM) as reservation:reservation.bind(('127.0.0.1',port))
   except OSError:continue
   reserved.add(port);handle.seek(0);handle.truncate();handle.write(json.dumps(sorted(reserved)));handle.flush();return port,minimum
 raise RuntimeError('No free explicit below-ephemeral loopback port')

def rewrite(arguments,candidate,port):
 result=list(arguments);index=candidate['publishIndex'];result[index]='127.0.0.1:'+str(port)+':8080/tcp'
 if len(arguments)!=len(result) or [i for i,(a,b) in enumerate(zip(arguments,result)) if a!=b]!=[index]:raise RuntimeError('Health adapter must change exactly one publish value')
 return result

def main():
 original=sys.argv[1:];config=os.environ.get('STORY49_CP_HEALTH_SCOPE');real='/usr/bin/docker'
 if not config:os.execv(real,[real,*original])
 scope=json.loads(Path(config).read_text());real=scope['realDocker'];candidate=shape(original,scope)
 if not candidate or not ownership(candidate,scope):os.execv(real,[real,*original])
 if digest(real)!=scope['realDockerSha256']:raise RuntimeError('Selected native Docker CLI changed')
 identity=native_identity(candidate,scope)
 if not identity:raise RuntimeError('Owned CP candidate failed exact cached image/proof fixture qualification')
 port,minimum=allocate(scope);effective=rewrite(original,candidate,port)
 unchanged_original=[a for i,a in enumerate(original) if i!=candidate['publishIndex']];unchanged_effective=[a for i,a in enumerate(effective) if i!=candidate['publishIndex']]
 vector_hash=lambda a:hashlib.sha256(json.dumps(a,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()
 if vector_hash(unchanged_original)!=vector_hash(unchanged_effective):raise RuntimeError('Non-publish arguments changed')
 record={'originalArgvSha256':vector_hash(original),'effectiveArgvSha256':vector_hash(effective),'originalUnchangedArgumentsSha256':vector_hash(unchanged_original),'effectiveUnchangedArgumentsSha256':vector_hash(unchanged_effective),'at':datetime.datetime.now(datetime.UTC).isoformat(),'scope':'temporary child-scoped environment experiment; no assertion changes','scopeSha256':digest(config),'adapterSha256':digest(__file__),'ownerPid':scope['ownerPid'],'ownerStartTicks':scope['ownerStartTicks'],'creatorStartTicks':stat(candidate['creatorPid'])['startTicks'],**candidate,'selectedImageId':scope['images'][candidate['image']],'proofRedisId':identity['id'],'effectivePublish':effective[candidate['publishIndex']],'linuxEphemeralMinimum':minimum,'changedArgumentIndexes':[candidate['publishIndex']],'argumentCount':len(original),'allOtherArgumentsPreserved':True}
 output=os.open(scope['log'],os.O_WRONLY|os.O_CREAT|os.O_APPEND,0o600)
 try:os.write(output,(json.dumps(record,sort_keys=True)+'\n').encode())
 finally:os.close(output)
 os.execv(real,[real,*effective])
if __name__=='__main__':main()
