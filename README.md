# Hexalith.Works

Hexalith.Works is the Hexalith work item domain module. This repository is an umbrella workspace with root-level Hexalith submodules and a Works-specific Aspire host for local and automated validation.

## Build

Initialize only the root submodules the build references. These are the three Hexalith libraries
consumed as `ProjectReference` (see `Directory.Build.props`):

```bash
git submodule update --init Hexalith.EventStore Hexalith.PolymorphicSerializations Hexalith.Tenants
```

Do not use recursive submodule initialization.

Restore and build the solution:

```bash
dotnet restore Hexalith.Works.slnx
dotnet build Hexalith.Works.slnx -c Release
```

Run the test projects:

```bash
dotnet test tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release --no-build
dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release --no-build
dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release --no-build
dotnet test tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj -c Release --no-build
```

In restricted sandboxes where the Microsoft.Testing.Platform named pipes are blocked, run the built
xUnit v3 executables directly from each project's `bin/Release/net10.0/` instead of `dotnet test`.
