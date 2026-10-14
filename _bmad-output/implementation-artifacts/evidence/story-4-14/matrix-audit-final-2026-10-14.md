# Final parent acceptance — Story 4.14 — 2026-10-14

The final local implementation passes acceptance after two independent three-layer review passes and the resulting corrections. Final immutable candidates are SDK `3.120.1-story414.20261014.10` and test-only Platform.Identity `1.0.0-story414.20261014.5`; Parties.Client/Contracts remain published `1.2.1`. This establishes source/candidate behavior only. It does not establish merge/release readiness: upstream public publication/public pins remain required.

| Current lane | Executed / passed | Skips |
| --- | --- | --- |
| Broad Release package integration | 633 / 633 | 0 |
| Genuine live command and scheduled resume | 2 / 2 | 0 |
| Architecture | 269 / 269 | 0 |
| Producer projection/client/circuit | 178 / 178 | 0 |
| Producer dispatch/rebuild/transport | 94 / 94 | 0 |
| Focused Debug source Works projection/protected query | 66 / 66 | 0 |
| Focused Platform JWT reader admission | 21 / 21 | 0 |
| Enabled projection class | 27 / 27 | 0 |
| Complete isolated restore/replay | 1 / 1 | 0 |

The broad integration and two live cases are disjoint: **635 current distinct integration cases pass**. Other rows overlap deliberately and are not added to that total. The enabled projection class includes in-memory restart regressions as well as real named Dapr/Redis cases; the complete isolated restore/replay uses its own real Redis container. The additional 21 recovery/cascade/mTLS cases remain historical `.8`/`.3` proof. Platform's narrow source test used the prior SDK `.9` graph; the final canonical protected package consumer uses Platform `.5` against SDK `.10`.

All six frozen matrix rows map to executed tests in the machine-readable audit. The original independent review harness passes again against final Release binaries: item 42 converges, first parking stays unavailable/degraded, exact transient-gap retry resolves, and interrupted capture reads are stale. All ten candidate identities/dependency pins/feed-cache hashes match. A separate proof starts with an empty package cache, restores six SDK archives matching the intended feed and proves persisted accepted content/checkpoint and restart-safe conflict readiness without project libraries. All five patched producer source hashes still match the final pack; there is no semantic producer edit afterward.

Two independent reviews ran all three layers, with every finding individually logged. The second-pass patches close mixed-catalog receiving validation, disabled ordinary invocation, cross-identity JWT privileges, repeated-gap audit exhaustion, complete-body token deadlines, renewal/corruption test gaps, numeric capacity disposition, truthful domain degradation and fixture/cache isolation. The accepted public delivery gate is carried once; operational latency at admitted bounds remains medium/unverified pending a representative benchmark. Both are recorded in `deferred-work.md`.

Release solution restore/build pass with zero warnings/errors. The final host facts complete cleanup. The isolated fixture verifies its own sidecar identity and removes its own container. Two exploratory runner invocations rejected unsupported exclusion/interior-wildcard syntax before running any tests; the retained final commands use supported filters and pass. No Git staging, commit, branch, push, submodule update, public publication or production activation was performed.

Live Keycloak realm-template materialization and service enrollment, protected authority validation, real Party/custody provisioning, deployed fleet/source-archive restore and AD-28/Story 4.16 remain independent gates. Protocol fixtures do not establish live authority validation. Existing finite context/document limits and recovery remain documented in the adoption guide.

[Exact matrix test names, counts, candidates and remaining limits](parent-final-acceptance-audit-2026-10-14.json). [Parent command records](2026-10-14-parent-final-commands.json). [Parent archive hashes](2026-10-14-parent-final-log-hashes.json). [Focused patch handoff and child archives](review-second-pass-implementation-2026-10-14.md).
