# Acceptance Auditor Review Prompt

Use the `bmad-review-adversarial-general` or equivalent acceptance-audit method. Read the current project, the approved spec at `../../_bmad-output/implementation-artifacts/spec-relocate-submodules-under-references.md`, `../../AGENTS.md`, and `../../Directory.Build.props` relative to this prompt. Verify every task, acceptance criterion, constraint, and verification claim. Report unmet criteria or misleading evidence with severity and exact file/path.

## Diff summary

- `.gitmodules`: ten existing submodule paths moved to `references/`; root `references/Hexalith.AI.Tools` was added for the shared baseline.
- Ten existing root gitlinks were relocated to `references/` with unchanged recorded commits; top-level `Hexalith.*` directories are absent.
- Build probes, editorconfig, solution folders, README instructions, AppHost diagnostics, and EventStore architecture lookup now use `references/`.
- New `SubmoduleLayoutTests` validates the exact eleven paths and old-path removal.
- Verification completed: focused architecture tests passed 42/42; serialized Release solution build passed with 0 warnings and 0 errors.
- Existing user edits in `AGENTS.md`, `CLAUDE.md`, and `.github/` must remain preserved.

Check especially whether registering `Hexalith.AI.Tools` is consistent with the context baseline, whether removing old MSBuild fallbacks is within the approved intent, whether all root consumers were updated, and whether the review claims distinguish user-provided changes from this migration.
