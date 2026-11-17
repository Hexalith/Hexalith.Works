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
        project = Path.GetFullPath(project);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(project))!,
        };
        foreach (string argument in new[] { "msbuild", project, "-nologo", "-nr:false", "-getProperty:TargetFramework", "-p:Configuration=" + configuration })
            start.ArgumentList.Add(argument);
        string framework = InspectFramework(start, TimeSpan.FromSeconds(30));
        return Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration, framework);
    }

    internal static string InspectFramework(ProcessStartInfo start, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not inspect the selected runtime project.");
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
            throw new InvalidOperationException("Selected runtime project framework inspection exceeded its finite deadline.");
        }
        string framework = output.GetAwaiter().GetResult().Trim();
        string errors = diagnostic.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || framework is not ("net10.0" or "net11.0"))
            throw new InvalidOperationException($"Cannot establish the selected runtime project's supported framework: {framework} {errors}");
        return framework;
    }

}
