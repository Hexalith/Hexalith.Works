using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Binds (or rebinds) an executor to a work item. Accepted from <c>Created</c>, <c>Assigned</c>
/// (rebind), and <c>Queued</c>; rejected elsewhere. Reassignment-while-active policy is Story 4.2.
/// </summary>
[PolymorphicSerialization]
public sealed partial record AssignWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ExecutorBinding Binding);
