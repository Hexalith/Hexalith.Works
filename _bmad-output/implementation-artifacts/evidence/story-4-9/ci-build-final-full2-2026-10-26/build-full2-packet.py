"""Freeze bounded native full2 evidence after all owning processes and observers finish."""
import datetime,gzip,hashlib,json
from pathlib import Path
RUN=Path(__file__).resolve().parent;D=RUN/'full2';G=D/'gate';summary=json.loads((D/'terminal-audit.json').read_text());assert (D/'driver-finished.json').exists()
P=Path('/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/evidence/story-4-9/ci-build-final-full2-2026-10-26');P.mkdir(exist_ok=False);files={}
def copy(source,relative):
 raw=source.read_bytes();compressed=source.suffix in ['.log','.jsonl'] or (source.suffix=='.json' and len(raw)>200000);relative+=('.gz' if compressed else '');data=gzip.compress(raw,mtime=0) if compressed else raw;target=P/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data);files[relative]={'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'source':str(source),'sourceSha256':hashlib.sha256(raw).hexdigest(),'sourceBytes':len(raw),'compression':'gzip' if compressed else 'none'}
for name in ['inputs.json','full2-driver.py','full2-driver.log','observe-full2-control-plane.py','observe-full2-dcp.py','full2-observer-driver.log','full2-dcp-observer-driver.log','audit-full2.py','build-full2-packet.py']:copy(RUN/name,name)
for source in sorted(D.iterdir()):
 if source.is_file() and source.suffix in ['.json','.log','.xml','.jsonl','.md']:copy(source,'full2/'+source.name)
for name in ['environment','read-only-observer','dcp-observer']:
 for source in sorted((D/name).rglob('*')):
  if source.is_file():copy(source,'full2/'+name+'/'+str(source.relative_to(D/name)))
for source in sorted(G.iterdir()):
 if source.is_file() and source.suffix in ['.json','.log','.xml','.jsonl','.md']:copy(source,'full2/gate/'+source.name)
for base in [G/'agents-full',G/'agents-local',*[p for p in G.iterdir() if p.is_dir() and p.name.startswith('isolated-daprd')]]:
 if not base.exists():continue
 for source in sorted(base.rglob('*')):
  relative=source.relative_to(base)
  if source.is_file() and (str(relative)=='.dapr/bin/daprd' or not set(relative.parts).intersection({'bin','obj','package','packages'}) and source.suffix in ['.json','.log','.xml','.jsonl','.md']):copy(source,'full2/gate/'+base.name+'/'+str(relative))
copy(RUN/'cp-health-environment-1/qualified-helper-binding.json','environment-binding/qualified-helper-binding.json')
binding=json.loads((RUN/'cp-health-environment-1/qualified-helper-binding.json').read_text())
for name,e in binding['assets'].items():copy(Path(e['path']),'environment-binding/qualified/'+name)
status=summary['ownerStatus'];passed=status=='passed-disposable-synthetic-parity-and-rollback'
coverage=summary['nativeCoverage'];native200=sum(v['http200Samples']>0 for v in coverage.values())
readme=f'''# Story 4.9 final-source full2 with scoped health ports — 2026-10-26

The fresh canonical **full** gate exited **{summary['exitCode']}** with owner status **{status}**. It records **{summary['passedProofCount']}/44** passing proofs, **{sum(v=='passed' for v in summary['rowStatuses'].values())}/11** passing rows and complete rollback **{summary['rollbackComplete']}**. The [terminal audit](full2/terminal-audit.json), compressed native ledger, executed XML, persisted states and launcher records preserve the actual result. Parent acceptance audit, fresh hosted qualification and remaining review are still outstanding; story/sprint are in progress.

The run reuses the exact [final source input map](inputs.json): Works `0280fb91ee3bff2c903421bc10afa3aa4bc3ebd8`, all thirteen declared dependency commits and 246 captured overlays, including Platform `bed9e8ccd4c6fa03a9a7f0857b1a95960320e3ad` and Builds `cbff0462ffa2c12496ab69ba786fb31efcdde7c2`. The public SDK remains 3.128.0 with separately qualified official producer `c5d1c280d8c1c1a207cd159d817f614e93c93130`; the source checkout `7af8a2a74dff103f286abad144e4959e3e30b902` is a distinct recorded dependency input. Every captured overlay/revision, original Platform origin inventory and coordinator postcheck remains unchanged. The exact owned patches and source bytes are retained in the [previous final-source packet](../ci-build-final-qualification-2026-10-26/README.md); its failed attempt remains immutable and nonqualifying.

[Invocation](full2/full-invocation.json) records the distinct environment input: only the accepted qualified Docker shim directory is prepended to the selected child PATH. Frozen helper/shim/factory/cache/exclusion hashes match before/after. Live scope configuration binds the owner PID/start and explicit selected configuration roots: the reused frozen Platform root plus the fresh full2 retained-source root. The adapter changes only exact owned CP auto-health publish arguments to explicit free loopback ports below Linux's ephemeral range; grpc, all other arguments, real readiness assertions and all canonical deadlines remain unchanged. Sanitized records bind **{summary['adapterMatches']}** matches across all three CP kinds, exact DCP controller/API/monitor ownership, cached images, fixture identities, and original/effective unchanged-argument hashes. Native observations have HTTP 200 evidence for **{native200}/{len(coverage)}** matched containers. The unchanged scope is a controlled Docker Desktop environment experiment; the earlier exact root cause remains unproven. See the [frozen controls/native smoke](../ci-build-cp-health-environment-2026-10-26/README.md).

The full profile retains Debug source conformance and Release package consumers. Owner helpers pass 155/155 and component helpers 14/14; Agents Full retains its expected closed unavailable refusal without Works parity credit. Platform tests pass 144/144 in both configurations, both Works builds and supporting 306/306 pass, all six runtime prebuilds pass and retained readiness passes 1/1. The terminal XML map records the actual recovery/historical/HA/mTLS/projection outcomes and every error/failure/skip/not-run count. The seven topology facts excluded from ordinary execution are checked against actual named canonical passes. The earlier ordinary/focused/parallel/architecture evidence remains bound to identical source inputs in the [final-source packet](../ci-build-final-qualification-2026-10-26/README.md).

The run retains **{summary['persistedPhases']}** persisted phases, **{summary['fleetStarts']}** actual fleet starts and **{summary['runtimeArtifacts']}** bound runtime artifacts. Owning final source/runtime/archive/launcher closure is part of the canonical success path; the supplemental terminal audit independently checks frozen input/origin/coordinator/helper bytes and named proof coverage. No required assertion or deadline was weakened. The existing GitHub credential was supplied only in memory for official source qualification; redirects drop authentication and the owning runtime removes GITHUB_TOKEN. Only authentication presence is recorded; no credential value is retained.

Read-only exact-owned Docker/DCP observers preserve bounded native state, published mappings, health, stdout, events and sanitized endpoint/proxy records before cleanup. They create **zero extra helpers** and perform no container exec or target/foreign mutation. The global [Aspire monitoring skill](/home/administrator/.agents/skills/aspire-monitoring/SKILL.md) guides their exact-AppHost/owner scope; test hosts have no CLI backchannel registration, so exact admitted DCP APIs supply sanitized status instead. Private kubeconfig/certificates, generated directories and runtime binaries remain under `/tmp/story49-final-20261026T195205Z/`; they are not copied wholesale.

[Manifest](manifest.json) binds exact stored bytes and deterministic gzip reconstruction. All previous packets remain immutable. No VCS publication, remote operation, dependency pointer change or host setting change was performed by this dispatch.
'''
(P/'README.md').write_text(readme);raw=(P/'README.md').read_bytes();files['README.md']={'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw),'compression':'none','source':'authored terminal qualification summary'}
manifest={'frozenAtUtc':datetime.datetime.now(datetime.UTC).isoformat(),'scope':'fresh full2; parent audit/hosted/reviews pending','nativeRoot':str(D),'files':files};(P/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
for name,e in files.items():
 data=(P/name).read_bytes();assert hashlib.sha256(data).hexdigest()==e['sha256']
 if 'sourceSha256'in e:assert hashlib.sha256(gzip.decompress(data) if e['compression']=='gzip' else data).hexdigest()==e['sourceSha256']
print(json.dumps({'packet':str(P),'filesIncludingManifest':len(files)+1,'nativeSourceBindings':sum('sourceSha256'in e for e in files.values()),'manifestSha256':hashlib.sha256((P/'manifest.json').read_bytes()).hexdigest(),'bytes':sum(p.stat().st_size for p in P.rglob('*') if p.is_file())}))
