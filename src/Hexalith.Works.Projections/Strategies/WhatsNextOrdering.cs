using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// The total, deterministic, order-tolerant ordering for the tenant what's-next queue (DC2). It is a
/// pure function of each item's identity and ordering inputs, so it is stable across rebuilds and immune
/// to out-of-order or duplicate delivery (B2/NFR-4). The relation, in order:
/// <list type="number">
/// <item>Priority rank — present <c>Critical(0) &lt; High(1) &lt; Normal(2) &lt; Low(3)</c>; absent or
/// <see cref="Priority.Unknown"/> ranks last (4).</item>
/// <item>Due Date — earliest first; absent sorts after every present due date (max sentinel).</item>
/// <item>Identity tiebreak — <c>WorkItemId.Value</c> ordinal. A pure function of identity (independent of
/// arrival order), chosen over first-seen order so the comparator is a strict total order — no two
/// distinct items (which have distinct ids within a tenant) ever compare equal.</item>
/// </list>
/// An item with <em>neither</em> Priority nor Due Date lands at the bottom by construction (rank 4 and
/// the due-date max sentinel) — FR-4 "neither sorts last".
/// </summary>
public sealed class WhatsNextOrdering : IComparer<WhatsNextItem>
{
    /// <summary>The rank assigned to an absent or <see cref="Priority.Unknown"/> priority — sorts last.</summary>
    public const int AbsentPriorityRank = 4;

    private WhatsNextOrdering()
    {
    }

    /// <summary>Gets the shared comparer instance.</summary>
    public static WhatsNextOrdering Instance { get; } = new();

    /// <summary>
    /// Maps a priority to its ordering rank: present priorities rank
    /// <c>Critical(0) &lt; High(1) &lt; Normal(2) &lt; Low(3)</c>; absent or
    /// <see cref="Priority.Unknown"/> ranks last (<see cref="AbsentPriorityRank"/>).
    /// </summary>
    public static int PriorityRank(Priority? priority)
        => priority is null or Priority.Unknown ? AbsentPriorityRank : (int)priority.Value - 1;

    /// <summary>Maps a due date to its ordering key; an absent due date becomes the max sentinel.</summary>
    public static DateOnly DueDateKey(DateOnly? dueDate) => dueDate ?? DateOnly.MaxValue;

    /// <inheritdoc/>
    public int Compare(WhatsNextItem? x, WhatsNextItem? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        int byPriority = PriorityRank(x.Priority).CompareTo(PriorityRank(y.Priority));
        if (byPriority != 0)
        {
            return byPriority;
        }

        int byDueDate = DueDateKey(x.DueDate).CompareTo(DueDateKey(y.DueDate));
        return byDueDate != 0
            ? byDueDate
            : string.Compare(x.WorkItemId.Value, y.WorkItemId.Value, StringComparison.Ordinal);
    }
}
