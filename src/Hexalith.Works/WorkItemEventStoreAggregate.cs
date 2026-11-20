using Hexalith.EventStore.Client.Aggregates;
using Hexalith.EventStore.Client.Attributes;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Runtime;
using Hexalith.Works.Recovery;

using KernelAggregate = Hexalith.Works.Server.Aggregates.WorkItemAggregate;

namespace Hexalith.Works;

/// <summary>
/// Adapter-edge aggregate that makes the pure static <see cref="KernelAggregate"/> discoverable by the
/// EventStore domain-service runtime. EventStore's assembly scanner only discovers concrete
/// <see cref="EventStoreAggregate{TState}"/> subclasses, while the Works kernel keeps command handling in a
/// pure static class that must not inherit EventStore runtime types (that would violate the
/// <c>Server -&gt; Contracts</c> dependency direction). Each <c>Handle</c> wrapper delegates to the corresponding
/// pure <see cref="KernelAggregate"/> handler. Every wrapper verifies the payload against the addressed work stream before delegation.
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
    public static DomainResult Handle(CreateWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="SpawnChild"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(SpawnChild command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="AssignWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(AssignWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="QueueWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(QueueWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ClaimWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ClaimWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="SuspendWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(SuspendWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ResumeWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ResumeWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="CompleteWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(CompleteWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ReportProgress"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ReportProgress command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ReEstimate"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ReEstimate command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="RescheduleWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(RescheduleWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Validates the addressed stream and delegates <see cref="LinkConversation"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(LinkConversation command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="CancelWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(CancelWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="RejectWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(RejectWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        return KernelAggregate.Handle(command, state);
    }

    /// <summary>Delegates <see cref="ExpireWorkItem"/> to the pure kernel handler.</summary>
    public static DomainResult Handle(ExpireWorkItem command, WorkItemState? state, CommandEnvelope envelope)
    {
        ValidateCommand(command, static value => (value.TenantId, value.WorkItemId), state, envelope);
        TrustedEffectOrigin? origin = TrustedEffectOriginScope.Current;
        if (origin is not null)
        {
            if (origin.Workload != WorksEffectPurposes.Workload
                || origin.Purpose != WorksEffectPurposes.ForKind(origin.Identity.EffectKind))
                throw new UnauthorizedAccessException("Works expiry delegated origin is denied.");
            if (origin.Identity.EffectKind == EffectKindCatalog.CascadeExpire)
                return KernelAggregate.HandleDelegatedCascadeExpiry(command, state);
            if (origin.Identity.EffectKind != EffectKindCatalog.Expiry || command.ExpiryWitness is null)
                throw new UnauthorizedAccessException("Works scheduled expiry requires its authenticated witness.");
        }
        else if (state?.Expiry is not null || command.ExpiryWitness is not null)
        {
            throw new UnauthorizedAccessException("Works witnessed expiry requires authenticated effect origin.");
        }
        return KernelAggregate.Handle(command, state);
    }

    private static void ValidateCommand<TCommand>(
        TCommand command,
        Func<TCommand, (TenantId? Tenant, WorkItemId? Item)> addressOf,
        WorkItemState? state,
        CommandEnvelope envelope)
        where TCommand : class
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(envelope);
        (TenantId? tenant, WorkItemId? item) = addressOf(command);
        WorksReadModelKeys.ThrowIfReservedTenantId(tenant?.Value);
        WorksReadModelKeys.ThrowIfReservedTenantId(envelope.TenantId);
        if (tenant is null || item is null || envelope.Domain != "work"
            || !string.Equals(tenant.Value, envelope.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(item.Value, envelope.AggregateId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{typeof(TCommand).Name} payload identity does not match its command envelope.");
        }
        if (state is { Status: not WorkItemStatus.Unknown } && (state.TenantId != tenant || state.WorkItemId != item))
        {
            throw new InvalidOperationException($"{typeof(TCommand).Name} payload identity does not match the established work-item state.");
        }

        if (TrustedEffectOriginScope.Current is { } origin)
        {
            bool allowed = (command, origin.Identity.EffectKind) switch
            {
                (ResumeWorkItem, EffectKindCatalog.DateResume or EffectKindCatalog.ChildCompletionResume) => true,
                (CancelWorkItem, EffectKindCatalog.CascadeCancel) => true,
                (ExpireWorkItem, EffectKindCatalog.Expiry or EffectKindCatalog.CascadeExpire) => true,
                _ => false,
            };
            if (!allowed || origin.Workload != WorksEffectPurposes.Workload
                || origin.Purpose != WorksEffectPurposes.ForKind(origin.Identity.EffectKind))
                throw new UnauthorizedAccessException("Works effect command or delegated origin is denied.");
        }
    }
}
