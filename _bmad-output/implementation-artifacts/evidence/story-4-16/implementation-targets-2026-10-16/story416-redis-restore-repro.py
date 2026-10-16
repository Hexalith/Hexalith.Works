import uuid,socket,subprocess,time,json,hashlib,random
from pathlib import Path
name='works-sdk416-restore-'+uuid.uuid4().hex;owner='hexalith.works.sdk-restore='+name;records=[]
def docker(*args,data=None):return subprocess.run(['docker',*args],input=data,stdout=subprocess.PIPE,stderr=subprocess.PIPE,check=True).stdout
try:
 docker('run','--rm','-d','--name',name,'--label',owner,'-p','127.0.0.1::6379','redis:7.4-alpine')
 port=int(docker('port',name,'6379/tcp').decode().strip().rsplit(':',1)[1]);conn=None
 for i in range(150):
  try:conn=socket.create_connection(('127.0.0.1',port),1);break
  except OSError:time.sleep(.1)
 if conn is None:raise RuntimeError('Owned Redis startup timed out')
 f=conn.makefile('rb')
 def resp():
  kind=f.read(1);v=f.readline()[:-2]
  if kind==b'+':return v
  if kind==b'-':raise RuntimeError(v.decode())
  if kind==b':':return int(v)
  if kind==b'$':
   n=int(v)
   if n==-1:return None
   x=f.read(n);assert f.read(2)==b'\r\n';return x
  if kind==b'*':return [resp() for _ in range(int(v))]
  raise RuntimeError('Malformed Redis response')
 def call(*args):
  args=[x if isinstance(x,bytes) else str(x).encode() for x in args];conn.sendall(b'*'+str(len(args)).encode()+b'\r\n'+b''.join(b'$'+str(len(x)).encode()+b'\r\n'+x+b'\r\n' for x in args));return resp()
 for kind in ['hash','set','zset','list','string']:
  for size in [2,8,32,513,1200]:
   key='fixture';pairs=[(str(i).encode(),('synthetic-value-'+str(i))*50) for i in range(size)];random.shuffle(pairs)
   if kind=='hash':call('HSET',key,*[v for pair in pairs for v in pair])
   elif kind=='set':call('SADD',key,*[pair[0] for pair in pairs])
   elif kind=='zset':call('ZADD',key,*[v for i,pair in enumerate(pairs) for v in [i,pair[0]]])
   elif kind=='list':call('RPUSH',key,*[pair[0] for pair in pairs])
   else:call('SET',key,'synthetic-value-'*size)
   def logical():
    if kind=='hash':
     a=call('HGETALL',key);return sorted(zip(a[::2],a[1::2]))
    if kind=='set':return sorted(call('SMEMBERS',key))
    if kind=='zset':return call('ZRANGE',key,0,-1,'WITHSCORES')
    if kind=='list':return call('LRANGE',key,0,-1)
    return call('GET',key)
   before=call('DUMP',key);values=logical();call('DEL',key);call('RESTORE',key,0,before);after=call('DUMP',key)
   records.append({'type':kind,'size':size,'rawDumpEqual':before==after,'completeLogicalValueEqual':values==logical(),'beforeSHA256':hashlib.sha256(before).hexdigest(),'afterSHA256':hashlib.sha256(after).hexdigest()});call('DEL',key)
 result={'scope':'owned disposable Redis7.4 RESTORE reproduction; no fleet data','samples':records,'nonidenticalDumpCount':sum(not x['rawDumpEqual'] for x in records),'logicalMismatchCount':sum(not x['completeLogicalValueEqual'] for x in records)}
 Path('/tmp/story416-redis-restore-reproduction-v2.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({k:v for k,v in result.items() if k!='samples'}));print('Noncanonical samples',[(x['type'],x['size']) for x in records if not x['rawDumpEqual']])
finally:
 own=docker('ps','--all','--filter','label='+owner,'--format','{{.Names}}').decode().strip()
 if own:
  if own!=name:raise RuntimeError('Ambiguous owned reproduction container')
  docker('rm','-f',name)
