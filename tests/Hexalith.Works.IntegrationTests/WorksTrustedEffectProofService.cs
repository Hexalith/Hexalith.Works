using System.Text.Json;
using System.Text.Json.Nodes;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Runs the finite replay inside its actual Works sender using the existing SDK submitter and issuer.</summary>
internal sealed class WorksTrustedEffectProofService(ITrustedEffectSubmitter submitter, IReminderDelegationTokenProvider delegation,
    IReadModelStore store, IRouteCatalogAuthority authority, IConfiguration configuration, IHostEnvironment environment) : BackgroundService
{
    /// <summary>Replays only the exact persisted effect, or its existing fixed semantic-conflict negative control.</summary>
    internal async Task<WorksProofCallerResponse> DispatchAsync(WorksProofCallerRequest request, string nonce, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string session = configuration[WorksTrustedEffectProofProtocol.SessionKey]!;
        if (!environment.IsDevelopment() || configuration["WORKS_SDK_PROOF"] != "1" || configuration["WORKS_SDK_PROOF_ROLE"] != "works"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] != "works"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] != "works"
            || !Guid.TryParseExact(session, "N", out _))
            throw new InvalidOperationException("The effect proof requires the actual primary Development Works host.");
        ProcessSource[] grants = JsonSerializer.Deserialize<ProcessSource[]>(configuration["WORKS_PLATFORM_RECOVERY_GRANTS"] ?? "[]")!;
        var target = WorksTrustedEffectProofProtocol.Validate(request, nonce, session, DateTimeOffset.UtcNow, grants);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(request.ExpiresAt - DateTimeOffset.UtcNow);
        var lease = await authority.AcquireAsync(deadline.Token).ConfigureAwait(false);
        await using var release = lease.ConfigureAwait(false);
        var before = await authority.ReadbackAsync(deadline.Token).ConfigureAwait(false);
        string[] required = ["eventstore", "eventstore-admin", "eventstore-operations", "works"];
        if (!before.Serving || before.Profile != "Development" || before.Epoch <= 0
            || required.Any(name => before.RequiredHosts.Count(host => host.HostId == name && host.AppId == name) != 1)
            || lease.Snapshot.LocalAppId != "works" || lease.Snapshot.RootDigest != before.RootDigest || lease.Snapshot.Epoch != before.Epoch)
            throw new InvalidOperationException("The effect proof has no matching complete committed generation.");
        using var scope = RouteCatalogRemoteCall.Enter(lease);
        WorksSdkProofFaultState fault = (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + target.Tenant,
            deadline.Token).ConfigureAwait(false)).Value ?? throw new InvalidOperationException("The original retained receipt is absent.");
        TrustedEffectSubmission submission = fault.Submission ?? throw new InvalidOperationException("The original retained submission is absent.");
        var identity = submission.Identity;
        string message = EffectIdentityCodec.ComputeMessageId(identity);
        if (fault.FailNext || fault.CommittedEffectId != target.EffectId || fault.Disposition != TrustedEffectDisposition.NoOp
            || identity.Tenant != target.Tenant || identity.SourceDomain != "work" || identity.SourceAggregate != target.Source
            || identity.TargetDomain != "work" || identity.TargetAggregate != target.Target || identity.SourceEnvelopeSequence <= 0
            || identity.EffectKind != EffectKindCatalog.ChildCompletionResume || identity.Ordinal != EffectKindCatalog.PrimaryTargetOrdinal
            || EffectIdentityCodec.ComputeEffectId(identity) != target.EffectId || submission.CommandType != "ResumeWorkItem"
            || submission.MessageId != message || submission.IdempotencyKey != message || string.IsNullOrWhiteSpace(fault.CausationId))
            throw new InvalidOperationException("The selected effect differs from its exact persisted fixture receipt.");
        if (request.Command == "conflict-child-completion-resume")
        {
            JsonObject payload = JsonNode.Parse(submission.CommandPayload)!.AsObject();
            string condition = payload.Select(property => property.Key).Single(name => string.Equals(name, "AwaitCondition", StringComparison.OrdinalIgnoreCase));
            if (payload[condition] is null) throw new InvalidOperationException("The original witnessed condition is absent.");
            payload[condition] = null;
            submission = submission with { CommandPayload = JsonSerializer.SerializeToUtf8Bytes(payload) };
        }
        string credential = await delegation.GetDelegationTokenAsync(new ReminderDelegationRequest(submission, "works",
            WorksTrustedEffectProofProtocol.Purpose, fault.CausationId), deadline.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual Works delegation issuer refused the effect.");
        int status = 200; byte[] body;
        try
        {
            var receipt = await lease.InvokeRemoteAsync(() => submitter.SubmitAsync(submission,
                new TrustedEffectContext("works", WorksTrustedEffectProofProtocol.Purpose, fault.CausationId, credential), deadline.Token)
                .WaitAsync(deadline.Token), deadline.Token).ConfigureAwait(false);
            body = JsonSerializer.SerializeToUtf8Bytes(receipt);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is { } code && (int)code is >= 400 and <= 599)
        {
            // Preserve the actual SDK HTTP refusal; InvokeRemoteAsync retains its conservative ambiguity fence.
            status = (int)exception.StatusCode.Value; body = [];
        }
        var after = await authority.ReadbackAsync(deadline.Token).ConfigureAwait(false);
        if (!after.Serving || after.RootDigest != before.RootDigest || after.Epoch != before.Epoch)
            throw new InvalidOperationException("The effect proof crossed its admitted generation.");
        return new(nonce, session, status, body, "application/json", after, null);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string directory = configuration[WorksTrustedEffectProofProtocol.DirectoryKey]!, session = configuration[WorksTrustedEffectProofProtocol.SessionKey]!;
        WorksProofCallerProtocol.RequireDirectory(directory);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.request.json"))
            {
                string nonce = Path.GetFileName(file)[..^".request.json".Length];
                if (!Guid.TryParseExact(nonce, "N", out _)) throw new InvalidOperationException("An effect nonce cannot select a path.");
                string cancel = Path.Combine(directory, nonce + ".cancel");
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                Task watcher = WatchCancellationAsync(cancel, cancellation);
                WorksProofCallerResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<WorksProofCallerRequest>(await WorksProofCallerProtocol.ReadAsync(file, stoppingToken).ConfigureAwait(false))!;
                    if (File.Exists(cancel)) cancellation.Cancel();
                    response = await DispatchAsync(request, nonce, cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    response = new(nonce, session, 0, JsonSerializer.SerializeToUtf8Bytes(WorksReminderPhaseDiagnostics.ExceptionShape(exception)),
                        "application/json", null, exception is OperationCanceledException ? "cancelled" : "caller-refused");
                }
                finally { cancellation.Cancel(); await watcher.ConfigureAwait(false); }
                File.Delete(file);
                await WorksProofCallerProtocol.WriteNewAsync(Path.Combine(directory, nonce + ".response.json"), response, stoppingToken).ConfigureAwait(false);
            }
            await Task.Delay(25, stoppingToken).ConfigureAwait(false);
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
