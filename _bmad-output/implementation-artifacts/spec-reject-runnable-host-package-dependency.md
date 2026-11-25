---
title: "Reject the runnable `Hexalith.Works` host package ID as a dependency in `validate-nuget-packages.py`."
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Reject the runnable `Hexalith.Works` host package ID as a dependency in `validate-nuget-packages.py`.

</frozen-after-approval>

## Implementation Notes

The NuGet validator rejects an exact case-insensitive match for the runnable Hexalith.Works host ID even when it appears in the expected dependency boundary. The separately packable Works libraries remain valid dependencies.

Before the fix, both host ID casings were incorrectly accepted. After the fix, all package validation tests passed, including the permitted Works.Contracts sibling.
