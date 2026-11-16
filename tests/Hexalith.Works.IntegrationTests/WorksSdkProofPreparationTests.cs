using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Requires real SDK qualification in a clean console, independent of other test assembly loads.</summary>
public sealed class WorksSdkProofPreparationTests
{
    /// <summary>The public SDK observes only the console's ordinary file-backed runtime dependencies.</summary>
    [Fact(Timeout = 300000)]
    public async Task PublishedSdkPreparesTheRealDeclaredProofAndGuardsItsStartAsync()
    {
        string scratch = Directory.CreateTempSubdirectory("works-proof-driver-").FullName;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(285));
        try
        {
            string proof = typeof(WorksSdkProofPreparationTests).Assembly.Location;
            string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(proof)));
            string project = Path.Combine(scratch, "WorksSdkProofQualification.csproj");
            await File.WriteAllTextAsync(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework><RuntimeFrameworkVersion>11.0.0</RuntimeFrameworkVersion>
                    <OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />
                    <Reference Include="Hexalith.Works.IntegrationTests"><HintPath>{SecurityElement.Escape(proof)}</HintPath><Private>true</Private></Reference>
                  </ItemGroup>
                </Project>
                """, deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(scratch, "Program.cs"), """
                var result = await Hexalith.Works.IntegrationTests.WorksSdkProofPreparationQualification.RunAsync(
                    args[0], args[1], args[3], CancellationToken.None);
                await File.WriteAllTextAsync(args[2], result.ToJsonString());
                """, deadline.Token);
            string output = Path.Combine(scratch, "output");
            await RunAsync(["build", project, "-c", "Release", "--output", output,
                "--artifacts-path", Path.Combine(scratch, "artifacts"), "--disable-build-servers", "-m:1", "-nr:false"], scratch, deadline.Token);
            string resultPath = Path.Combine(scratch, "qualification.json");
            string runId = Guid.NewGuid().ToString("N");
            await RunAsync([Path.Combine(output, "WorksSdkProofQualification.dll"), SourceRoot(), proof, resultPath, runId], scratch, deadline.Token);
            using var evidence = JsonDocument.Parse(await File.ReadAllBytesAsync(resultPath, deadline.Token));
            var result = evidence.RootElement;
            result.GetProperty("schema").GetString().ShouldBe("hexalith.works-sdk-proof-qualification.v1");
            result.GetProperty("runId").GetString().ShouldBe(runId);
            result.GetProperty("processId").GetInt32().ShouldNotBe(Environment.ProcessId);
            result.GetProperty("launcherProofPath").GetString().ShouldBe(proof);
            result.GetProperty("launcherProofSha256").GetString().ShouldBe(digest);
            result.GetProperty("proofSha256").GetString()!.Length.ShouldBe(64);
            result.GetProperty("consumerManifestSha256").GetString()!.Length.ShouldBe(64);
            result.GetProperty("consumerFileCount").GetInt32().ShouldBeGreaterThan(3);
            result.GetProperty("runtimeEntrySha256").GetString()!.Length.ShouldBe(64);
            Guid.Parse(result.GetProperty("moduleVersionId").GetString()!).ShouldNotBe(Guid.Empty);
            result.GetProperty("declarations").GetInt32().ShouldBeGreaterThan(0);
            result.GetProperty("operations").GetInt32().ShouldBeGreaterThan(0);
            result.GetProperty("beforeStartVerified").GetBoolean().ShouldBeTrue();
            result.GetProperty("changedOriginalRefused").GetBoolean().ShouldBeTrue();
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static async Task RunAsync(string[] arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The isolated SDK proof process did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
        }
        string diagnostics = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        process.ExitCode.ShouldBe(0, diagnostics);
    }

    private static string SourceRoot([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
