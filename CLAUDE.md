# Hexalith.Works

This repository aggregates Hexalith components as **git submodules located at the root** of the
repository. Each root submodule (for example `Hexalith.Builds`, `Hexalith.Commons`,
`Hexalith.EventStore`) is itself a repository that declares its **own** submodules nested inside it.

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
- `Hexalith.Tenants`
