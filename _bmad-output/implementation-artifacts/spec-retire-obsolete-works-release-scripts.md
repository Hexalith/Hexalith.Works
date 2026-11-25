---
title: "Retire the module release scripts that no workflow invokes, or document a supported local use for them."
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Retire the module release scripts that no workflow invokes, or document a supported local use for them.

</frozen-after-approval>

## Implementation Notes

Removed five unused shell publication and verification scripts and their dedicated tests. The current shared Builds publication path and the publication-free local pack, checksum, validation, and consumer rehearsal remain available.

Checked the tracked caller graph, kept both the direct Python entry point and unittest discovery, and tested the local checksum ledger.
