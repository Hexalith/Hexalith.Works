from pathlib import Path
import json,hashlib,subprocess
p=Path('/home/administrator/projects/hexalith/platform');out=Path('/tmp/story416-candidate-3124-host-debug-v2');prefix='/tmp/story416-candidate-3124-host-debug-v2'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def inputs():
 files=[p/'apphost.cs',p/'Directory.Packages.props',p/'global.json']
 for base in [p/'src',p/'references/Hexalith.Builds/src']:
  files += [x for x in base.rglob('*') if x.is_file() and x.suffix in ('.cs','.csproj','.props','.targets') and not any(c in ('bin','obj','artifacts') for c in x.relative_to(base).parts)]
 for base in [p,p/'references/Hexalith.Builds']:
  files += [x for x in base.iterdir() if x.is_file() and x.suffix in ('.props','.targets','.json')]
 return {str(x):sha(x) for x in sorted(set(files))}
cmd=['dotnet','build','./apphost.cs','-c','Debug','--artifacts-path',str(out),'-p:AspireUseCliBundle=true','-p:NuGetAudit=false','-p:UseHexalithProjectReferences=true','-p:UseNuGetDeps=false','-p:HexalithEventStoreRoot=/tmp/story416-sdk-3.124-public-source','-p:HexalithEventStoreVersion=3.124.0','-p:Version=3.124.0','-p:MinVerVersionOverride=3.124.0']
before=inputs();proof={'scope':'UNPUBLISHED V2 candidate diagnostic hosting; Debug host official public3.124 source; runtime children explicit separate candidate3.124.1','cwd':str(p),'command':cmd,'log':prefix+'-build.log','sourceBefore':before}
with open(prefix+'-build.log','w') as log:r=subprocess.run(cmd,cwd=p,stdout=log,stderr=subprocess.STDOUT)
after=inputs();proof.update(exitCode=r.returncode,sourceAfter=after,sourceChangedDuringBuild=before!=after,logSha256=sha(Path(prefix+'-build.log')))
assembly=out/'bin/debug/apphost.dll'
if assembly.exists():proof.update(assembly=str(assembly),assemblySha256=sha(assembly))
Path(prefix+'-command.json').write_text(json.dumps(proof,indent=2)+'\n');print('exitCode',r.returncode,'sourceChangedDuringBuild',before!=after,flush=True)
raise SystemExit(r.returncode or (1 if before!=after else 0))
