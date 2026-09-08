using Hexalith.EventStore.Client.Aggregates;
using Hexalith.EventStore.Client.Attributes;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Runtime;

using KernelAggregate = Hexalith.Works.Server.Aggregates.WorkItemAggregate;

namespace Hexalith.Works;

/// <summary>
/// Adapter-edge aggregate that makes the pure static <see cref="KernelAggregate"/> discoverable by the
/// EventStore domain-service runtime. EventStore's assembly scanner only discovers concrete
/// <see cref="EventStoreAggregate{TState}"/> subclasses, while the Works kernel keeps command handling in a
/// pure static class that must not inherit EventStore runtime types (that would violate the
/// <c>Server -&gt; Contracts</c> dependency direction). Each <c>Handle</c> wrapper delegates to the corresponding
/// pure <see cref="KernelAggregate"/> handler; envelope-aware wrappers also enforce adapter-boundary identity.
/// </summary>
/// <remarks>
/// Decorated with <c>[EventStoreDomain("work")]</c> because the naming convention would otherwise derive
/// <c>work-item-event-store</c> from the type name, whereas the canonical domain (matching
/// <c>WorkItemId</c>/<c>WorkItemState</c> identity and the AppHost registration) is <c>work</c>.
/// </remarks>
[EventStoreDomain("work")]
public sealed class WorkItemEventStoreAggregate : EventStoreAggregate<WorkItemState>
{
    /// <summary>Delegates <see cref="CreateWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(CreateWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="SpawnChild"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(SpawnChild command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="AssignWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(AssignWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="QueueWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(QueueWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ClaimWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ClaimWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="SuspendWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(SuspendWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ResumeWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ResumeWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="CompleteWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(CompleteWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ReportProgress"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ReportProgress command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ReEstimate"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ReEstimate command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="RescheduleWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(RescheduleWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Validates the addressed stream and delegates <see cref="LinkConversation"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(LinkConversation command, WorkItemState? state, CommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(envelope);
        RefuseReservedTenant(command, static value => value.TenantId);
        WorksReadModelKeys.ThrowIfReservedTenantId(envelope.TenantId);
        if (!string.Equals(envelope.Domain, WorkCommandSubmission.WorkDomain, StringComparison.Ordinal)
            || !string.Equals(envelope.TenantId, command.TenantId?.Value, StringComparison.Ordinal)
            || !string.Equals(envelope.AggregateId, command.WorkItemId?.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LinkConversation payload identity does not match its command envelope.");
        }

        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="CancelWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(CancelWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="RejectWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(RejectWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ExpireWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ExpireWorkItem command, WorkItemState? state)
    {
        RefuseReservedTenant(command, static value => value.TenantId);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>
    /// Refuses reserved tenant <c>tenants</c> at the <c>/process</c> adapter edge so that id cannot persist
    /// a stream the projection poller can never park or index.
    /// </summary>
    private static void RefuseReservedTenant<TCommand>(TCommand command, Func<TCommand, TenantId?> tenantId)
        where TCommand : class
    {
        ArgumentNullException.ThrowIfNull(command);
        WorksReadModelKeys.ThrowIfReservedTenantId(tenantId(command)?.Value);
    }
}
