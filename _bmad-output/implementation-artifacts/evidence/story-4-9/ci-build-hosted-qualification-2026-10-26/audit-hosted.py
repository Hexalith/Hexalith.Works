"""Read-only hosted artifact audit using the unchanged frozen owning predicates."""
import copy,hashlib,importlib.util,json,sys,xml.etree.ElementTree as E
from pathlib import Path
ROOT=Path(__file__).resolve().parent;LOCAL=Path('/tmp/story49-final-20261026T195205Z/full2');artifact=ROOT/'artifacts'/sys.argv[1]/'extracted';ledger_path=next(artifact.rglob('ledger.json'));G=ledger_path.parent;A=G.parent
h=json.loads(ledger_path.read_text());local=json.loads((LOCAL/'gate/ledger.json').read_text());hb=json.loads((A/'bootstrap.json').read_text());lb=json.loads((LOCAL/'bootstrap.json').read_text())
sys.path.insert(0,'/tmp/story49-final-20261026T195205Z/full1/workspace/platform/eng');import verify_works_host
spec=importlib.util.spec_from_file_location('hosted_checked_coordinator','/tmp/story49-final-20261026T195205Z/works/scripts/verify-platform-parity-ci.py');coordinator=importlib.util.module_from_spec(spec);spec.loader.exec_module(coordinator)
assert h['verifierSha256']==local['verifierSha256'] and h['verifierHelpers']==local['verifierHelpers']
coordinator.require_complete_gate(A,local['qualifiedToolchain']['linuxAmd64DaprdSha256'])
assert h['selectedSdk']['version']=='3.128.0';assert h['runtimeProducerQualification']['repositoryCommit']=='c5d1c280d8c1c1a207cd159d817f614e93c93130';assert hb['sdkCommit']=='c5d1c280d8c1c1a207cd159d817f614e93c93130'
assert h['canonicalSources']['Works']['commit']=='09cc8e19c23ec15a0542499892bbf5cd4b8f38dd';assert h['canonicalSources']['Platform']['commit']=='73bebc0a336ebf68cee77ab0114d96dde66c386d';assert hb['platformCommit']=='73bebc0a336ebf68cee77ab0114d96dde66c386d'
equivalence={}
for name in ['Works','Platform']:
 a,b=local['canonicalSources'][name],h['canonicalSources'][name];same=a['sourceFiles']==b['sourceFiles'];equivalence[name]={'sameSourceFiles':same,'fileCount':len(a['sourceFiles']),'localCommit':a['commit'],'hostedCommit':b['commit'],'sameCommit':a['commit']==b['commit'],'onlyLocal':sorted(a['sourceFiles'].keys()-b['sourceFiles'].keys()),'onlyHosted':sorted(b['sourceFiles'].keys()-a['sourceFiles'].keys()),'changed':[p for p in a['sourceFiles'].keys()&b['sourceFiles'].keys() if a['sourceFiles'][p]!=b['sourceFiles'][p]]};assert same,equivalence[name]
for name in ['fixtureSources','platformConsumerSources','verifierHelpers']:
 assert h[name]==local[name],name;equivalence[name]={'sameFiles':True,'files':len(h[name])}
assert hb['coordinatorSha256']==lb['coordinatorSha256'];equivalence['coordinators']={'sameFiles':True,'files':len(hb['coordinatorSha256'])}
def linked_normalized(l,b):
 roots=[(b['platformRoot'],'Platform'),(b['worksRoot'],'Works'),(b['sdkRoot'],'SDK'),(b['platformOrigin'],'PlatformOrigin'),(l['canonicalSources']['Platform']['root'],'PlatformOrigin'),(l['linkedFixtureSources']['compositionRoot'],'Platform')];roots.sort(key=lambda x:len(x[0]),reverse=True);result={}
 for name,sha in l['linkedFixtureSources']['sourceFiles'].items():
  for prefix,label in roots:
   try:relative=Path(name).relative_to(prefix)
   except ValueError:continue
   key=label+'/'+str(relative);assert key not in result or result[key]==sha;result[key]=sha;break
  else:raise ValueError('Unmapped linked input path: '+name)
 return result
ln,hn=linked_normalized(local,lb),linked_normalized(h,hb);assert ln==hn;equivalence['linkedFixtureSources']={'sameRelativePathsAndHashes':True,'files':len(hn)}
for name in ['daprCli','daprRuntime','linuxAmd64DaprdSha256','catalogSha256','runtimeSha256','pinSha256']:assert h['qualifiedToolchain'][name]==local['qualifiedToolchain'][name],name
for name in ['repositoryCommit','sourceInventorySha256','matchesPublishedProducer','qualification','runtimeFileCount']:assert h['runtimeProducerQualification'][name]==local['runtimeProducerQualification'][name],name
for name in ['archiveSha256','manifestSha256','baselineCommit','sourceFiles']:assert h['retainedHostSource'][name]==local['retainedHostSource'][name],name
xml={};passing=set()
for p in sorted(G.glob('*.xml')):
 r=E.parse(p).getroot();assemblies=list(r.iter('assembly'));counts={k:sum(int(a.get(k,'0')) for a in assemblies) for k in ['total','passed','failed','errors','skipped','not-run']};assert counts['total']==counts['passed']>0 and all(counts[k]==0 for k in ['failed','errors','skipped','not-run']);xml[p.name]=counts;passing.update(t.get('name') for t in r.iter('test') if t.get('result')=='Pass')
