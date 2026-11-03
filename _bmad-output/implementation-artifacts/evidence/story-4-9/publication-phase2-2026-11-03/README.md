# Story 4.9 — published owners adopted, SDK prerequisite pending

This packet is **nonqualifying**. Builds and Platform were genuinely published,
Works adopted their SDK 4.0.4 tuple, and ordinary source verification passed.
The fresh full gate then refused retained-host readiness because the public
SDK's raw actor-placement metadata request omitted its sidecar API credential.
A minimal SDK-owner fix is prepared and tested. It is unpublished; no replacement
tuple, full parity, actual CI, rollback, or production admission is accepted.
Story/sprint remain in progress and the final qualification task remains unchecked.

The earlier [resume packet](../resume-2026-11-03/README.md) and
[phase 1 packet](../publication-phase1-2026-11-03/README.md), including the original
wait decision, remain unchanged. The later user instruction “merge branch”
superseded that wait. The root agent announced and used the account's owner
exemption with an exact-head guard to merge Platform PR 15. No code-owner
approval is claimed and no protection setting changed. The exact authorization,
validated full merge message, command, and result are retained in `merge-steering/`.

| Published owner | Genuine identity | Qualification/publication |
| --- | --- | --- |
| Builds 4.43.3 | `fc743715f7d0993392dee34b2ef9c4eb85cdd399` | Protected release `41606852507`, published `2026-11-03T10:19:01Z`; phase 1 retains signed package, embedded catalog, official inventory, and producer-run bindings |
| Platform v1.1.4 | `0635826905d88049bc8a9531206178e289ebb316` | Actual-merge tuple `41623840840` and catalog `41623613559` passed; existing source/tag release convention published `2026-11-03T11:13:37Z` |
| EventStore 4.0.4 | `326bfef884543cdbcaf96de7f4c4ad378dbad6d7` | Nine official consumer archives/signatures and all 2,597 runtime/build inputs qualified; the newly observed readiness defect prevents final consumer acceptance |

The original Platform checkout was aligned after validating and saving its
nine-file overlay in selective stash `07dce7643f01c07c6be285551600303108a20804`.
No nested dependencies were initialized. Works adopted genuine SDK 4.0.4 pins
while keeping reviewed developer SDK source
`98a298a084ec6dedecf99e0abefd649051038376` distinct from the public producer.
The original-workspace source/pin preflight passed three times. Exact command,
exit, result-hash, and available environment records are retained; the third
replay records the explicit tool override and nonsecret root/mode values. The
first two runs inherited environment values that were not separately captured.
Identity preflight does not establish source/package behavioral equivalence.

Final-input source verification passed **136 commands**, with zero build
warnings/errors, and **597 unit**, **3 property**, **404 architecture**, and
**1,403 integration** facts. All executed facts passed with zero errors, failures,
or skips. Exactly nine explicit integration facts were NotRun in the ordinary
suite; complete canonical gates must execute them. The only later root changes
were the bootstrap copy correction and its Python regression. Independent
complete-diff comparison proves runtime, C# tests, owner source, and frozen intent
unchanged, so the ordinary evidence retains that exact scope. Revised Works
script controls pass **126/126**. Ruff, bytecode compilation, Bash syntax, and the
CRLF-policy-aware whitespace check passed. Shellcheck was absent and is not claimed.

The root agent read/exported the complete baseline and untracked diff twice
without changing the index. The final export is
`/tmp/story49-step03-final-diff2-smsb35tb/baseline.diff`, 129,642,273 bytes,
SHA-256 `146a18792cf4f54555d820b0973d0410c62367235286426c5c3f62d7cc638bfa`.
The parent audit records preserve the source, ordinary-suite, and correction scope.

The first final full attempt at
`/tmp/story49-published-full-20261103-jz15ssts/full` exited **1** before live lanes.
It correctly detected twenty missing official
`src/**/.artifacts/ui-test-obj` files. The files were present in the genuine
public SDK export; bootstrap wrongly omitted nested `.artifacts` directories.
The initial diagnosis of newly generated files was corrected. Bootstrap now
preserves nested canonical artifact bytes, excludes output trees only at the
export root, and retains bin/obj exclusions and run-owned dependency links. A
regression proves those boundaries, and both the unchanged owner checker and
an independent official-tree/blob audit confirm all 2,597 exact inputs, including
the twenty files. The source guard was not weakened.

Fresh full attempt 2 used the following exact canonical command and exited **1**:

```text
python3 /tmp/story49-published-full-20261103-o3q0a32r/full/workspace/platform/eng/verify_works_host.py --profile full --works-root /home/administrator/projects/hexalith/works --runtime-sdk-root /tmp/story49-published-full-20261103-o3q0a32r/full/workspace/Hexalith.EventStore --platform-source-binding /tmp/story49-published-full-20261103-o3q0a32r/full/platform-source-binding.json --artifacts-path /tmp/story49-published-full-20261103-o3q0a32r/full/gate
```

