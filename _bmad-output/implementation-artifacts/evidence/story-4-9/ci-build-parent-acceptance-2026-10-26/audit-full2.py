from pathlib import Path
import json,hashlib,xml.etree.ElementTree as ET,gzip,sys,copy
root=Path('/tmp/story49-final-20261026T195205Z');p=root/'full2';g=p/'gate';j=json.loads((g/'ledger.json').read_text());errors=[]
phase_records=[json.loads(line) for line in (g/'persisted-states.jsonl').read_text().splitlines() if line]
phases={record['phase']:record for record in phase_records}
def require(check,message):
 if not check: errors.append(message)
require(j['status']=='passed-disposable-synthetic-parity-and-rollback','terminal status')
require(j['profile']['name']=='full' and j['profile']['consumerConfigurations']==['Debug','Release'],'full profile')
require(set(j['rows'])=={'R'+str(i) for i in range(1,12)},'eleven rows')
require(len(phase_records)==len(phases)==32,'unique persisted phases')
for name,row in j['rows'].items():
 require(row['status']=='passed',name+' status')
 require(set(row['requiredProofs'])=={'persisted','negative','restart','rollback'},name+' proof kinds')
 for kind,proof in row['requiredProofs'].items():
  require(proof['passed'] is True and bool(proof['evidence']),name+' '+kind+' passed/evidence')
  for evidence in proof['evidence']:
   phase=phases.get(evidence['phase']);require(phase is not None,name+' '+kind+' executed phase')
   require(phase is not None and hashlib.sha256(json.dumps(phase,sort_keys=True).encode()).hexdigest()==evidence['recordSha256'],name+' '+kind+' actual persisted record')
 require(row['producer']['version']=='3.128.0',name+' producer')
 relative=row['consumer'];base=root/'full1/workspace/platform' if relative.startswith('P/') else root/'works';relative=relative[2:] if relative.startswith('P/') else relative
 require(hashlib.sha256((base/relative).read_bytes()).hexdigest()==row['consumerSha256'],name+' actual consumer hash')
require(j['rollback']['complete'] is True,'complete rollback')
expected={'platform-tests-debug':144,'platform-tests-release':144,'works-consumer-supporting':306,'retained-host-preflight':1,'platform-recovery':4,'platform-historical':1,'platform-ha':1,'platform-mtls':1,'platform-projection':1}
xml={};methods=[]
for name,count in expected.items():
 command=next(c for c in j['commands'] if c['name']==name);require(command['exitCode']==0,name+' exit')
 source=Path(command['command'][command['command'].index('-result-xml')+1]);r=ET.parse(source).getroot();tests=list(r.iter('test'));xml[source.name]=len(tests);methods.extend(t.get('method') for t in tests)
 require(len(tests)==count,name+' count');require(all(t.get('result')=='Pass' for t in tests),name+' all facts executed/pass')
 for assembly in r.iter('assembly'):
  for key in ['errors','failed','skipped','not-run']:require(assembly.get(key,'0')=='0',name+' '+key)
finish=json.loads((p/'driver-finished.json').read_text());require(finish['exitCode']==0 and finish['helperBytesUnchanged'] is True,'driver/helper final')
before=json.loads((p/'helper-hashes-before.json').read_text());after=json.loads((p/'helper-hashes-after.json').read_text());require(before==after,'helper maps unchanged')
packet=Path('/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/evidence/story-4-9/ci-build-final-full2-2026-10-26');m=json.loads((packet/'manifest.json').read_text());native=0
for name,row in m['files'].items():
 data=(packet/name).read_bytes();require(len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256'],name+' packed bytes')
 if 'sourceSha256' in row:
  native+=1;raw=gzip.decompress(data) if row.get('compression')=='gzip' else data
  require(len(raw)==row['sourceBytes'] and hashlib.sha256(raw).hexdigest()==row['sourceSha256'],name+' decoded source')
  source=Path(row['source']);require(source.is_file() and hashlib.sha256(source.read_bytes()).hexdigest()==row['sourceSha256'],name+' native source')
sys.dont_write_bytecode=True;sys.path.insert(0,str(root/'full1/workspace/platform/eng'));import verify_works_host
verify_works_host.require_unchanged_inputs(copy.deepcopy(j),root/'works',root/'full1/workspace/platform',p/'platform-source-binding.json',tuple(j['controlPlanePortSelection']['excludedEphemeralRange']),tuple(tuple(x) for x in j['controlPlanePortSelection']['excludedReservedRanges']))
rebound=verify_works_host.bind_persisted_outcomes(g/'persisted-states.jsonl',copy.deepcopy(j['rows']),j['actualStarts']);require(rebound==j['rollback'],'all proof predicates and cross-phase rollback rebinding')
result={'status':'passed' if not errors else 'failed','errors':errors,'rows':len(j['rows']),'proofs':sum(len(row['requiredProofs']) for row in j['rows'].values()),'xml':xml,'executedMethods':methods,'rollbackComplete':j['rollback']['complete'],'runtimeArtifacts':len(j['runtimeArtifacts']),'owningStrictFinalClosureIndependentlyRechecked':True,'packetEntries':len(m['files']),'nativeBindings':native,'packetManifestSHA256':hashlib.sha256((packet/'manifest.json').read_bytes()).hexdigest()}
Path('/tmp/story49-final-full2-parent-audit.json').write_text(json.dumps(result,indent=2)+'\n');print({k:v for k,v in result.items() if k!='executedMethods'})
raise SystemExit(1 if errors else 0)
