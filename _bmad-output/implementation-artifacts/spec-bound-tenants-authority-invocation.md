---
title: Bound Tenants invocation around complete native authority checks
type: bugfix
created: '2026-11-02'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 6b034a2d3b70533024343e721c47ffdfd1830a10
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.Tenants/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="User authorized CI repairs preserving authorization and waived human validation">

## Intent

The real owner profile bootstraps successfully, but CreateTenant fails because its five-second SDK invocation deadline interrupts mandatory native authority reads. A preceding successful authority check took6.4 seconds. Give this composed Tenants host a finite30-second invocation default, preserving explicit operator selection and complete source verification; align its existing production Dapr target.

## Boundaries & Constraints

Keep the10-second replay and separate2-second final observation, exact endpoint binding, signed workload/source proofs, current membership/revocation and every real202/terminal/persisted-state gate. Configure only this host's existing SDK seam; SDK-wide defaults and algorithms stay unchanged. Preserve retries, circuit breakers, read/state/pubsub policies and both worker profiles. Preserve explicit configuration values rather than replacing them with the new default.

Never return synthetic202, cache or bypass authority, change business authorization, increase test deadlines, skip cases, alter peer pointers or start local native/container resources. Do not add a local apps.tenants Dapr target: none exists, and adding one would change retry behavior. Any later failure remains a real blocker.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Failure handling |
|---|---|---|---|
| Default composition | No operator timeout; run/publish model | EventStore resource receives30 seconds | Other resources receive no new setting |
| Explicit selection | Effective timeout supplied, including5 or a higher value | Exact operator value retained | Existing SDK validation/cancellation remains authoritative |
| Dapr deployment | Existing tenants app target | Finite30-second timeout; same retry/breaker | Other target policies unchanged |
| Local Dapr | Existing local template | No new tenants target or retries | Existing EventStore30-second policy retained |
| Authority | Existing native proof/removed/unavailable controls | Original acceptance or fail-closed denial | Replay10/final2 deadlines unchanged |

</frozen-after-approval>

## Code Map

Owner is Works `references/Hexalith.Tenants`, clean main at baseline. Parent owns Git/review/publication and fresh native CI. Read tracked build/editor/configuration, source-authority docs and tests. No skills under references. No intent gaps or irreversible implementation steps; cross-layer host/deployment policies warrant dispatch.

- `src/Hexalith.Tenants.AppHost/Program.cs:70`: reuse the adjacent effective-configuration/WithEnvironment pattern. Set `EventStore__DomainServices__InvocationTimeoutSeconds` from `EventStore:DomainServices:InvocationTimeoutSeconds`, defaulting to30 only when absent, on the existing EventStore resource. Its command registrations are the two owned Tenants domains. Do not change SDK source.
- `deploy/dapr/resiliency.yaml:51`: only its existing apps.tenants deadline changes. Use a clearly named30-second policy; retain EventStore30, tenants-api5/noReadRetry, state/pubsub/retry/breaker values. Avoid misleading policy naming. Local `src/Hexalith.Tenants.AppHost/DaprComponents/resiliency.yaml` has no tenants target and should remain unchanged.
- `deploy/source-authority-settings.example.json:15` and `docs/production-auth-readiness.md`: show the matching EventStore-host setting and explain the enclosing finite budget and operator/Dapr coordination; no publication or native acceptance claim.
- `tests/Hexalith.Tenants.AppHost.Tests/SourceAuthorityBackpressureTests.cs`: its actual run/publish resource-model callbacks demonstrate default/override testing without starting services. Add a focused separate test type if needed; assert correct resource placement and preserved explicit selections.
- `tests/Hexalith.Tenants.Server.Tests/Configuration/EventPublicationConfigurationTests.cs:373`: update the existing policy control for the production Tenants30-second deadline while retaining other assertions. Do not weaken unrelated expectations.

Evidence: run41362685140/job125016223280 at SDK a82555477689e3e021c3c74bcab0ccfefd4ff307. CreateTenant trace4477d699d24624cc78df90ab1514bc3b starts23:46:19.858; owner reads return200 after1471ms and1917ms, then the stable-head read is canceled by SDK event3004 at24.863 (configured timeout5). The preceding four-call native check completed in6.4s. Parent safe receipt: Works `.artifacts/upgrade-release-orchestration-20261101/tenants-6b-native-source-failure/summary.json`.

