# Story 4.16 — review iteration 6 corrections

T213–T219 are implemented. The fresh canonical verifier exits **0**, status `passed-disposable-synthetic-parity-and-rollback`: every R1–R11 row derives all four proofs from **32 unique phases**, with **zero skips**, nine Platform starts and one retained rollback start. Independent review and root acceptance are complete; the spec is done and sprint tracking is review.

The [command result](canonical-admission/command-result.json), [ledger](canonical-admission/ledger.json), [derived result](canonical-admission/result.json), command logs, executed test XML, host starts and compressed persisted outcomes retain the exact run. It ran from **2026-10-20T07:34:24.125576+00:00** to **2026-10-20T08:07:24.884579+00:00** (33.0 minutes):

```bash
cd /tmp/story416-review6-69hay8nu/workspace/platform
HexalithBuildsRoot=/tmp/story416-builds-b435f2e-public-source \
HexalithEventStoreRoot=/tmp/story416-sdk-3.126.0-public-source \
bash eng/verify-works-host.sh \
  --works-root /home/administrator/projects/hexalith/works \
  --runtime-sdk-root /tmp/story416-sdk-3.126.0-public-source \
  --artifacts-path /tmp/story416-review6-69hay8nu/canonical-admission-attempt2
```

[Isolation provenance](isolated-platform.json) binds an ordinary-source export of Platform `f0e33167fcdb5f539da159c54f7505b72a65c9b2` plus the four reviewed overlays, the same graph used by the preceding qualification. Both the isolated Platform and SDK expose the [bound Builds export](builds-source-provenance.json), commit `b435f2e3b3892e03f69322d24de6003b0baf7a7d`; all 165 recorded catalog/tooling files still match. The isolated workspace exposes the selected Works checkout at `../works` as its topology tests require. No Git worktree, branch, nested submodule, staging, commit, push or publication was performed by this implementation run.

The [input recheck](input-binding.json) has zero mismatches for the complete isolated Platform inputs, current Works consumer and fixture membership/bytes, and current reviewed Platform overlays, configuration and verifier helpers. It explicitly lists the six unrelated current Platform source differences outside that isolated graph; those edits remain untouched and unqualified by this packet. The [root evidence audit](root-independent-audit.json) independently validates all 44 row proofs, 48 record bindings, current Works inputs, command-log hashes, public producers and executed results.

| Check | Result |
| --- | --- |
| Evidence-gate tests | 54 passed |
| Three focused mutations | Each weakened implementation refused |
| Agents contract | Full expected exit 1 `EXT-HOST-1`; LocalScaffold exit 0 |
| Platform source Debug / package Release | 112 / 112 passed |
| Works source Debug / package Release consumers | Both builds passed; Platform runtime DLLs byte-identical |
| Supporting consumer | 241 passed in 16 classes |
| Public producer | Nine package DLLs archive-bound; 2,103 runtime/build files match official `v3.126.0`, producer `fcf78d6cafb6c0c9f52dc350a9a09aff54ded288` |
| Recovery and complete rollback | 4/4, 811.212 s |
| Historical lifecycle/tree/claim/security/restart | 1/1, 137.906 s |
| Two-host HA, lost firing, stale callback and mTLS | 1/1, 103.186 s |
| Focused projection rebuild/automatic catch-up | 1/1, 164.085 s |
| Focused composition checks | 3/3 |
| Complete fleet restore | 684 Redis keys and 1,070 runtime entries; queries and receipt replay usable; one date resume, one child cancellation, one child expiry |

T213 compares the complete end-of-run fixture inventory with the pre-command baseline, detecting added, removed and edited sources. T215 extracts the actual published-producer loop and tests synthetic restore assets, public metadata, archive entries and an altered consumer DLL. T219 independently exercises early receipt versus late export and refuses boolean/float exporter times. The [mutation results](mutation-results.json) show that removing each substantive safeguard fails its targeted regression. T214 documents the bound catalog export, both isolated catalog paths, the Works sibling path and both build environment variables. T216–T218 correct the current public version, conditional gitlink guidance and trust-probe flag semantics.

[Attempt 1](failed-attempts/attempt1-missing-sibling/) remains failed: eight Platform topology cases could not find the omitted `../works` sibling in the fresh isolation workspace, before any live parity execution. Adding the owned sibling link and documenting it resolved the setup; the unchanged verifier and fixture then passed attempt 2 in a new empty artifacts directory. A preliminary edit-helper path error made no source changes and is recorded separately. Earlier evidence is preserved with its original scope.

This qualifies disposable synthetic parity and rollback on the recorded graph. T220–T222 remain the accepted deferred work. Production/real-data/new-catalog admission, AD-28, Full Agents qualification and Story 4.9 cutover remain separate. The packet retains no runtime credential files or binary build trees.

## Independent review closeout

Three review lenses returned 13 findings; every verdict and its evidence is recorded in the spec's review iteration 7 table. T232 corrected this packet's scanner to recognize captured app-channel tokens and authority keys, including gzip content, while preserving ordinary SHA-256 fingerprints. All five [focused regressions](tools/test_secret_scan.py) pass; the retained packet scans clean. The canonical runtime, consumer, fixture and verifier bytes did not change after their successful run. The final root input recheck has zero mismatches.

T235 records one new pre-existing Story 4.15 test follow-up: cross-tenant preservation and ETag-conflict coverage for the suspend-subscription discovery writer. T220–T222 retain their earlier dispositions. The [root closeout](root-review-closeout.json) binds the final spec/tracking bytes and records the completed review and matrix audit. No local commit was created under the story's explicit no-staging/no-commit/no-push scope.
