---
title: 'Qualify native solution project and target selection'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Settle the deferred Story 4.15 concern that solution targets ignore project build selection or compile a selected project twice. Exercise the current Works imports through native MSBuild solution dispatch and retain runtime evidence.

</frozen-after-approval>

## Verification

The regression uses the actual Works solution imports and a disposable `.slnx` with two common projects and one project excluded in Release. Release `Build,Pack` ran each included target exactly once in request order. Debug included all three projects. Release `Pack` ran only the two selected Pack targets. Current `Directory.Solution.targets` contains only the reviewed-source initialization before Restore. Native MSBuild owns project selection and dispatch.

The 13-case release build policy suite and the focused final case passed on the pinned .NET SDK. Set `WORKS_SOLUTION_BUILD_EVIDENCE` to retain each configuration's MSBuild binary log outside the disposable test directory.

## Review Triage Log

- **Medium, patched.** Check Build precedes Pack for every selected project. A reversed request previously produced the same marker counts.
- **Low, patched.** Resolve the evidence directory from the calling process before starting MSBuild in the temporary solution directory.
