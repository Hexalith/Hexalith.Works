import hashlib,json,stat,subprocess,sys,zipfile
from pathlib import Path,PurePosixPath
ROOT=Path(__file__).resolve().parent;rid=int(sys.argv[1]);assert rid in [40407181685,40407181557,40407181599];D=ROOT/'run-logs'/str(rid);D.mkdir(parents=True,exist_ok=False)
command=['gh','api','repos/Hexalith/Hexalith.Works/actions/runs/'+str(rid)];p=subprocess.run(command,capture_output=True,check=True);meta=json.loads(p.stdout);assert meta['status']=='completed' and meta['head_sha']=='09cc8e19c23ec15a0542499892bbf5cd4b8f38dd';(D/'run.json').write_bytes(p.stdout)
command=['gh','api','repos/Hexalith/Hexalith.Works/actions/runs/'+str(rid)+'/logs'];archive=D/'original-logs.zip'
with archive.open('wb') as f:subprocess.run(command,stdout=f,stderr=subprocess.PIPE,check=True)
extract=D/'extracted';extract.mkdir();members={};total=0
with zipfile.ZipFile(archive) as z:
 for i in z.infolist():
  p=PurePosixPath(i.filename);assert not p.is_absolute() and '..' not in p.parts and '\\' not in i.filename and not stat.S_ISLNK(i.external_attr>>16);assert str(p) not in members;total+=i.file_size;assert total<500000000;target=extract.joinpath(*p.parts);assert target.resolve().is_relative_to(extract.resolve())
  if i.is_dir():target.mkdir(parents=True,exist_ok=True);members[str(p)]={'directory':True};continue
  target.parent.mkdir(parents=True,exist_ok=True);raw=z.read(i);target.write_bytes(raw);members[str(p)]={'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()}
record={'runId':rid,'headSha':meta['head_sha'],'conclusion':meta['conclusion'],'command':command,'zipSha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'zipBytes':archive.stat().st_size,'expandedBytes':total,'members':members};(D/'download-and-extraction.json').write_text(json.dumps(record,indent=2)+'\n');print({k:v for k,v in record.items() if k!='members'})
