# Blind Hunter Review Prompt

Review the following migration diff adversarially. You have no project or conversation context; use only this diff summary. Report only concrete findings with severity, file/path, and a concise remediation. Do not invent requirements.

## Diff output

- `.gitmodules`: the ten existing root submodule `path` values changed from `Hexalith.*` to `references/Hexalith.*`; a root `references/Hexalith.AI.Tools` entry was added.
- Ten gitlink directories were renamed 100% from the repository root to `references/Hexalith.*`; their recorded commits were preserved.
- `Directory.Build.props`: EventStore, Tenants, and PolymorphicSerializations root probes now resolve only beneath `references/`; old direct-root and parent-directory fallbacks were removed.
- `.editorconfig`: the PolymorphicSerializations source glob now starts with `references/`.
- `Hexalith.Works.slnx`: ten submodule README solution folders and file paths now start with `references/`.
- `README.md` and `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`: initialization and recovery commands now use `references/Hexalith.*` paths.
- `EventStoreApiSurfaceCharacterizationTests.cs`: EventStore lookup changed to `PathFromRoot("references", "Hexalith.EventStore")`.
- `SubmoduleLayoutTests.cs`: new test parses `.gitmodules`, expects eleven exact `references/Hexalith.*` paths, checks each directory exists, and asserts no top-level `Hexalith.*` directories remain.
- Existing user changes to `AGENTS.md`, `CLAUDE.md`, and `.github/copilot-instructions.md` are present in the worktree but are not part of the migration logic.

Review risks including incorrect path assumptions, lost submodule pointers, incomplete path migration, fresh-checkout failures, and unsafe nested-submodule behavior.
