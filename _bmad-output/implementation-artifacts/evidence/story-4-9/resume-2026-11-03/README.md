# Story 4.9 resumed qualification and publication handoff — 2026-11-03

**Status: in progress.** Safe local prerequisites and candidate preparation are ready for review. Final canonical full, actual CI, R1–R11/44 proofs and complete-fleet rollback remain unqualified. The story/sprint stay in progress and the final qualification task stays unchecked. No push, publication, commit, staging, dependency checkout update or production admission occurred.

## Applied changes

- Works bootstrap/CI include root-declared McpCli and Memories, with regression coverage for actual copied dependency links and explicit source roots. The optional `--runtime-sdk-root` accepts an independently obtained filesystem export without fetching local tags; it copies ordinary source into the run-owned workspace, records `sdkCommit:null`, and preserves mandatory canonical public-producer qualification. It neither changes the package selection nor accepts candidate tooling.
- The reviewed local source pairing and architecture markers identify EventStore `98a298a084ec6dedecf99e0abefd649051038376`. The live Release pin remains public **4.0.0**, producer `653124dced81830e932edb7738a78ee61104d55a`. No source/package behavioral equivalence is claimed.
- Platform passes selected-child compilation the parent's actual SDK root, version, configuration, source/package switches and artifacts root. Exact SDK bytes and loaded-MVID enforcement remain unchanged. The original refusal is preserved: equal 4.0.0 metadata masked different embedded PDB paths and DLL hashes. Matching the real compilation context passes native model tests without changing measured parent DLL bytes.
- Platform withdraws only `GET /api/v1/commands/status/workload/*/*`. It already denied the actual two-parameter workload status endpoint under Dapr's trie semantics. Human status, mTLS, caller/policy identities, other grants and application authorization remain. The unmatched one-segment workload prefix becomes denied, a narrowing. Four existing package-capability conditionals explicitly recognize public SDK **4.0.2**; this prepares its existing catalog APIs and does not select a new accepted tuple.

The exact applied owner diffs are in `handoff/applied-works.diff` and `handoff/applied-platform.diff`. These are review artifacts, not publication evidence.

## Latest canonical full: exact command and terminal result

Attempt 6: `/tmp/story49-current-20261103-e50fzbzy/full`. This completed before the final ACL withdrawal and 4.0.2 preparation; it is not evidence for all final inputs. The retained driver invocation was:

```bash
python3 /tmp/story49-full-current-driver.py > /tmp/story49-full-current-driver-attempt6.log 2>&1
```

The driver used the existing bootstrap and changed only its returned profile to `full`. Its exact canonical argv was:

```bash
bash /tmp/story49-current-20261103-e50fzbzy/full/workspace/platform/eng/verify-works-host.sh --profile full --works-root /home/administrator/projects/hexalith/works --runtime-sdk-root /tmp/story49-current-20261103-e50fzbzy/full/workspace/Hexalith.EventStore --platform-source-binding /tmp/story49-current-20261103-e50fzbzy/full/platform-source-binding.json --artifacts-path /tmp/story49-current-20261103-e50fzbzy/full/gate
```

The environment used run-owned TMPDIR, fresh `/tmp/story49-current-20261103-e50fzbzy/packages` for both `NUGET_PACKAGES` and `RestorePackagesPath`, official-only `RestoreSources=https://api.nuget.org/v3/index.json`, the bootstrap's explicit root dependencies, and verified published Builds tool root `/tmp/works-published-builds.0YYy7o/verified/published/tools/net10.0/any`. Invocation, bootstrap, source binding, commands, XML and terminal ledger are retained under `attempts/6/`; drivers are retained separately.

**Exit 1; ledger `incomplete`: `retained-host-preflight failed`.** The real retained receiver exited after mandatory catalog bootstrap refused `Topology: undeclared-acl-operation`. Control-plane health passed. Before refusal: 159 owner helper controls, 14 component controls, both 148-case Platform modes, both Works consumers, 384 supporting cases, all nine public producer/source qualifications, six runtime prebuilds, selected-binary fact and telemetry fact passed. Recovery/live parity and rollback did not run; every R1–R11 row remains open. Actual CI was not launched.

Attempts 1–5 remain nonqualifying: foreign temporary ancestor catalog in a negative fixture; missing Memories bootstrap link; selected-child SDK identity mismatch; deliberate owned stop to include root/mode correction; shared-cache provenance refusal. The shared cache was not purged, overwritten or relabeled. Independent official downloads found eight same-version candidate archives differed from official producer bytes. Attempt 6's fresh public-only cache passed that guard.

