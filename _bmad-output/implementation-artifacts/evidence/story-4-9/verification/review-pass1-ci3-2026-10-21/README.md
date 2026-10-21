# Review pass 1: third CI attempt, nonqualifying

The complete unchanged CI entry point exited 1 on 2026-10-21 after 6 minutes
41 seconds. Preparation and the 114-test Platform package suite passed.
Supporting Works consumers executed all 255 cases: 254 passed, one failed,
zero skips or unexecuted cases. No live parity or complete rollback followed.

The `string` mutation of
`WorksPlatformFleetRestoreTests.RestoredInventoryRejectsEveryChangedValueTypeAndExpiry`
failed before its body when `WorksSdkProofRedis.StartAsync` exhausted the
60-second published-host readiness budget. An independent read-only observer
recorded the owned container running and answering internal `redis-cli PING`
while its published `127.0.0.1:49214` endpoint refused TCP connections. It never
recorded a successful host response before disposal. This establishes the
host-binding symptom for this attempt, without identifying its underlying
Docker Desktop/WSL cause. The observer and exact JSONL are in `closure/`.

A subsequent isolated environment probe created and removed twenty containers
with automatic port assignment and ten with explicit loopback assignment. All
thirty answered real host PING, so that probe did not reproduce the intermittent
failure. Its script, actual bindings, requests, replies and cleanup results are
preserved. No existing container or global Docker configuration changed.

The parent prepared a local qualification-only adapter to request free explicit
loopback ports for the exact labelled `works-sdk415-*` Redis creation command.
It forwards every other Docker request unchanged, retains the fixture's actual
TCP/PING readiness and complete state assertions, and logs every original and
effective argument list. No repository source or CI job was changed for this
environment experiment. A fresh fourteen-case focus must pass before another
complete CI run. This failed packet remains nonqualifying.

The wrapper restored the approved temporary watch quota to 1,048,576 with
successful read-back. Platform edits stayed paused. Agents Full remains
unavailable and is not a Works prerequisite; no Agents qualification is claimed.
`packet-manifest.json` binds 31 gzip members by stored/uncompressed SHA-256 and
byte count. Raw results remain at
`/mnt/wsl/story49-qualification-20261021T072935Z/review-pass1-ci3/gate`.
