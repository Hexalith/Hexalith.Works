from pathlib import Path,PurePosixPath
import json,hashlib,xml.etree.ElementTree as ET,gzip,zipfile,stat,sys,copy
run=Path('/tmp/story49-hosted-20261026T215001Z');a=run/'artifacts/12693567134/extracted';g=a/'gate';ci=json.loads((g/'ledger.json').read_text());boot=json.loads((a/'bootstrap.json').read_text());full=json.loads(Path('/tmp/story49-final-20261026T195205Z/full2/gate/ledger.json').read_text());errors=[]
def require(ok,message):
 if not ok:errors.append(message)
archives={'12693567134':'0dcbd6ac49bda95a029551bfcf1af09255b42f6cce58997d28c3ab1dbfcbaec7','12691976576':'d59b453016ad2e8df7b1a0b39405534f1c75c27882168181dfe6d564633a3a17'}
for artifact,expected in archives.items():
 p=run/'artifacts'/artifact/'original-artifact.zip';require(hashlib.sha256(p.read_bytes()).hexdigest()==expected,artifact+' ZIP digest')
 with zipfile.ZipFile(p) as z:
  for info in z.infolist():
   path=PurePosixPath(info.filename);require(not path.is_absolute() and '..' not in path.parts and '\\' not in info.filename and not stat.S_ISLNK(info.external_attr>>16),artifact+' safe ZIP member')
require(boot['status']==ci['status']=='passed-disposable-synthetic-parity-and-rollback','terminal acceptance')
require(boot['sdkVersion']==ci['selectedSdk']['version']=='3.128.0','selected SDK')
require(boot['platformCommit']=='73bebc0a336ebf68cee77ab0114d96dde66c386d','owner commit')
require(ci['profile']=={'name':'ci','consumerConfigurations':['Release'],'agentsConfiguration':'Release','sourceConformance':'not-requested; full-local-profile-only','liveProofs':'all-R1-R11-and-complete-fleet-rollback'},'CI profile')
require('Debug' not in ci['checks'] and set(ci['dependencyModes'])=={'Release'},'no false Debug qualification')
require('GITHUB_TOKEN' not in ci['runtimeEnvironment'],'credential excluded from runtime ledger')
producer='c5d1c280d8c1c1a207cd159d817f614e93c93130';q=ci['runtimeProducerQualification'];require(q['repositoryCommit']==producer and q['matchesPublishedProducer'] is True,'official producer source qualification')
for name,item in ci['publishedProducers'].items():require(item['version']=='3.128.0' and item['repositoryCommit']==producer and item['source']=='https://api.nuget.org/v3/index.json',name+' public provenance')
expected_xml={'platform-release.xml':144,'works-consumer-supporting.xml':306,'retained-preflight.xml':1,'platform-recovery.xml':4,'platform-historical.xml':1,'platform-ha.xml':1,'platform-mtls.xml':1,'platform-projection.xml':1};passed_methods={};xml_counts={}
for name,count in expected_xml.items():
 r=ET.parse(g/name).getroot();tests=list(r.iter('test'));xml_counts[name]=len(tests);require(len(tests)==count,name+' count')
 for t in tests:require(t.get('result')=='Pass',name+' executed Pass');passed_methods[t.get('type')+'.'+t.get('method')]=name
 for assembly in r.iter('assembly'):
  for key in ['failed','errors','skipped','not-run']:require(assembly.get(key,'0')=='0',name+' '+key)
records=[json.loads(x) for x in (g/'persisted-states.jsonl').read_text().splitlines() if x];phases={r['phase']:r for r in records};require(len(records)==len(phases)==32,'32 unique persisted phases');require(len(ci['actualStarts'])==11,'11 actual main starts')
require(set(ci['rows'])=={'R'+str(i) for i in range(1,12)},'11 rows')
for name,row in ci['rows'].items():
 require(row['status']=='passed' and row['producer']['version']=='3.128.0',name+' status/producer')
 require(set(row['requiredProofs'])=={'persisted','negative','restart','rollback'},name+' proof kinds')
 for kind,proof in row['requiredProofs'].items():
  require(proof['passed'] is True and bool(proof['evidence']),name+' '+kind+' passed')
  for evidence in proof['evidence']:
   phase=phases.get(evidence['phase']);require(phase is not None and hashlib.sha256(json.dumps(phase,sort_keys=True).encode()).hexdigest()==evidence['recordSha256'],name+' '+kind+' native record SHA')
require(ci['rollback']['complete'] is True,'complete rollback')
for command in ci['commands']:
 require(command['exitCode']==(1 if command['name']=='agents-full-refusal' else 0),command['name']+' expected exit')
 require(hashlib.sha256((g/command['log']).read_bytes()).hexdigest()==command['sha256'],command['name']+' log digest')
