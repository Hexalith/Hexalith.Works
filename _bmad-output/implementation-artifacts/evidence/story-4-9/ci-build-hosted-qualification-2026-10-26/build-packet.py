import datetime,gzip,hashlib,json
from pathlib import Path
R=Path(__file__).resolve().parent;accept=json.loads((R/'hosted-acceptance.json').read_text());latest=json.loads((R/'latest.json').read_text());assert latest['ci']['status']=='completed' and latest['ci']['conclusion']=='success'
P=Path('/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/evidence/story-4-9/ci-build-hosted-qualification-2026-10-26');P.mkdir(exist_ok=False);files={}
def copy(source,relative):
 raw=source.read_bytes();compress=source.suffix in ['.log','.jsonl','.trx'] or source.suffix=='.json' and len(raw)>200000;relative+=('.gz' if compress else '');data=gzip.compress(raw,mtime=0) if compress else raw;target=P/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data);files[relative]={'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'source':str(source),'sourceSha256':hashlib.sha256(raw).hexdigest(),'sourceBytes':len(raw),'compression':'gzip' if compress else 'none'}
for source in sorted(R.iterdir()):
 if source.is_file() and source.suffix in ['.json','.py','.jsonl','.log']:copy(source,source.name)
for source in sorted((R/'completed-job-logs').glob('*.log')):copy(source,'completed-job-logs/'+source.name)
for base in sorted((R/'artifacts').iterdir()):
 for source in sorted(base.rglob('*')):
  if source.is_file():copy(source,'artifacts/'+base.name+'/'+str(source.relative_to(base)))
for base in sorted((R/'run-logs').iterdir()):
 for source in sorted(base.iterdir()):
  if source.is_file():copy(source,'run-logs/'+base.name+'/'+source.name)
# Retain the final original API responses; intermediate polls stay native/private.
final=R/'polls'/latest['at']
for source in final.iterdir():copy(source,'final-api/'+source.name)
artifacts=[]
for d in (R/'artifacts').iterdir():
 q=json.loads((d/'download-and-extraction.json').read_text());artifacts.append({'id':q['artifactId'],'name':q['name'],'sha256':q['zipSha256'],'bytes':q['zipBytes']})
(R/'artifact-digests.json').write_text(json.dumps(artifacts,indent=2)+'\n');copy(R/'artifact-digests.json','artifact-digests.json')
text=f'''# Story 4.9 fresh hosted qualification — 2026-10-26

[Push CI 40407181685](https://github.com/Hexalith/Hexalith.Works/actions/runs/40407181685) passed on Works **`09cc8e19c23ec15a0542499892bbf5cd4b8f38dd`**, consuming Platform **`73bebc0a336ebf68cee77ab0114d96dde66c386d`**. Ordinary build/tests, lint, toolchain and canonical Platform parity are green. [Commitlint 40407181557](https://github.com/Hexalith/Hexalith.Works/actions/runs/40407181557) and [CodeQL 40407181599](https://github.com/Hexalith/Hexalith.Works/actions/runs/40407181599) are also successful for the same head. Configured optional performance/Aspire jobs and alternate/governed workflow steps are recorded as skipped; no required canonical case is skipped.

The [hosted audit](hosted-acceptance.json) accepts the artifact using the unchanged frozen coordinator and owning proof predicates. CI selects **Release only**, with Agents Release and no Debug check. All **11 rows**, **44 proofs**, complete rollback, required final closure and all **{accept['canonicalExecutedPasses']}** canonical executed XML facts pass, with zero failures/errors/skips/not-run. The exact 32 persisted phases and eleven fleet starts rebind every evidence SHA and cross-phase predicate. Native start inventories, loaded SDK versions and isolated-launch records bind the selected outputs and runtime. The public SDK is **3.128.0**, its official producer is **`c5d1c280d8c1c1a207cd159d817f614e93c93130`**, and the Platform-owned CLI/runtime/catalog/pin hashes match local full2.

Ordinary TRX independently records **597 unit**, **398 architecture**, **3 property** and **1,023 integration** passes with zero failures/errors. Exactly seven explicit topology facts are NotRunnable in ordinary execution; all seven names are matched to executed passing canonical facts. Hosted build logs show zero warnings/errors and exactly one compilation output each for Platform.Custody, Platform.Works.Runtime and Builds.Tooling, with no MSB3026 collision.

Revisions differ truthfully: [local full2](../ci-build-final-full2-2026-10-26/README.md) used Works `0280fb91ee3bff2c903421bc10afa3aa4bc3ebd8` and Platform `bed9e8ccd4c6fa03a9a7f0857b1a95960320e3ad` plus exact working overlays. Parent publication created Platform `73bebc0a336ebf68cee77ab0114d96dde66c386d`, Works pointer commit `352729a12d2551e404886b350708343bc761f0f5` and core/main `09cc8e19c23ec15a0542499892bbf5cd4b8f38dd`. Consumed canonical source, Platform consumer, fixture, normalized linked-fixture, verifier/helper and coordinator file hashes match full2 exactly. This is content equivalence across intentionally different revisions; no old origin-HEAD closure was applied to the moving published worktree. Standard hosted Linux ran its canonical profile without the local Docker Desktop PATH adapter. Hosted success does not establish the historical local forwarding failure's exact cause.

Original artifact ZIPs and run log ZIPs are preserved unchanged. [Artifact digests](artifact-digests.json), metadata and extraction maps bind every member; extraction refuses traversal, absolute paths, symlinks, duplicate normalized paths and excessive expanded size. The [manifest](manifest.json) binds all stored bytes and deterministic gzip reconstruction. Runtime binaries are not uploaded by CI: their remote native inventories/closing verifier are audited alongside start records, while source/XML/proof/binding bytes are independently compared or rebound. Private/generated native originals and intermediate polling responses remain under `{R}/`; no whole build directory or private API material is copied.

This dispatch performed read-only GitHub API/log/artifact operations only. No source-code change, staging, commit, push, workflow rerun/cancellation, dependency update or message to another service occurred. Story/sprint, Task 9 and the CI correction task remain in progress for parent acceptance and remaining workflow reviews. This packet records verification; parent owns final acceptance and review.
'''
(P/'README.md').write_text(text);raw=(P/'README.md').read_bytes();files['README.md']={'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw),'compression':'none','source':'authored hosted verification summary'}
m={'frozenAtUtc':datetime.datetime.now(datetime.UTC).isoformat(),'scope':'read-only hosted verification; parent acceptance/reviews pending','nativeRoot':str(R),'files':files};(P/'manifest.json').write_text(json.dumps(m,indent=2)+'\n')
for name,e in files.items():
 b=(P/name).read_bytes();assert hashlib.sha256(b).hexdigest()==e['sha256']
 if 'sourceSha256'in e:assert hashlib.sha256(gzip.decompress(b) if e['compression']=='gzip' else b).hexdigest()==e['sourceSha256']
print(json.dumps({'packet':str(P),'filesIncludingManifest':len(files)+1,'sourceBindings':sum('sourceSha256'in x for x in files.values()),'manifestSha256':hashlib.sha256((P/'manifest.json').read_bytes()).hexdigest(),'bytes':sum(p.stat().st_size for p in P.rglob('*') if p.is_file())}))
