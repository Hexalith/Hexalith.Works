# Parent acceptance audit after review loop 1 — 2026-10-14

The parent independently checked the current per-case XML, all 28 archive hash pairs and the ten candidate identities/dependency pins/cache archive hashes. SDK `.9` and test-only Platform `.4` have 630 package integration plus 2 disjoint genuine local-host cases passing: 632 distinct, zero failures or skips. Producer client 177, producer domain 15, source consumer 56 and architecture 269 pass. The enabled projection class passes 24 cases and complete isolated restore/replay passes one; added bounded-history/capacity/orphan-parking cases are in-memory restart proofs, while the original enabled named Dapr/Redis cases and complete restore use real persistence.

| Frozen scenario | Current executed proof |
| --- | --- |
| Live/redelivery/restart | Ordinary complete family/checkpoint; conflicting restart; named two-aggregate concurrency; 65-member bounded-history growth and restart. |
| Concurrent rebuild | Capture and complete promotion; concurrent named delivery; abort preserves serving generation; producer interrupted Capturing/Building stale reads. |
| Executor/coordinator | Canonical protected HTTP and actual Platform reader admission, published Parties client and unchanged authoritative runtime with synthetic data; permitted coordinator and sorted scoped queue. |
| Wrong actor/tenant/origin | Protected authority negative cases plus zero Works reads; named ingress rejection preserves persisted state/checkpoint; all query route spellings protected. |
| Corrupt/gapped/conflicting evidence | Durable conflicts, exact transient-gap retry, first-delivery parking surviving unrelated work/restart, capacity stays pending/degraded without poison, conservative reminder/cascade readers. |
| Rollback | Exactly one selected ordinary writer in both modes; actual isolated complete keyspace restore and retained-source legacy replay. |

The parent reran the original independent review harness against corrected Release binaries. It now accepts the 42nd item with no pending journal, reports first-delivery parking as unavailable/degraded on list and detail, resolves a successful transient-gap retry, and marks both interrupted capture read APIs stale. The harness changed only how it prints the now-unavailable query result. Exact output and hashes are retained in the linked audit.

This is local implementation acceptance evidence pending the second independent review. It does not establish merge/release readiness. Private candidate publication/public-pin, live authority realm-template provisioning, deployed fleet/source archive restore and production AD-28/4.16 gates remain. The additional 21 recovery/cascade/mTLS cases retain historical `.8`/`.3` provenance; they are not included in the 632 current distinct cases. Protocol-only authority tests do not establish live Keycloak validation. One SDK post-pack source difference removes a trailing blank line only; the immutable candidate was not overwritten.

[Machine-readable counts, exact matrix test names, package identities and independent reproduction](parent-loop1-acceptance-audit-2026-10-14.json). [Implementation details and archived proof](review-loop1-implementation-2026-10-14.md).
