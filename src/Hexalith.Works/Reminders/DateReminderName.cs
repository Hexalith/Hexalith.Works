using System.Security.Cryptography;
using System.Text;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Pure, deterministic naming for date-based Dapr actor reminders (Story 4.6, AC #1/#2). The reminder
/// <see cref="For(string, string, string)"/> name and the host <see cref="ActorId(string, string)"/> are a
/// stable function of bounded identifiers only — the tenant id, the work item id, and the await condition's
/// deterministic <c>CorrelationKey</c> (for a <c>DateReached</c> await this is the round-trip UTC instant
/// string). The output never embeds a wall-clock "now", a random id, an attempt counter, or any payload /
/// obligation text, so a duplicate registration of the same condition always resolves to the same reminder
/// and cannot create a second accepted resume.
/// </summary>
/// <remarks>
/// The composite is hashed to a fixed, URL-safe token (a lowercase SHA-256 hex prefix) so the instant's
/// <c>:</c>/<c>+</c>/<c>.</c> characters never leak into a reminder name or actor id that the Dapr runtime
/// places in scheduler keys and HTTP paths — matching the simple-name convention sibling actors use. A
/// record-separator delimiter keeps the three fields unambiguous, so two different splits of the same
/// concatenation cannot collide.
/// </remarks>
public static class DateReminderName
{
    /// <summary>The shared prefix marking every Works date-resume reminder/actor token.</summary>
    public const string Prefix = "work-date-resume";

    private const char FieldSeparator = '\u001f';
    private const int TokenHexLength = 32;

    /// <summary>
    /// Builds the deterministic reminder name from the full <c>(tenantId, workItemId, correlationKey)</c>
    /// identity of a date await. The same inputs always produce the same name; a different correlation key
    /// (a different instant) always produces a different name.
    /// </summary>
    public static string For(string tenantId, string workItemId, string correlationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationKey);

        return $"{Prefix}-{StableToken(tenantId, workItemId, correlationKey)}";
    }

    /// <summary>
    /// Builds the deterministic actor id that co-locates every date-resume reminder for a single work item,
    /// derived from the <c>(tenantId, workItemId)</c> identity only.
    /// </summary>
    public static string ActorId(string tenantId, string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        return $"{Prefix}-{StableToken(tenantId, workItemId)}";
    }

    private static string StableToken(params string[] fields)
    {
        string composite = string.Join(FieldSeparator, fields);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(composite));
        return Convert.ToHexStringLower(hash)[..TokenHexLength];
    }
}
