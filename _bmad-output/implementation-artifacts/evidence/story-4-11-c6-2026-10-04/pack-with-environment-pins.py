from pathlib import Path
import json, subprocess, sys
repo=Path('/home/administrator/projects/hexalith/works/references/Hexalith.EventStore')
sys.path.insert(0,str(repo/'tools'))
from release_package_contract import load_release_manifest, validate_manifest_projects
out=Path(sys.argv[1]).resolve()
version=sys.argv[2]
packages=load_release_manifest()
validate_manifest_projects(packages)
out.mkdir(parents=True,exist_ok=True)
commands=[]
for package in packages:
    command=['dotnet','pack',package.project,'--configuration','Release','--output',str(out),f'-p:Version={version}','-p:GeneratePackageOnBuild=false','-p:UseHexalithProjectReferences=false','-m:1','-p:NuGetAudit=false']
    print(' '.join(command),flush=True)
    with (out.parent/f'pack-{package.package_id}.log').open('w') as log:
        try:
            result=subprocess.run(command,cwd=repo,stdout=log,stderr=subprocess.STDOUT,timeout=180)
            code=result.returncode
        except subprocess.TimeoutExpired:
            code=124
    commands.append({'command':command,'exitCode':code})
    (out.parent/'pack-commands.json').write_text(json.dumps(commands,indent=2)+'\n')
    if code:
        print('Failed:',package.package_id,'exit:',code,flush=True)
        raise SystemExit(code)
print('Packed',len(packages),'release packages at',version,flush=True)
