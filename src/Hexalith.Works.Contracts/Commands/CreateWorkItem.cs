using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

[PolymorphicSerialization]
public sealed partial record CreateWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string? Obligation,
    WorkItemEffort? InitialEffort = null,
    WorkItemSchedule? Schedule = null,
    ParentWorkItemReference? Parent = null,
    ExecutorBinding? ExecutorBinding = null,
    ConversationCorrelationId? ConversationCorrelationId = null);
