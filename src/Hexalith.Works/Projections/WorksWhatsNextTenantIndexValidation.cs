namespace Hexalith.Works.Projections;

/// <summary>Validates the closed current-generation Works tenant manifest shape.</summary>
internal static class WorksWhatsNextTenantIndexValidation
{
    /// <summary>Returns whether an index is a complete supported current-generation manifest.</summary>
    public static bool IsValidCurrent(WorksWhatsNextTenantIndex? index)
        => index is
        {
            SchemaVersion: WorksReadModelKeys.CurrentSchemaVersion,
            Items: not null,
            LastSequences: not null,
            MemberWorkItemIds: not null,
        };

    /// <summary>Returns whether a legacy index has the collections needed for safe use.</summary>
    public static bool IsUsableLegacy(WorksWhatsNextTenantIndex? index)
        => index is
        {
            Items: not null,
            LastSequences: not null,
            MemberWorkItemIds: not null,
        };
}
