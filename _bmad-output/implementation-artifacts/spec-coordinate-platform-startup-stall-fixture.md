---
title: 'Coordinate the startup acquisition deadline fixture'
type: 'bugfix'
created: '2026-11-02'
status: 'in-progress'
route: 'oneshot'
review_loop_iteration: 0
baseline_commit: 6064b363a8e1a3e0fdaec00443b46318abc74c75
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

The tuple startup stall test starts a 100ms wall timer before its target acquisition begins, so loaded CI can expire without exercising that acquisition. Coordinate actual acquisition entry with an internal optional TimeProvider test seam and then advance the same100ms duration. Null/default must preserve the exact linked CTS and System CancelAfter production path. Retain all28 cases, false/no-resource assertions,5s outer and2s total cleanup bounds; track the actual worker until release/drain. No skips, deadline inflation, public API changes or changes to authority, authentication or acceptance.

</frozen-after-approval>

## Implementation Notes

Owner worktree is `/tmp/works-platform-package-ci-workspace-20261102/references/Hexalith.Platform`. Read-only investigation produced `/tmp/platform-sdk31321-qualification-draft-20261102/platform-startup-stall-coordination.patch` with detailed notes and actual-worker mechanism probe11pass. Apply only that patch, not finiteBase or snapshot files. The coordinated changes touch AgentsHostComposition.cs, AgentsHostStartupBoundaryTests.cs and one centrally versioned test-only TimeProvider.Testing reference in Composition.Tests.csproj. Preserve the concurrently owned cohort/disposal changes. No local live topology or nested init. Reviewer input must contain only this unit's diff.

## Verification

Build the actual owner Release test project with the same public package/Builds roots as cohort unit, serialized with its build. Run all28 actual startup fixture cases, then complete first tuple group80cases with XML. Inspect exact counts and zero failures/errors/skips. The probe11cases is mechanism evidence only; no candidate/runtime acceptance.

## Review Triage Log

- Blind1 medium/patch: cleanup could skip ownership drainage after a composition fault. All completion failures are now retained while ownership drainage is attempted independently within the original shared2s cleanup budget; files remain owned if a worker cannot drain, and an original test failure is preserved.
- Blind2 medium/patch: deterministic fake path alone omitted production System timer coverage. Added one production System path stall case using the existing3s non-fake duration and unchanged5s outer bound. Original three100ms stall cases remain deterministic and unchanged in duration.
- Blind3 medium/patch: endpoint-only clock advance could miss early expiry. Original fake stall cases now advance99ms and assert the actual acquisition cancellation token remains uncancelled, then1ms to expiry.
- Blind4 medium/patch: pre-cancellation did not test linked two-token branch after acquisition. Added one caller-cancellation stall with fake time unchanged. Original28cases retained plus2 newcontrols; tuple first group now82.

After review patches: actual nonincremental full owner package Release build0warnings/errors; startup30/30 and tuple82/82, allzero failures/skips/errors/not-run. A project-only incremental invocation had an incomplete local dependency copy and could not launch xUnit; the actual full solution rebuild restored the complete runtime closure. Domain2 local check separately cannot resolve release tag because its test assumes initialized nested Builds; actual root Builds tag exists, no nested initialization performed.
