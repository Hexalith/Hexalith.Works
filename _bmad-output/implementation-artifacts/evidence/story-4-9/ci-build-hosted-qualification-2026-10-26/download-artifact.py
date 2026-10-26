import datetime,hashlib,json,stat,subprocess,sys,zipfile
from pathlib import Path,PurePosixPath
ROOT=Path(__file__).resolve().parent;aid=int(sys.argv[1]);folder=ROOT/'artifacts'/str(aid);folder.mkdir(parents=True,exist_ok=False)
command=['gh','api','repos/Hexalith/Hexalith.Works/actions/artifacts/'+str(aid)];p=subprocess.run(command,capture_output=True,check=True);meta=json.loads(p.stdout);(folder/'metadata.json').write_bytes(p.stdout);assert meta['workflow_run']['id']==40407181685 and meta['workflow_run']['head_sha']=='09cc8e19c23ec15a0542499892bbf5cd4b8f38dd'
archive=folder/'original-artifact.zip';download=['gh','api','repos/Hexalith/Hexalith.Works/actions/artifacts/'+str(aid)+'/zip']
with archive.open('wb') as f:p=subprocess.run(download,stdout=f,stderr=subprocess.PIPE,check=True)
sha=hashlib.sha256(archive.read_bytes()).hexdigest();assert meta.get('digest') in (None,'sha256:'+sha)
extract=folder/'extracted';extract.mkdir();members={};total=0
with zipfile.ZipFile(archive) as z:
 for i in z.infolist():
  name=PurePosixPath(i.filename);assert not name.is_absolute() and '..' not in name.parts and '\\' not in i.filename and not stat.S_ISLNK(i.external_attr>>16),i.filename
  normalized=str(name);assert normalized not in members,normalized;total+=i.file_size;assert total<500000000
  destination=extract.joinpath(*name.parts);assert destination.resolve().is_relative_to(extract.resolve())
  if i.is_dir():destination.mkdir(parents=True,exist_ok=True);members[normalized]={'directory':True};continue
  destination.parent.mkdir(parents=True,exist_ok=True);raw=z.read(i);destination.write_bytes(raw);members[normalized]={'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()}
record={'downloadedAt':datetime.datetime.now(datetime.UTC).isoformat(),'metadataCommand':command,'downloadCommand':download,'artifactId':aid,'name':meta['name'],'zipBytes':archive.stat().st_size,'zipSha256':sha,'declaredDigestMatched':True,'safeExtraction':'relative unique paths only; no symlinks/traversal/backslashes; bounded expanded bytes','expandedBytes':total,'members':members};(folder/'download-and-extraction.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps({k:v for k,v in record.items() if k!='members'},indent=2));print('members',list(members))
