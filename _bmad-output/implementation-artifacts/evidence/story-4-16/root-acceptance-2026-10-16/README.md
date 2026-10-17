# Story 4.16 root verification evidence

Story4.15 remains **accepted/done**. All270 preserved evidence files and both approved frozen specifications still match their recorded SHA-256 values. Story4.16 remains **in progress**; story4.9 has not resumed. [Story/sprint synchronization](story-sprint-synchronization.json) confirms the tracking file already agrees.

All nine official NuGet indexes still select **public EventStore3.124.0** at **21:46UTC on2026-10-16**, producer `0737c0befce5338e294a789092a0e223e8264a04`. [Final index receipts](public-3.124.0/latest-public-final.json), [nine official archives](public-3.124.0/public-producers.json), [official1,942-file producer qualification](public-3.124.0/official-tree-qualification.json), and [final canonical Release DLL/archive comparison](public-3.124.0/canonical-release-archive-identity-final.json) bind the public producer independently.

The **final canonical verifier exits1** after all preparation/supporting checks pass. Its exact command, working directory, logs, XML and ledger are retained in the [final admission result](canonical-admission/final/result.json):

```bash
cd /home/administrator/projects/hexalith/platform
bash eng/verify-works-host.sh --works-root /home/administrator/projects/hexalith/works --runtime-sdk-root /tmp/story416-sdk-3.124-source --artifacts-path /tmp/story416-final-public-admission-20261016-v2
```

```text
Runtime SDK differs from published producer 0737c0befce5338e294a789092a0e223e8264a04: src/Hexalith.EventStore.Server/Commands/TrustedEffectRouter.cs, src/Hexalith.EventStore.Server/Projections/ProjectionUpdateOrchestrator.LegacyReplay.cs
```

| Final check | Result | Evidence |
| --- | --- | --- |
| Canonical Platform source Debug/package Release | 92passed in each mode, zero skips | [Ledger](canonical-admission/final/ledger.json) |
| Canonical Works Debug/Release builds and supporting Release tests | Both builds pass;193passed, zero skips | [Result](canonical-admission/final/result.json) |
| Source/evidence admission guards | 18passed; modified source and mixed/missing configurations refuse | [Log](canonical-admission/final/evidence-gate-tests.log) |
| Approved Agents contract | Full exits1 with EXT-HOST-1; LocalScaffold exits0 | [Logs and exact outcomes](canonical-admission/final/result.json) |
| Prepared SDK corrections | 138passed, zero skips; public router regression6/6fails as expected | [Manifest](public-3.124.0/trusted-effect-activation-fix/manifest.json) |
| Actual Redis restore and mutation refusals | 15passed, zero skips | [Manifest](complete-fleet-restore-regression/manifest.json) |
| Captured component startup | 2passed, zero skips; existing bytes survive changed/missing retained templates | [Manifest](complete-fleet-restore-regression/component-startup/manifest.json) |
| Current architecture | 270passed, zero skips | [XML](story416-root-architecture-current.xml) |

The separately versioned unpublished `3.124.1-story416-gateway` candidate passes **all four fresh live facts**, zero failures/skips, with unchanged host/consumer/runtime bytes between facts. [Independent aggregate audit](public-3.124.0/candidate-runtime-preparation/final-v2-live-facts.json) binds every exact command and result.

| Candidate fact | Result | Evidence |
| --- | --- | --- |
| Complete recovery and retained-host rollback | 1passed,603.330s;19phases/five starts | [Audit](public-3.124.0/candidate-runtime-preparation/full8/root-audit.json) |
| Callback, scoped audit, telemetry and quarantine | 1passed,152.915s;five phases | [Audit](public-3.124.0/candidate-runtime-preparation/callback-v2/root-audit.json) |
| Historical lifecycle/tree/claim/query/security/restart | 1passed,131.962s;three phases/two starts | [Audit](public-3.124.0/candidate-runtime-preparation/historical-v2/root-audit.json) |
| Two-host HA and observed stale callback | 1passed,95.223s;primary loss, reminder removal, one resume | [Audit](public-3.124.0/candidate-runtime-preparation/ha-v2/root-audit.json) |

Complete candidate rollback restores677Redis keys, offline Scheduler contents,17configuration/authority inventory entries and1,211runtime binary entries. Captured configuration stays byte-identical after retained startup. Protected queries work; the original NoOp receipt replays; resume/cancellation/expiry remain single logical effects; all eleven process sources converge with zero unresolved work. The read-only observer independently verifies all four restored Debug runtime entries and mapped SDK Server hashes, with zero mismatches.

Two SDK producer gaps remain absent from public3.124: an active failed legacy lease blocks later named heads, and trusted-effect ingress omits durable target projection activation. The [reviewable two-fix patch](public-3.124.0/producer-correction-v2.patch) applies cleanly to the official producer, preserves the released constructor, and uses the existing SDK outbox. The [actual public R4 failure](public-3.124.0/public-r4-blocker.json) remains1failed/zero skips/283.125s: generation2 stays owner3/global3 rather than owner4/global14; one diagnostic dispatch advances it and never qualifies acceptance.

Earlier failed attempts remain archived. Full6 exposed unordered Redis DUMP serialization; full7 exposed the retained component comment overwrite. The real restore regressions and successful full8 resolve those candidate failures. The [first canonical attempt](canonical-admission/attempt1-version-conflict/manifest.json) exposed an arbitrary1.0source-version override; the verifier now uses the actual3.124producer version and both Works modes build cleanly.

A published corrected producer and fresh complete public R1–R11/rollback qualification remain required. All eleven canonical ledger rows stay open. The approved scope excludes external publication/deployment. Formal review remains gated by the [bmad-build skill](../../../../../.agents/skills/bmad-build/SKILL.md) and its rendered implementation instruction: “If any task is not done or any acceptance criterion is not satisfied, finish the missing work before proceeding.” No4.16acceptance or4.9cutover is claimed.

Owned resources are cleaned up. The temporary inotify increase was [restored to its original runtime limit](environment-capacity-final.json) after checking complete readable watch usage below that limit; no foreign process was stopped and no persistent kernel configuration was edited. [Final remaining gates](remaining-acceptance-gates.json) and [preservation check](accepted-415-preservation-current.json) retain the final state.