Exact SDK inspection found no hidden command HTTP deadline: the named domain-service-invocation client has infinite HttpClient.Timeout and removes resilience handlers. Unnamed query policy remains10-second attempts/30-second total. Sample metadata-inventory warnings are unrelated. Aspire WithEnvironment string API was verified through CLI documentation and https://aspire.dev/reference/api/csharp/aspire.hosting/resourcebuilderextensions/methods/#withenvironment-a6965494. Runtime baseline is the actual failed hosted topology; no local native qualification is claimed.

## Tasks & Acceptance

- [x] Configure the finite host default and document the corresponding deployment input.
- [x] Align only the existing production Tenants Dapr target, retaining all other policies.
- [x] Execute every matrix control and existing authority/bootstrap compatibility cases; retain exact commands/counts/hashes and failures.

Given an absent override, when the real host model is composed, then its command invocation budget encloses unchanged native authority deadlines. Given explicit configuration, when composed, then it retains that exact selection. Parent must subsequently prove real202, terminal completion and persisted/current-authority effects with unchanged native tests.

## Implementation Notes

Use public SDK3.131.0 and explicit workspace Builds root for finite serialized Release builds. No new public API, peer edits, Git/remotes or broad solution test run. Notify parent before substantial compilation. Preserve all failed attempts.

## Spec Change Log

## Review Triage Log

- blind-1: medium, patch. deploy/dapr/README.md65 explicitly instructs UI-only loading. The new apps.tenants policy controls EventStore-to-Tenants calls and must also be loaded on the EventStore caller; document applicable caller placement without changing receiver ACL guidance.
- blind-2: medium, patch. The ten cases differ by manifest-publisher arguments but never inspect ExecutionContext.IsPublishMode. A direct assertion must verify that advertised publish coverage is actually publish mode.
- blind-3: low, patch. The documented environment input and its precedence are absent from current cases. Add focused environment/command-line precedence controls with restoration and existing test isolation, preserving exact effective values.
- blind-4: low, reject-low. This change configures an existing SDK timeout; SDK cancellation and authority algorithms remain byte-identical. The retained real SourceCatalogOwnerProfileTests already require actual202/terminal/persisted/current-authority effects and will be rerun; its baseline log proves a genuine6.4-second check and actual5-second cancellation. Additional forced native fault injection is more than a direct test correction and is not needed to establish this configuration contract. No native qualification is claimed from local tests.

## Verification

Build/run the affected AppHost and Server test projects individually, using built xUnit single-dash selectors. Resource-model tests must not start applications. Verify default/override and YAML controls execute without skips, plus focused authority and bootstrap compatibility controls. Return complete owned diff and receipts. Parent performs final verification, independent review and fresh native CI.

### Parent implementation verification

Parent read every owned diff section, verified it matches the six staged files and audited matrix cases against passing XML. Both Release builds are clean;48 AppHost and426 Server cases pass without failures/skips/not-run. Source authority, runtime Program, local Dapr and CI workflow bytes remain unchanged. Native qualification remains pending. Evidence: Works .artifacts/tenants-authority-invocation-20261102/.

### Parent final patch verification

All three reviewers completed before individual triage of four findings. The same implementation owner patched policy placement guidance, real execution-mode assertions and environment/CLI precedence with restoration/nonparallel isolation. Parent read the full patch delta and verified hashes. Both Release builds are clean; all56 AppHost and1774 Server cases pass with zero failures/skips/not-run. No new review loop ran for these patches. The owned seven-file diff preserves SDK defaults, source algorithms, runtime Program, local Dapr and CI selections. Fresh actual native qualification remains required.

### Current source integration

Incoming020e40e68dca2d489d5341e0911e70baeb7d35c2 independently supplied the same host default and three model cases. A normal rebase retained its implementation/comment and all incoming tests; the owned follow-up is ca93fe19847d3535353c8b4850497350aaba47d3. The combined Release build is clean and all59 AppHost cases pass without skips. The1774 Server cases remain verified against identical Server source. Fresh exact-source native CI remains pending.
