import pathlib,subprocess,json,hashlib,difflib
w=pathlib.Path('/home/administrator/projects/hexalith/works'); p=pathlib.Path('/home/administrator/projects/hexalith/platform'); sdk=pathlib.Path('/tmp/story416-sdk-3.124-source'); out=w/'artifacts/story-4-16/candidate-3.124.1'
v='3.124.1-story416-gateway'
props=['-p:NuGetAudit=false','-p:UseHexalithProjectReferences=true','-p:UseNuGetDeps=false','-p:HexalithAspireHostingDaprVersion=13.6.1-beta.935','-p:PlatformWorksRuntimeRoot='+str(p),'-p:HexalithEventStoreRoot='+str(sdk),'-p:HexalithEventStoreVersion='+v,'-p:Version='+v,'-p:PackageVersion='+v,'-p:FileVersion=3.124.1.0','-p:InformationalVersion='+v,'-p:MinVerVersionOverride='+v]
proof={'scope':'UNPUBLISHED3.124.1 SOURCE DIAGNOSTIC ONLY; never public parity acceptance','runtimeSdkRoot':str(sdk),'baseProducerCommit':'0737c0befce5338e294a789092a0e223e8264a04','commands':[]}
projects=[w/'src/Hexalith.Works/Hexalith.Works.csproj', *[sdk/'src'/n/(n+'.csproj') for n in ['Hexalith.EventStore','Hexalith.EventStore.Operations','Hexalith.EventStore.Admin.Server.Host']], w/'src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj', w/'tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj']
projects=projects[-1:]
source=w/'tests/Hexalith.Works.IntegrationTests/WorksSdkRecoveryPipelineSmokeTests.cs'
before=source.read_bytes(); overlay=before.replace(b'WithEnvironment("MinVerVersionOverride", "3.124.0")', b'WithEnvironment("MinVerVersionOverride", "3.124.1-story416-gateway")').replace(b'WithEnvironment("Version", "3.124.0")', b'WithEnvironment("Version", "3.124.1-story416-gateway")')
assert before.count(b'"3.124.0"')==2 and overlay!=before
proof['fixtureOverlay']={'source':str(source.relative_to(w)),'beforeSha256':hashlib.sha256(before).hexdigest(),'overlaySha256':hashlib.sha256(overlay).hexdigest(),'diff':''.join(difflib.unified_diff(before.decode().splitlines(True),overlay.decode().splitlines(True)))}
source.write_bytes(overlay)
try:
 for i,project in enumerate(projects):
  c=['dotnet','build',str(project),'-c','Debug','--artifacts-path',str(out),'-m:1',*props]
  log='/tmp/story416-candidate-3124-consumer-retained-config-v2-build-'+str(i)+'.log'
  with open(log,'w') as f:r=subprocess.run(c,cwd=w,stdout=f,stderr=subprocess.STDOUT)
  proof['commands'].append({'cwd':str(w),'command':c,'log':log,'exitCode':r.returncode})
  pathlib.Path('/tmp/story416-candidate-3124-consumer-retained-config-v2-prepare.json').write_text(json.dumps(proof,indent=2)+'\n')
  print('candidate preparation',i,'exit',r.returncode,flush=True)
  if r.returncode:raise SystemExit(r.returncode)
finally:
 if source.read_bytes()!=overlay:raise RuntimeError('Concurrent fixture edit; preserve bytes and stop rather than overwrite')
 source.write_bytes(before)
 proof['fixtureOverlay']['restoredSha256']=hashlib.sha256(source.read_bytes()).hexdigest()
 pathlib.Path('/tmp/story416-candidate-3124-consumer-retained-config-v2-prepare.json').write_text(json.dumps(proof,indent=2)+'\n')
print('candidate preparation complete',flush=True)
