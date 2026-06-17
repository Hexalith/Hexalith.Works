using System.Text.Json;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Pure construction of the descendant terminal command submissions a cascade dispatches (Story 4.6, AC
/// #4/#6). The submission is rebuilt deterministically from bounded primitives — tenant, descendant id, kind,
/// and the parent terminal identity — so checkpoint replay can reconstruct the exact same command (and the
/// same dedup correlation id) without storing a payload, and a redelivery stays a no-op at the aggregate.
/// </summary>
public static class CascadeCommands
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    /// <summary>The deterministic correlation/causation id for one cascade target command.</summary>
    public static string CorrelationId(string tenantId, string parentWorkItemId, long parentSequence, string descendantWorkItemId, string kind)
        => $"cascade-{kind}-{tenantId}-{parentWorkItemId}-{parentSequence}-{descendantWorkItemId}";

    /// <summary>Builds the deterministic submission for one cascade target descendant.</summary>
    public static WorkCommandSubmission BuildSubmission(string tenantId, string descendantWorkItemId, string kind, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descendantWorkItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var tenant = new TenantId(tenantId);
        var descendant = new WorkItemId(descendantWorkItemId);

        (string CommandType, object Command) intent = kind switch
        {
            CascadeCheckpoint.CancelKind => (nameof(CancelWorkItem), new CancelWorkItem(tenant, descendant)),
            CascadeCheckpoint.ExpireKind => (nameof(ExpireWorkItem), new ExpireWorkItem(tenant, descendant)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported cascade kind."),
        };

        return new WorkCommandSubmission(
            Tenant: tenant.Value,
            AggregateId: descendant.Value,
            CommandType: intent.CommandType,
            Payload: JsonSerializer.SerializeToElement(intent.Command, s_web),
            CorrelationId: correlationId,
            CausationId: correlationId);
    }
}
