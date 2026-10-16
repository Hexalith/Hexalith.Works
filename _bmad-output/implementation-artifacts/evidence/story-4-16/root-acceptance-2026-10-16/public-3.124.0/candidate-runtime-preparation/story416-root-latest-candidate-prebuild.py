from pathlib import Path
import ast,json,subprocess,hashlib
source=Path('/tmp/story416-candidate-3124-prepare.py').read_text()
namespace={}
module=ast.parse(source)
for node in module.body:
    if isinstance(node,ast.Assign) and any(isinstance(target,ast.Name) and target.id=='source' for target in node.targets):break
    exec(compile(ast.Module(body=[node],type_ignores=[]),'<candidate-build-definition>','exec'),namespace)
works=namespace['w'];out=namespace['out'];projects=namespace['projects'][:5]
props=[arg for arg in namespace['props'] if not arg.startswith('-p:AssemblyVersion=')]
record={'scope':'UNPUBLISHED latest-source candidate runtime preparation only','baseProducerCommit':namespace['proof']['baseProducerCommit'],'sdkRoot':str(namespace['sdk']),'output':str(out),'fixtureSourceChanged':False,'assemblyVersionPolicy':'Preserve Works.Contracts3.0.0.0; SDK derives3.124.1.0 from explicit Version. No global AssemblyVersion override.','commands':[]}
fixture=works/'tests/Hexalith.Works.IntegrationTests/WorksSdkRecoveryPipelineSmokeTests.cs';before=hashlib.sha256(fixture.read_bytes()).hexdigest()
for index,project in enumerate(projects):
    command=['dotnet','build',str(project),'-c','Debug','--artifacts-path',str(out),'-m:1',*props]
    log=Path('/tmp/story416-root-latest-candidate-runtime-'+str(index)+'.log')
    with log.open('w') as output:result=subprocess.run(command,cwd=works,stdout=output,stderr=subprocess.STDOUT)
    record['commands'].append({'cwd':str(works),'command':command,'log':str(log),'exitCode':result.returncode,'logSha256':hashlib.sha256(log.read_bytes()).hexdigest()})
    record['fixtureSha256Before']=before;record['fixtureSha256After']=hashlib.sha256(fixture.read_bytes()).hexdigest()
    Path('/tmp/story416-root-latest-candidate-runtime.json').write_text(json.dumps(record,indent=2)+'\n')
    print('Latest candidate runtime project',index,'exit',result.returncode,flush=True)
    if result.returncode:raise SystemExit(result.returncode)
    if record['fixtureSha256After']!=before:raise RuntimeError('A concurrent fixture edit changed the bound source')
