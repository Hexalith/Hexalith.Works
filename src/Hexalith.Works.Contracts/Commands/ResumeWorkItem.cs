using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Resumes suspended work (<c>Suspended</c> → <c>InProgress</c>). Resume is a transition back to
/// <c>InProgress</c> only — there is no resting <c>Resumed</c> status. Correlation-key matching is
/// out of scope here (Story 3.5).
/// </summary>
public sealed record ResumeWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId);
