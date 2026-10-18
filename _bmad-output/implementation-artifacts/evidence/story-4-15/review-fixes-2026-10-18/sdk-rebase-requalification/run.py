"""Requalify source Debug at the pushed SDK gitlink after the /pushall rebase (2026-10-18 review decision, option 1)."""
import gzip,hashlib,json,os,shutil,subprocess,threading,time
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path('/home/administrator/projects/hexalith/works')
OUT=Path(__file__).resolve().parent
RAW=OUT/'raw'
APPHOST=ROOT/'src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj'
PROJECT='tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj'
RESULTS=[]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def git(*args,cwd=ROOT):return subprocess.check_output(['git',*args],cwd=cwd,text=True).strip()
def binding_snapshot(label, stop):
    records={}
    while not stop.is_set():
        try:
            r=subprocess.run(['aspire','describe','--apphost',str(APPHOST),'--format','Json'],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,timeout=10)
            if r.returncode==0:
                for item in json.loads(r.stdout)['resources']:
                    resource=item.get('displayName')
                    if resource not in ['works','eventstore']:continue
                    pid=item.get('properties',{}).get('executable.pid')
                    if not pid:continue
                    pending=[int(pid)]; visited=set(); paths=set()
                    while pending and len(visited)<32:
                        current=pending.pop(0)
                        if current in visited:continue
                        visited.add(current); base=Path('/proc')/str(current)
                        children=base/'task'/str(current)/'children'
                        if children.exists():pending.extend(int(v) for v in children.read_text().split())
                        maps=base/'maps'
                        if maps.exists():
                            for line in maps.read_text().splitlines():
                                if '/Hexalith.EventStore.' in line and '.dll' in line:paths.add(line.split(None,5)[-1])
                    if not paths:continue
                    key=(resource,int(pid)); previous=records.get(key,{'stage':label,'resource':resource,'pid':int(pid),'assemblies':{}})
                    for value in paths:
                        f=Path(value)
                        if f.exists():previous['assemblies'][value]=sha(f)
                    records[key]=previous
                    (RAW/(label+'-bindings.json')).write_text(json.dumps(list(records.values()),indent=2))
        except Exception:pass
        stop.wait(8)
def run(label, cmd, env=None, xml=None, live=False):
    print('START '+label,flush=True); started=time.time(); stop=threading.Event(); watcher=None
    if live:
        watcher=threading.Thread(target=binding_snapshot,args=(label,stop),daemon=True); watcher.start()
    try:
        with (RAW/(label+'.log')).open('w') as f:r=subprocess.run(cmd,cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
    finally:
        stop.set()
        if watcher:watcher.join(15)
    result={'label':label,'command':cmd,'exit':r.returncode,'seconds':round(time.time()-started,3),'log':label+'.log'}
    if xml and Path(xml).exists():
        a=ET.parse(xml).getroot().find('assembly'); result['tests']={k:a.get(k) for k in ['total','passed','failed','errors','skipped','not-run','time']}; result['xml']=Path(xml).name
    RESULTS.append(result); (OUT/'results.json').write_text(json.dumps(RESULTS,indent=2)); print(json.dumps(result),flush=True)
    return r.returncode==0
def tests(label,live_method=None):
    xml=RAW/(label+'.xml'); assembly=ROOT/'tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll'
    cmd=['dotnet',str(assembly)]; env=None
    if live_method:
        cmd+=['-method','*'+live_method,'-longRunning','45']
        env=dict(os.environ,WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS='180',WORKS_SDK_PROOF_DAPR_CONFIG_DIRECTORY='/tmp/story415-20261018-live/dapr-configs',WORKS_SDK_PROOF_EVIDENCE_PATH=str(RAW/(label+'-runtime.jsonl')))
    else:cmd+=['-class-','*SmokeTests','-class-','*Platform*']
    cmd+=['-result-xml',str(xml)]; return run(label,cmd,env,xml,live=bool(live_method))
RAW.mkdir(parents=True,exist_ok=True)
sdk=ROOT/'references/Hexalith.EventStore'
(OUT/'heads.json').write_text(json.dumps({'worksHead':git('rev-parse','HEAD'),'sdkGitlink':git('ls-tree','HEAD','references/Hexalith.EventStore').split()[2],'sdkCheckout':git('rev-parse','HEAD',cwd=sdk),'sdkDescribe':git('describe','--tags',cwd=sdk),'sdkDirty':git('status','--porcelain',cwd=sdk)!='','submodules':git('submodule','status').splitlines(),'started':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())},indent=2))
original=Path('/proc/sys/fs/inotify/max_user_watches').read_text().strip()
try:
    built=run('debug-build',['dotnet','build',PROJECT,'--configuration','Debug','-m:1'])
    for name in ['UnitTests','ArchitectureTests','PropertyTests']:
        built=run(name+'-build',['dotnet','build',f'tests/Hexalith.Works.{name}/Hexalith.Works.{name}.csproj','--configuration','Debug','-m:1']) and built
    if built:
        client=ROOT/'tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.EventStore.Client.dll'
        (OUT/'debug-binaries.json').write_text(json.dumps({str(f.relative_to(ROOT)):sha(f) for f in client.parent.glob('Hexalith*.dll')},indent=2))
        tests('debug-integration')
        subprocess.run(['sudo','-n','sysctl','-w','fs.inotify.max_user_watches=2097152'],check=True,stdout=subprocess.PIPE)
        tests('debug-restart','SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart')
        tests('debug-callback','SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery')
        for name in ['UnitTests','ArchitectureTests','PropertyTests']:
            xml=RAW/(name+'.xml'); run(name,['dotnet',str(ROOT/f'tests/Hexalith.Works.{name}/bin/Debug/net10.0/Hexalith.Works.{name}.dll'),'-result-xml',str(xml)],xml=xml)
finally:
    subprocess.run(['sudo','-n','sysctl','-w','fs.inotify.max_user_watches='+original],check=True,stdout=subprocess.PIPE)
    cleanup={'watchLimit':Path('/proc/sys/fs/inotify/max_user_watches').read_text().strip(),'aspirePs':subprocess.run(['aspire','ps','--format','Json'],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE).stdout,'runningContainers':subprocess.check_output(['docker','ps','--format','{{.Names}}'],text=True).splitlines()}
    (OUT/'cleanup.json').write_text(json.dumps(cleanup,indent=2))
    for f in sorted(RAW.iterdir()):
        if f.suffix!='.gz':
            with f.open('rb') as src, gzip.GzipFile(str(f)+'.gz','wb',mtime=0) as dst:shutil.copyfileobj(src,dst)
            f.unlink()
    print('DONE',flush=True)
