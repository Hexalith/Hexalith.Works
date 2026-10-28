# Five deferred reminder recovery outcomes

The [specification](../../spec-five-deferred-reminder-outcomes.md) and [manifest](manifest.json) bind five canonical ledger findings to the current SDK consumer controls. The source hashes cover the tests, test scheduler, operator guidance and ledger. Compressed evidence hashes preserve raw commands and xUnit results; the manifest records full dependency commits and the executed assembly hash.

- Debug Integration build: zero warnings and errors.
- Focused reminder classes: 29 passed, zero failures, errors, skips or not-run.
- Deterministic integration lane: 1,028 passed, zero failures, errors, skips or not-run. Live Smoke and Platform classes are explicitly excluded.
- Whitespace formatting verification passes, with the generic workspace-load warning retained.
- Exact Platform startup exits 2 with existing NU1605 Dapr/EventStore package downgrades. No live Platform acceptance or public-package qualification is claimed.

The new cases execute guarded covering parked-source rebuild plus explicit SDK repair resolution, isolated persistent scheduler failure, due-now fault diagnostics, nine uncertain receipt/audit/cancellation cases spanning all durable target dispositions, and failure followed by exact caller cancellation. They use actual production Works composition and SDK state machinery over serialized in-memory storage; gateway, clock, scheduler, registrar transport and target receipt transport are controlled boundaries. Target receipts are synthetic persisted external-boundary evidence, not a claim of live actor execution.

A covering rebuild alone retains unresolved parking evidence. The supported SDK maintenance API resolves it after promotion and drain; this change adds no operator HTTP endpoint. Scheduler failure keeps Pending retry work without a finite terminal exhaustion policy. Invalid receipts and cancellation failures keep Retrying witnesses; durable rejection is terminal and audited.

Verify each SHA-256 in manifest.json against the named source or compressed file. Decompress the XML to inspect case membership and counts. Re-running requires the repository's current initialized root source dependencies; the recorded dependency commits identify the tested graph. Independent review and any corrections are recorded in the specification.
