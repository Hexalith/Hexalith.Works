# Development verification boundaries

The local Debug source graph and CI Release package graph have separate producer identities.
`tools/source-debug-dependencies.json` records the reviewed source revision and Release version for
EventStore and PolymorphicSerializations, the two dependencies named by the original drift finding.
`scripts/verify-source-debug.py` refuses a changed indexed root gitlink, consumed checkout revision or evaluated
Release pin until that pair is reviewed together. It explicitly selects those source roots during
restore and compilation. Other dependencies remain governed by the repository's existing build rules.
CI invokes the same gate with `--check-only`: it checks identities using Release/package evaluation
without restoring or compiling Debug assets. A changed reviewed pair therefore fails the tooling job.
Git-visible tracked and untracked source contents are hashed before compilation and checked again
afterward. A changed revision or source snapshot invalidates the result. When updating dependencies,
stage the reviewed root gitlinks before running the gate; this supports verification before committing.
The script never stages files itself. The separate Architecture marker guard characterizes committed
revisions. A local build with an overlay qualifies the observed overlay, rather than pristine source alone.

The current EventStore source is `61e6fa853d81cf822d67d6abfd6a130bdbb14ce2`, three commits after public
`v3.129.0` (`c02afa0fce310ccdb696232f18829df96cba088e`). Release pins 3.129.0.
PolymorphicSerializations source is `92ede24cc11129379efe52cfe7fd2c820c567f7f`, two commits after public
`v1.19.6` (`2e68a028c695d577cd733d0c1e40ec57531c7547`), and Release pins 1.19.6. The current source markers in the API and boundary records bind the consumed
EventStore checkout; the existing Architecture drift controls verify those markers independently.
Historical public-producer and live-proof revisions retain their original scope.

The unmapped source entry point explicitly selects Commons (including Http) and McpCli
source edges as well as the two governed producer pairs. It refuses an active module-tool
mapping so its source selectors cannot overwrite that mapping. Restore and compilation
share a private output tree under the selected evidence directory; concurrent package
restores cannot replace this proof's assets. Use a directory under `.artifacts/` when running
test assemblies that locate their owning repository through build-directory ancestors.
The added root Memories dependency supplies Platform's current source preparation contracts;
nested submodules remain uninitialized.

The original CI/CD review asked for Debug compilation in CI because evaluating project references
does not compile them. The required Hexalith baseline keeps CI in Release/package mode. The replacement
is an explicit, reproducible **local** Debug compilation tier: initialize only root dependencies, run
`python3 scripts/verify-source-debug.py`, inspect its retained commands and require `status: passed`.
No hosted Debug CI check, current live parity, or equivalence to the Release producer is claimed.

Three Story 6.1 tooling findings are superseded by retirement in EventStore commit
`6a0b6441f6d78a3007678a1d0ec336e9cb8ff961`, an ancestor of the currently consumed source:

| Original finding | Retired machinery | Current disposition |
| --- | --- | --- |
| Missing 3.109 evidence-project exemptions in the package authority validator | Three projects under `evidence/6-1-p1r-3109` | Those projects and the obsolete exemption mechanism are gone; current Contracts tests use the shared package-authority validator. |
| Unbounded commands in the public-package replay | `6-1-p1r-3109/verify_public_packages.py` | The replay program was deleted; it is no longer an executable qualification path. |
| Raw state keys in rollback read URIs | `6-1-p1r-3109/rollback-probe/Program.cs` | The probe was deleted; no retained entry point runs it. |

The retirement commit explicitly removed committed evidence and its verification machinery in favor
of tests and CI. The accompanying owner spec is `spec-evidence-storage-simplification.md`. The closure
packet checks each historical path's presence in the parent tree, absence in the retirement/current
trees, and retirement ancestry. It does not execute or repair the deleted programs, relabel their old
evidence, or qualify the current PublishedCompatibility runtime.

The [verification packet](../_bmad-output/implementation-artifacts/evidence/five-deferred-development-assurance-2026-10-28/README.md)
retains the commands and results for these five ledger dispositions, including the full local Debug
compile, source characterization and focused failure controls.
