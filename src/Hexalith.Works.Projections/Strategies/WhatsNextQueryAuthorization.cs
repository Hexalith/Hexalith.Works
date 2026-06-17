using Hexalith.Works.Contracts.Models;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// The pure query-side authorization / result filter for the what's-next queue. It is a <em>distinct
/// control</em> from the projection's own tenant key-scoping (defense-in-depth — D2/NFR-1): it re-applies
/// an authoritative tenant-id check on top of whatever the projection returned, and accepts an optional
/// caller-supplied authorization predicate — the seam a future <c>IDomainQueryHandler</c> fills from
/// <c>QueryEnvelope.UserId</c>. It mirrors <c>Hexalith.Projects</c>' <c>ProjectQueryTenantFilter</c>:
/// it is pure (no I/O, no router, no authority enforcement — <c>AuthorityLevel</c> stays
/// carried-not-enforced, D1/FR-19) and <em>fail-closed</em> (a null / empty authoritative tenant yields
/// an empty result). The authoritative tenant id is the normalized <c>TenantId.Value</c>.
/// </summary>
public static class WhatsNextQueryAuthorization
{
    /// <summary>
    /// Returns the single item only when it belongs to the authoritative tenant and passes the optional
    /// authorization predicate; otherwise <see langword="null"/> (fail-closed on a null / empty tenant).
    /// </summary>
    public static WhatsNextItem? Filter(
        string? authoritativeTenantId,
        WhatsNextItem? item,
        Func<WhatsNextItem, bool>? authorize = null)
    {
        if (string.IsNullOrWhiteSpace(authoritativeTenantId) || item is null)
        {
            return null;
        }

        return BelongsTo(authoritativeTenantId.Trim(), item) && (authorize is null || authorize(item))
            ? item
            : null;
    }

    /// <summary>
    /// Filters a what's-next result to the authoritative tenant and the optional authorization predicate,
    /// preserving order. A null / empty authoritative tenant returns an empty result (fail-closed).
    /// </summary>
    public static IReadOnlyList<WhatsNextItem> FilterList(
        string? authoritativeTenantId,
        IEnumerable<WhatsNextItem> items,
        Func<WhatsNextItem, bool>? authorize = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (string.IsNullOrWhiteSpace(authoritativeTenantId))
        {
            return [];
        }

        string tenant = authoritativeTenantId.Trim();
        return [.. items.Where(item => BelongsTo(tenant, item) && (authorize is null || authorize(item)))];
    }

    private static bool BelongsTo(string authoritativeTenantId, WhatsNextItem item)
        => string.Equals(item.TenantId.Value, authoritativeTenantId, StringComparison.Ordinal);
}
