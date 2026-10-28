# Five deferred SDK boundary checks — 2026-10-28

The [spec](../../spec-five-deferred-sdk-boundary-verification.md) and [current boundary map](../../../../docs/sdk-boundary-verification.md) identify the five independent ledger findings. The baseline is `0f820eed6d9dfb0b1141f57b3827a3af0b16273d` on clean main. No dependency revision changes are part of this batch.

The packet retains exact commands, exit codes, compressed build/test logs, xUnit result XML, source/assembly digests and dependency commits. Final validation: the complete Debug Integration project build passes with zero warnings/errors; focused consumer/harness classes pass 268/268; deterministic Integration passes 1,055/1,055; Architecture passes 404/404. Every qualifying test has zero failures/errors/skips/not-run. The focused suite is a subset, not additional unique cases. Formatting and Conventional Commit validation pass.

The required baseline `aspire ps --format Json` returned `[]`. The exact Platform start command is recorded in the command manifest; it exited 2 with existing NU1605 package downgrades (EventStore Aspire/ServiceDefaults 3.128.3 versus 3.128.0, and Dapr hosting). This does not qualify fresh live Platform parity. Consumer HTTP tests run the real thin Works SDK host in process with synthetic dependencies. The actor proof uses an unused actor deactivation route, without a sidecar. Failure accounting injects a transient failure at the SDK fold seam before real Works folding; immutable evidence policy remains unchanged.


The [independent review](review.md) records all seven findings and their corrections. No finding is deferred. Exactly five status/resolution pairs were added to the ledger; removing them restores the baseline bytes exactly. Root dependency checkouts are unchanged and clean. Fetch confirmed zero divergence from origin/main before commit.

Run `python3 _bmad-output/implementation-artifacts/evidence/five-deferred-sdk-boundary-2026-10-28/verify.py` from the recorded workspace to check artifacts, exercised source/binary hashes, exact qualifying test counts and the additive ledger changes. It requires the recorded build outputs; retained compressed XML/logs remain independently readable. Preliminary compile, fixture and query-credential failures remain nonqualifying in commands.json and their raw logs; they are not replaced by the final results.

Independent correction recheck: all seven findings resolved, no new material corrections or deferred work. The reviewer ran the integrity verifier and independently confirmed the final XML, thirteen named regression cases, 421 source hashes, 49 binary hashes and exactly five additive ledger closures.
