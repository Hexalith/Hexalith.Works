# Story 4.15 final acceptance — published EventStore 3.123.0

All three acceptance criteria and all five approved matrix rows pass. The existing implementation, frozen intent, historical archives and earlier evidence are preserved. This packet supersedes pending final-review and current-publication statements in earlier dated notes; it does not relabel their results.

## Producer and consumer

All nine official NuGet indexes list **3.123.0** as latest. The independently downloaded archives share repository commit `f78916d9534a72ba1515d97cfd51408e6eb809f1`; every ordinary Release cache archive matches its official SHA-256. [Published producers](published-producers.json) records exact URLs, index/archive hashes and check time. [Dependency modes](release-dependency-mode.json) proves all nine are packages at exactly 3.123.0, while [Debug](debug-dependency-mode.json) proves all nine checked-out source projects. Their local source version remains 3.122.0; production source is the public release commit's source, not an older published package. The user's test-only Platform.Identity source edge remains in both modes.

All 1,945 production inputs and seven producer build configuration files match the prior candidate inputs. Between the candidate pack commit and published commit only three test fixture files changed, already exercised by the corrected 105-case trusted/security/snapshot lane. [Continuity](producer-continuity.json) also checks all 81 historical, nine preliminary and nine selected candidate archives unchanged. No package was packed or published by this final verification.

## Review and verification

The three independent review layers completed; the edge layer found no issue. All five direct corrections are resolved: final actor-catalog exclusivity, authenticated 204/202 disposition with durable audit ordering and real revalidation, heterogeneous await filtering, exact ABI history prerequisites, and XML-safe helper configuration. [Scoped review fixes](../review-fixes-2026-10-16/implementation.md) includes 101 passing cases and a meaningful filter-removal mutation failure followed by byte-identical restoration. Stable public and immutable prerelease helper paths both pass, including a checkout with XML-special path characters; see [public helper proof](../published-3.123.0-helper-2026-10-16/implementation.md).

| Final gate | Result |
| --- | --- |
| Source Debug integration | 791 passed; zero failures/skips |
| Public 3.123.0 Release integration | 791 passed; zero failures/skips |
| Architecture | 270 passed; zero failures/skips |
| Enabled Redis/Dapr callback and restart lane | 2 passed in 471.558 seconds; zero failures/skips |
| Exact live Dapr configuration copies | 8 passed; ACL scope preserved |
| Public Release complete-store restore | 27 projection checks plus 1 rollback check passed |
| SDK package-only cold host/API/ABI probes | Public 3.123.0 passed with six independently matched SDK archives |
| Works unchanged-binary ABI | All six original constructors/deconstructions and historical payload hashes passed |
| Final Debug/Release builds | Zero warnings/errors |

[Exact commands/results](results-manifest.json) retains all 15 final-round build/test logs, including corrected failures. [XML results](test-results.json) records every selected passing test and zero skips. The earlier SDK DomainService 974, trusted/security/snapshot 105, client 37, unit 589 and property 3 gates retain their original source/candidate scope in [resumed verification](../resume-2026-10-16/implementation.md). Their unchanged relevant production sources are bound separately; they are not represented as new public-package executions.

The first final live attempt passed callback and expiry replay but timed out observing a date witness scheduled only twenty seconds ahead. The second, with a ninety-second future window and an explicit assertion that deletion precedes due time, reached deletion then failed when a one-shot strict stream read returned 503. The final fixture uses its existing bounded complete-stream polling helper for that recovery observation: only explicit not-ready/unavailable 503 responses retry, all other errors fail, and exactly one accepted resume remains required. No runtime timeout, product assertion, authentication rule or production implementation was weakened. The earlier failures remain in the manifest; their precise root cause is not inferred beyond observed evidence. The fixture delta is [retained](retry-fixture.patch).

## Persisted acceptance

All ten actual final runtime phases are [redacted for review](persisted-states.json) and independently checked by [the executable audit](audit-live.py) against the [exact raw-state hash](root-runtime-check.json): callback membership/witness, delegated expiry target commit before checkpoint, expiry receipt replay, canonical missing-child degraded evidence without sibling cancellation, five retained cancellation effects, five single target events and Success receipts, replay then checkpoint 7, one parent resume with a retained NoOp receipt, complete coverage of eleven sources with zero unresolved work, and NoOp replay then checkpoint 4.

The [five-row matrix](matrix.json) binds 120 current passing Debug/Release test observations; the live lane supplies the persisted restart composition. These establish once-logical convergence, truthful discovery/readiness and audited same-validator recovery, and rejection of forbidden tenant/origin/purpose/semantics without mutation or receipt disclosure. The positive authorized HTTP cases also pin audit-before-mutation for both immediate and deferred disposition.

[Root audit](root-acceptance-audit.json) verifies 5,398 unchanged runtime source/assembly inputs; the minimized [consumer bindings](consumer-runtime-inputs.json) retain the reviewable subset. [Cleanup](cleanup.json) confirms no AppHost or owned synthetic Redis remains. The synthetic RSA authority, dedicated Redis and exact HotReload-only Dapr copies retain the approved test boundary.

## Status and remaining gates

4.15 is accepted and its story and sprint entry are synchronized to done. RB6-6's unpublished EventStore restore prerequisite is superseded by the verified public release; the review-time verdict remains historical. The earlier separate SDK deferred concerns remain recorded. Production data/authority/retention approval, Platform parity and host removal belong to their existing later gates. Proceed to 4.16, then resume 4.9 only after 4.16 passes. Root did not stage, commit, push, switch branches or update submodules; concurrent external commits are identified in the root audit.
