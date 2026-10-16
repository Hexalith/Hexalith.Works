import pathlib,subprocess,json,hashlib
w=pathlib.Path('/home/administrator/projects/hexalith/works'); p=pathlib.Path('/home/administrator/projects/hexalith/platform'); sdk=pathlib.Path('/tmp/story416-sdk-3.124-public-source'); out=w/'artifacts/story-4-16/public-3.124.0'; host=pathlib.Path('/tmp/story416-public-3124-host')
props=['-p:NuGetAudit=false','-p:UseHexalithProjectReferences=false','-p:UseNuGetDeps=true','-p:HexalithAspireHostingDaprVersion=13.6.1-beta.935','-p:PlatformWorksRuntimeRoot='+str(p),'-p:HexalithEventStoreRoot='+str(sdk),'-p:HexalithEventStoreVersion=3.124.0','-p:Version=3.124.0','-p:MinVerVersionOverride=3.124.0']
proof={'scope':'PUBLIC3.124.0 qualification preparation; exact unmodified producer0737c0befce5338e294a789092a0e223e8264a04 runtime source','runtimeSdkRoot':str(sdk),'commands':[]}
projects=[(p,'./apphost.cs',host), (w,str(w/'src/Hexalith.Works/Hexalith.Works.csproj'),out), *[(w,str(sdk/'src'/n/(n+'.csproj')),out) for n in ['Hexalith.EventStore','Hexalith.EventStore.Operations','Hexalith.EventStore.Admin.Server.Host']],(w,str(w/'src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj'),out),(w,'tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj',out)]
for i,(cwd,project,output) in enumerate(projects):
 command=['dotnet','build',project,'-c','Release','--artifacts-path',str(output),*(['-p:AspireUseCliBundle=true'] if project.endswith('.cs') else ['-m:1']),*props]
 log='/tmp/story416-public-3124-prepare-'+str(i)+'.log'
 with open(log,'w') as stream:r=subprocess.run(command,cwd=cwd,stdout=stream,stderr=subprocess.STDOUT)
 proof['commands'].append({'cwd':str(cwd),'command':command,'log':log,'exitCode':r.returncode})
 pathlib.Path('/tmp/story416-public-3124-prepare.json').write_text(json.dumps(proof,indent=2)+'\n')
 print('public preparation',i,'exit',r.returncode,flush=True)
 if r.returncode:raise SystemExit(r.returncode)
print('public preparation complete',flush=True)