## Exact refusal diagnosis and bounded policy proof

The retained fixture deliberately loads current checked Platform policy into an owned directory; historical archive bytes were not changed. Offline replay uses the actual captured SDK 4.0.0 observation and activation with the real public Contracts validator. Exactly two individual grants refuse: the nested workload-status grant and `POST /api/v1/trusted-effects`. Removing only the nested grant leaves the trusted-effects refusal; removing both is an isolated diagnostic acceptance, **not** the implementation. The trusted-effects grant remains in the applied policy.

The captured public 4.0.0 controller lacks the SDK technical-controller annotation. Independently verified public **4.0.2**, producer `f06abada8dc3aab4a5fd240847fd680d515c98a5`, contains it in source and the actual Gateway DLL metadata, retaining `DaprInternal:eventstore:trusted-effect` authorization. All nine consumer package archives have official repository signatures and this producer identity. The canonical qualifier verifies the official `v4.0.2` reference and all **2,597** runtime/build inputs by exact official commit/tree/blob identities. A second qualification binds the actual candidate consumer DLLs to those official archives and the candidate's explicitly unaccepted catalog inputs. No new SDK publication is required for this candidate.

The policy replay measures all **69 observed receiver endpoints**, with **261 caller/method comparisons**: fixed-depth route intersections and concrete request decisions are unchanged. Eight exact requests retain human status, two-parameter denial, unknown-caller denial, neighboring-path denial, DELETE denial, trusted-effects sidecar admission, and existing POST prefix admission; the unmatched one-segment workload prefix narrows from allow to deny. The SDK's own trie matching and denial rules are unchanged. This proves the observed inventory and listed cases only, not every URI or native runtime authorization. Sanitized diagnostics retain paths, decisions and source hashes; private observations, activation/custody/key files are not copied.

## Verification and limits

| Scope | Result |
| --- | --- |
| Ordinary reviewed source, before final verifier/ACL/cohort preparation | Source solution gate: 136 commands, build 0 warnings/errors; unit 597, property 3, architecture 404, integration 1,403 pass. Nine explicit topology facts not run. These are not final-input full-suite evidence. |
| Architecture restore context | Initial 403/404 used stale default restore assets; fresh Debug restore made the same binary pass 404/404. |
| Earlier SDK identity diagnosis | 1 native identity case and all 14 model cases pass; repeated with hostile ambient SDK roots/source flags. Parent Client hash unchanged. |
| Final live policy at accepted SDK 4.0.0 | Fresh bootstrap/package build: Platform **155/155**; rebuilt Works copied-policy, ingress and captured-file controls **37/37**, including all 12 DaprConfigCopy cases; no warnings/errors/failures/skips. |
| Bootstrap controls | Full Works script suite **124/124** before one final missing-export negative; final bootstrap subset **19/19**, including that new negative. Original export red is retained. |
| Platform helpers | **159/159**; full/CI × default/explicit SDK root controls cover conflicting ambient context. |
| SDK 4.0.2 executable candidate | Platform package build clean and **155/155** tests; Works Release build clean; copied-policy/ingress/captured-file **37/37**. Supporting first run **383/384**, one Redis host-readiness timeout; the exact unchanged failed roundtrip then passes **1/1**. Original failure is retained; no clean 384-case rerun is claimed. |
| Candidate tooling | Existing owner catalog generator validates SDK 4.0.2, selected Dapr/Aspire compatibility and generated catalog. Nine executed SDK consumer DLLs match official archives; source qualification passes. |
| Local syntax/style | Root Ruff, bytecode and shell syntax pass; shellcheck unavailable. Existing broad Platform Ruff backlog is outside this change; focused syntax/error checks pass. Default `git diff --check` reports CRLF additions as trailing whitespace. Both owners pass explicit `blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol`; the default check is not called green. |

Two discarded candidate checks remain explicit: the initial isolated artifacts directory was outside the repo ancestry and caused root-discovery failures; relocating artifacts under each copied repository made all 155 cases pass. A combined class-and-method retry filter selected zero cases and supplies no evidence; separate 37-case and 1-case commands supply the actual results. An early direct Platform build under the outer Works EditorConfig hit pre-existing CA2007; repository-owned isolated builds pass without suppressing analyzers.

## Prepared publication handoff, in order

