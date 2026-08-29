using Hexalith.Works.Contracts.Models;

namespace Hexalith.Works.Projections;

/// <summary>Applies the shared fail-closed policy to a Works roll-up at a persisted/query boundary.</summary>
internal static class WorkItemProjectionBoundarySanitizer
{
    /// <summary>Clears both rolled shapes when the caller cannot prove a complete subtree.</summary>
    public static WorkItemRollUp? Sanitize(WorkItemRollUp? model, bool rolledTotalsUnavailable)
        => model is not null && rolledTotalsUnavailable
            ? model with
            {
                RolledRemaining = null,
                RolledRemainingByUnit = [],
            }
            : model;
}
