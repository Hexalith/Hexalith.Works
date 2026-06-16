using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Server.Aggregates;

public static class WorkItemAggregate
{
    public static DomainResult Handle(CreateWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        if (string.IsNullOrWhiteSpace(command.Obligation))
        {
            return DomainResult.Rejection([
                new WorkItemCannotBeCreatedWithoutObligation(command.TenantId, command.WorkItemId),
            ]);
        }

        var created = new WorkItemCreated(
            command.WorkItemId.Value,
            state is null ? 1 : 2,
            command.TenantId,
            command.WorkItemId,
            new Obligation(command.Obligation),
            NormalizeInitialEffort(command.InitialEffort),
            command.Schedule,
            command.Parent,
            command.ExecutorBinding,
            command.ConversationCorrelationId);

        return DomainResult.Success([created]);
    }

    private static WorkItemEffort? NormalizeInitialEffort(WorkItemEffort? effort)
        => effort is null || effort.Done == 0
            ? effort
            : new WorkItemEffort(effort.Estimated, effort.Unit);
}
