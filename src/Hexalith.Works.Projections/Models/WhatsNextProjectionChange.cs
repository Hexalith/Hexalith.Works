using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Projections.Models;

/// <summary>
/// The pure change-detection signal returned by <c>WhatsNextQueueProjection.Project</c>: did this
/// delivery change the tenant's what's-next <em>eligibility set</em> or <em>ordering</em>? The deferred
/// runtime adapter (Stories 4.5/4.6) calls
/// <c>IProjectionChangeNotifier.NotifyProjectionChangedAsync(projectionType, tenantId, …)</c> only when
/// <see cref="Changed"/> is <see langword="true"/>, so SignalR surfaces refresh on real change and not on
/// binding/remaining-only updates. The pure kernel ships this decision plus the stable
/// <c>projectionType</c> token; it never references the notifier (dependency direction). [AC #4 / DC1]
/// </summary>
public readonly record struct WhatsNextProjectionChange(bool Changed, TenantId TenantId);
