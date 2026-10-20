import json,os,subprocess,sys
from pathlib import Path
platform=Path('/tmp/story49-platform-immutable-v6');sys.path.insert(0,str(platform/'eng'));import verify_works_host as g, works_host_snapshot
old=Path('/tmp/story49-post-snapshot-preparation-20261020-attempt3');p=json.loads((old/'ledger.json').read_text());assert p['status']=='preparation-passed; parity-incomplete'
root=Path('/tmp/story49-snapshot-focused-build-v6');root.mkdir();works=Path('/home/administrator/projects/hexalith/works');sdk=Path('/tmp/story416-sdk-3.126.0-public-source');env=os.environ.copy();env.update(HexalithBuildsRoot='/tmp/story416-builds-b435f2e-public-source',HexalithEventStoreRoot=str(sdk))
for lane,project,cwd,extra in [('host','./apphost.cs',platform,['-p:AspireUseCliBundle=true']),('consumer','tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj',works,['-m:1','-p:PlatformWorksRuntimeRoot='+str(platform),'-p:HexalithAspireHostingDaprVersion=13.6.1-beta.935','-p:HexalithEventStoreRoot='+str(sdk),'-p:HexalithEventStoreVersion=3.126.0'])]:
 output=root/lane
 command=['dotnet','build',project,'-c','Release','-p:TreatWarningsAsErrors=true','-p:NuGetAudit=false','-p:Version=3.126.0','-p:MinVerVersionOverride=3.126.0','-p:MSBuildWarningsAsErrors=MSB3277','-p:UseHexalithProjectReferences=false','-p:UseNuGetDeps=true','--artifacts-path',str(output),g.path_map(output),*extra]
 print(lane,flush=True)
 with (root/(lane+'.log')).open('w') as out:r=subprocess.run(command,cwd=cwd,env=env,stdout=out,stderr=subprocess.STDOUT,timeout=600)
 if r.returncode:raise SystemExit(r.returncode)
assembly=root/'host/bin/release/apphost.dll';consumer=root/'consumer/bin/Hexalith.Works.IntegrationTests/release';p['host']={'source':str(platform/'apphost.cs'),'sourceSha256':g.sha256(platform/'apphost.cs'),'assembly':str(assembly),'sha256':g.sha256(assembly),'configuration':'Release'}
p['platformAdapterAssemblies']={str(path):g.sha256(path) for path in [assembly.parent/'Hexalith.Platform.Composition.dll',assembly.parent/'Hexalith.Platform.Works.Runtime.dll',consumer/'Hexalith.Platform.Works.Runtime.dll']}
p['platformRuntimeIdentity']=g.require_identical_runtime(assembly.parent/'Hexalith.Platform.Works.Runtime.dll',consumer/'Hexalith.Platform.Works.Runtime.dll');p['runtimeOutput']=str(old/'runtime');p['platformSourceSnapshot']=works_host_snapshot.verify(Path('/tmp/story49-platform-immutable-v6.json'),platform)
(root/'ledger.json').write_text(json.dumps(p,indent=2)+'\n');print('exact host/runtime identity passed',p['platformRuntimeIdentity']['sha256'],flush=True)
