using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Suspends in-flight work (<c>InProgress</c> → <c>Suspended</c>). Await-condition payloads and
/// correlation-key matching are out of scope here (Story 3.5): v1 carries no await payload.
/// </summary>
[PolymorphicSerialization]
public sealed partial record SuspendWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId);
