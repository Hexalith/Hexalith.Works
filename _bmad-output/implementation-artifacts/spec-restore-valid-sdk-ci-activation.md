---
title: Restore the valid established SDK CI activation
status: done
baseline_commit: 27ebfa332cb6f59a16eee7c338311a30be1d1006
---

The user's maintenance/release task includes fixing CI. Incoming ba4 added future platform-package activation fields to the existing legacy domain-ci.yml caller even though its comment says activation requires a separately qualified producer. Actual current Builds main4d892075 legacy schema does not declare integration-test-profiles or registry secrets. V2 declares those fields but explicitly refuses legacy integration/AppHost inputs and lacks the two supplied Dapr version parameters. Renaming the workflow or deleting runtime pins would therefore be an invalid migration.

Restore the established valid legacy caller while leaving future profile files staged and honestly unqualified. This is the separate Root follow-up to SDK review6 blind-2, not a new SDK runtime repair. The concrete unapplied patch and schema/inventory proof are at .artifacts/upgrade-release-orchestration-20261101/sdk-ci-activation-draft/ under Works.

Work only in the private SDK owner /tmp/works-sdk-compat-qualification-20261101. Preserve frozen136-path loop6 diff/archive/claims receipts and all earlier evidence. Keep every established unit/integration/Aspire job, project inventory, test platform, failure/coverage rule, continue-on-error value and explicit Dapr CLI/runtime pin byte-identical. Remove only the never-valid newly added platform-package activation (its module-manifest/profile arguments and registry secrets) from this legacy caller. Keep the staged manifest/project/profile source unchanged. Do not mark that future profile passing or remove it from the pending integration account. No disabled test, deleted test project, new bypass variable or reduced test filter is allowed.

Verify the remaining caller against the real current legacy workflow schema, and separately assert every nonactivation input and all established test inventories/pins remain identical. Inspect ordinary SDK build configuration for any mandatory module receipt; if it requires the staged producer, report that fact instead of bypassing the guard. Use only narrow workflow/source/release qualifiers and actionlint for this configuration-only change, with normal pinned21.2.4 commitlint and normal hooks. No unchanged native/all20/full239 reruns, no Git remote operations or releases.

The current SDK prerequisite claims may append this independently scoped Root CI correction and pending future-profile limitation in non-frozen text; do not alter frozen intent or increment review loops for this routine upstream configuration correction. Preserve before/after exact data and create one combined complete baseline freeze for the next three reviewers, then return and end the owner turn. Parent retains review/publication ownership. Do not rerender BMAD or spawn another implementation agent.

## Final verification

The upstream fix is committed in d83ecb43ef997f947d08b789d0625a8fa90193b6 and preserved at c1952e97ac8b33b1578207e91c1c574ef69ede47. Parent comparison against the captured baseline confirms that only the four unsupported activation fields were removed and all remaining workflow semantics are identical. The real current Builds legacy schema declares every remaining input. Hosted CI 41357198277 succeeded with 15,606 passing cases, 49 existing skips and zero failures across 20 projects. The staged Platform profile remains separately unqualified; no acceptance is claimed for it. Detailed evidence is in Works `.artifacts/upgrade-release-orchestration-20261101/sdk-ci-activation-draft/applied-validation-2310.json`.
