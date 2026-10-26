# Additional hosted CI corrections, 2026-10-26

Exact-current hosted run 40370134405 failed before complete qualification: its ordinary build raced duplicate Platform.Custody contexts, while canonical parity hit the anonymous GitHub source API rate limit. The preserved logs and tests-first controls identify and verify the two bounded corrections.

The real SDK adds transitive source references during target execution, after the existing evaluation-time property forwarding. The new hook gives these edges the same selected SDK/configuration identity; a fresh parallel rebuild compiles each affected source project once. Internal Works project identities retain their existing behavior.

Official Git API requests may use GITHUB_TOKEN, scoped to the fixed producer repository path and excluded from redirects and live runtime environments. CI supplies its read-only job token. Commit/tree/tag/source bounds remain strict. No credential value is retained.

results.json records the exact local checks and code hashes. These records supply no complete full, rollback, hosted CI or final closure acceptance. The next full gate captures these corrected inputs separately.
