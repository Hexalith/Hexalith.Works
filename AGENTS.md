# Hexalith.Works

## Shared Hexalith LLM Instructions

Before starting any work in this repository, read and follow
[`Hexalith.AI.Tools\hexalith-llm-instructions.md`](./Hexalith.AI.Tools/hexalith-llm-instructions.md).

This repository aggregates Hexalith components as **git submodules located at the root** of the
repository. Each root submodule (for example `Hexalith.Builds`, `Hexalith.Commons`,
`Hexalith.EventStore`) is itself a repository that declares its **own** submodules nested inside it.

## Repository responsibility

This repository should contain primarily domain code for managing work items. Do not add technical
layers here unless they are absolutely required for work items and are not common to other domain
modules.

Factor technical concerns into the relevant shared Hexalith modules. For example, persistence belongs
in `Hexalith.EventStore`, and unique identifier generation belongs in `Hexalith.Commons`.

The .NET Aspire Host is an acceptable technical component in this repository because each Hexalith
module needs a repository-specific host with servers and dependencies tailored to that module. Aspire
is required to run both manual and automated tests.

## Hexalith library references — ALWAYS use ProjectReference, NEVER PackageReference

When a project in this repository depends on another Hexalith library, reference it as a
`ProjectReference` to the `.csproj` inside the corresponding **root submodule**. Never add a NuGet
`PackageReference` for a `Hexalith.*` library, and never add `Hexalith.*` versions to
`Directory.Packages.props`. The submodules are the source of truth; building against project
references keeps every module on the exact checked-out source and avoids stale/conflicting published
package versions.

**Do** — reference the submodule project through its root-path variable
(`$(Hexalith<Module>Root)`, defined in `Directory.Build.props`):

```xml
<ProjectReference Include="$(HexalithEventStoreRoot)\src\Hexalith.EventStore.Contracts\Hexalith.EventStore.Contracts.csproj" />
<ProjectReference Include="$(HexalithTenantsRoot)\src\Hexalith.Tenants.Abstractions\Hexalith.Tenants.Abstractions.csproj" />
```

**Do NOT** — pull a Hexalith library from NuGet:

```xml
<!-- ❌ never do this for a Hexalith.* library -->
<PackageReference Include="Hexalith.EventStore.Contracts" />
```

If a needed root-path variable does not exist yet, add it to `Directory.Build.props` (following the
existing `$(Hexalith<Module>Root)` pattern) rather than hard-coding a relative path or falling back to
a package reference. Third-party (non-`Hexalith.*`) dependencies continue to use central package
management via `Directory.Packages.props` as usual.

## Submodule rules — READ BEFORE RUNNING ANY `git submodule` COMMAND

Only the submodules at the **root** of this repository may be initialized and updated. The submodules
**nested inside** those submodules must be left uninitialized (empty gitlink directories).

**Do:**

- Initialize/update only root submodules, non-recursively:
  ```bash
  git submodule update --init            # all root submodules, NOT recursive
  git submodule update --init <name>     # a single root submodule, e.g. Hexalith.Commons
  ```

**Do NOT:**

- ❌ Never use `--recursive` on any submodule command. For example, do **not** run:
  ```bash
  git submodule update --init --recursive
  git submodule update --recursive
  git clone --recursive ...
  git clone --recurse-submodules ...
  ```
- ❌ Never initialize a submodule that lives inside another submodule
  (e.g. `Hexalith.EventStore/Hexalith.Tenants`, `Hexalith.Commons/Hexalith.Builds`).
  These nested paths are shared dependencies that are checked out independently at the root.

Initializing nested submodules duplicates the shared components, produces inconsistent/conflicting
versions across the tree, and is never required to build or work in this repository.

## Root submodules

- `Hexalith.Builds`
- `Hexalith.Commons`
- `Hexalith.PolymorphicSerializations`
- `Hexalith.EventStore`
- `Hexalith.FrontComposer`
- `Hexalith.Parties`
- `Hexalith.Tenants`
- `Hexalith.Chatbot`
- `Hexalith.Projects`
- `Hexalith.Conversations`
