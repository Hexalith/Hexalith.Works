# Hexalith.Works

Hexalith.Works is the Hexalith work item domain module. This repository is an umbrella workspace with root-level Hexalith submodules and a Works-specific Aspire host for local and automated validation.

## Build

Initialize only root submodules when needed:

```bash
git submodule update --init Hexalith.EventStore Hexalith.Tenants
```

Do not use recursive submodule initialization.

Restore and build the scaffold:

```bash
dotnet restore Hexalith.Works.slnx
dotnet build Hexalith.Works.slnx -c Release
```

Run test projects individually:

```bash
dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release --no-build
```
