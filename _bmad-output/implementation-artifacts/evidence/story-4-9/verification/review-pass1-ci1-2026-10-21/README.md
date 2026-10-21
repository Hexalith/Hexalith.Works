# Review pass 1: first CI attempt, nonqualifying

The actual `bash scripts/verify-platform-parity-ci.sh` entry point exited 1 on
2026-10-21 after 6 minutes 45 seconds. Bootstrap, 110 Python helper checks,
the expected Agents Full refusal, the Release/package scaffold, the 114-test Platform package suite,
255 supporting Works facts, public producer qualification and all six runtime
prebuilds passed.

Agents Full remains unavailable and is not a Works prerequisite. Its nested
commands used Release/package flags: custody passed 129 tests, while the guard
probe failed before executing any test. No Agents Full qualification is claimed;
the retained component diagnostics name its missing coverage and runtime inputs.

The archived-host readiness preflight failed (1 executed, 1 failed, no skips).
Startup/readiness probes had completed, but the subsequent body assertion read
`Degraded` rather than `Healthy`; the diagnostic named
`eventstore-processes-unresolved` as incomplete, unresolved or overdue.
`retained-host-preflight.log.gz` and `retained-preflight.xml.gz` preserve the
failure and host diagnostics. This observation alone does not establish a root
cause. The exact same fact passed in the separately qualifying full-profile run.

No recovery, historical, HA, mTLS, shared-rebuild or complete rollback lane ran.
The ledger remains `incomplete`, every row remains unqualified, and bootstrap
remains `bootstrap-validated; parity-incomplete`. This attempt cannot qualify
any current parity or rollback claim. The parent retained it and started one
complete unchanged CI retry from fresh isolated inputs; no source or assertion
was changed in response to this failure.

The wrapper restored the approved temporary watch quota to 1,048,576 with a
successful read-back. Platform edits remained paused; no foreign fleet was
changed. The closure directory preserves the exact command, environment,
bootstrap and adjacent source binding. `packet-manifest.json` binds all 39 gzip
members by stored and decompressed SHA-256 and byte count. Raw results remain
at `/mnt/wsl/story49-qualification-20261021T072935Z/review-pass1-ci/gate`.
