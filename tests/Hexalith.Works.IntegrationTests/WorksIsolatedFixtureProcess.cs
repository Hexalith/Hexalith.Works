using System.Diagnostics;
using System.Security;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Owns a disposable console for tests that require isolated process environment or inherited pipes.</summary>
internal sealed class WorksIsolatedFixtureProcess(string directory, string assembly) : IDisposable
{
    internal string DirectoryPath => directory;

    internal static async Task<WorksIsolatedFixtureProcess> CreateAsync(CancellationToken token)
    {
        string directory = System.IO.Directory.CreateTempSubdirectory("works-isolated-fixture-").FullName;
        try
        {
            string project = Path.Combine(directory, "Fixture.csproj");
            string proofDirectory = Path.GetDirectoryName(typeof(WorksFixtureDaprAuthenticationTests).Assembly.Location)!;
            string references = string.Join(Environment.NewLine, new[] { "Hexalith.Works.IntegrationTests", "Aspire.Hosting", "Dapr.Client", "Dapr.Common", "Grpc.Core.Api" }
                .Select(name => $"<Reference Include=\"{name}\"><HintPath>{SecurityElement.Escape(Path.Combine(proofDirectory, name + ".dll"))}</HintPath><Private>true</Private></Reference>"));
            await File.WriteAllTextAsync(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
                    <NuGetAudit>true</NuGetAudit><NuGetAuditMode>all</NuGetAuditMode></PropertyGroup>
                  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />
                    {references}
                  </ItemGroup>
                </Project>
                """, token).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), Program, token).ConfigureAwait(false);
            string output = Path.Combine(directory, "output");
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in new[] { "build", project, "-c", "Release", "--output", output,
                "--artifacts-path", Path.Combine(directory, "artifacts"), "--disable-build-servers", "-m:1", "-nr:false" }) start.ArgumentList.Add(argument);
            var built = await RunAsync(start, token).ConfigureAwait(false);
            if (built.ExitCode != 0) throw new InvalidOperationException("The isolated fixture console did not build: " + built.Output);
            return new(directory, Path.Combine(output, "Fixture.dll"));
        }
        catch { System.IO.Directory.Delete(directory, recursive: true); throw; }
    }

    internal ProcessStartInfo StartInfo(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(assembly);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task<(int ExitCode, string Output)> RunAsync(ProcessStartInfo start, CancellationToken token)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        operation.CancelAfter(TimeSpan.FromSeconds(60));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The isolated fixture did not launch.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(operation.Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(operation.Token);
        try { await Task.WhenAll(output, errors, process.WaitForExitAsync(operation.Token)).WaitAsync(operation.Token).ConfigureAwait(false); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await Task.WhenAll(output, errors, process.WaitForExitAsync(cleanup.Token)).WaitAsync(cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cleanup.IsCancellationRequested) { }
        }
        return (process.ExitCode, await output.ConfigureAwait(false) + await errors.ConfigureAwait(false));
    }

    public void Dispose()
    {
        foreach (string path in System.IO.Directory.EnumerateFiles(directory, "*.pid"))
        {
            string[] identity = File.ReadAllText(path).Split('|');
            try
            {
                using var child = Process.GetProcessById(int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture));
                if (!child.HasExited && child.StartTime.ToUniversalTime().Ticks == long.Parse(identity[1], System.Globalization.CultureInfo.InvariantCulture))
                {
                    child.Kill(entireProcessTree: true);
                    if (!child.WaitForExit(5000)) throw new InvalidOperationException("Owned fixture descendant did not stop.");
                }
            }
            catch (ArgumentException) { }
        }
        System.IO.Directory.Delete(directory, recursive: true);
    }

    private const string Program = """
        using System.Diagnostics;
        using System.Reflection;
        using Aspire.Hosting;
        using Aspire.Hosting.ApplicationModel;
        using Dapr;
        using Dapr.Client;
        using Grpc.Core;
        if (args[0] == "hold") { await Task.Delay(TimeSpan.FromSeconds(10)); return; }
        if (args[0] == "pipes")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("hold");
            using var child = Process.Start(start)!;
            File.WriteAllText(args[1], child.Id + "|" + child.StartTime.ToUniversalTime().Ticks);
            Console.Write("net11.0"); Console.Error.Write("held pipe"); return;
        }
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var resource = builder.AddResource(new ProjectResource("tokenless"))
            .WithEnvironment("DAPR_HTTP_ENDPOINT", args[1]).WithEnvironment("DAPR_GRPC_ENDPOINT", args[2]);
        using var app = builder.Build();
        var snapshot = new ResourceEvent(resource.Resource, resource.Resource.Name, new CustomResourceSnapshot { ResourceType = "project", Properties = [] });
        var helpers = typeof(Hexalith.Works.IntegrationTests.WorksFixtureDaprAuthenticationTests).Assembly.GetType("Hexalith.Works.IntegrationTests.WorksAppHostTestReadiness")!;
        using DaprClient client = await (Task<DaprClient>)helpers.GetMethod("CreateSdkDaprClientAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [app, snapshot, CancellationToken.None])!;
        try { await client.SaveStateAsync("statestore", "owned", "proof"); }
        catch (DaprException error) when (error.InnerException is RpcException { StatusCode: StatusCode.Unauthenticated })
        { Console.Write("ambient-token-refused"); return; }
        throw new InvalidOperationException("A tokenless selected resource inherited ambient authority.");
        """;
}
