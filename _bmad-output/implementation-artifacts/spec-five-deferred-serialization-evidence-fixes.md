---
title: 'Close five deferred serialization evidence gaps'
type: 'bugfix'
created: '2026-10-22'
status: 'in-progress'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** DW-61, DW-62, DW-64, DW-65, and DW-66 weaken the evidence for Works serialization compatibility: Web round trips claim to prove persistence, the cross-tenant rejection sample names a same-tenant parent, optional nested writer shapes are absent, stream fakes use Web casing, and stale copied JSON can conceal a deleted source fixture.

**Approach:** Correct the three flow-test descriptions while preserving Web reader compatibility. Give only the cross-tenant rejection a foreign parent and regenerate its two fixtures. Enrich the catalog's Created event with an expectation reference, effort, and conversation, regenerate its exact writer bytes, and assert the nested values explicitly. Serialize Story48 stream pages without options and prove the helper emits writer-shaped bytes. Check both corpus memberships against source as well as copied fixtures, then prove deletion is refused even when the copied fixture survives. Preserve historical Web Created bytes, production contracts, dependencies, and the existing EventStore/Platform working changes. Resolve exactly these five ledger rows, validate, independently review, and commit/push to main as requested.

</frozen-after-approval>

## Implementation Notes

- Investigation confirmed all five defects at Works revision `e2b3d88`. The bundle changes test evidence and fixtures only; no runtime or persisted production format changes, intent gaps, or migrations are required. Publishing the commit is explicitly authorized.
- Code map: `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`; the three named contract-flow classes; `Story48Streams.cs`; both `SchemaEvolution/*GoldenCorpusTests.cs` classes and their JSON/README files. Reuse the existing upward repository-root discovery pattern for source membership without depending on a compile-machine path.
- The user pushed the previously staged Story 4.9 changes. The Works tree is clean; the pre-existing EventStore and Platform overlays remain outside this bundle.
- Validation: build the IntegrationTests project in Debug, run the focused direct-xUnit classes and deterministic integration lane, exercise source-deletion controls with copied output retained, and run `git diff --check`. Keep exact JSON compact with no BOM or trailing newline.
