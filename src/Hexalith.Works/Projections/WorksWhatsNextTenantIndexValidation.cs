namespace Hexalith.Works.Projections;

/// <summary>Validates the closed current-generation Works tenant manifest shape.</summary>
internal static class WorksWhatsNextTenantIndexValidation
{
    /// <summary>Returns whether an index is a complete supported current-generation manifest.</summary>
    public static bool IsValidCurrent(WorksWhatsNextTenantIndex? index)
    {
        if (index is not
        {
            SchemaVersion: WorksReadModelKeys.CurrentSchemaVersion,
            Items: not null,
            LastSequences: not null,
            MemberWorkItemIds: not null,
        })
            return false;

        var members = new HashSet<string>(StringComparer.Ordinal);
        return index.MemberWorkItemIds.All(id => !string.IsNullOrWhiteSpace(id) && members.Add(id))
            && index.Items.Keys.All(members.Contains)
            && index.LastSequences.Keys.All(members.Contains);
    }
}
