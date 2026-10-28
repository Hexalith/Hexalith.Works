# Five deferred host-migration qualifications — 2026-10-28

Baseline: `68dc24b32c3322fb8cc0667f10dbeb59c9d0ba9e` on `main`.
Scope and the five exact ledger closures are recorded in
[the specification](../../spec-five-deferred-host-migration-qualification.md).
The original evidence is preserved; this packet qualifies current replacement contracts.
It contains no fresh live Platform parity or production-admission claim.

| Check | Result |
| --- | --- |
| Final Integration Debug build | Exit 0, zero warnings/errors |
| Policy, explicit selection, shared harness and discovery controls | 121/121 pass |
| Actual Works startup, protected HTTP and four unprotected-route refusals | 6/6 pass |
| Valid configured policy-copy directory | 4/4 pass |
| Architecture Debug build | Exit 0, zero warnings/errors |
| Platform ownership guards | 39/39 pass |
| Evaluated Debug/Release dependency contracts | 1/1 pass |
| Full Architecture suite | 403/404 pass; only the existing revision-marker mismatch fails |
| Mutated control-plane, ACL and caller domains, committed and fixture-copy lanes | Six domain-mutation runs and two duplicate-caller runs each reject exactly the mutated Works configuration; three other configurations pass |
| Formatting and CRLF-aware whitespace check | Exit 0; format reports its generic workspace-load warning |

The broad blocker is
`P0_CurrentSourceRevisionMarkersMatchGitlinkAndConsumedCheckout`: both maintained markers name
`39a3d4dc98b786244e732d628a0828b9d19ad96e`, while the unchanged EventStore gitlink/checkout names
`36c78f3435d2d492a73f1b0707cf865c8735d740`. The test and both marker files are byte-identical to
baseline; the manifest records that comparison. The failure remains visible and its gate is unchanged.

`initial-host-inventory-http` is a nonqualifying test-development run: its plain duplicate
`/work/events` endpoint lacked Dapr topic metadata, so it was not a subscription for inventory
classification. The final test covers `/project`, `/dapr/config`, a Dapr actor callback and `/work/events` carrying real topic metadata; all four startup refusals pass.
The existing HTTP test still exercises the actual topic subscription and actor discovery.
The nonqualifying `review-analyzer-build-failure` records CA1062 while adding caller-identity assertions; the required null guard is present in the final passing build.

`commands.json` retains exact argument arrays, numeric exit codes, environment overrides and XML
totals. Commands ran from the repository root with `HexalithBuildsRoot` pointing to the root-declared
Builds checkout. The XML/log files are gzip-compressed. Historical absolute paths identify this run;
to replay a command, substitute the current repository and an owned output directory. Direct xUnit
invocations use `-class` and `-method`, not MTP project-level filters.

`harness-audit.json` discovers current harness callers from the complete Integration source inventory and records six live source classes with twelve calls. A direct testing-builder factory outside the shared harness is refused. This is a source audit; no live fact executes in this packet. The seven explicit facts are
checked by the executing selection test. Prior live qualification stays in the immutable Story 4.9
packets linked from [the hosting replacement map](../../../../docs/platform-hosting-cutover.md).

`mutations/` retains isolated copies of the selected Platform configurations. Reproduce a committed
configuration refusal by pointing `WORKS_PLATFORM_ROOT` at an owned directory containing one mutated
`DaprComponents` directory; reproduce the fixture-copy refusal by pointing
`WORKS_SDK_PROOF_DAPR_CONFIG_DIRECTORY` directly at that directory. Run only
`*PlatformAndFixturePoliciesPinEveryTrustDomain`. Each lane must report exactly one failure among
four cases. No committed dependency file was changed for these controls.

Run the portable integrity check from the repository root:

```bash
python3 _bmad-output/implementation-artifacts/evidence/five-deferred-host-migration-2026-10-28/verify.py
```

It verifies source/artifact and executed-assembly hashes, dependency revisions, the complete Integration source inventory, required command exits and exact qualifying XML totals. It also verifies no skips/errors/unexecuted cases, the unchanged baseline inputs and diagnostic of the one known broad failure, eight mutation refusals with exact test/filename/assertion identity, and five inserted ledger status/resolution pairs. The recorded Debug assemblies must still be available at their recorded relative paths; rebuilding with different outputs requires fresh qualification.
Removing those ten annotation lines restores the original ledger byte-for-byte.
