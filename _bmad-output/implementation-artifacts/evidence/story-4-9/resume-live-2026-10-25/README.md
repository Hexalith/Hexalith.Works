# Story 4.9 fresh live qualification — 2026-10-25

Implementation remains **in progress**. Linux Docker is available again, and the prior sixteen ordinary Docker-fixture failures are resolved. Fresh canonical qualification exposed a new producer defect: public EventStore 3.127.0 automatically installs the gateway application-channel token, while the unchanged archived topology also calls the generated-token helper. The helper adds the same resource twice and prevents retained-host startup. No new complete parity, rollback, CI-profile or production acceptance is claimed.

This attempt aligns Platform's two file-host directives with the already-selected public 3.127.0 catalog, corrects the stale Parties 1.4.0 fixture assertion by emitting the current catalog-selected version and checking both Client/Contracts package origins, and fixes repeat generated-token registration in EventStore. The producer keeps one private SDK-generated token shared with the same application's sidecar; caller-supplied tokens are not adopted as the generated cache, name collisions still refuse, and a same-name replacement cannot substitute for the original resource object. Seven focused regressions cover these behaviors. The existing Platform hosted-publishing null guards were preserved. Complete owner patches and exact changed source bytes are included.

| Check | Result |
| --- | --- |
| Full attempt 1 | Refused stale file-host 3.126.1 directives before builds or live lanes |
| Full attempt 2 | Both Platform modes 144/144 and both consumers built; supporting lane 305/306, stale Parties literal |
| Corrected Release package-contract focus | Build: zero warnings/errors; 2/2 facts pass |
| Full attempt 3 | 150 helper + 13 component checks; both Platform modes 144/144; both consumer builds; supporting 306/306; public producer qualification; six serial runtime builds pass |
| Full attempt 3 retained-host readiness | 0/1: duplicate `eventstore-app-api-token` parameter before fleet startup; no accepted rows or complete rollback |
| Final EventStore owner Debug/source build | Zero warnings/errors; 20/20 focused facts pass, including seven new regressions |
| Public producer after local fix | Strict refusal: exactly the new binding annotation and changed app-channel helper differ from producer `e1d8b17160202761ab5c9606cafbcf4d1a4b92c3` |
| Fresh final Works Debug/source integration build | Against the modified EventStore checkout; zero warnings/errors |
| Fresh ordinary integration | 1,020 passed, zero failures/skips, exactly seven existing explicit facts NotRun; 119.435 seconds |
| Targeted hosted-publishing diagnostic | Build: zero warnings/errors; 6/11 pass; five require the unavailable Memories manifest before their graph assertions |
| Final CRLF-aware diff checks | Works, Platform and EventStore all pass |

The latest official Client, DomainService and Aspire indexes still select 3.127.0. The producer fix is local and unpublished. The actual CI profile was not rerun: the required preceding full gate is incomplete and public-producer qualification now correctly refuses the local fix. Publish a producer containing the fix, select it through the owning dependency authority, then run a fresh unchanged full gate followed by the actual CI entry point. Both qualification-bearing tasks remain unchecked.

The archived source bundle, its equality requirements, runtime policies, live assertions, timeouts, owning catalog selections and gitlinks were preserved. No nested dependency was initialized, and no commit, push or publication occurred. The targeted hosted-publishing diagnostic uses an isolated existing source export plus the exact fresh Works-built Tooling assembly and its restored runtime closure. Its five remaining failures require `references/Hexalith.Memories`, which Works does not root-declare; that diagnostic is not canonical parity evidence. The earlier out-of-tree producer-owner test attempt failed all nineteen selected facts in the existing fixed-depth repository-path resolver before test bodies; the unchanged resolver succeeds in the owning repository artifacts layout. Initial missing-assembly and interrupted owner-layout attempts are retained separately.

[commands.json](commands.json) records exact commands and observed results; full attempt 1's terminal exit was not separately retained, so its record intentionally leaves that field null. [results.json](results.json) contains parsed XML and the strict producer refusal. [source-inputs.json](source-inputs.json) binds final changed sources, owner revisions and the executed assemblies. [diff-checks.json](diff-checks.json) records the three final checks. [manifest.json](manifest.json) binds stored and original bytes of every retained stream and all packet files except itself. Earlier evidence packets remain unchanged.
