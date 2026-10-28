---
title: 'Close five deferred recovery admission checks'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five root-owned deferred recovery and build-admission items, counting repeated observations once:

1. P18-E2: make synthetic Redis restore verification account for captured hash fields expiring before restore or verification, while preserving exact live bytes, absolute deadlines and corruption refusal.
2. Verify that WorksSourceStreamReader bounds protection metadata before serialization and charges valid metadata against the complete context byte budget. Add focused controls for the retained contract.
3. Verify duplicate and case-variant tenant handling at the actual SDK control-index/catalog boundary; preserve fail-closed scope handling and healthy membership.
4. Settle the removed WorksDomainEventProcessor validation-exception finding against current SDK ownership and existing architecture/composition guards.
5. Settle the mixed-mode Contracts PackageVersion/NU1010 finding against current build policy with executing restore controls, covering both repeated ledger observations as one item.

Keep changes small and reversible. Reuse current SDK and test seams; preserve dependencies, runtime schemas, public APIs and producer qualification. Close only the selected ledger entries with current evidence. Run focused checks and independent review, then commit and push to main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: clean main at `3b28359c6d3c633999aa6992aab01479eaee19d8`; no submodule changes. Five bounded checks share recovery evidence admission; no unresolved product choices, new public APIs or irreversible implementation steps. Use the one-shot route.
- Read root build/test configuration, current architecture, the Hexalith LLM/Git/state baselines, and existing source-reader, catalog, composition, release-policy and Redis restore helpers. The current source reader already validates bounded metadata; the old event processor and conditional Contracts removal are gone. Verify these contracts rather than recreating removed adapters.
- Aspire baseline: `aspire ps --format Json` returned `[]`; exact Platform start exited 2. Output: `/tmp/works-five-recovery-admission-baseline.log`. Record the concrete startup failure separately from focused evidence.
- The exact baseline command was `aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json`. It failed with NU1605: Dapr hosting 13.6.2-beta.959 to 13.6.1-beta.935 and EventStore Aspire/ServiceDefaults 3.128.3 to 3.128.0. No AppHost started; no dependency or topology changes are part of this batch.
- Added captured hash snapshots and an atomic Redis script returning server time, exact field bytes and absolute deadlines. Verification projects the capture to that observation time, permits an absent hash only after every captured field expired, and retains strict corruption checks. The canonical fingerprint encoding for unchanged hashes remains identical.
- Expanded current SDK-reader, catalog and wakeup controls; added executing mixed-mode Restore checks. These four deferred claims are superseded by current contracts rather than requiring production changes. The two PackageVersion observations count once.
- First updated broad run: 151/153 passed. Both remaining failures isolated Redis retaining a physical hash after all its fields were logically expired; hashed inventory diagnostics identified exactly the complete-expiry fixture key. Inventory verification now independently observes that known captured hash, accepting an empty logical hash while refusing any resurrected field or wrong type.
- Review strengthened timing preconditions, dictionary admission before serialization, multi-envelope/page cumulative budget controls, and a missing-package sensitivity control for actual restore ordering. Exact field-deadline projection follows the strict read-expiry comparison in [Redis 7.4's implementation](https://raw.githubusercontent.com/redis/redis/7.4/src/t_hash.c); the atomic script also handles the listpack deadline lookup's equality sentinel. A synthetic equality control covers projection; it does not claim a live exact-millisecond observation.

## Review Triage Log

- Medium, patched: field visibility used a strict-greater comparison, wrongly excluding an exact-deadline read. Projection now includes equality and has a deterministic boundary control. Redis source also shows the listpack expiry lookup's different equality sentinel; atomic HGETALL visibility disambiguates it.
- Medium, patched: KeyExists cannot establish logical absence of an all-expired hash. Two real Redis controls reproduced inventory failures; fresh atomic logical inspection now removes only known captured empty hashes from inventory comparison. Extra/resurrected fields and wrong types remain refusals.
- Maybe-false, unverified medium, deferred: a final field may expire between key enumeration and DUMP/type acquisition, causing capture to refuse before the new atomic hash observation. This precedes the change and does not make a false capture pass; a deterministic gated acquisition/expiry control would settle its operational reachability. The selected item concerns fields present at capture expiring afterward. A separate ledger entry retains this capture-acquisition follow-up.
- Low, patched: a slow capture/restore could cross the initial four-second test deadline and make both theory rows exercise the same phase. Extended setup allowance and explicit post-capture/post-restore field-presence assertions establish the intended boundary.
- Medium, patched: invalid metadata values alone did not prove validation preceded serialization. An oversized-count dictionary now throws on enumeration; the production read must instead return the metadata-admission refusal.
- Medium, patched: the one-envelope budget control did not establish cumulative accounting. Two valid metadata-bearing envelopes now exercise same-page and multi-page exact totals and one-byte-short refusal, where either envelope alone fits.
- Low, patched: a package-free Restore did not establish ordering before package resolution. Added an absent package in an empty offline feed: all four invalid mode combinations return HXW0001 without NU1101, while the consistent-mode sensitivity control reaches NU1101 for that package.
- Medium, patched on correction recheck: the new awaited hash inventory observations enlarged a whole-key TTL race between the initial expected set and final actual-key cleanup. Both whole-key inventories now use one shared observation after the hash reads; the expiry theories also retain a whole-key expiration control alongside partial/complete hash expiration.

## Verification

- Original helper with the new `HashFieldExpirationAcrossRestorePreservesLiveEvidence` theory: two failures, at verification after expiry and restore after expiry respectively; `/tmp/works-five-recovery-admission-red.log` and `.xml`.
- `python3 -m unittest discover -s scripts/tests`: 87 passed; `/tmp/works-five-recovery-admission-tooling.log`. Focused release-policy suite: 11 passed, including four actual mixed-mode Restore refusals; `/tmp/works-five-recovery-admission-build-policy.log`.
- Debug integration and architecture project builds passed with zero warnings/errors; `/tmp/works-five-recovery-admission-debug-build.log` and `/tmp/works-five-recovery-admission-architecture-build.log`.
- `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -class '*PlatformHostingBoundaryTests' -class '*RuntimeAdapterGovernanceTests' -result-xml /tmp/works-five-recovery-admission-architecture.xml`: 66 passed, zero failures/skips/not-run.
- `uv tool run --from ruff==0.13.2 ruff check scripts/tests/test_release_build_policy.py`: passed.
- Final reviewed source: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1 --nologo` passed with zero warnings/errors; `/tmp/works-five-recovery-admission-final-build.log`.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -explicit off -class '*WorksSourceStreamReaderTests' -class '*WorksSdkRecoverySourceTests' -class '*WorksSdkRecoveryCompositionTests' -class '*WorksPlatformFleetRestoreTests' -class '*WorksSdkProofRedisOwnershipTests' -result-xml /tmp/works-five-recovery-admission-final.xml`: 157 passed, zero failures/skips/not-run, including real isolated Redis restoration and all corruption controls.
- The strengthened final release-policy suite passed all 11 tests after review; `/tmp/works-five-recovery-admission-reviewed-policy.log`. The earlier complete tooling suite passed all 87 tests; the changed policy module was rerun with its missing-package sensitivity control.
- `git -c core.whitespace=cr-at-eol diff --check` passed under the repository's CRLF policy. `npm run commitlint -- --edit /tmp/works-five-recovery-admission-commit.txt` passed for `fix: close five deferred recovery admission gaps`. Fetch showed no divergence from origin/main before commit.
- Independent review and correction recheck completed. Seven findings were patched (including the correction recheck's whole-key inventory race); the pre-existing unverified capture-acquisition finding was deferred. No new production contract, dependency or topology changes were made.
- [Retained evidence packet](evidence/five-deferred-recovery-admission-2026-10-28/README.md) contains original failing controls, intermediate inventory diagnostics, final results and a SHA-256 manifest binding test sources and artifacts. The exact Platform startup blocker remains separate; no fresh full Platform qualification is claimed.
