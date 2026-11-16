using System.Diagnostics;

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

    /// <summary>Uses the selected project's framework, including an explicitly retained historical SDK target.</summary>
    internal static string DefaultDirectoryFor(string project, string configuration)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(project))!,
        };
        foreach (string argument in new[] { "msbuild", project, "-nologo", "-nr:false", "-getProperty:TargetFramework", "-p:Configuration=" + configuration })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not inspect the selected runtime project.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> diagnostic = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("Selected runtime project framework inspection exceeded 30 seconds.");
        }
        string framework = output.GetAwaiter().GetResult().Trim();
        string errors = diagnostic.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || framework is not ("net10.0" or "net11.0"))
            throw new InvalidOperationException($"Cannot establish the selected runtime project's supported framework: {framework} {errors}");
        return Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration, framework);
    }
}
