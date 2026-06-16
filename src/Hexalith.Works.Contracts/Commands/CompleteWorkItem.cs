using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Explicitly completes a work item (<c>InProgress</c> | <c>Suspended</c> → <c>Completed</c>).
/// Remaining=0 auto-completion is out of scope here (Story 2.3).
/// </summary>
[PolymorphicSerialization]
public sealed partial record CompleteWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId);
