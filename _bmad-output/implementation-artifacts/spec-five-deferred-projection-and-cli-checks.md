---
title: 'Close five deferred projection and CLI verification items'
type: 'bugfix'
created: '2026-11-22'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem.** The Works deferred-work ledger leaves the durable projection and callback proof boundaries unclear. The lifecycle poll can accept an entirely absent actor-state scope, and the BMAD party resolver has helper tests without a subprocess test for its collision-check JSON.

**Approach.** Reconcile the archived AD-20 and explicit recovery proofs with their current selectors, map and restore coverage of the retired projection scenarios, require persisted SDK lifecycle evidence before the restart poll settles, and exercise the actual `--list-codes` command. Record the five dispositions in the ledger.

</frozen-after-approval>

## Implementation Notes

- Selected R29B-D1, R29B-D2, R29R-V1, R30C-D1 and R30C-D3 from the Works deferred-work ledger.
- Verified the two persisted R4 evidence records and the three passing explicit projection and recovery facts in the archived CI5 packet. The cutover guide records the covered phases and preserves the packet's source scope.
- The lifecycle poll invokes the SDK's `ReadSnapshotAsync` method through the existing selected sidecar client. It uses the SDK aggregate identity and actor type and checks for an idle phase on every source plus a positive persisted revision somewhere in the scope.
- The focused Debug build uses a temporary checkout at the Works gitlink revisions, including the root FrontComposer dependency. Two existing CA2007 errors in Platform's `PlatformFixtureToolRelease.cs` required explicit async disposal configuration in that checkout.
- Focused validation passed 89 .NET tests across the three modified classes and all 60 Python tests across the skill copies. The two explicit recovery facts remain assigned to live Platform parity. Git whitespace checks and the Builds v6.6.1 commit policy passed.
- `python3 references/Hexalith.Builds/check.py` on the clean base stopped while acquiring the anonymous `delivered/Hexalith.Works` policy branch.

## Review Triage Log

- **Patch.** A delivery could enter between the lease and epoch reads. The SDK lifecycle snapshot reports both fields in one actor turn.
- **Patch.** A missing lease key could appear idle beside an existing epoch. The poll reads the SDK actor snapshot instead of interpreting state keys.
- **Patch.** The R4 mapping identifies the exact retired post-Commit, pre-catch-up reconstruction and the separate Stage-to-Commit shutdown scenario.
- **Patch.** The scenario map identifies the retired manifest, legacy roll-up reader and terminal index-write notification contracts and links current coverage.
- **Patch.** The rejection regression verifies both served query watermarks as well as the persisted documents.
- **Patch.** The post-Commit delivery regression verifies the new owner's persisted checkpoint as well as family membership, detail and history.
- **Patch.** The CLI test exercises a project customization override of an installed alias, plus the fallback when installed-agent resolution fails.
