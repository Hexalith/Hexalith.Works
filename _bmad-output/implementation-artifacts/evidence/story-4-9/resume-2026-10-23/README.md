# Story 4.9 prerequisite recheck — 2026-10-23

The implementation remains present, and qualification remains blocked. Fresh official NuGet indexes still end at **3.126.1** for Client, DomainService and Aspire. All three selected public archives identify producer `1748b16b11fb63b885dfa2fd4a9c7a420ea103a9`, which excludes the retained-seal fix. Platform's unchanged `qualify_runtime_source` refuses the current EventStore checkout `2361e7b6760a7d0065c67661c2e95c2af94bdb4d`, including the three retained-seal implementation files among its source differences.

The two unchecked tasks require fresh complete qualification. Their implementation already has the explicit Release/package CI profile and blocking Works entry point, plus the disposable Redis fixture's bounded 60-second readiness budget with real host TCP/PING, cancellation and cleanup. This attempt changes only the verification record and this diagnostic packet.

| Executed check | Result |
| --- | --- |
| Official package indexes and selected archive metadata | Latest stable 3.126.1; all three archives identify the same old producer |
| Unchanged canonical source/producer qualifier | Exit 1: `ValueError: Runtime SDK differs from published producer 1748b16b11fb63b885dfa2fd4a9c7a420ea103a9: ...` |
| Works tooling regression suite | 53 passed |
| Actual Works CI entry point with `--bootstrap-only` | Exit 0; bootstrap validated, parity incomplete |
| Platform verifier, producer, snapshot, retained-host and toolchain contracts in the bootstrap's isolated layout | 102 passed |

The first two direct-layout Platform contract runs each failed three of 102 tests because the fixture reached the intentionally uninitialized nested Builds catalog. An environment override does not alter that hard-coded path. Both failures are retained. The successful retry uses the existing bootstrap's exported Platform tree and links to already initialized root dependencies; no nested submodule was initialized, no owner source or test assertion changed, and no live command ran. The contract tests simulate their gate lanes; their output is not runtime parity evidence.

From the Works root, replay the public prerequisite check into a new output directory:

```bash
python3 _bmad-output/implementation-artifacts/evidence/story-4-9/resume-2026-10-23/probe-public-producer.py \
  --works-root /home/administrator/projects/hexalith/works \
  --output-dir /tmp/story49-public-producer-replay
```

The recorded invocation, exact output, exit code and timing are in `commands.json` and `raw/public-producer-probe.log.gz`. The probe downloads official index responses and selected archive metadata, then invokes the canonical Platform helper with official commit/tag/tree responses. A refusal returns exit 1. It does not build, launch a fleet, or qualify consumer assemblies. `raw/public-producer/` preserves the exact responses, nuspecs, package digests, source snapshot and refusal. `source-inputs.json` binds the current relevant sources and full local revisions; `manifest.json` binds every packet file and both compressed and original bytes.

Story and sprint remain `in-progress`; both complete-qualification tasks remain unchecked. No new full profile, actual CI-profile parity, live rollback, hosted CI or production acceptance is claimed. Completion still requires a public EventStore producer containing the fix, authorized adoption of that producer, then the unchanged full gate followed by the actual CI profile against unchanged inputs. Package pins, frozen intent, `baseline_commit`, historical evidence and production admission policy are preserved.