equivalence={}
for owner in ['Works','Platform']:
 left=full['canonicalSources'][owner]['sourceFiles'];right=ci['canonicalSources'][owner]['sourceFiles'];require(left==right,owner+' full/hosted source equivalence');equivalence[owner]=len(right)
for key in ['fixtureSources','platformConsumerSources','verifierHelpers']:
 require(full[key]==ci[key],key+' equivalence');equivalence[key]=len(ci[key])
require(json.loads(Path('/tmp/story49-final-20261026T195205Z/full2/bootstrap.json').read_text())['coordinatorSha256']==boot['coordinatorSha256'],'coordinator equivalence')
def normalized(j):
 roots=[(j['canonicalSources'][k]['root'],k) for k in ['Platform','Works']]+[(j['linkedFixtureSources']['compositionRoot'],'Platform')];result={}
 require(j['linkedFixtureSources']['compositionRoot']==str(Path(j['host']['source']).parent),'linked composition root equals actual host source root')
 for name,digest in j['linkedFixtureSources']['sourceFiles'].items():
  matches=sorted([(len(root),label+'/'+name[len(root)+1:]) for root,label in roots if name.startswith(root+'/')],reverse=True)
  require(bool(matches),'linked fixture root '+name)
  if matches:
   selected=matches[0][1];require(selected not in result,'unique normalized fixture');result[selected]=digest
 return result
left,right=normalized(full),normalized(ci);require(left==right,'normalized linked fixture equivalence');equivalence['normalizedLinkedFixtures']=len(right)
for key in ['daprCli','daprRuntime','linuxAmd64DaprdSha256','runtimeSha256','pinSha256']:require(full['qualifiedToolchain'][key]==ci['qualifiedToolchain'][key],key+' pin')
for key in ['archiveSha256','manifestSha256','sourceFiles']:require(full['retainedHostSource'][key]==ci['retainedHostSource'][key],'retained '+key)
sys.dont_write_bytecode=True;sys.path.insert(0,'/home/administrator/projects/hexalith/works/references/Hexalith.Platform/eng');import verify_works_host
rebound=verify_works_host.bind_persisted_outcomes(g/'persisted-states.jsonl',copy.deepcopy(ci['rows']),ci['actualStarts']);require(rebound==ci['rollback'],'all owning proof predicates/cross-phase rebind')
matrix=json.loads(Path('/tmp/story49-matrix-parent-audit.json').read_text())
for row in matrix['rows']:
 row['hostedExecutedCoverage']={}
 for name,local in row['localExecutedCoverage'].items():
  qualified=local['type']+'.'+name;require(qualified in passed_methods,'matrix hosted fact '+qualified);row['hostedExecutedCoverage'][name]={'xml':passed_methods.get(qualified),'result':'Pass'}
matrix['status']='all four frozen rows covered by executed passing local full and hosted CI facts';Path('/tmp/story49-matrix-parent-audit.json').write_text(json.dumps(matrix,indent=2)+'\n')
packet=Path('/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/evidence/story-4-9/ci-build-hosted-qualification-2026-10-26');manifest=json.loads((packet/'manifest.json').read_text());native=0
for name,item in manifest['files'].items():
 data=(packet/name).read_bytes();require(len(data)==item['bytes'] and hashlib.sha256(data).hexdigest()==item['sha256'],name+' packed bytes')
 if 'sourceSha256' in item:
  native+=1;raw=gzip.decompress(data) if item.get('compression')=='gzip' else data;require(len(raw)==item['sourceBytes'] and hashlib.sha256(raw).hexdigest()==item['sourceSha256'],name+' decoded source');source=Path(item['source']);require(source.is_file() and hashlib.sha256(source.read_bytes()).hexdigest()==item['sourceSha256'],name+' native source')
report={'status':'passed' if not errors else 'failed','errors':errors,'ciRun':40407181685,'rootCommit':'09cc8e19c23ec15a0542499892bbf5cd4b8f38dd','profile':'ci Release-only','canonicalFacts':sum(xml_counts.values()),'xml':xml_counts,'rows':11,'proofs':44,'phases':len(phases),'fleetStarts':len(ci['actualStarts']),'rollbackComplete':ci['rollback']['complete'],'sourceEquivalence':equivalence,'frozenMatrixRows':4,'manifestEntries':len(manifest['files']),'nativeBindings':native,'manifestSha256':hashlib.sha256((packet/'manifest.json').read_bytes()).hexdigest(),'limit':'CI binaries are bound by executed owning final closure and uploaded inventory/start records; they are not uploaded for local rehash.'}
Path('/tmp/story49-hosted-parent-audit.json').write_text(json.dumps(report,indent=2)+'\n');print(report);raise SystemExit(bool(errors))
