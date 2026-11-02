---
title: Preserve exact Tenants caller credentials across gateway retries
type: bugfix
created: '2026-11-02'
status: done
route: oneshot
review_loop_iteration: 0
baseline_commit: 3a603db76f894c4ffacbae16683eb04f179d5f99
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.Tenants/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="User authorized CI repairs/latest synchronization and waived human checkpoints">

## Intent

The API bearer forwarder appends Authorization on every send, so the existing gateway retry can accumulate duplicate credentials and fail authentication. Replace outgoing Authorization with the exact current incoming raw values on every send, clearing stale credentials when the header or HTTP context is absent. Preserve ambiguous raw values without selecting or normalizing them into valid authority.

Preserve concurrent main3a603db76f894c4ffacbae16683eb04f179d5f99's thirty-second attempt/total standard resilience, retries, circuit breaker, handler order, all six queries/eleven commands, catalog guards and authorization rules. Verify repeated headers plus successive/overlapping actual-factory contexts, and strengthen its genuine retry control to require exactly one unchanged credential on each attempt. Document only raw collection preservation and cooperative policy budgets; no wire-separation or strict cleanup-deadline claim. No SDK/package/gitlink changes or local native/container starts.

</frozen-after-approval>

## Implementation Notes

Owner is Works references/Hexalith.Tenants, clean main at baseline after a normal fast-forward. This is a small reversible forwarding repair with no intent gaps or new public API. The superseded one-attempt design and its complete36-case evidence remain archived under .artifacts/tenants-api-gateway-transport-20261102/keep-before-upstream-reconciliation; it was not shipped. Do not load skills under references or rerender the active build workflow.

Change InboundBearerForwardingHandler.cs, direct handler controls in TenantsApiGatewayHandlerTests.cs, exact-credential retry/context controls in existing TenantsApiGatewayResilienceTests.cs, and production-auth-readiness.md. Reuse the actual production factory/capture already added by upstream. Preserve every existing assertion and actual30-second timeout test. Use the existing public3.131.0/workspace Builds serialized Release command and built xUnit single-dash class selectors. Parent owns one-shot blind review, normal pinned commitlint/hooks/push and fresh native CI. Existing14 command-runtime failures are reproduced on untouched ca93 and belong to the separate public SDK migration; do not weaken them here. Existing noncancelable SDK durable cleanup is separately recorded in deferred-work.

Initial clean Release build and all85 focused handler/production-policy/controller cases pass with zero failures/skips/not-run. Parent read the complete four-file diff. Evidence: Works .artifacts/tenants-retry-credentials-20261102/verification.json. Production Program and incoming timeout/standard retry/circuit-breaker implementation remain byte-identical to3a603.

## Review Triage Log

- blind-1: low, patch. Existing real retry control detects duplication with an unchanged bearer but does not independently exercise rereading changed/missing input between retries. Extend that same control with changed/absent cases; retain original success, exact-owned headers and two-attempt assertions. No production or fixture enrollment changes.
- blind-2: low, reject-low. The direct reused-request control verifies every raw value, duplicate values, comma input, whitespace, empty headers and null context. New documentation explicitly limits preservation to the in-memory collection and permits wire combination; no receiver authentication behavior changed. A separate live receiver/authentication harness would add complexity for a hypothetical normalization branch absent from this implementation and already caught by the complete raw-array assertion.
- blind-3: low, reject-low. Existing GatewayClient_WhenRegisteredLikeTenantsApi_UsesSidecarBaseAddressAndHandlers exercises SubmitCommandAsync through the same SDK typed client and forwarder. Actual Program retry/pooling controls exercise that shared pipeline using queries; the forwarder has no command/query or payload branch. Adding new technical HTTP command fixture inventory would test unchanged SDK routing rather than this credential replacement. Keep existing command assertions, all66 existing generated-controller authorization controls and actual Program handler-order coverage.

The sole one-shot reviewer completed before individual triage. The accepted correction is a direct test patch; no new review loop is run. Prior full-review and superseded transport-design evidence are preserved separately.

## Final verification

The final Release build is clean. All87 cases pass with zero failures/skips/not-run:13 handler controls,8 actual production-policy controls and66 generated-controller cases. The production retry now independently checks unchanged, changed and absent credentials on its second attempt. Existing11-second held-query and actual30-second timeout controls remain and pass. Formatting and diff checks pass; Program is byte-identical to concurrent3a603. Parent read the complete final patch. Evidence: Works .artifacts/tenants-retry-credentials-20261102/final/verification.json. Hosted native/public SDK migration acceptance remains a separate gate; no release claim is made from these isolated tests.
