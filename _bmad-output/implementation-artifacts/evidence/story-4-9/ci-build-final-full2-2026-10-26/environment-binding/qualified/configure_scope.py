"""Create a PID/start-bound configuration for only the caller's descendant Docker experiment."""
import hashlib,json,os,re,time
from pathlib import Path
import docker_health_adapter as adapter
ROOT=Path(__file__).resolve().parent;SOURCE=ROOT.parent

def create_scope(directory, sentry_config_roots):
 directory=Path(directory);directory.mkdir(exist_ok=True,parents=True)
 excluded=[]
 for line in (ROOT/'windows-excluded-ports.txt').read_text().splitlines():
  match=re.match(r'^\s+(\d+)\s+(\d+)\s*\*?\s*$',line)
  if match:excluded.append([int(match[1]),int(match[2])])
 scope={'purpose':'temporary child-scoped CP health port experiment; product source/deadlines/assertions unchanged','ownerPid':os.getpid(),'ownerStartTicks':adapter.stat(os.getpid())['startTicks'],'createdAtEpoch':time.time(),'realDocker':'/usr/bin/docker','realDockerSha256':adapter.digest('/usr/bin/docker'),'images':{key:value['id'] for key,value in json.loads((ROOT/'cached-images.json').read_text()).items()},'sentryConfigRoots':[str(Path(p).resolve()) for p in sentry_config_roots],'portRange':[20000,32767],'windowsExcludedRanges':excluded,'allocationState':str(directory/'allocated-ports.json'),'log':str(directory/'adapter-matches.jsonl')}
 path=directory/'scope.json';path.write_text(json.dumps(scope,indent=2)+'\n');path.chmod(0o600);return path,scope
