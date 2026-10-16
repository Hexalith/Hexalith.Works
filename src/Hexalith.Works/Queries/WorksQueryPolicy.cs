using System.Text.Json;

using Hexalith.EventStore.Contracts.Queries;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Parties.Contracts.Queries;
using Hexalith.Parties.Contracts.ValueObjects;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Queries;

/// <summary>Verifies current Executor binding or an explicitly permitted coordinator workload.</summary>
public sealed class WorksQueryPolicy(
    IPartiesIdentityClient parties,
    IOptions<WorksQueryOptions> options,
    ILogger<WorksQueryPolicy>? logger = null)
{
    // Leave time for a fail-closed response within the canonical five-second Dapr invocation budget.
    private static readonly TimeSpan s_identityResolutionTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<WorksQueryPolicy> _logger = logger ?? NullLogger<WorksQueryPolicy>.Instance;
    private readonly IPartiesIdentityClient _parties = parties ?? throw new ArgumentNullException(nameof(parties));
    private readonly WorksQueryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    /// <summary>Returns a scoped authorization decision before any Works state is read.</summary>
    internal async Task<WorksQueryAuthority> AuthorizeAsync(QueryEnvelope query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.TenantId)
            || string.IsNullOrWhiteSpace(query.AuthenticatedWorkloadId))
        {
            return new WorksQueryAuthority(false, false, null);
        }

        if (!query.IsDelegated && string.IsNullOrWhiteSpace(query.OriginalActorId)
            && _options.PermittedCoordinatorWorkloads.Contains(query.AuthenticatedWorkloadId, StringComparer.Ordinal))
        {
            return new WorksQueryAuthority(true, true, null);
        }

        if (!query.IsDelegated
            || !string.Equals(query.AuthenticatedWorkloadId, _options.GatewayWorkloadId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(query.OriginalActorId)
            || !string.Equals(query.OriginalActorId, query.UserId, StringComparison.Ordinal)
            || !TryReadPartyId(query.Payload, out string? requestedPartyId))
        {
            return new WorksQueryAuthority(false, false, null);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(s_identityResolutionTimeout);
        try
        {
            var identity = await _parties.ResolvePartyIdentityAsync(
                query.TenantId,
                new ResolvePartyIdentity(query.TenantId, requestedPartyId!, query.OriginalActorId),
                deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            return identity.Outcome == PartyIdentityOutcome.Resolved
                && identity.Evidence is { Classification: PartyIdentityClassification.Human, IsActive: true,
                    IsRestricted: false, IsErasingOrErased: false, HumanBinding: { } binding }
                && string.Equals(identity.Evidence.TenantId, query.TenantId, StringComparison.Ordinal)
                && string.Equals(identity.Evidence.PartyId, requestedPartyId, StringComparison.Ordinal)
                && string.Equals(binding.ActorId, query.OriginalActorId, StringComparison.Ordinal)
                ? new WorksQueryAuthority(true, false, requestedPartyId)
                : new WorksQueryAuthority(false, false, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Parties identity resolution failed for tenant {TenantId}; denying the query.", query.TenantId);
            return new WorksQueryAuthority(false, false, null);
        }
    }

    private static bool TryReadPartyId(byte[] payload, out string? partyId)
    {
        partyId = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("partyId", out JsonElement value)
                || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            partyId = value.GetString();
            return !string.IsNullOrWhiteSpace(partyId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
