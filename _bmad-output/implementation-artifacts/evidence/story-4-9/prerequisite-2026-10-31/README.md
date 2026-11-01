# Story 4.9 prerequisite qualification — 2026-10-31

The complete committed-baseline 3.128.4 gate stops before live proof. Platform custody calls two APIs absent from that public release. Official 3.129.0 package metadata and the corrected exact-type API diff establish the producer prerequisite. No hosting assertion or gate was changed.

The Release pin guard passes. A focused zero-warning Release build runs the unchanged package-contract class: 2 passed, zero failures/skips. Its temporary MSBuild focus removes unrelated uncompilable Composition source and explicitly retains Composition’s selected Aspire package; it does not qualify the full consumer or Platform topology.

The owner helper checks pass 157 and component checks pass 15. Parent script checks record 110/112 passes; absent locked node_modules and a stale source-pair releaseVersion cause the two failures. Failed focus setup, the moving-input build and unsupported direct-layout refusal remain separate diagnostics. The first dotnet-inspect --oneline command used an unsupported option and supplies no API evidence.

All exact commands, selected inputs, original failures, corrected API evidence and focused XML are retained as deterministic gzip files in manifest.json. No live parity, complete rollback, fresh hosted CI or production acceptance is claimed. A separate immutable compatible 3.129.0 candidate is being qualified; its result is not credited to 3.128.4.
