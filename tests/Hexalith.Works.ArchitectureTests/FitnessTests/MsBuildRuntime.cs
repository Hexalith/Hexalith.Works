using System.Diagnostics;
using System.Text.RegularExpressions;

using Microsoft.Build.Locator;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>Registers the native policy-selected SDK before code using MSBuild types is compiled.</summary>
internal static class MsBuildRuntime
{
    private static readonly object _registrationLock = new();
    private static readonly Lazy<string> _toolsPath = new(() => Register(RepositoryRoot.Locate()));
    private static string? _registeredToolsPath;

    /// <summary>Gets the selected installed SDK path for assembly loading and import filtering.</summary>
    internal static string ToolsPath => _toolsPath.Value;

    /// <summary>Registers exactly the SDK selected by native resolution of the repository policy.</summary>
    /// <param name="repositoryRoot">The directory containing the repository global.json.</param>
    /// <returns>The selected installed SDK tools path.</returns>
    internal static string Register(string repositoryRoot)
    {
        lock (_registrationLock)
        {
            if (_registeredToolsPath is not null)
            {
                return _registeredToolsPath;
            }

            try
            {
                string nativePath = ResolveNativeSdk(repositoryRoot);
                VisualStudioInstance[] instances = [.. MSBuildLocator.QueryVisualStudioInstances(new()
                {
                    WorkingDirectory = repositoryRoot,
                    DiscoveryTypes = DiscoveryType.DotNetSdk,
                })];
                string selectedPath = RequireNativeSelection(nativePath, instances.Select(instance => instance.MSBuildPath));
                VisualStudioInstance selected = instances.First(instance => PathsEqual(instance.MSBuildPath, selectedPath));
                if (!File.Exists(Path.Combine(selectedPath, "Microsoft.Build.dll"))
                    || !Directory.Exists(Path.Combine(selectedPath, "Sdks")))
                {
                    throw new InvalidOperationException($"Native-selected SDK '{selectedPath}' has incomplete MSBuild tools.");
                }

                MSBuildLocator.RegisterInstance(selected);
                _registeredToolsPath = selectedPath;
                return selectedPath;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException
                or System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException(
                    $"Cannot register installed MSBuild under SDK policy '{Path.Combine(repositoryRoot, "global.json")}'. "
                    + $"Install a compatible .NET SDK and check DOTNET_ROOT/PATH. {exception.Message}", exception);
            }
        }
    }

    /// <summary>Rejects Locator fallback when its accepted instances omit the actual native-selected SDK.</summary>
    /// <param name="nativePath">The native resolver's selected SDK path.</param>
    /// <param name="acceptedPaths">The SDK paths accepted by Locator.</param>
    /// <returns>The native path when Locator accepts it.</returns>
    internal static string RequireNativeSelection(string nativePath, IEnumerable<string> acceptedPaths)
    {
        string? acceptedPath = acceptedPaths.FirstOrDefault(path => PathsEqual(path, nativePath));
        if (acceptedPath is null)
        {
            throw new InvalidOperationException(
                $"Native SDK policy selected '{nativePath}', but MSBuild Locator filtered or could not load that SDK. "
                + "Refusing to substitute another installed SDK; use a test runtime compatible with the selected SDK.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(acceptedPath));
    }

    private static string ResolveNativeSdk(string repositoryRoot)
    {
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? Environment.ProcessPath! : "dotnet");
        var start = new ProcessStartInfo(host)
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--info");
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native SDK resolution.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("Native SDK policy resolution timed out after 15 seconds.");
        }

        string standardOutput = output.GetAwaiter().GetResult();
        string standardError = error.GetAwaiter().GetResult();
        Match basePath = Regex.Match(standardOutput, @"(?m)^\s*Base Path:\s*(.+?)\r?$");
        if (process.ExitCode != 0 || !basePath.Success)
        {
            throw new InvalidOperationException($"No compatible native-selected SDK was found. {standardOutput} {standardError}");
        }

        return Path.GetFullPath(basePath.Groups[1].Value.Trim());
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(PhysicalDirectory(left), PhysicalDirectory(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Native SDK output resolves installation links on some hosts; Locator may retain their spelling.
    // Compare their actual directories, while keeping Locator's path for consistent SDK import filtering.
    private static string PhysicalDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string resolved = Path.GetPathRoot(fullPath)!;
        foreach (string segment in Path.GetRelativePath(resolved, fullPath).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, segment);
            var directory = new DirectoryInfo(resolved);
            if (directory.Exists && directory.LinkTarget is not null)
            {
                resolved = directory.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
            }
        }

        return Path.TrimEndingDirectorySeparator(resolved);
    }
}
