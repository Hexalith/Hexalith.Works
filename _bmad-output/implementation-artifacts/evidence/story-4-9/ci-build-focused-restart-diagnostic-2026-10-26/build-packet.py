import datetime,gzip,hashlib,json
from pathlib import Path
D=Path(__file__).resolve().parent;RUN=D.parent;W=Path('/home/administrator/projects/hexalith/works')
P=W/'_bmad-output/implementation-artifacts/evidence/story-4-9/ci-build-focused-restart-diagnostic-2026-10-26';P.mkdir(exist_ok=False);files={}
def copy(source,relative):
 raw=source.read_bytes();compress=source.suffix in ['.log','.jsonl'];relative+=('.gz' if compress else '');data=gzip.compress(raw,mtime=0) if compress else raw
 target=P/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data);files[relative]={'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'source':str(source),'sourceSha256':hashlib.sha256(raw).hexdigest(),'sourceBytes':len(raw),'compression':'gzip' if compress else 'none'}
for source in sorted(D.iterdir()):
 if source.is_file() and source.suffix in ['.json','.xml','.log','.py']:copy(source,source.name)
for folder in ['control-plane-observer','dcp-observer']:
 for source in sorted((D/folder).iterdir()):
  if source.is_file():copy(source,folder+'/'+source.name)
for relative in ['binding.json','.dapr/bin/daprd','launches.jsonl']:copy(D/'isolated-daprd'/relative,'isolated-daprd/'+relative)
for name in ['prepare-focus.py','prepare-focus-argument-refused.py','focused-preparation.log','focused-preparation-argument-refused.log']:copy(RUN/name,'preparation/'+name)
copy(RUN/'inputs.json','captured-inputs.json')
readme='''# Story 4.9 focused restart diagnostic — 2026-10-26

The unchanged `SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart` fact passed **1/1**, zero failures/errors/skips/not-run cases, in **694.330 seconds**. It recorded 20 persisted phases and five successful fleet starts, including its restore section. This is a focused diagnostic with **zero complete/full acceptance credit**. The separate [starting-revision](../ci-build-corrections-2026-10-26/README.md) and [current-input](../ci-build-current-inputs-2026-10-26/README.md) full failures remain nonqualifying; their ledgers and proof status are not relabeled.

[Command](command.json) and [result](finished.json) bind the exact selected fact, captured source roots, 1,800-second command budget and unchanged 180-second canonical startup setting. Before and after inventories bind **909 runtime/consumer/launcher files**, all byte-identical; the owning strict source/runtime/fixture/archive/snapshot/port-range recheck passed both before and after. The isolated launcher and binding were copied without changing their bytes so diagnostic launch records use a fresh directory. Only evidence-output paths and single-fact selection differ. The initial preparation wrapper supplied the wrong collection type to the owner's reserved-range API; that refusal and corrected wrapper are retained. Actual host ranges did not change and no owning check was weakened.

The global [Aspire monitoring skill](/home/administrator/.agents/skills/aspire-monitoring/SKILL.md) guided read-only inspection. Exact-file `aspire describe` reported that the testing host has no CLI backchannel registration. The observer instead used the exact admitted DCP creator process's `--kubeconfig` to access only its loopback API, checked container resource UIDs, and saved sanitized state, endpoint and proxy resolution. No kubeconfig/client credentials, certificate contents, container environments or DCP effective environment values are copied here.

[Diagnostic summary](diagnostic-summary.json) retains 13 unique control-plane container admissions (five Sentry, four Placement, four Scheduler), 312 native state/port/health observations and 78 sanitized endpoint/status snapshots from four exact owned DCP APIs. Sentry admission requires the exact run-owned read-only config mount. Placement/Scheduler additionally require the same exact credential mount and matching DCP creator PID/start/group labels. Image/name alone never admits a target. Bounded native stdout and container-filtered Docker events were captured before cleanup.

The targets lacked native HTTP tools. Internal checks used the exact cached Redis/alpine image ID verified from this fact's owned Redis fixture, only its network namespace, no host or target filesystem mounts, a read-only helper root, dropped capabilities and no privileged mode. Docker's image-created anonymous `/data` scratch volumes were explicitly authorized. All **56** unique helper containers and their anonymous volumes were verified absent, with no active helper owner labels remaining. The initial periodic observer produced 55 helper attempts before a late rate-policy handoff; the replacement reused previous baselines and made one new baseline, thereafter permitting only health transitions or explicit diagnostic need. Both helper versions, scopes and transition counts are preserved. Future qualifying full runs must use **no extra helper containers**.

There were 55 internal HTTP 200 responses, 310 published HTTP 200 responses, and 216 localhost DCP proxy HTTP 200 responses. IPv6-only proxy connections refused while localhost/IPv4 worked; this alone is not a cause. One published Scheduler connection reset was immediately followed by Docker reporting that the exact target had exited when a helper tried to join its namespace. That sample produced no canonical startup failure and is not evidence for the earlier Placement failure.

**Remaining evidence gap:** the startup failure did not recur. The original failed Placement interval still lacks simultaneous internal, Docker-published and Aspire/DCP-proxy health samples. These passing observations cannot attribute that earlier failure to server readiness, port forwarding, a proxy, quota, or teardown. The parent is independently correcting the real root build/CI issues; those later source changes are outside this captured diagnostic input set and receive no qualification from this result.

[Manifest](manifest.json) binds exact stored bytes and deterministic gzip reconstruction. Private/generated originals, cached API material and runtime binaries remain under `/tmp/story49-group2-current-20261026T182217Z/`; no native directory is copied wholesale. No canonical source, runtime, launcher byte, assertion, proof deadline, host limit, target filesystem or foreign resource changed. Story/sprint remain in progress pending fresh complete final-input full/hosted qualification and review.
'''
(P/'README.md').write_text(readme);files['README.md']={'sha256':hashlib.sha256((P/'README.md').read_bytes()).hexdigest(),'bytes':(P/'README.md').stat().st_size,'compression':'none','source':'authored diagnostic summary'}
(P/'manifest.json').write_text(json.dumps({'createdAtUtc':datetime.datetime.now(datetime.UTC).isoformat(),'scope':'focused unchanged fact diagnostic; zero complete/full acceptance credit','nativeRoot':str(D),'files':files},indent=2)+'\n')
for name,x in files.items():
 data=(P/name).read_bytes();assert hashlib.sha256(data).hexdigest()==x['sha256']
 if x.get('compression')=='gzip':assert hashlib.sha256(gzip.decompress(data)).hexdigest()==x['sourceSha256']
print(json.dumps({'packet':str(P),'files':len(files),'bytes':sum(p.stat().st_size for p in P.rglob('*') if p.is_file())}))
