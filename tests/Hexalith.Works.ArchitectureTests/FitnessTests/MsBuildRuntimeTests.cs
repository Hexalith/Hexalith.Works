using System.Diagnostics;
using System.Reflection;

using Microsoft.Build.Locator;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>Exercises runtime registration in new processes with independently located SDK installations.</summary>
public sealed class MsBuildRuntimeTests
{
    private const string ProbeRootVariable = "HEXALITH_WORKS_MSBUILD_PROBE_ROOT";
    private const string ProbeFailureVariable = "HEXALITH_WORKS_MSBUILD_PROBE_FAILURE";
    private const string ProbeFilteredVariable = "HEXALITH_WORKS_MSBUILD_PROBE_FILTERED";

    /// <summary>MSBuild and EventLog runtime assemblies must come from the selected SDK.</summary>
    [Fact]
    public void OutputDoesNotCarrySdkRuntimeAssembliesOrCapturedToolsPath()
    {
        Directory.GetFiles(AppContext.BaseDirectory, "Microsoft.Build*.dll", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "Microsoft.Build.Locator.dll").ShouldBeEmpty();
        Directory.GetFiles(AppContext.BaseDirectory, "System.Diagnostics.EventLog*.dll", SearchOption.AllDirectories).ShouldBeEmpty();
        typeof(MsBuildRuntimeTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ShouldNotContain(attribute => attribute.Key == "MSBuildToolsPath");
        foreach (string dependency in new[] { "Microsoft.NET.StringTools.dll", "System.Configuration.ConfigurationManager.dll", "System.Reflection.MetadataLoadContext.dll" })
        {
            File.Exists(Path.Combine(AppContext.BaseDirectory, dependency)).ShouldBeFalse();
        }
    }

    /// <summary>A native-selected SDK filtered by Locator cannot be replaced by an accepted alternate SDK.</summary>
    [Fact]
    public void FilteredNativeSdkSelectionIsRefused()
    {
        string selected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "native-sdk"));
        string alternate = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "alternate-sdk"));
        MsBuildRuntime.RequireNativeSelection(selected, [alternate, selected]).ShouldBe(selected);
        InvalidOperationException failure = Should.Throw<InvalidOperationException>(() =>
            MsBuildRuntime.RequireNativeSelection(selected, [alternate]));
        failure.Message.ShouldContain(selected);
        failure.Message.ShouldContain("filtered", customMessage: failure.ToString());
        failure.Message.ShouldContain("Refusing to substitute");
    }

    /// <summary>Runs unchanged compiled tests through a relocated SDK, and refuses an unsatisfied SDK policy.</summary>
    /// <param name="missingSdk">Whether to request an unavailable SDK.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshProcessUsesRelocatedSdkAndHonorsRepositoryPolicyAsync(bool missingSdk)
        => await RunRelocationProbeAsync(missingSdk, filteredSdk: false).ConfigureAwait(true);

    /// <summary>Register itself refuses a native-selected SDK that Locator filters while an alternate remains accepted.</summary>
    [Fact]
    public async Task FreshProcessRegistrationRefusesFilteredNativeSdkAsync()
        => await RunRelocationProbeAsync(missingSdk: false, filteredSdk: true).ConfigureAwait(true);

    private static async Task RunRelocationProbeAsync(bool missingSdk, bool filteredSdk)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.MsBuildRelocation-");
        try
        {
            string selectedSdk = MsBuildRuntime.ToolsPath;
            string installedRoot = Directory.GetParent(selectedSdk)!.Parent!.FullName;
            string relocatedRoot = Path.Combine(temporaryRoot.FullName, "dotnet");
            string repository = Path.Combine(temporaryRoot.FullName, "repository");
            Directory.CreateDirectory(relocatedRoot);
            Directory.CreateDirectory(repository);
            Directory.CreateDirectory(Path.Combine(relocatedRoot, "sdk"));
            string executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            string host = Path.Combine(relocatedRoot, executable);
            File.Copy(Path.Combine(installedRoot, executable), host);
            foreach (string directory in new[] { "host", "shared" })
            {
                AliasInstallationDirectory(Path.Combine(relocatedRoot, directory), Path.Combine(installedRoot, directory));
            }

            string relocatedSdk = Path.Combine(relocatedRoot, "sdk", Path.GetFileName(selectedSdk));
            if (filteredSdk)
            {
                CopySdkForFilterControl(relocatedSdk, selectedSdk);
                SetSdkReportedVersion(relocatedSdk, "99.0.0");
                string alternateSdk = Path.Combine(relocatedRoot, "sdk", "10.0.100");
                CopySdkForFilterControl(alternateSdk, selectedSdk);
                SetSdkReportedVersion(alternateSdk, "10.0.100");
            }
            else
            {
                AliasInstallationDirectory(relocatedSdk, selectedSdk);
            }
            string policy = missingSdk
                ? """{"sdk":{"version":"99.0.100","rollForward":"disable"}}"""
                : File.ReadAllText(Path.Combine(RepositoryRoot.Locate(), "global.json"));
            File.WriteAllText(Path.Combine(repository, "global.json"), policy);
            File.WriteAllText(Path.Combine(repository, "Probe.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
                """);

            var start = new ProcessStartInfo(host)
            {
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(typeof(MsBuildRuntimeTests).Assembly.Location);
            start.ArgumentList.Add("-method");
            start.ArgumentList.Add("*MsBuildRuntimeTests.FreshProcessProbe");
            start.Environment[ProbeRootVariable] = repository;
            start.Environment[ProbeFailureVariable] = missingSdk.ToString();
            start.Environment[ProbeFilteredVariable] = filteredSdk.ToString();
            start.Environment["DOTNET_ROOT"] = relocatedRoot;
            start.Environment["DOTNET_ROOT_X64"] = relocatedRoot;
            start.Environment["DOTNET_HOST_PATH"] = host;
            start.Environment["DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR"] = relocatedRoot;
            start.Environment["PATH"] = relocatedRoot;
            start.Environment.Remove("MSBUILD_EXE_PATH");
            start.Environment.Remove("MSBuildSDKsPath");
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start relocated SDK probe.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            string diagnostic = await output.ConfigureAwait(true) + await error.ConfigureAwait(true);
            process.ExitCode.ShouldBe(0, diagnostic);
            diagnostic.ShouldContain("MSBUILD_PROBE_PASSED", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>Child-process entry point; normal suite execution has no probe state.</summary>
    [Fact]
    public void FreshProcessProbe()
    {
        string? repository = Environment.GetEnvironmentVariable(ProbeRootVariable);
        if (repository is null)
        {
            return;
        }

        if (bool.Parse(Environment.GetEnvironmentVariable(ProbeFilteredVariable) ?? "false"))
        {
            VisualStudioInstance[] accepted = [.. MSBuildLocator.QueryVisualStudioInstances(new()
            {
                WorkingDirectory = repository,
                DiscoveryTypes = DiscoveryType.DotNetSdk,
            })];
            accepted.ShouldContain(instance => Path.GetFileName(instance.MSBuildPath) == "10.0.100");
            InvalidOperationException failure = Should.Throw<InvalidOperationException>(() => MsBuildRuntime.Register(repository));
            failure.Message.ShouldContain("filtered", customMessage: failure.ToString());
            failure.Message.ShouldContain("Refusing to substitute");
            MSBuildLocator.IsRegistered.ShouldBeFalse();
        }
        else if (bool.Parse(Environment.GetEnvironmentVariable(ProbeFailureVariable)!))
        {
            InvalidOperationException failure = Should.Throw<InvalidOperationException>(() => MsBuildRuntime.Register(repository));
            failure.Message.ShouldContain("global.json", Case.Sensitive);
            failure.Message.ShouldContain("Install a compatible .NET SDK", Case.Sensitive);
        }
        else
        {
            string toolsPath = MsBuildRuntime.Register(repository);
            toolsPath.ShouldStartWith(Path.Combine(Directory.GetParent(repository)!.FullName, "dotnet", "sdk"));
            MsBuildProjectEvaluation.TryEvaluate(Path.Combine(repository, "Probe.csproj"),
                out MsBuildProjectSnapshot? snapshot, out string diagnostic).ShouldBeTrue(diagnostic);
            snapshot.ShouldNotBeNull().Items.ShouldNotBeNull();
            snapshot.ImportPaths.ShouldBeEmpty("Selected SDK imports must not enter the custom import closure.");
            string worksRoot = RepositoryRoot.Locate();
            MsBuildProjectEvaluation.TryEvaluateIndependentSupportedLanes(
                Path.Combine(worksRoot, "src", "Hexalith.Works.Contracts", "Hexalith.Works.Contracts.csproj"),
                out IReadOnlyList<MsBuildProjectSnapshot> worksLanes, out string worksDiagnostic).ShouldBeTrue(worksDiagnostic);
            worksLanes.ShouldNotBeEmpty();
            worksLanes.ShouldAllBe(lane => lane.ImportPaths.Contains(Path.Combine(worksRoot, "Directory.Build.props")));
            worksLanes.SelectMany(lane => lane.ImportPaths).ShouldNotContain(path => path.StartsWith(toolsPath, StringComparison.Ordinal));
            worksLanes.Single(lane => lane.PropertyValue("Configuration") == "Debug")
                .ItemsOfType("ProjectReference").ShouldContain(item => item.Identity.Contains("Hexalith.EventStore.Contracts", StringComparison.Ordinal));
        }

        Console.WriteLine("MSBUILD_PROBE_PASSED");
    }
    private static void SetSdkReportedVersion(string sdk, string version)
    {
        string path = Path.Combine(sdk, ".version");
        string[] lines = File.ReadAllLines(path);
        lines[1] = version;
        File.WriteAllLines(path, lines);
    }

    // Keep mutable .version files and SDK entry-point assemblies inside the owned installation.
    // Directory aliases below it are read-only sources and are never mutated by this control.
    private static void CopySdkForFilterControl(string destination, string source)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            AliasInstallationDirectory(Path.Combine(destination, Path.GetFileName(directory)), directory);
        }
    }

    private static void AliasInstallationDirectory(string alias, string source)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, source);
            return;
        }

        // Windows agents may lack symlink privileges. Copying the installation is slower but keeps
        // the same relocation assertion without requiring administrator or developer-mode settings.
        Directory.CreateDirectory(alias);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(alias, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(alias, Path.GetRelativePath(source, file)));
        }
    }

}
