# Deferred Work

Tracks items surfaced during review that are real but intentionally not actioned in their originating story.

## Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)

- **Kernel-purity test is transitive-blind** — `P0_KernelProjectsStayInfrastructureFree` only string-matches kernel `.csproj` text, so a `<ProjectReference>` to `Hexalith.EventStore.Client` (which carries `Dapr.Client`) would pass undetected. Documented in the story's Dev Notes ("Kernel-purity vs EventStore.Client") and assigned to Story 1.2, where `Works.Server` first subclasses the EventStore aggregate base. `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:100-134`.
- **Warnings-as-errors defeats the "scaffolding phase" analyzer intent** — `TreatWarningsAsErrors=true` promotes the `.editorconfig` CA1062/CA1822/CA2007 `severity=warning` settings to build errors (no `WarningsNotAsErrors` escape hatch). Latent while the scaffold is empty; will break the build the moment real kernel code triggers one of these rules (e.g. CA1062 argument null-checks). Add `WarningsNotAsErrors` (or escalate the editorconfig to match) when domain code lands. `.editorconfig:56-60`; `Directory.Build.props:11`.
- **Placeholder tests prove little** — `ScaffoldIntegrationTests` and `ScaffoldPropertyTests` assert only that their own assembly loads (no Aspire boot, no FsCheck `Prop.ForAll`); some governance assertions (forbidden tokens that can never appear) can't fire. Acceptable for a scaffold-only story; real integration/property coverage belongs to later stories. `tests/Hexalith.Works.IntegrationTests`; `tests/Hexalith.Works.PropertyTests`.
