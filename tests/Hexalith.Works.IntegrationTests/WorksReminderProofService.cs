using System.Text.Json;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Finite test driver inside the actual Works host; it borrows no other host's identity.</summary>
internal sealed class WorksReminderProofService(IReminderRegistrar registrar, IRouteCatalogAuthority authority,
    IConfiguration configuration, IHostEnvironment environment, WorksReminderPhaseDiagnostics? diagnostics = null) : BackgroundService
{
    internal async Task<WorksReminderProofResult> DispatchAsync(WorksProofCallerRequest request, string nonce, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!environment.IsDevelopment() || configuration["WORKS_SDK_PROOF"] != "1" || configuration["WORKS_SDK_PROOF_ROLE"] != "works"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] != "works"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] != "works"
            || !Guid.TryParseExact(configuration[WorksReminderProofProtocol.SessionKey], "N", out _))
            throw new InvalidOperationException("The reminder proof driver requires the actual selected Development Works host.");
        ProcessSource[] grants = JsonSerializer.Deserialize<ProcessSource[]>(configuration["WORKS_PLATFORM_RECOVERY_GRANTS"] ?? "[]")!;
        ReminderTarget target = WorksReminderProofProtocol.Validate(request, nonce, configuration[WorksReminderProofProtocol.SessionKey]!, DateTimeOffset.UtcNow, grants);
        RouteCatalogDispatchLease lease = await ObserveAsync("driver-acquire", nonce, () => authority.AcquireAsync(token)).ConfigureAwait(false);
        await using var release = lease.ConfigureAwait(false);
        var before = await ObserveAsync("driver-readback-before", nonce, () => authority.ReadbackAsync(token)).ConfigureAwait(false);
        if (!before.Serving || before.Profile != "Development" || before.Epoch <= 0 || before.RequiredHosts.Count < 4
            || before.RequiredHosts.Count(host => host.HostId == "works" && host.AppId == "works") != 1
            || lease.Snapshot.LocalAppId != "works" || lease.Snapshot.RootDigest != before.RootDigest || lease.Snapshot.Epoch != before.Epoch)
            throw new InvalidOperationException("The actual Works driver has no matching complete committed generation.");
        using IDisposable scope = RouteCatalogRemoteCall.Enter(lease);
        ReminderConvergenceResult[] outcomes = await ObserveAsync("driver-converge", nonce, () => lease.InvokeRemoteAsync(
            () => Task.WhenAll(registrar.ConvergeAsync(target, token), registrar.ConvergeAsync(target, token)).WaitAsync(token), token)).ConfigureAwait(false);
        var after = await ObserveAsync("driver-readback-after", nonce, () => authority.ReadbackAsync(token)).ConfigureAwait(false);
        if (!after.Serving || after.RootDigest != before.RootDigest || after.Epoch != before.Epoch)
            throw new InvalidOperationException("The reminder proof crossed a catalog generation.");
        return new(outcomes, after);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string directory = configuration[WorksReminderProofProtocol.DirectoryKey]!;
        string session = configuration[WorksReminderProofProtocol.SessionKey]!;
        WorksProofCallerProtocol.RequireDirectory(directory);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.request.json"))
            {
                string nonce = Path.GetFileName(file)[..^".request.json".Length];
                if (!Guid.TryParseExact(nonce, "N", out _)) throw new InvalidOperationException("A reminder nonce cannot select a path.");
                string cancel = Path.Combine(directory, nonce + ".cancel");
                WorksProofCallerResponse response;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                Task watcher = WatchCancellationAsync(cancel, cancellation);
                try
                {
                    var request = JsonSerializer.Deserialize<WorksProofCallerRequest>(await WorksProofCallerProtocol.ReadAsync(file, stoppingToken).ConfigureAwait(false))!;
                    if (request.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("The reminder proof expired.");
                    cancellation.CancelAfter(request.ExpiresAt - DateTimeOffset.UtcNow);
                    if (File.Exists(cancel)) cancellation.Cancel();
                    WorksReminderProofResult result = await DispatchAsync(request, nonce, cancellation.Token).ConfigureAwait(false);
                    response = new(nonce, session, 200, JsonSerializer.SerializeToUtf8Bytes(result), "application/json", result.Generation, null);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    response = new(nonce, session, 0, JsonSerializer.SerializeToUtf8Bytes(WorksReminderPhaseDiagnostics.ExceptionShape(exception)),
                        "application/json", null, exception is OperationCanceledException ? "cancelled" : "caller-refused");
                }
                finally
                {
                    cancellation.Cancel();
                    await watcher.ConfigureAwait(false);
                }
                await WorksProofCallerProtocol.WriteNewAsync(Path.Combine(directory, nonce + ".response.json"), response, stoppingToken).ConfigureAwait(false);
                File.Delete(file);
            }
            await Task.Delay(25, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<T> ObserveAsync<T>(string phase, string nonce, Func<Task<T>> operation)
    {
        long sequence = diagnostics?.Begin(phase, nonce) ?? 0;
        try
        {
            T result = await operation().ConfigureAwait(false);
            diagnostics?.End(sequence, null);
            return result;
        }
        catch (Exception exception)
        {
            diagnostics?.End(sequence, exception);
            throw;
        }
    }

    private static async Task WatchCancellationAsync(string path, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (File.Exists(path)) { cancellation.Cancel(); return; }
                await Task.Delay(25, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
}
