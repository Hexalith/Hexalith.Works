# Story 4.9 publication continuation — phase 1, 2026-11-03

**Current state:** Builds **4.43.3 is genuinely published and verified**. Platform [PR #15](https://github.com/Hexalith/Hexalith.Platform/pull/15), exact head `2db699c53219721ed2b9bf2860082662b73fa00d`, passes hosted tuple and all four catalog-builder checks. It remains `REVIEW_REQUIRED` / `BLOCKED`. No Platform merge/tag, live Works pin adoption, final ordinary qualification, canonical full or actual CI acceptance is claimed. Story/sprint remain in progress and the final qualification task stays unchecked.

This continues the immutable [resumed qualification packet](../resume-2026-11-03/README.md) after the user explicitly authorized the prepared owning Builds/Platform publication chain. The original packet and historical archives are unchanged. This separate phase records genuine publication, subsequent source/ref events, checks and the pending review decision.

## Genuine Builds and SDK identities

Concurrent owners had already committed the central SDK 4.0.2 update, then advanced it through 4.0.3 to **4.0.4** with updated audits and fixtures. Those newer selections were preserved. The initialized top-level Builds checkout fast-forwarded cleanly to `fc743715f7d0993392dee34b2ef9c4eb85cdd399`; no redundant catalog/audit commit or downgrade was made. Its real audit validates **314 packages / 154 families**. Exact-source push CI [41583178980](https://github.com/Hexalith/Hexalith.Builds/actions/runs/41583178980) and commitlint were successful.

The normal protected [Builds Release 41606852507](https://github.com/Hexalith/Hexalith.Builds/actions/runs/41606852507) passed. Its production deployment was approved by the eligible authenticated account under the user's explicit authorization; protections and source/package controls were left intact. Full source validation, all required controls, actual-version pre-tag qualification, NuGet Trusted Publishing and remote verification completed. [Release v4.43.3](https://github.com/Hexalith/Hexalith.Builds/releases/tag/v4.43.3) was published at **2026-11-03T10:19:01Z**, with its direct official tag naming the exact source above.

| Measured field | Exact value |
| --- | --- |
| Catalog Git blob | `d1a73f920cfe979af63f0194d826ca9efec5d6b4` |
| Raw catalog SHA256 | `0fa46660fa14ad57616ab915201e8426f42b4d71741db3ee7ae25d390ec0e8fe` |
| Signed Module.Cli archive SHA256 | `50e392a207f6e82b15b20b109eed429ad4332044b9dc96e2ed8da77fab8342dd` |
| Embedded Tooling DLL SHA256 | `e3782217e4d6a73d749d309e74d4aea9ecdb10e043edfff84d86cdd6d946540c` |
| Official release inventory SHA256 | `c6a0276b2195208fb298eb1d3d58b24eb983e6fff9d0119f9206311566bc9839` |

The official release assets' reported sizes/digests, complete carrier membership/evidence hashes, actual producer run **41606852507 / attempt 1**, source, version and inventory all match. NuGet repository signature and nuspec source/id/version checks pass. The owner artifact preparer verifies the actual executable's `4.43.3+fc743715f7d0993392dee34b2ef9c4eb85cdd399` identity. The published Tooling's embedded catalog is byte-for-byte identical to the existing owner generator's evaluated catalog and selects SDK **4.0.4**. `candidateAcceptance` remains false; artifact identity is not runtime acceptance.

Public SDK **4.0.4**, producer `326bfef884543cdbcaf96de7f4c4ad378dbad6d7`, is independently verified: all nine consumer package archives share the official producer and valid repository signatures; all **2,597** runtime/build inputs match official commit/tree/blob identities and `v4.0.4`. Actual candidate consumer DLLs match their official archives. The compiled trusted-effects controller retains its technical-controller annotation and Dapr internal authorization. The SDK trie decision implementation is byte-identical to the 4.0.0 implementation used by the prior bounded policy replay; actual current host inventory remains for the canonical gate.

## Platform source, protection and PR history

The prepared changes were replayed over current owner `f90e68a0c0305d18dc2cde8d4d4c856aa5a49725`, preserving its newer four SDK capability files, gate coverage and gitlinks. Five remaining code/doc files were replayed; measured publication fields, tool pin/range and current binding documentation then changed to the genuine values above. The recorded owner Builds gitlink already names the required source. Local checks use the initialized top-level Builds checkout, with a run-owned sibling link for helpers that require it; no Platform nested dependency checkout was initialized or materialized.

Commit `5b4ecc682c94e0416d82f321994f8bfd68d4b434` contains the ten-file delta. Exact full commit messages pass the pinned **commitlint 21.2.4**. The ordinary direct main push succeeded, but GitHub reported the account's exemption from its pull-request requirement. No bypass flag or protection-setting change was requested. This notice is retained transparently; it is not retroactive PR enforcement.

A separate **force-push at 2026-11-03T10:29:01Z**, recorded by the repository activity API under account `jpiquot`, restored main from `5b4ecc682c94e0416d82f321994f8bfd68d4b434` to `f90e68a0c0305d18dc2cde8d4d4c856aa5a49725`. This agent did not issue that reset. No inference is made about which human/process used the account. That event cancelled catalog run **41619360461**. Original exact candidate tuple run [41619360415](https://github.com/Hexalith/Hexalith.Platform/actions/runs/41619360415) completed successfully for 5b4ecc68, with **153/153** required facts.

The restored main was respected. The candidate was preserved on `fix/works-sdk404-platform-publication`, with documentation-only commit `2db699c53219721ed2b9bf2860082662b73fa00d` explicitly labeling the old **3.126.0** records/table as historical and removing the stale active selection claim. Historical proofs were not reinterpreted as SDK 4.0.4 evidence.

[PR #15](https://github.com/Hexalith/Hexalith.Platform/pull/15) now has:

- Successful exact-head [tuple CI 41620736259](https://github.com/Hexalith/Hexalith.Platform/actions/runs/41620736259): **83 + 2 + 24 + 44 = 153/153**, fresh signature/source/pin/catalog checks, clean recorded source, package-only graph/build and extracted-byte/output identity.
- Successful [catalog CI 41620469841](https://github.com/Hexalith/Hexalith.Platform/actions/runs/41620469841): all four SDK matrix jobs.
- No approving reviews. Effective main rules require one approving code-owner review; `.github/CODEOWNERS` names only **@jpiquot** for every file, and **@jpiquot** authored the PR. The API reports `REVIEW_REQUIRED` / `BLOCKED`. Cursor's automated review could not run because its usage limit was reached; its neutral checks supply no approval.

No bypass merge, repeated direct main push or unmerged Platform tag/publication has been attempted. The user is being asked separately to resolve normal code-owner eligibility or explicitly authorize an owner-exempt merge of this fully green PR. Nothing dependent on that choice proceeds before an answer.

## Focused verification and remaining scope

| Check | Result / scope |
| --- | --- |
| New published Tooling + SDK 4.0.4 Platform Works model/policy suite | **155/155**, zero warnings/errors/failures/skips. Exact SDK-byte and loaded-MVID guards remain intact. |
| Owner helper controls | **159/159** with the explicit initialized Builds root. Initial bare worktree run lacked that root and had one error; retained as nonqualifying setup evidence. |
| Finite SDK capability evaluation | **208/208** real project evaluations. |
| Tuple / host-resolution controls | **19/19** and **15/15**. |
| Candidate SDK 4.0.4 Works package build / focused facts | Clean build; **173/173**: captured files 10, ingress 15, Dapr config copy 12, endpoint cancellation 11, package contracts 2, protected HTTP 123. This preparation used the previous verified 4.43.2 Tooling and supplies no new final tuple/runtime acceptance. |
| Candidate source/pin identity experiment | Isolated Works worktree correctly refused symlinked source checkouts as a different Git worktree. No guard was weakened; the real original-workspace preflight is still required after live adoption. |
| Formatting | Both owner diffs pass explicit CRLF-aware whitespace rules. Earlier default-CRLF mismatch remains documented in the original packet. |

`handoff/platform-pr-source.diff` is the exact current PR delta. `handoff/works-sdk404-pins-unapplied.patch` updates the two Works default SDK pins, integration assembly metadata, reviewed source/Release pairing, and two producer documents to SDK 4.0.4 while preserving current source `98a298a084ec6dedecf99e0abefd649051038376`. It applies cleanly. Its documentation limits provenance/signature claims to the nine verified packages and leaves the new pair preflight pending.

The live Works Release pin remains **4.0.0**. Original Works changes remain reviewable and uncommitted; the root Platform's original nine-file overlay is preserved, with its exact hash inventory and backup diff retained. The top-level Builds checkout is the newly published source, so the old root Platform tuple and Works pin are intentionally not final aligned inputs yet. Strict preparation must refuse inconsistent authority rather than infer acceptance. No root Works commit/push or local nested dependency initialization occurred.

The latest canonical full remains the prior packet's **attempt 6**, exit **1 / incomplete**, retained-host refusal `Topology: undeclared-acl-operation` on the preceding SDK 4.0.0 inputs. No new canonical full or actual CI has run for this publication chain. Earlier ordinary 597/3/404/1,403 results retain their earlier input scope. Final R1–R11/44 proofs, complete rollback and source/runtime closure remain pending.

## Resume after the review decision

Complete the permitted normal merge or explicitly authorized owner exemption. Requalify the **actual resulting source revision** through exact tuple CI and required catalog checks before source/tag publication. The existing Platform convention is a manual GitHub source/tag release with no package assets; no NuGet/package pipeline is being invented. Its history since v1.1.3 contains fix/build/docs changes and supports proposed patch **v1.1.4**, subject to verifying that identity is still available and its exact qualified target. No such tag is created in this phase.

Then align the original root Platform checkout while preserving its saved overlay, apply the prepared Works pins, run the real source/pin preflight, and update current documentation with its actual result before final snapshots. Refresh default Debug restore assets for architecture inspection; run a fresh local source solution gate and ordinary suites. Finally run fresh canonical **full**, then the actual `scripts/verify-platform-parity-ci.sh` entry point, using the independently qualified SDK 4.0.4 filesystem export, run-owned TMPDIR and fresh official-only package caches. Require all 44 proof kinds, complete-fleet rollback and final input closure before changing acceptance status. Keep Works changes reviewable unless a root commit is actually required.

The native continuation is `/tmp/story49-publication-20261103-evyasum6`; `state.json` records the resume sequence. This packet retains bounded text/structured artifacts, official qualification carrier and exact diffs, not runtime/package binaries or local private catalog keys/custody/fleet snapshots. The historical archive hashes remain manifest `b8cbf1bb6490b6fc6bb8ad7aa12842c202fb95a9877afd7a0a5fa1caf79caca8`, archive `5ce3247ded7ab59ff70d5b25896fed1826b8d78b0728a2d1a4c1f2dceb7ae696`.
