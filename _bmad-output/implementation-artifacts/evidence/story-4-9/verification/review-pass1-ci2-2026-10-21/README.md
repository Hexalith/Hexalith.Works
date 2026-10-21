# Review pass 1: second CI attempt, nonqualifying

The unchanged complete CI entry point exited 1 on 2026-10-21 after 6 minutes
41 seconds. Bootstrap, 110 Python checks, expected Agents Full refusal,
Release/package scaffold and the Platform package suite (114/114) passed.
Supporting Works consumers executed all 255 cases: 254 passed and one failed,
with no skipped or unexecuted cases.

`WorksPlatformFleetRestoreTests.RestoredInventoryRejectsEveryChangedValueTypeAndExpiry`
failed for `mutation: stream-pending` before its body: the disposable Redis
fixture exhausted its 60-second published-host readiness budget. Cancellation
surfaced at `WorksSdkProofRedis.StartAsync` line 68. The fixture preserved
failure and owned-resource cleanup. No runtime prebuild, archived readiness,
live parity or complete rollback followed; the ledger remains `incomplete`.

The parent then invoked the unchanged compiled method directly, running all
fourteen mutation cases: 14/14 passed, no skips, 69.194 seconds. During that
focused run an independent observation received `PONG` inside one current owned
container and through its actual published host binding. This observation
does not identify the failed attempt's root cause; the disposed failed
container was not retained. Exact focused XML/log and the observation are in
`closure/`. No source, assertion or timeout changed after either CI failure.

The wrapper restored the approved temporary watch quota to 1,048,576 with
successful read-back. Platform remained frozen, and no foreign resources were
changed. A third fresh complete CI run follows the successful focused check;
this failed attempt cannot qualify parity. Agents Full remains unavailable and
is not a Works prerequisite; no component runtime qualification is claimed.

`packet-manifest.json` binds all 30 gzip members by stored and uncompressed
SHA-256, byte count and original path. Raw results remain at
`/mnt/wsl/story49-qualification-20261021T072935Z/review-pass1-ci2/gate`.
