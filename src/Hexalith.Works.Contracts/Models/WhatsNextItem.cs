using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Models;

/// <summary>
/// The tenant "what's next" read model: one eligible (<see cref="WorkItemStatus.Queued"/> or
/// <see cref="WorkItemStatus.Assigned"/>) work item, exposing only the data Works owns so a future read
/// surface can order and render the claimable pool. It carries the work item identity, its current
/// <see cref="WorkItemStatus"/>, the ordering inputs (<see cref="Priority"/> + <see cref="DueDate"/>),
/// the uniform <see cref="ExecutorBinding"/> (PartyId + Channel + AuthorityLevel as data — there is
/// deliberately no executor-kind discriminator), the item's own <see cref="OwnRemaining"/> burn-down,
/// the eventual <see cref="RolledRemaining"/> subtree totals where a co-available roll-up supplies them
/// (own and rolled stay distinct types — AR-9), the await-condition data for a future "Waiting on…"
/// pill, and a <see cref="LatestAcceptedSourceSequence"/> freshness watermark.
/// <para>
/// This is a plain <see cref="System.Text.Json"/> record — not a polymorphic catalog type, not
/// stream-appended, not in the golden corpus (DC3). It exposes no UI-specific types (no colour, glyph,
/// label, or DataGrid type): presentation is resolved outside the Works kernel. The pure
/// <c>WhatsNextQueueProjection</c> in <c>Hexalith.Works.Projections</c> populates it.
/// </para>
/// </summary>
public sealed record WhatsNextItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemStatus Status,
    Priority? Priority,
    DateOnly? DueDate,
    ExecutorBinding? ExecutorBinding,
    OwnRemaining? OwnRemaining,
    RolledRemaining? RolledRemaining,
    IReadOnlyList<RolledRemaining> RolledRemainingByUnit,
    IReadOnlyList<AwaitCondition> AwaitConditions,
    long LatestAcceptedSourceSequence);
