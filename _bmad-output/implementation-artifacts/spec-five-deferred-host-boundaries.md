---
title: 'Close five deferred host boundary and operations gaps'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred Works items leave deployment and verification boundaries unclear: R3B4 permits disagreement between Works and SDK query gateway identities; B8 treats authenticated-server transport resets as client-certificate refusal; R3B10 omits the captured service-bearer rotation boundary; B2 still documents a long-lived release secret despite OIDC; DW-58 points at a retired cancellation-swallowing probe without regression coverage for its replacement.

**Approach:** Validate paired gateway settings at startup and prove custom delegated caller authority through real HTTP ingress. Make mTLS refusal evidence require a recognized certificate-refusal alert, with inconclusive transport failures failing the proof. Document and rehearse replacement-bearer host restart, align release prerequisites with the committed trusted-publishing workflow, and prove the shared replacement TCP probe preserves cancellation. Resolve exactly these five ledger entries after deterministic verification and independent review, then commit and push Works main. Preserve existing EventStore and Platform checkouts and exclude both gitlink changes, as the user confirmed.

</frozen-after-approval>

## Implementation Notes

- Investigation baseline: Works `a0416e962c3df6f6852308e101c2ecc7b69e763d`. The user explicitly requested five deferred items and authorized the final push. These are bounded changes to existing host options, test probes and operator docs; no migration, dependency updates, deployments or new production API is required. Select the oneshot route.
- Code map: `Runtime/WorksDomainAdapters.cs` validates `WorksQueryOptions`; SDK `DomainQueryDispatcher` independently uses `Authentication:QueryAuthority:GatewayWorkloadId`, default `eventstore`. Require exact equality. Extend `WorksSdkProtectedHttpTests` with signed `QueryCallerAuthorityBinding` for each exact list/detail envelope and preserve persisted family/checkpoint assertions; test startup refusal for partial, blank and case-mismatched configuration.
- TLS map: `WorksPlatformTrustChecks`, `WorksPlatformTrustProbeResult`, and the existing loopback server-purpose controls. Track explicit server refusal separately from HTTP/2 acceptance and generic transport errors. On the supported Linux OpenSSL runtime recognize structured received certificate alerts only; unsupported/error-ambiguous providers remain inconclusive. Prove actual local certificate refusals and post-authentication close/reset controls.
- Bearer map: the existing typed client captures `IOptions.Value`; retain restart-based rotation and document deployment/revocation ordering in `docs/sdk-projection-query-adoption.md`. Add a real HTTP rehearsal covering old credential rejection, unchanged running host after configuration reload, and successful replacement host with preserved query state.
- DW-58: its command-pipeline class was removed. Every current live lane uses `WorksAppHostSmokeHarness.PrerequisiteGapAsync`, whose TCP probe already filters local timeout from caller cancellation. Expose that test-only helper internally and add cancellation, reachable and unavailable loopback controls in deterministic `WorksAppHostSmokeHarnessTests`; record retirement honestly.
- Release docs: `README.md` must require `NUGET_USER` and a trusted-publisher policy matching this repository, `release.yml` and `production`; the committed workflow obtains the temporary key from `NuGet/login`.
- Verification: build IntegrationTests explicitly in source Debug, execute changed classes directly with xUnit single-dash filters, then full deterministic integration coverage excluding live SmokeTests and Platform-only classes; execute changed Platform-named probe controls explicitly. Run architecture checks if needed and `git diff --check`. Works has no AppHost; no live Platform qualification is claimed. Keep concise commands/results with the spec.

