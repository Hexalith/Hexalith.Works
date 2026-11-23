---
title: 'Verify the inputs behind Platform Works qualification'
type: 'bugfix'
created: '2026-11-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Address five deferred Works verifier findings. Bind canonical qualification to the actual root-dependency build and source inputs, detect changes to deployed runtime file membership, determine which global.json selects the SDK during canonical compilation, verify restored producer archives against independent NuGet evidence, and prevent reuse of a component attempt after its suite or dependencies change. Exercise each boundary and record the outcome in the Works ledger.

</frozen-after-approval>

## Implementation Notes

Platform now records revisions and working file hashes for the additional root dependencies consumed by the Works gate. Direct checkouts and exported snapshots use the same inventory. The Works bootstrap fixture now creates dependency commits, so the test exercises the same Git lookup as a real checkout.

The runtime verifier records every selected output directory during preparation and walks those directories again after the live lanes. Tests change, add, and delete files under distinct output roots and check link refusal.

The producer comparison now includes the SDK selector in both local Git and official source-tree paths. A real .NET 11.0.100 experiment rebuilt an external project from the Works directory after changing the producer pin to unavailable 99.0.100. Launching the prebuilt project from its own directory then failed with SDK resolution exit 155. Aspire uses the project directory for these child launches.

Before reading a producer's nuspec and comparing its DLL, Platform checks the cached archive bytes against the SHA-512 in NuGet's public catalog. A live check verified Hexalith.EventStore.Client 5.3.1. Fixture tests also exercise a coherently modified cache and an unavailable catalog.

EXT-HOST-1 records the selected custody and guard projects, source and resource membership, dependencies, imports, compiler arguments, runner settings and prepared outputs. Replaying after an input change preserves the first attempt and refuses its reuse. A fresh preparation failure retains the project command and its build log. Two SDK-built xUnit fixtures produced the same fingerprint after a warm build and test run. Changing a private copy of a NuGet DLL changed the fingerprint.

Platform implementation [f04b0d1673446c55cfb1ca616914fd98df651fe1](https://github.com/Hexalith/Hexalith.Platform/commit/f04b0d1673446c55cfb1ca616914fd98df651fe1) passed 165 host, producer, snapshot, input-closure and NuGet verification tests plus 21 EXT-HOST-1 tests. All 21 Works parity bootstrap tests passed. Independent review identified the cases recorded below, which are covered by the revised implementation.

The broad Works Python suite ran 127 tests and reported the same 27 failing assertions on the original Works main and with this change. They are in release policy, source Debug verification and transitive reference fixtures. The local Works shared checker on the original committed inputs failed to compile Platform.Composition because the selected Builds tooling lacks CompositionFixtureProducer. A full custody Debug build in the umbrella layout also encountered existing CA2007 diagnostics. These verifier results cover the input checks and fixtures. Live Platform parity was outside this batch.

## Review Triage Log

- Medium, patched. Dependency selection accounts for populated siblings and configured MSBuild roots, including overrides resolved relative to an exported execution directory. The snapshot also preserves its original direct-link binding.
- Medium, patched. Component evidence must distinguish compiler arguments that produce different suite binaries from identical source files.
- Medium, patched. Input collection must account for imports used by the actual build, including the caller's custom targets.
- Medium, patched. The component attempt includes the global.json selected from the suite command's working directory. Changing the runner changes the attempt identity.
- Medium, patched. The EventStore guard's custom copy inputs participate in the component evidence, including downloaded fixture assemblies.
- Medium, patched. A failed first build retains its diagnostics and records an unqualified attempt.
- Low, patched. Each of the five original ledger entries links to this verification record.
