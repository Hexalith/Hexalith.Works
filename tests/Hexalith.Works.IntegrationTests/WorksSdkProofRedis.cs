using System.Diagnostics;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Owns an isolated disposable Redis keyspace and generated Dapr component for the live SDK proof.</summary>
internal sealed class WorksSdkProofRedis(string name, string directory) : IAsyncDisposable
{
    internal string ComponentPath => Path.Combine(directory, "statestore.yaml");

    internal static async Task<WorksSdkProofRedis> StartAsync(CancellationToken cancellationToken)
    {
        string name = "works-sdk415-" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(directory);
        var fixture = new WorksSdkProofRedis(name, directory);
        try
        {
            _ = await DockerAsync(["run", "--rm", "-d", "--name", name, "-p", "127.0.0.1::6379", "redis:7.4-alpine"], cancellationToken).ConfigureAwait(false);
            string binding = (await DockerAsync(["port", name, "6379/tcp"], cancellationToken).ConfigureAwait(false)).Trim();
            string yaml = """
                apiVersion: dapr.io/v1alpha1
                kind: Component
                metadata:
                  name: statestore
                spec:
                  type: state.redis
                  version: v1
                  metadata:
                    - name: redisHost
                      value: "BINDING"
                    - name: redisPassword
                      value: ""
                    - name: actorStateStore
                      value: "true"
                scopes:
                  - eventstore
                  - works
                  - eventstore-admin
                  - eventstore-operations
                """;
            await File.WriteAllTextAsync(fixture.ComponentPath, yaml.Replace("BINDING", binding, StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ = await DockerAsync(["rm", "-f", name], CancellationToken.None).ConfigureAwait(false);
        Directory.Delete(directory, true);
    }

    private static async Task<string> DockerAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch { process.Kill(true); throw; }
        string diagnostic = await error.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException("Synthetic Redis fixture Docker operation failed: " + diagnostic);
        return await output.ConfigureAwait(false);
    }
}
