---
title: 'Relocate root submodules under references'
type: 'refactor'
created: '2026-07-20'
status: 'in-review'
baseline_commit: '13b94a2f7163ddb0518d7173e1e6b644c19369e8'
context:
  - '{project-root}/AGENTS.md'
  - '{project-root}/Directory.Build.props'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Hexalith.Works currently checks out its shared Hexalith repositories as top-level directories, while the shared assistant baseline and sibling Hexalith repositories require root-declared dependencies to live under `references/`. This makes repository tooling, source references, and baseline-instruction discovery inconsistent.

**Approach:** Register every root dependency under `references/`, including the required `Hexalith.AI.Tools` baseline repository, and physically relocate the existing gitlinks without initializing or updating any nested submodule. Update build probes, fallback paths, documentation, solution folders, and architecture checks so the repository remains buildable and validates the new layout.

## Boundaries & Constraints

**Always:** Preserve each existing submodule commit; use only non-recursive root-submodule operations; keep ProjectReferences pointed at checked-out source; retain the user’s existing changes in `AGENTS.md`, `CLAUDE.md`, and `.github/`.

**Ask First:** Stop if a submodule contains local modifications that would be lost or if adding `Hexalith.AI.Tools` requires selecting a commit other than the repository’s current local/remote default.

**Never:** Do not initialize nested submodules, use recursive submodule commands, replace Hexalith ProjectReferences with PackageReferences, or leave compatibility copies at the old top-level paths.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| HAPPY_PATH | All current root gitlinks are clean and initialized | Eleven root submodule paths resolve under `references/`; no `Hexalith.*` directory remains at repository top level | N/A |
| FRESH_CHECKOUT | Only the superproject is checked out | `.gitmodules`, README, MSBuild probes, solution paths, and tests consistently identify `references/Hexalith.*` | Validation reports the exact missing root submodule; no recursive initialization is attempted |
| NESTED_SUBMODULES | Root submodule repositories declare nested dependencies | Nested gitlink directories remain uninitialized and are not duplicated under the superproject | Fail the layout check if nested contents are initialized by this change |

</frozen-after-approval>

## Code Map

- `.gitmodules` -- declares the eleven root submodules and their `references/` checkout paths.
- `references/Hexalith.*` -- relocated root gitlinks; their recorded commits must not change.
- `Directory.Build.props` -- resolves EventStore, Tenants, and PolymorphicSerializations from `references/`.
- `Hexalith.Works.slnx` -- exposes relocated submodule README files in solution folders.
- `README.md` -- documents non-recursive initialization using `references/Hexalith.*` paths.
- `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` -- reports corrected initialization commands.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs` -- locates EventStore under `references/`.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubmoduleLayoutTests.cs` -- guards root-only `references/` layout and old-path removal.

## Tasks & Acceptance

**Execution:**
- [x] `.gitmodules` -- move all existing entries to `references/` and add the root `Hexalith.AI.Tools` entry required by the shared baseline -- make checkout metadata authoritative.
- [x] `references/Hexalith.*` -- relocate the ten existing gitlinks and initialize only the new root AI.Tools gitlink -- preserve recorded commits and leave nested submodules empty.
- [x] `Directory.Build.props` -- probe the three source dependencies beneath `references/` -- keep ProjectReferences resolved after the move.
- [x] `Hexalith.Works.slnx` -- update solution folder/file paths to `references/Hexalith.*` -- keep all submodule documentation visible.
- [x] `README.md` and `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` -- update checkout instructions and diagnostics -- make fresh-checkout recovery accurate.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubmoduleLayoutTests.cs` -- update EventStore lookup and add layout assertions -- prevent regressions.

**Acceptance Criteria:**
- Given the updated superproject, when `.gitmodules` is parsed, then exactly eleven root submodule paths are declared and every path starts with `references/`.
- Given the relocated checkout, when the solution and MSBuild files are evaluated, then all referenced submodule files resolve beneath `references/` and no old top-level `Hexalith.*` path is required.
- Given the architecture tests, when the focused test project runs, then it confirms the EventStore API surface through `references/Hexalith.EventStore` and confirms nested submodules were not initialized by this change.
- Given the user-edited assistant entry points, when the final diff is inspected, then their content is preserved unchanged.

## Verification

**Commands:**
- `git diff --check` -- expected: no whitespace errors.
- `dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release` -- expected: all architecture tests pass.
- `dotnet build Hexalith.Works.slnx -c Release` -- expected: solution builds with the relocated source references.
