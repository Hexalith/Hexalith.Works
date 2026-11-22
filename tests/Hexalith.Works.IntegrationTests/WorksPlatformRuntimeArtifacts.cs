using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Resolves the exact qualified child deployment without sharing another host's build outputs.</summary>
internal static class WorksPlatformRuntimeArtifacts
{
    internal static string RequireMatchingConfigurations(string? consumer, string? host, string? retainedHost)
    {
        if (consumer is not ("Debug" or "Release") || host != consumer || retainedHost != consumer)
            throw new InvalidOperationException($"Qualified runtime configuration must match consumer, Platform host, and retained host: consumer={consumer ?? "missing"}, host={host ?? "missing"}, retained={retainedHost ?? "missing"}.");
        return host;
    }

    internal static string DirectoryFor(string project, string configuration)
    {
        string? artifacts = Environment.GetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS");
        if (artifacts is null) return DefaultDirectoryFor(project, configuration);
        if (!Path.IsPathFullyQualified(artifacts))
            throw new InvalidOperationException("Qualified runtime artifacts require an absolute owned output path.");
        return Path.Combine(artifacts, "bin", Path.GetFileNameWithoutExtension(project), configuration.ToLowerInvariant());
    }

    /// <summary>
    /// Aspire appends the AppHost's baked GlobalArtifactsPath after resource WithArgs.
    /// Bind that path to the serial-prebuilt runtime outputs so child <c>dotnet run</c>
    /// receives a single --artifacts-path.
    /// </summary>
    internal static void BindLaunchArtifacts(DistributedApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? artifacts = Environment.GetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS");
        if (string.IsNullOrEmpty(artifacts)) return;
        if (!Path.IsPathFullyQualified(artifacts) || !Directory.Exists(artifacts))
            throw new InvalidOperationException("Qualified runtime artifacts require an absolute existing output path.");
        PropertyInfo property = typeof(DistributedApplicationOptions).GetProperty("GlobalArtifactsPath",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Aspire did not expose GlobalArtifactsPath for the qualified runtime outputs.");
        property.SetValue(options, artifacts);
    }

    internal static string? ReadBoundLaunchArtifacts(DistributedApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return typeof(DistributedApplicationOptions).GetProperty("GlobalArtifactsPath",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(options) as string;
    }

    /// <summary>
    /// Serial prebuild already wrote the runtime outputs. <c>dotnet run</c> must not rebuild them
    /// under BuildProjectReferences=false.
    /// </summary>
    internal static void BindPrebuiltLaunch(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        string? artifacts = Environment.GetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS");
        if (string.IsNullOrEmpty(artifacts)) return;
        if (!Path.IsPathFullyQualified(artifacts) || !Directory.Exists(artifacts))
            throw new InvalidOperationException("Qualified runtime artifacts require an absolute existing output path.");
        foreach (ProjectResource project in builder.Resources.OfType<ProjectResource>().ToArray())
            builder.CreateResourceBuilder(project).WithArgs("--no-build", "--no-restore");
    }

    /// <summary>Resolves the .NET 11 project's evaluated output directory in its own SDK context.</summary>
    internal static string DefaultDirectoryFor(string project, string configuration)
    {
        project = Path.GetFullPath(project);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(project))!,
        };
        foreach (string argument in new[] { "msbuild", project, "-nologo", "-nr:false", "-getProperty:TargetFramework,TargetDir", "-p:Configuration=" + configuration })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not inspect the selected runtime project.");
        return InspectTargetDirectory(process, TimeSpan.FromSeconds(30));
    }

    /// <summary>Reads the evaluated target metadata within a deadline that also bounds redirected streams.</summary>
    internal static string InspectTargetDirectory(Process process, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> diagnostic = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            Task.WhenAll(output, diagnostic, process.WaitForExitAsync(deadline.Token)).WaitAsync(deadline.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                Task.WhenAll(output, diagnostic, process.WaitForExitAsync(cleanup.Token)).WaitAsync(cleanup.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cleanup.IsCancellationRequested) { }
            throw new InvalidOperationException("Selected runtime project metadata inspection exceeded its finite deadline.");
        }
        string metadata = output.GetAwaiter().GetResult();
        string errors = diagnostic.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Cannot evaluate the selected runtime project's output directory: {metadata} {errors}");

        try
        {
            using JsonDocument document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Properties", out JsonElement properties)
                && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("TargetFramework", out JsonElement framework)
                && framework.ValueKind == JsonValueKind.String && framework.ValueEquals("net11.0")
                && properties.TryGetProperty("TargetDir", out JsonElement directory) && directory.ValueKind == JsonValueKind.String
                && directory.GetString() is { } target && Path.IsPathFullyQualified(target))
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid output metadata for the selected runtime project: {errors}", exception);
        }

        throw new InvalidOperationException($"Cannot establish the selected runtime project's .NET 11 output directory: {metadata} {errors}");
    }

}
