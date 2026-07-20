# Edge-Case Hunter Review Prompt

Use the `bmad-review-edge-case-hunter` method. Read the current project files and the approved spec at `../../_bmad-output/implementation-artifacts/spec-relocate-submodules-under-references.md` relative to this prompt. Walk every boundary and branching path. Report only unhandled edge cases caused or exposed by this migration, with severity and exact file/path.

## Diff summary

- `.gitmodules` now declares eleven root paths, all under `references/`, including the staged `references/Hexalith.AI.Tools` baseline submodule.
- Ten existing clean root gitlinks moved to `references/` without changing their SHAs; nested submodules were not initialized or updated.
- `Directory.Build.props` now probes only `references/Hexalith.EventStore`, `references/Hexalith.Tenants`, and `references/Hexalith.PolymorphicSerializations`.
- `.editorconfig`, `README.md`, `Hexalith.Works.slnx`, AppHost diagnostics, and EventStore architecture tests were updated to use the new paths.
- `SubmoduleLayoutTests.cs` asserts exact eleven paths, directory presence, and absence of old top-level `Hexalith.*` directories.

Pay particular attention to fresh clones, partially initialized root submodules, Windows/Linux path handling, `.gitmodules` and gitlink consistency, solution discovery, nested-submodule boundaries, and untracked/staged state.
