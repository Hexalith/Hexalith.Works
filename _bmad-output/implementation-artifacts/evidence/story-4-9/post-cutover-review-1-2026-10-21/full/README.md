# Review pass 1: qualifying full profile

The complete canonical Platform gate exited 0 on 2026-10-21, from 12:13:11 to
12:40:16 UTC. Its ledger reports
`passed-disposable-synthetic-parity-and-rollback`: all eleven AD-20 rows, all
44 persisted/negative/restart/rollback proof kinds, complete-fleet rollback and
final source/runtime/archive/launcher closure passed. This qualifies disposable
synthetic parity; production preview admission remains refused.

The command and environment are preserved in
`closure/review-pass1-full4-command.json.gz`. The command uses the canonical
`eng/verify-works-host.sh --profile full`, the real Works checkout, an immutable
Platform execution export and the independently qualified public EventStore
3.126.0 producer source. Platform is based on
`7b5548f0d308d27af9d5fda5b68c6b959c053185` plus the exact seven executable/test working overlays
recorded in `closure/platform-source-binding.json.gz`; that manifest's SHA-256
is `dc69918c3a20c57eb1ef7de9b980cb257d6c2b0bcd97413e5843f2f8e2c5a8fc`.
EventStore producer commit is
`fcf78d6cafb6c0c9f52dc350a9a09aff54ded288`. The current Builds binding records
`17d0c6ffec5fb3c8d8058e67cb85644460cb5256` and 1,779 exact ordinary inputs.

All required lanes executed without failures, skips or unexecuted tests:

| Lane | Passed |
| --- | ---: |
| Canonical Python evidence and component helpers | 110 |
| Platform source Debug | 114 |
| Platform package Release | 114 |
| Supporting Works consumer | 255 |
| Archived-host readiness preflight, separate from rollback | 1 |
| Recovery, including date callback and complete rollback | 4 |
| Historical lifecycle/security/restart | 1 |
| Two-host HA | 1 |
| mTLS | 1 |
| Shared rebuild and concurrent catch-up | 1 |

The ledger binds 32 persisted phases, eleven actual fleet starts and 439 runtime
artifacts. The callback proof includes a committed future-date suspension and
its exact persisted date-resume kind, identity, target and due time, with both
periodic reconciliation workers disabled. Existing process/checkpoint,
authorization, quarantine and no-effect assertions remain active.

`closure/review-pass1-full4-audit.json.gz` independently checks the passing XML,
all 44 proof records and their hashes, rollback, date callback and producer
identity. The immutable ledger SHA-256 is
`2c8fc365d0a10314002fc51f123718bedeacd4467c16a943b15b4aa79ef002ef`.
The closure directory also preserves ordinary suite results and the 47 passing
Redis ownership/cancellation checks. Only the fixture infrastructure readiness
budget changed from 15 to 60 seconds; actual TCP/PING admission and all
application/domain proof deadlines remain intact.

The user-approved watch limit was restored to 1,048,576 and read back by the
parent wrapper. No foreign process or fleet was changed. Earlier failed
attempts remain separate, nonqualifying records under `verification/`.

`packet-manifest.json` binds each gzip member's stored SHA-256, uncompressed
SHA-256, byte count and original source path. All 67 members were independently
decompressed and compared to their original bytes after packaging. The raw
run remains at
`/mnt/wsl/story49-qualification-20261021T072935Z/review-pass1-full4`.
