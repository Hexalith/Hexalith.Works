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

Current maintenance selects EventStore source `aa16227bd07f0564a3026d619a5abd973e02656f`,
the exact producer recorded by the public `v4.2.0` tag and 4.2.0 package metadata.
Both build defaults, the literal Release pin, source gitlink and reviewed pair manifest
select this identity. The public Contracts archive passed NuGet signature verification
and exact source/identity checks. The Contracts and Server Release builds pass with zero
warnings or errors. Release policy controls pass 12/12. This does not extend the old
nine-archive verification or establish full runtime/parity acceptance. Source-mode and
canonical Platform parity remain separate required checks.

The previous reviewed EventStore source was `fe974f485fa2c29d9ea055f5597b41ae38d072f1`.
Its Release pin was public `4.0.4`, produced by `326bfef884543cdbcaf96de7f4c4ad378dbad6d7`.
PolymorphicSerializations source is `bce4eb9f45c6b131b960ab4fa39e50ff01e51960`,
one npm lockfile refresh after public `v1.19.8` (`2a66be84ac99753713a077a018ba650134396818`);
Release pins 1.19.8. The current source markers in the API and boundary records bind the consumed
EventStore checkout. The nine EventStore 4.0.4 archives consumed by the package graph have verified
exact versions, official repository/source metadata and signatures; their executed consumer DLLs
match those archives. This does not extend the prior fourteen-archive 4.0.0 verification to 4.0.4.
Both PolymorphicSerializations archives retain their separately verified 1.19.8 scope.
The previous identity preflight passed for that source/4.0.4 pairing.
It checks producer identity and does not establish behavioral equivalence. Runtime/parity acceptance
requires fresh ordinary checks, canonical full and actual CI with complete-fleet rollback.
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
