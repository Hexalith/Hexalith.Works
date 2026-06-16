using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Suspends in-flight work (<c>InProgress</c> → <c>Suspended</c>). Direct suspend commands still
/// carry no await payload; Story 3.2 adds only the child-completion await condition emitted by spawn.
/// </summary>
[PolymorphicSerialization]
public sealed partial record SuspendWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId);
