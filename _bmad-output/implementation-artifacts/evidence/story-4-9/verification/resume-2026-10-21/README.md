# Story 4.9 resumed verification — 2026-10-21

Final post-cutover acceptance remains blocked. The canonical SDK composition, removed hosting projects, regression guards, accepted prerequisite stories and immutable pre-cutover proof are unchanged. All eleven rows in the fresh full-gate ledger remain open; thirteen persisted recovery phases and a passing archived-host startup do not establish the complete gate or rollback.

The complete fourth attempt used this exact command, with the environment recorded in `gate-attempt4-command.json`:

```bash
HexalithBuildsRoot=/mnt/wsl/story49-qualification-20261021T072935Z/Hexalith.Builds-source \
HexalithEventStoreRoot=/mnt/wsl/story49-qualification-20261021T072935Z/Hexalith.EventStore-source \
TMPDIR=/mnt/wsl/story49-qualification-20261021T072935Z/tmp \
XDG_DATA_HOME=/mnt/wsl/story49-qualification-20261021T072935Z/xdg-data \
PYTHONDONTWRITEBYTECODE=1 \
/mnt/wsl/story49-qualification-20261021T072935Z/platform-source/eng/verify-works-host.sh \
  --works-root /home/administrator/projects/hexalith/works \
  --runtime-sdk-root /mnt/wsl/story49-qualification-20261021T072935Z/Hexalith.EventStore-source \
  --platform-source-binding /mnt/wsl/story49-qualification-20261021T072935Z/platform-source.json \
  --artifacts-path /mnt/wsl/story49-qualification-20261021T072935Z/attempt4
```

It exited 1 in `platform-recovery`: two of four facts passed, with no skips. The long recovery fact completed thirteen persisted phases before the actual wrong-purpose receiver-refusal request exceeded its unchanged transport deadline without an HTTP response. That timeout's cause remains unresolved. The callback fact separately failed because Sentry could not allocate a credentials filesystem watch; this does not establish the cause of the earlier timeout. Historical/security, HA, mTLS, projection/rebuild and complete-fleet rollback lanes did not run in this attempt.

Preparation passed 90 Python checks, the expected Agents refusal, the Agents scaffold build, 114 Platform tests in each dependency mode, both fresh Works consumer builds, 255 supporting facts, official public EventStore source/package qualification, six serial runtime builds and archived readiness (1/1, 34.554 s). The ledger binds 439 runtime artifacts. Bound revisions are Works `9dbd2f07d3313b6df3c491e9cf3510b87b32fe65`, Platform `cec03d32adfce691155b2e7c85bba434e6294da2`, public EventStore `fcf78d6cafb6c0c9f52dc350a9a09aff54ded288` (3.126.0), and Builds `b435f2e3b3892e03f69322d24de6003b0baf7a7d`. Their immutable manifest hashes and exact command/result records are retained; no source or binary export is duplicated here.

Both the host and an isolated container report a 1,048,576-watch limit. Observed host watches increased from 1,047,950 to 1,048,546, leaving 30 slots; host and temporary filesystems had about 46 GB free and millions of free inodes. A focused archived-readiness retry with only `DOTNET_USE_POLLING_FILE_WATCHER=1` changed reproduced watch-allocation failures in Go Placement and Scheduler (1 failed, 17.492 s). Its original runtime, consumer and source bytes remained unchanged. That retry stopped before callback/operations execution. The quota sysctls are root-owned and not writable by UID 1000. No limits, foreign processes or foreign fleets were changed.

Fresh source-backed Debug solution restore/build subsequently passed. The four ordinary suites ran sequentially with polling enabled: unit 589/589; property 3/3 (300 generated cases); architecture 270/270; integration 728 executed with zero failures/skips and exactly seven explicit topology facts not run (735 discovered, 77.610 s). Those seven still require the unpassed complete gate. Binary relocation inventories prove identical compiled bytes. The initial architecture run inherited the SDK export root and failed two default-layout assertions; the identical fresh executable passed after that runner-only override was removed. Both results are preserved.

All earlier attempts remain nonqualifying: attempt 1 hit the root .NET file-host cache ENOSPC; attempt 2 refused an externally removed SDK export; attempt 3 passed 253/255 supporting facts with two Redis host-readiness timeouts. Their exact focused theory retry passed 14/14 before the sequential fourth attempt passed all 255 supporting facts. Stale existing-binary and cancelled overlapping-suite outputs are preserved separately and establish no acceptance.

`summary.json` selects the final ordinary results and records the unresolved gate. `packet-manifest.json` binds stored bytes, their decompressed bytes and original paths; the full relevant failure streams and XML are compressed. The accepted pre-cutover packet independently retained all 45 file hashes, 44/44 proof kinds and complete rollback. `postGatePending` remains true and the fourth execution task remains unchecked. Restore sufficient watch capacity, then rerun the complete canonical verifier without skipped required lanes and with complete rollback before accepting the cutover.
