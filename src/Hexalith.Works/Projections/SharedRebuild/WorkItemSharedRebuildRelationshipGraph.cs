namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Tracks relationship completeness independently of the pure roll-up calculation.</summary>
internal sealed class WorkItemSharedRebuildRelationshipGraph
{
    private readonly Dictionary<string, HashSet<string>> _children = new(StringComparer.Ordinal);
    private readonly HashSet<string> _incomplete = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _parents = new(StringComparer.Ordinal);

    /// <summary>Adds one same-tenant parent-to-child relationship.</summary>
    public void AddEdge(string parentId, string childId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childId);

        if (!_children.TryGetValue(parentId, out HashSet<string>? children))
        {
            children = new HashSet<string>(StringComparer.Ordinal);
            _children.Add(parentId, children);
        }

        _ = children.Add(childId);
        if (!_parents.TryGetValue(childId, out HashSet<string>? parents))
        {
            parents = new HashSet<string>(StringComparer.Ordinal);
            _parents.Add(childId, parents);
        }

        _ = parents.Add(parentId);
        if (parents.Count > 1)
        {
            _ = _incomplete.Add(childId);
            foreach (string parent in parents)
            {
                _ = _incomplete.Add(parent);
            }
        }
    }

    /// <summary>Marks an aggregate whose persisted evidence cannot prove a complete roll-up.</summary>
    public void MarkIncomplete(string aggregateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        _ = _incomplete.Add(aggregateId);
    }

    /// <summary>
    /// Returns whether the aggregate or any reachable descendant is incomplete, missing from the sealed
    /// authoritative membership, or cyclic.
    /// </summary>
    public bool IsRolledTotalUnavailable(string aggregateId, IReadOnlySet<string> authoritativeMembers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentNullException.ThrowIfNull(authoritativeMembers);
        return IsRolledTotalUnavailable(aggregateId, authoritativeMembers, new HashSet<string>(StringComparer.Ordinal));
    }

    private bool IsRolledTotalUnavailable(
        string aggregateId,
        IReadOnlySet<string> authoritativeMembers,
        HashSet<string> traversal)
    {
        if (!authoritativeMembers.Contains(aggregateId) || _incomplete.Contains(aggregateId))
        {
            return true;
        }

        if (!traversal.Add(aggregateId))
        {
            return true;
        }

        if (_children.TryGetValue(aggregateId, out HashSet<string>? children))
        {
            foreach (string childId in children)
            {
                if (IsRolledTotalUnavailable(childId, authoritativeMembers, traversal))
                {
                    _ = traversal.Remove(aggregateId);
                    return true;
                }
            }
        }

        _ = traversal.Remove(aggregateId);
        return false;
    }
}
