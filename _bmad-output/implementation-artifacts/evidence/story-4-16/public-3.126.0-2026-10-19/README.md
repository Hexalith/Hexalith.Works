# Story 4.16 — public EventStore 3.126.0

The canonical verifier exits **0**, status `passed-disposable-synthetic-parity-and-rollback`. The spec is done and sprint tracking is `review`. Every R1–R11 row derives its persisted, negative, restart and rollback proofs from named phase fields, with **zero skips**. The separate [retained-host source Debug lane](retained-source-debug/results.json) also passes **4/4**, with zero skips, on the final fixture, harness and exact 3.126.0 source bytes.

The owner selected public **3.126.0** on 2026-10-19. The nine selected package consumers match DLL entries in their official NuGet archives. The runtime export's **2,103** source/build files match the official commit and tag `v3.126.0`, producer `fcf78d6cafb6c0c9f52dc350a9a09aff54ded288`. Earlier 3.125.1 packets retain their historical scope.

## Exact canonical run

```bash
cd /tmp/story416-31260-isolated/workspace/platform
HexalithBuildsRoot=/tmp/story416-builds-b435f2e-public-source \
HexalithEventStoreRoot=/tmp/story416-sdk-3.126.0-public-source \
bash eng/verify-works-host.sh \
  --works-root /home/administrator/projects/hexalith/works \
  --runtime-sdk-root /tmp/story416-sdk-3.126.0-public-source \
  --artifacts-path /tmp/story416-31260-isolated-canonical-d12d987c
```

The command ran from **2026-10-19T16:09:49.182338+00:00** to **2026-10-19T16:34:06.834387+00:00** (24.3 minutes). The [command result](canonical-admission/command-result.json), [ledger](canonical-admission/ledger.json), logs, test XML, host starts and compressed persisted outcomes are retained. The [input recheck](input-binding.json) has zero mismatches.

The [isolated Platform receipt](isolated-platform.json) binds a detached worktree at `f0e33167fcdb5f539da159c54f7505b72a65c9b2` plus the four Works-owned overlays. Concurrent Platform/MCP CLI edits remain untouched in their original worktrees. [Builds provenance](builds-source-provenance.json) binds committed catalog/tooling `b435f2e3b3892e03f69322d24de6003b0baf7a7d`, also selected by Works' root-declared Builds dependency. Archive checkout line endings are recorded explicitly. No nested Git submodule was initialized.

## Results

| Check | Result |
| --- | --- |
| Evidence-gate unit tests | 52 passed |
| Agents Full contract | expected exit 1 `DependencyNotAvailable: EXT-HOST-1`; LocalScaffold exit 0 |
| Platform source Debug / package Release | 112 / 112 passed |
| Supporting consumer cases | 241 passed in 16 classes |
| Recovery / full rollback class | 4/4, 791.534 s |
| Historical lifecycle/tree/claim/security | 1/1, 138.956 s |
| Two-host HA, lost firing, stale callback, mTLS | 1/1, 89.429 s |
| Focused projection rebuild/catch-up | 1/1, 142.688 s |
| Retained Works AppHost source Debug recovery class | 4/4, 415.446 s; eleven persisted phases; post-run source and runtime hashes unchanged |
| Platform composition Works checks | 3/3 in the isolated snapshot |
| Deterministic Works suites, each mode | Unit 589, Property 3, Architecture 269, Integration 908; zero skips |
| Durable evidence | 32 unique phases; nine Platform starts and one retained rollback start |
| Complete restore | 723 Redis keys, 1070 runtime entries; independently re-observed inventories match |

The full restore verifies store values and expiries, offline Scheduler files, configuration/credential bytes and runtime deployment. The retained host executes delegated Executor queries and replays the Platform-written NoOp receipt. Resume, cancellation and expiry counts remain one. R6 additionally records that the primary stopped before the due date with zero resumes committed, before the surviving host recovers it.

## Review corrections

The three independent review lenses and root triage are recorded in the story's Review Triage Log (T197–T212). The [reviewed implementation diff](reviewed-implementation.diff.gz) is retained. Corrections require both export and receipt timestamps after observed degradation; authenticate the expected server before attributing certificate refusal; capture fixture hashes before compilation; bind package-consumer DLLs to archive entries; and prove no resume occurred before primary shutdown. The loopback reset, delayed-export, early-inventory and altered-extracted-DLL regressions pass. Two pre-existing Story 4.15 follow-ups were recorded in the deferred-work ledger.

## Attempts and scope

[Failed/cancelled attempts](failed-attempts/) remain preserved: concurrent catalog movement, in-flight Builds and Platform source edits, the required Commons dependency downgrade before catalog alignment, and a cancelled superseded live run. Deterministic preliminary failures were stale default restore assets and repository-discovery tests executed from an unsupported `/tmp` binary layout; restore and repository-local artifacts resolved them. The first retained-source Debug attempt passed its recovery body but failed the 30-second application-disposal bound (3/4); it remains recorded as failed. A fresh run of the unchanged lane passed 4/4, including cleanup. Final Debug and Release cases above all passed.

This qualifies disposable synthetic parity and rollback for the recorded graph. Production/real-data/new-catalog admission remains gated by AD-28 and the Maintainer's preview exception; broker/network policy hashes remain format-only until that gate. Full Agents qualification and Story 4.9 cutover are not claimed. No staging, commit, push or publication was performed.
