"""Retain bounded native evidence after the owning final gate reaches a terminal result."""
import collections,datetime,gzip,hashlib,json,subprocess,xml.etree.ElementTree as ET
from pathlib import Path
RUN=Path(__file__).resolve().parent
PACKET=Path('/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/evidence/story-4-9/ci-build-final-qualification-2026-10-26')
GATE=RUN/'full1/gate'
ledger=json.loads((GATE/'ledger.json').read_text())
assert ledger.get('failure') or ledger.get('status')=='passed-disposable-synthetic-parity-and-rollback','Owning gate is still running'
assert not PACKET.exists(),'Previous packet must stay immutable'
inputs=json.loads((RUN/'inputs.json').read_text())
checks={}
for path,entry in inputs['overlays'].items():
 raw=(RUN/'works'/path).read_bytes();checks[path]=hashlib.sha256(raw).hexdigest()==entry['sha256']
assert all(checks.values()),'Captured overlay changed'
heads={'works':subprocess.check_output(['git','rev-parse','HEAD'],cwd=RUN/'works',text=True).strip()}
heads.update({p:subprocess.check_output(['git','rev-parse','HEAD'],cwd=RUN/'works'/p,text=True).strip() for p in inputs['rootDependencies']})
assert heads['works']==inputs['worksRevision']
assert all(heads[p]==commit for p,commit in inputs['rootDependencies'].items())
(RUN/'final-input-recheck.json').write_text(json.dumps({'checkedAtUtc':datetime.datetime.now(datetime.UTC).isoformat(),'revisions':heads,'overlays':checks,'allUnchanged':True},indent=2)+'\n')
xml={};passing=set()
for source in sorted(GATE.glob('*.xml')):
 root=ET.parse(source).getroot();assemblies=list(root.iter('assembly'))
 xml[source.name]={k:sum(int(a.get(k,'0')) for a in assemblies) for k in ['total','passed','failed','errors','skipped','not-run']}
 passing.update(t.get('name') for t in root.iter('test') if t.get('result')=='Pass')
ordinary=json.loads((RUN/'ordinary/summary.json').read_text())
reserved=[t['name'] for a in ordinary['assemblies'].values() for t in a['notRunTests']]
coverage={name:name in passing for name in reserved}
proofs={row:{name:bool(value.get('passed')) if isinstance(value,dict) else False for name,value in record.get('requiredProofs',{}).items()} for row,record in ledger.get('rows',{}).items()}
phases=(GATE/'persisted-states.jsonl').read_text().splitlines() if (GATE/'persisted-states.jsonl').exists() else []
starts=(GATE/'host-starts.jsonl').read_text().splitlines() if (GATE/'host-starts.jsonl').exists() else []
summary={'scope':'final fixed current revisions plus exact captured overlays; later unrelated changes excluded','fullExitCode':1 if ledger.get('failure') else 0,'ownerStatus':ledger.get('status'),'failure':ledger.get('failure'),'profile':ledger.get('profile'),'unexecutedRequiredLanes':[n for n in ['platform-historical','platform-ha','platform-mtls','platform-projection'] if not any(c['name']==n and c.get('exitCode')==0 for c in ledger['commands'])],'finalClosureExecuted':ledger.get('status')=='passed-disposable-synthetic-parity-and-rollback','xml':xml,'rowStatuses':{k:v.get('status') for k,v in ledger.get('rows',{}).items()},'requiredProofs':proofs,'passedProofCount':sum(sum(p.values()) for p in proofs.values()),'rollbackComplete':ledger.get('rollback',{}).get('complete'),'persistedPhases':len(phases),'fleetStarts':len(starts),'ordinaryTopologyFactCoverage':coverage,'capturedInputsUnchanged':True,'authenticationPresenceOnly':True,'hostedQualification':'pending parent handoff','remainingReviewGroups':'pending parent workflow'}
if ledger.get('status')=='passed-disposable-synthetic-parity-and-rollback':
 assert len(proofs)==11 and summary['passedProofCount']==44
 assert all(v=='passed' for v in summary['rowStatuses'].values()) and summary['rollbackComplete'] is True
 assert all(coverage.values()) and len(coverage)==7,'Ordinary topology exclusions are not all executed and passing'
 assert all(x['failed']==x['errors']==x['skipped']==x['not-run']==0 for x in xml.values())
(RUN/'full1-summary.json').write_text(json.dumps(summary,indent=2)+'\n')
PACKET.mkdir()
files={}
def copy(source,relative):
 raw=source.read_bytes();compress=source.suffix in ['.log','.jsonl'] or (source.suffix=='.json' and len(raw)>200000)
 if compress:relative+='.gz'
 data=gzip.compress(raw,mtime=0) if compress else raw;target=PACKET/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
 files[relative]={'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'source':str(source),'sourceSha256':hashlib.sha256(raw).hexdigest(),'sourceBytes':len(raw),'compression':'gzip' if compress else 'none'}
for name in ['inputs.json','full-driver.py','full1-driver.log','run-ordinary.py','ordinary-driver.log','ordinary-runtime-before.json','ordinary-runtime-after.json','observe-control-plane.py','observe-dcp.py','read-only-observer-driver.log','dcp-observer-driver.log','full1-summary.json','diagnostic-summary.json','observer-versions.json','final-input-recheck.json','build-final-packet.py']:
 source=RUN/name
 if source.exists():copy(source,name)
for folder in ['checks','ordinary','owned-overlays','read-only-observer','dcp-observer','read-only-bridge-probe']:
 for source in sorted((RUN/folder).rglob('*')):
  if source.is_file():copy(source,folder+'/'+str(source.relative_to(RUN/folder)))
for name in ['bootstrap.json','full-invocation.json','platform-source-binding.json']:copy(RUN/'full1'/name,'full1/'+name)
for source in sorted(GATE.iterdir()):
 if source.is_file() and source.suffix in ['.json','.jsonl','.xml','.log','.md']:copy(source,'full1/gate/'+source.name)
for base in [GATE/'agents-full',GATE/'agents-local',*[p for p in GATE.iterdir() if p.is_dir() and p.name.startswith('isolated-daprd')]]:
 if not base.exists():continue
 for source in sorted(base.rglob('*')):
  relative=source.relative_to(base)
  if source.is_file() and not any(p in ['bin','obj','package','packages'] for p in relative.parts) and source.suffix in ['.json','.jsonl','.xml','.log','.md']:copy(source,'full1/gate/'+base.name+'/'+str(relative))
(RUN/'packet-copy-index.json').write_text(json.dumps(files,indent=2)+'\n')
(PACKET/'manifest.json').write_text(json.dumps({'createdAtUtc':datetime.datetime.now(datetime.UTC).isoformat(),'scope':'bounded final-input canonical full evidence; parent audit/hosted/reviews outstanding','nativeRoot':str(RUN),'files':files},indent=2)+'\n')
for relative,entry in files.items():
 data=(PACKET/relative).read_bytes();assert hashlib.sha256(data).hexdigest()==entry['sha256']
 if entry['compression']=='gzip':assert hashlib.sha256(gzip.decompress(data)).hexdigest()==entry['sourceSha256']
print(json.dumps({'packet':str(PACKET),'files':len(files),'bytes':sum(p.stat().st_size for p in PACKET.rglob('*') if p.is_file()),'ownerStatus':summary['ownerStatus'],'proofs':summary['passedProofCount']}))