- A concurrent workspace operation committed `939d5dd6d7085a4d921396f8aa29345d934b87e4`, reconciling EventStore `2361e7b6760a7d0065c67661c2e95c2af94bdb4d` and Platform `068ceff07cfa72812cb6a2bd47cc612ce39ad7f3`. Those changes are preserved and are not part of this batch.
- Broad source build command: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1`, exit 1, zero warnings and six external Platform errors: missing AddKubernetesEnvironment, SelectedTests, ResourceClaimStateDirectory and CompositionResourceClaimStore, plus CA1062 in PlatformHostedPublishing. Exact output is retained in `/tmp/works-five-host-boundaries-build.log`. No upstream source or gate was changed to conceal this blocker.
- Focused fallback builds use the same command with `-p:BuildProjectReferences=false`, consuming available dependency outputs after the broad build successfully rebuilt the changed Works runtime. This isolates the current test compilation from the unrelated Platform source errors; it does not qualify a complete current Platform source build. The focused Debug build passes with zero warnings/errors; the architecture project also compiles with zero warnings/errors and its full executable passes 308/308. Existing CI/release configuration checks pass 17/17.
- Initial focused execution passed 189/191. The two failures were fixture errors: custom workload enrollment was missing from the SDK receiver allow-list, and adding a configuration source after host construction was unsupported. The custom fixture now enrolls its workload and the rotation fixture reloads an independently chained secret provider configured before construction. Both now include signed caller-authority envelopes. The request-scoped observation wrapper covers single, bulk and batch access and proves observation through successful requests as well as zero denied operations.

## Review Triage Log

- **medium / patch — gateway restart boundary:** Works captures IOptions.Value while the SDK reads its gateway key per request. Deployment guidance now requires paired keys, workload enrollment and host restart/replacement for identity changes.
- **medium / patch — negative caller binding evidence:** default/custom gateway cases now reject unapproved callers, missing digest and post-signature envelope changes before Parties resolution or single/bulk/batch state access; persisted family/checkpoint assertions remain.
- **low / patch — rotation signed authority path:** every rotation phase now uses exact signed QueryCallerAuthorityBinding rather than the unbound compatibility path.
- **medium / patch — denied rotation reads:** the HTTP observation wrapper now covers all three store entry points, counts authorized access positively, and requires unchanged operation counts on both revoked-credential requests.
- **medium / patch — foreign certificate omitted silently:** explicit alert reasons are retained. The foreign-CA proof refuses certificate_required (1116), while native foreign controls require unknown_ca (1048); absent certificate controls remain distinct.
- **low / patch — native CLI prerequisite:** README declares Linux plus openssl on PATH for native refusal controls, and a missing executable now produces a specific actionable diagnostic.
- **low / patch — unsupported native control false pass:** native handshake cases explicitly report unsupported platforms as skipped rather than passing a synthetic substitute. The existing failSkips gate prevents an unsupported run from qualifying; pure provider classification is tested separately.

- Final focused command: `tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests -class '*WorksSdkProtectedHttpTests' -class '*WorksSdkRecoveryCompositionTests' -class '*WorksAppHostSmokeHarnessTests' -class '*WorksTlsCertificateRefusalTests' -class '*WorksServerCertificatePurposeTests' -result-xml /tmp/works-five-host-boundaries-focused2.xml`, exit 0, 191/191 passed. Final deterministic command: the same executable with `-class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-five-host-boundaries-deterministic.xml`, exit 0, 744/744 passed. All have zero errors, failures, skips and unexecuted cases. These include 18 native TLS/alert controls on Linux with OpenSSL, signed default/custom authority controls and the actual reload/replacement rehearsal.
- Full architecture command: `tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests -result-xml /tmp/works-five-host-boundaries-architecture.xml`, exit 0, 308/308 passed with zero errors/failures/skips/unexecuted. Logs and XML remain under `/tmp/works-five-host-boundaries-*`. The separate broad Platform source-build blocker remains as recorded above; no complete source-build or live Platform proof is claimed.
- Closed exactly DW-58, B2, B8, R3B4 and R3B10 in the existing deferred-work ledger. No additional work has been deferred by this review. Final remote inspection before commit found main and origin/main aligned at `939d5dd6d7085a4d921396f8aa29345d934b87e4`.

- Independent follow-up review inspected all seven patches and the retained evidence, confirmed every finding resolved, and found no concrete new bugs. The final deterministic run also passed 744/744 with zero errors, failures, skips or unexecuted tests. `git -c core.whitespace=cr-at-eol diff --check` passes. This batch leaves the reconciled submodule checkouts and gitlinks unchanged.
