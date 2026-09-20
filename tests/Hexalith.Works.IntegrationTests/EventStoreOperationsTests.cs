using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Regresses the runtime-only EventStore Operations executable consumed by the Works topology.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class EventStoreOperationsTests
{
    /// <summary>Builds the isolated executable and verifies Dapr reminder dispatch interfaces in compiled metadata.</summary>
    [Fact]
    public async Task DeadLetterDrainActorCompiledMetadataDeclaresBothDispatchInterfacesAsync()
    {
        string root = LocateRepositoryRoot();
        string project = Path.Combine(
            root,
            "references",
            "Hexalith.EventStore",
            "src",
            "Hexalith.EventStore.Operations",
            "Hexalith.EventStore.Operations.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-p:UseHexalithProjectReferences=false");

        using Process process = Process.Start(startInfo).ShouldNotBeNull();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException exception)
        {
            await KillAndReapAsync(process).ConfigureAwait(true);

            if (TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException("The isolated EventStore Operations Release build exceeded ten minutes.", exception);
        }

        string standardOutput = await standardOutputTask.ConfigureAwait(true);
        string standardError = await standardErrorTask.ConfigureAwait(true);
        process.ExitCode.ShouldBe(
            0,
            "The runtime-only EventStore Operations project must build independently of every Works compile graph."
                + Environment.NewLine
                + standardOutput
                + Environment.NewLine
                + standardError);

        string assembly = Path.Combine(
            Path.GetDirectoryName(project).ShouldNotBeNull(),
            "bin",
            "Release",
            "net10.0",
            "Hexalith.EventStore.Operations.dll");
        File.Exists(assembly).ShouldBeTrue($"The isolated build did not produce {assembly}.");

        using FileStream stream = File.OpenRead(assembly);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        TypeDefinitionHandle actorHandle = FindTypeDefinition(
            metadata,
            "Hexalith.EventStore.Operations.Actors",
            "DeadLetterDrainActor");
        TypeDefinition actor = metadata.GetTypeDefinition(actorHandle);
        string[] interfaces = [.. actor.GetInterfaceImplementations()
            .Select(handle => metadata.GetInterfaceImplementation(handle).Interface)
            .Select(handle => MetadataTypeIdentity(metadata, handle))];

        interfaces.ShouldContain("Dapr.Actors:Dapr.Actors.Runtime.IRemindable");
        interfaces.ShouldContain(
            "Hexalith.EventStore.Operations:Hexalith.EventStore.Operations.Actors.IDeadLetterDrainActor");

        TypeDefinition deadLetterRecord = metadata.GetTypeDefinition(FindTypeDefinition(
            metadata,
            "Hexalith.EventStore.Operations.Models",
            "DeadLetterRecord"));
        string[] recordProperties = [.. deadLetterRecord.GetProperties()
            .Select(handle => metadata.GetString(metadata.GetPropertyDefinition(handle).Name))];
        recordProperties.ShouldContain("Body");
    }

    private static TypeDefinitionHandle FindTypeDefinition(
        MetadataReader metadata,
        string typeNamespace,
        string typeName)
        => metadata.TypeDefinitions.Single(handle =>
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Namespace) == typeNamespace
                && metadata.GetString(type.Name) == typeName;
        });

    private static string MetadataTypeIdentity(MetadataReader metadata, EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => $"{AssemblyName(metadata)}:{JoinName(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)handle))}",
            HandleKind.TypeReference => TypeReferenceIdentity(metadata, metadata.GetTypeReference((TypeReferenceHandle)handle)),
            _ => throw new InvalidDataException($"Unexpected interface metadata handle kind: {handle.Kind}."),
        };

    private static string TypeReferenceIdentity(MetadataReader metadata, TypeReference type)
        => $"{ResolutionScopeAssemblyName(metadata, type.ResolutionScope)}:{JoinName(metadata, type)}";

    private static string ResolutionScopeAssemblyName(MetadataReader metadata, EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.AssemblyReference => metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)handle).Name),
            HandleKind.ModuleDefinition => AssemblyName(metadata),
            HandleKind.TypeReference => ResolutionScopeAssemblyName(
                metadata,
                metadata.GetTypeReference((TypeReferenceHandle)handle).ResolutionScope),
            _ => throw new InvalidDataException($"Unexpected type resolution scope: {handle.Kind}."),
        };

    private static string AssemblyName(MetadataReader metadata)
        => metadata.GetString(metadata.GetAssemblyDefinition().Name);

    private static string JoinName(MetadataReader metadata, TypeDefinition type)
        => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";

    private static string JoinName(MetadataReader metadata, TypeReference type)
        => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";

    private static async Task KillAndReapAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The child exited between the state check and the kill request; it still must be reaped below.
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private static string LocateRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Hexalith.Works.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Hexalith.Works repository root.");
    }
}
