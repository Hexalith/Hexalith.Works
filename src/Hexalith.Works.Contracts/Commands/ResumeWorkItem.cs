using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Resumes suspended work (<c>Suspended</c> → <c>InProgress</c>) when the supplied await condition
/// matches one current suspension condition. Resume is a transition back to <c>InProgress</c> only.
/// </summary>
[PolymorphicSerialization]
public sealed partial record ResumeWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    AwaitCondition? AwaitCondition = null);
