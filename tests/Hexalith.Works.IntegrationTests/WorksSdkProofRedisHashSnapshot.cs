using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Atomic Redis hash contents and absolute field deadlines at one server observation time.</summary>
/// <param name="Type">The observed Redis key type, including none after the last field expires.</param>
/// <param name="ObservedAt">The Redis server time in Unix milliseconds.</param>
/// <param name="Fields">Exact field names, values and absolute deadlines; minus one denotes persistence.</param>
internal sealed record WorksSdkProofRedisHashSnapshot(string Type, long ObservedAt, (HashEntry Entry, long ExpiresAt)[] Fields)
{
    /// <summary>Projects captured fields to Redis 7.4's strict expiry boundary at the supplied server time.</summary>
    /// <param name="observedAt">The Redis server observation in Unix milliseconds.</param>
    /// <returns>Fields still visible at that observation, including fields exactly at their deadline.</returns>
    internal (HashEntry Entry, long ExpiresAt)[] LiveFieldsAt(long observedAt)
        => [.. Fields.Where(field => field.ExpiresAt < 0 || field.ExpiresAt >= observedAt)];
}