It passed both **155-test** Platform suites, Debug/Release consumer builds,
**384 supporting** facts, exact public-producer qualification, selected-runtime
binary capture **1/1**, and telemetry admission **1/1**. Retained startup then
failed **1/1** after 141.708 seconds. The captured EventStore readiness response
was 503 because `dapr-actor-placement` repeatedly received HTTP **401 Unauthorized**
from `/v1.0/metadata`. The Dapr sidecar, state store, pub/sub, config store,
app-channel token, committed route catalog, and projection protocol checks were
Healthy. Placement gRPC connected and actor types disseminated. This is a
metadata-HTTP credential omission, not proof that placement was unavailable.
Full matrix scenarios and actual CI did not run; no 44-proof or complete-rollback
acceptance is claimed. The original failure ledger/XML/logs remain intact.

The [SDK candidate patch](handoff/sdk-placement-token-fix.patch) has exactly two
source/test files and is based on actual upstream main
`1aec4a7f466cfae7f590703fbde222d39f00d659`. Its old probe bytes equal public 4.0.4.
The fix reads configured `DAPR_API_TOKEN` and attaches it to each metadata
request without changing `HttpClient.DefaultRequestHeaders`. Existing endpoint
selection and unhealthy/refusal behavior remain. The tests use real loopback
HTTP: missing/wrong credentials stay Unhealthy/401 and the configured credential
becomes Healthy; they also cover endpoint/port/default selection, request-only
header override, and missing/blank compatibility. Corrected red evidence is
**8 cases: 3 passed, 5 failed** before changing production source; green evidence
is **19/19**, including eleven existing placement-health facts. The source Debug
build has zero warnings/errors. An initial test-context mistake and two source
alias build failures are retained separately with their corrections. The exact
full candidate commit message passed the owner's pinned commitlint 21.2.4.

This candidate is executable supporting evidence, not a public producer. The
isolated owner checkout and diagnostic roots are recorded in `sdk-prerequisite/`;
the original developer SDK remains unchanged. No SDK commit, push, or publication
has occurred. Owner main already contains five post-4.0.4 commits including
`feat(aspire): add owned development peer transport`. The locked owner commit
analyzer 13.0.1 predicts **minor 4.1.0** for that observed history. This is a
version preview, not publication authority; the actual future merged history and
protected release determine the version. No old-source release or retrofit tag
is proposed.

The remaining ordered handoff is:

1. Obtain authorization for the **new SDK normal publication** and consequent
   owning catalog/Platform publications. The previous approval covered the
   prepared Builds/Platform chain. The user subsequently chose **“Publish and resume 4.9”**, explicitly
   authorizing the tested SDK normal release (including existing upstream peer
   transport), required Builds/Platform updates, and full/CI qualification, with
   eligible owner approvals/exemptions only after green checks. The approval
   addendum records this steering; publication execution now resumes.
2. Apply/rebase the two-file SDK patch through the owner's normal PR process,
   validate exact commit/PR/merge messages with pinned commitlint, obtain exact
   source checks and genuinely required reviews/approved protections, then use
   its existing protected release workflow. Preserve concurrent owner history.
   Qualify all fourteen normal release packages/containers and the genuine
   resulting release/tag/source/signature identities; never fabricate a tuple.
3. After genuine SDK publication, update the Builds catalog through its required
   audit and protected release process. Bind resulting signed tooling, embedded
   catalog, inventory, producer run, exact source revision/blob, and raw hash.
4. Qualify that exact SDK cohort in Platform's finite capability gates and tests;
   do not admit future versions by range. Bind the genuine Builds publication and
   gitlink, pass actual source tuple/catalog CI, and publish using its source/tag
   convention. Keep historical archives unchanged and source/package evidence
   distinct. No hypothetical version has been added to accepted metadata.
5. Adopt the genuine Works pins and reviewed source/pin pair, run required
   ordinary/source checks, then a fresh canonical full and the actual shipped CI
   entry point with all eleven rows, 44 proofs, nine explicit facts, complete-fleet
   rollback, and final source/consumer/runtime/archive/launcher closure. Obtain
   independent final audits before completing tracking or reading the next
   workflow closure step.

Works changes remain reviewable in its working tree; no root Works commit or
push occurred and no new hosted Works run is claimed. The strict success-only
packet builder remains guarded against incomplete full/CI ledgers. This separate
packet preserves the current nonqualifying continuation. Private keys, custody
material, runtime binaries, package caches, and private fleet snapshots remain
in native temporary roots; the packet contains bounded diagnostic files and
hash-bound receipts. Production preview and agent admission remain closed.
