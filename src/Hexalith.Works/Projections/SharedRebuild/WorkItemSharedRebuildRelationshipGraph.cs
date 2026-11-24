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

    /// <summary>Returns a deterministic snapshot of the direct-child graph.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ChildrenByParent()
        => _children.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<string>)[.. pair.Value.Order(StringComparer.Ordinal)],
                StringComparer.Ordinal);

    /// <summary>Finds authoritative members whose roll-ups cannot be proven complete.</summary>
    /// <param name="authoritativeMembers">The sealed set of aggregate identities.</param>
    /// <param name="cancellationToken">The cancellation token for the fold.</param>
    /// <returns>Members with incomplete, missing or cyclic descendant evidence.</returns>
    public IReadOnlySet<string> FindUnavailableRolledTotals(
        IReadOnlySet<string> authoritativeMembers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authoritativeMembers);

        var unavailable = new HashSet<string>(authoritativeMembers, StringComparer.Ordinal);
        var remainingChildren = new Dictionary<string, int>(StringComparer.Ordinal);
        var complete = new Queue<string>();
        foreach (string member in authoritativeMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_incomplete.Contains(member))
            {
                continue;
            }

            if (_children.TryGetValue(member, out HashSet<string>? children))
            {
                remainingChildren.Add(member, children.Count);
            }
            else
            {
                complete.Enqueue(member);
            }
        }

        // A member becomes complete only after every child is proven complete. Missing children,
        // incomplete members and cycles never reach the queue, so their ancestors remain unavailable.
        while (complete.TryDequeue(out string? child))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = unavailable.Remove(child);
            if (!_parents.TryGetValue(child, out HashSet<string>? parents))
            {
                continue;
            }

            foreach (string parent in parents)
            {
                if (remainingChildren.TryGetValue(parent, out int remaining))
                {
                    remainingChildren[parent] = remaining - 1;
                    if (remaining == 1)
                    {
                        complete.Enqueue(parent);
                    }
                }
            }
        }

        return unavailable;
    }
}