1. **Already published SDK:** retain exact public 4.0.2 source/package producer above. `public-sdk-4.0.2/` retains package/signature/source/compiled-annotation observations. The ordinary filesystem export is `/tmp/story49-public402-20261103-dgi6bg93/export/Hexalith.EventStore-f06abada8dc3aab4a5fd240847fd680d515c98a5`; no root checkout/ref was changed. Candidate workspace `/tmp/story49-public402-candidate-20261103` is executable preparation, with measured source inventories, commands, XML and limits retained under `candidate-4.0.2/`.
2. **Owning Builds source and audit:** review `handoff/builds-candidate-pins.patch` (only central EventStore 4.0.0 → 4.0.2). Before feed discovery, create the separately authorized source commit according to Builds guidance. Preserve the prior schema-2 audit, then run the real owner generator and validator; do not hand-edit provenance or invent a producer revision:

   ```powershell
   Copy-Item ./Tools/package-version-audit.json ./artifacts/package-version-audit.before-4.0.2.json
   pwsh -NoProfile -File ./Tools/audit-central-package-versions.ps1 -PriorAuditPath ./artifacts/package-version-audit.before-4.0.2.json -ChangedFamily hexalith-eventstore -OutputPath ./Tools/package-version-audit.json
   pwsh -NoProfile -File ./Tools/validate-package-version-audit.ps1
   pwsh -NoProfile -File ./Tools/test-package-version-audit-generator.ps1
   pwsh -NoProfile -File ./Tools/test-package-version-audit-validator.ps1
   ```

   Review actual feed observations and commit the audit separately as a descendant, following Builds' pinned commitlint and release workflow. The local candidate intentionally has no generated accepted audit or new owner commit.
3. **Genuine Builds publication:** run the owning G-4 source/package controls and normal release workflow. Obtain a real new release/tool archive and official inventory containing the selected 4.0.2 catalog. Record its actual producer revision, normalized catalog blob/SHA256, publication version/tag, archive/tooling/inventory SHA256 and official inventory locator. Do not reuse 4.43.2 publication fields with different bytes.
4. **Owning Platform authority and Works pins:** update `eng/builds-tuple.json`, `.config/dotnet-tools.json` and `hexalith-platform.json` only from those actual publication observations and requalify the tuple. Review/apply `handoff/works-candidate-pins.patch`: the two Works default pins, integration assembly pinned-version metadata, source/debug Release pairing, and two Release producer documents change to verified 4.0.2; current source remains 98a298. Candidate documentation limits archive verification to the nine consumed packages and explicitly leaves the new pair identity preflight pending; it does not inherit the old fourteen-archive 4.0.0 claim. The documentation-only scope correction is separately recorded; original candidate inventory remains historical. Preserve all current policy/identity guards and accepted source exceptions. Root gitlinks/checkouts may be aligned only through separately authorized owning version-control work.
5. **Fresh final qualification:** rerun ordinary source checks on final reviewed inputs. Use the same bootstrap with `runtime_sdk_root=<qualified official export>`; change only its returned canonical profile argument to `full`, as the retained full driver does. After full passes every frozen matrix row, all 44 proof kinds and complete-fleet rollback with final input closure, run the actual entry point in a new directory:

   ```bash
   bash scripts/verify-platform-parity-ci.sh --runtime-sdk-root /absolute/path/to/qualified-official-export --artifacts-path /tmp/new-unused-story49-ci
   ```

   Preserve failed attempts, use a fresh public-only package cache and owned TMPDIR, and independently audit the full and CI ledgers/producer closure before marking the task complete.

Official latest-publication metadata was refreshed at **2026-11-03T07:58:25.887812Z**: Builds remains **4.43.2**, Platform **1.1.3**. The current accepted Builds tuple binds producer `6585f5860e87c146c26aa066d3a52b94eb56f271`, whose catalog selects SDK 4.0.0. No suitable new owning Builds publication was available at that observation. Its exact source/catalog/publication checks remain enforced. The pending owner publication is the remaining authority dependency; local candidate success cannot replace it.

`manifest.json` binds every compressed artifact and decoded native bytes, plus the plain report and handoff documents. Runtime binaries, package archives, private catalog keys/custody and fleet snapshots remain private. Both historical hashes remain unchanged: manifest `b8cbf1bb6490b6fc6bb8ad7aa12842c202fb95a9877afd7a0a5fa1caf79caca8`, archive `5ce3247ded7ab59ff70d5b25896fed1826b8d78b0728a2d1a4c1f2dceb7ae696`.