ordinary=json.loads((ROOT/'ordinary-summary.json').read_text());excluded=[x['testName'] for a in ordinary.values() for x in a['nonPassed']];assert len(excluded)==7;coverage={x:x in passing for x in excluded};assert all(coverage.values())
starts=[json.loads(x) for x in (G/'host-starts.jsonl').read_text().splitlines()];rebound_rows=copy.deepcopy(h['rows']);rollback=verify_works_host.bind_persisted_outcomes(G/'persisted-states.jsonl',rebound_rows,starts);assert rollback==h['rollback'];assert all(rebound_rows[k]['requiredProofs']==h['rows'][k]['requiredProofs'] for k in rebound_rows)
# Uploaded native inventories bind the remote files; raw runtime binaries are not uploaded.
def check_starts(path,preflight=False):
 records=[json.loads(x) for x in path.read_text().splitlines()]
 if preflight:assert len(records)==1 and records[0]['role']=='retained-rollback'
 for s in records:
  assert s['controlPlanePorts']==h['controlPlanePorts'] and s['dependencyBuildsSuppressed'] is True
  expected=h['host'] if s['role']=='platform-target' else {'assembly':h['retainedHostSource']['assembly'],'sha256':h['retainedHostSource']['assemblySha256']};assert s['assembly']==expected['assembly'] and s['sha256'].lower()==expected['sha256'].lower()
  assert (s['worksReadinessExpectation']=='Healthy' and s['readinessPassed'] is True) or (s['worksReadinessExpectation']=='Degraded' and s['readinessPassed'] is False)
  for m in s['runtimeModules']:
   assert m['configuration']=='Release' and h['runtimeArtifacts'][m['entry']].lower()==m['entrySha256'].lower()
   for sdk in m['sdkAssemblies']:assert sdk['version']=='3.128.0.0' and h['runtimeArtifacts'][sdk['path']].lower()==sdk['sha256'].lower()
  for sdk in s['sdkAssemblies']:assert sdk['version']=='3.128.0.0' and h['consumer']['artifacts'][sdk['path']].lower()==sdk['sha256'].lower()
 return records
check_starts(G/'host-starts.jsonl');preflight=check_starts(G/'retained-preflight-starts.jsonl',True)
launch_checks={}
for name,records in [('isolated-daprd',starts),('isolated-daprd-retained-preflight',preflight)]:
 d=G/name;b=json.loads((d/'binding.json').read_text());assert b['runtimeSha256']==h['qualifiedToolchain']['runtimeSha256'];launches=[json.loads(x) for x in (d/'launches.jsonl').read_text().splitlines()];observed=set()
 for l in launches:
  assert all(l.get(k)==v for k,v in b.items());assert l['originalSentryAddress'] in ('localhost:50001',b['sentryAddress']);observed.add((l['appId'],l['config'],l['configSha256']))
 required={(x['appId'],x['config'],x['configSha256']) for r in records for x in r['sidecarLaunches']};assert observed==required and required;launch_checks[name]={'launches':len(launches),'configurations':len(required),'binding':b}
summary={'status':'hosted artifacts accepted by unchanged coordinator and rebound owning predicates','runId':40407181685,'worksCommit':'09cc8e19c23ec15a0542499892bbf5cd4b8f38dd','platformCommit':'73bebc0a336ebf68cee77ab0114d96dde66c386d','profile':h['profile'],'xml':xml,'canonicalExecutedPasses':sum(x['passed'] for x in xml.values()),'rows':len(h['rows']),'proofs':sum(len(x['requiredProofs']) for x in h['rows'].values()),'proofRecordHashesAndCrossPhasePredicatesRebound':True,'rollbackComplete':rollback['complete'],'phases':len((G/'persisted-states.jsonl').read_text().splitlines()),'fleetStarts':len(starts),'runtimeArtifacts':len(h['runtimeArtifacts']),'consumerArtifacts':len(h['consumer']['artifacts']),'ordinaryExclusionCoverage':coverage,'inputEquivalence':equivalence,'toolchainPinEquivalent':True,'publicRuntimeProducerEquivalent':True,'retainedArchiveEquivalent':True,'launchChecks':launch_checks,'verificationLimit':'Remote runtime binaries are bound by native owning inventories/closure and uploaded start records; binaries are not rehashed locally because CI does not upload them. Source files and required XML/proof/binding bytes are independently compared/rebound.','rootCauseClaim':'No local Docker Desktop historical cause is inferred from hosted Linux success.'}
(ROOT/'hosted-acceptance.json').write_text(json.dumps(summary,indent=2)+'\n');print(json.dumps({k:v for k,v in summary.items() if k not in ['inputEquivalence','launchChecks','ordinaryExclusionCoverage']},indent=2))
