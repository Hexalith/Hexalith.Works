using System.Diagnostics;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises the actual SDK interpretation of the finite qualification worker policy.</summary>
public sealed class WorksQualificationBuildEnvironmentTests
{
    /// <summary>Both installed SDKs honor compiler isolation; unrelated model resources receive no qualification override.</summary>
    /// <param name="sdk">The actual selected SDK, without a download or fallback.</param>
    [Theory]
    [InlineData("10.0.404")]
    [InlineData("11.0.100")]
    public async Task OwnedLaunchesDisableReusableCompilersInTheActualSdk(string sdk)
    {
        string directory = Directory.CreateTempSubdirectory("works-build-isolation-").FullName;
        try
        {
            string project = Path.Combine(directory, "Probe.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "global.json"), JsonSerializer.Serialize(new
            { sdk = new { version = sdk, rollForward = "disable" } }), TestContext.Current.CancellationToken).ConfigureAwait(true);
            IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder([]);
            ProjectResource owned = builder.AddProject("works", project).Resource;
            ProjectResource unrelated = builder.AddProject("unrelated", project).Resource;
            EnvironmentCallbackAnnotation[] unrelatedBefore = unrelated.Annotations.OfType<EnvironmentCallbackAnnotation>().ToArray();
            WorksAppHostSmokeHarness.ConfigureQualificationProjectLaunches(builder);
            unrelated.Annotations.OfType<EnvironmentCallbackAnnotation>().ShouldBe(unrelatedBefore);
            Dictionary<string, object> environment = [];
            var context = new EnvironmentCallbackContext(builder.ExecutionContext, owned, environment, TestContext.Current.CancellationToken);
            foreach (EnvironmentCallbackAnnotation annotation in owned.Annotations.OfType<EnvironmentCallbackAnnotation>())
                await annotation.Callback(context).ConfigureAwait(true);
            environment["UseSharedCompilation"].ShouldBe("false");
            environment["DOTNET_CLI_USE_MSBUILD_SERVER"].ShouldBe("0");
            environment["MSBUILDDISABLENODEREUSE"].ShouldBe("1");
            var start = new ProcessStartInfo("dotnet")
            { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in new[] { "msbuild", project, "-nologo", "-getProperty:UseSharedCompilation" })
                start.ArgumentList.Add(argument);
            foreach (var entry in environment) start.Environment[entry.Key] = entry.Value.ToString();
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            process.ExitCode.ShouldBe(0, await error.ConfigureAwait(true));
            (await output.ConfigureAwait(true)).Trim().ShouldBe("false");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
