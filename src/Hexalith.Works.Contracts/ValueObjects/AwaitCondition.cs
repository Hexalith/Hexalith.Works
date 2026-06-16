using System.Globalization;
using System.Text.Json.Serialization;

namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record AwaitCondition
{
    public AwaitCondition(WorkItemId childWorkItemId)
        : this(AwaitConditionKind.ChildCompleted, null, childWorkItemId, null, null)
    {
    }

    [JsonConstructor]
    public AwaitCondition(
        AwaitConditionKind kind,
        string? correlationKey = null,
        WorkItemId? childWorkItemId = null,
        DateTimeOffset? instant = null,
        string? externalCorrelationId = null)
    {
        (AwaitConditionKind resolvedKind, string resolvedKey, WorkItemId? resolvedChild, DateTimeOffset? resolvedInstant, string? resolvedExternal) =
            Normalize(kind, correlationKey, childWorkItemId, instant, externalCorrelationId);

        Kind = resolvedKind;
        CorrelationKey = resolvedKey;
        ChildWorkItemId = resolvedChild;
        Instant = resolvedInstant;
        ExternalCorrelationId = resolvedExternal;
    }

    public AwaitConditionKind Kind { get; }

    public string CorrelationKey { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkItemId? ChildWorkItemId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? Instant { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalCorrelationId { get; }

    public static AwaitCondition ChildCompleted(WorkItemId childWorkItemId) => new(childWorkItemId);

    public static AwaitCondition DateReached(DateTimeOffset instant)
        => new(AwaitConditionKind.DateReached, instant: instant);

    public static AwaitCondition ExternalSignal(string correlationId)
        => new(AwaitConditionKind.ExternalSignal, externalCorrelationId: correlationId);

    private static (AwaitConditionKind Kind, string CorrelationKey, WorkItemId? ChildWorkItemId, DateTimeOffset? Instant, string? ExternalCorrelationId) Normalize(
        AwaitConditionKind kind,
        string? correlationKey,
        WorkItemId? childWorkItemId,
        DateTimeOffset? instant,
        string? externalCorrelationId)
    {
        if (kind == 0 && childWorkItemId is not null)
        {
            kind = AwaitConditionKind.ChildCompleted;
        }

        return kind switch
        {
            AwaitConditionKind.ChildCompleted => NormalizeChild(correlationKey, childWorkItemId),
            AwaitConditionKind.DateReached => NormalizeDate(correlationKey, instant),
            AwaitConditionKind.ExternalSignal => NormalizeExternal(correlationKey, externalCorrelationId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported await condition kind."),
        };
    }

    private static (AwaitConditionKind, string, WorkItemId?, DateTimeOffset?, string?) NormalizeChild(
        string? correlationKey,
        WorkItemId? childWorkItemId)
    {
        ArgumentNullException.ThrowIfNull(childWorkItemId);
        string key = RequireKey(correlationKey ?? childWorkItemId.Value, nameof(correlationKey));
        if (!StringComparer.Ordinal.Equals(key, childWorkItemId.Value))
        {
            throw new ArgumentException("Child-completion correlation key must match the child work item id.", nameof(correlationKey));
        }

        return (AwaitConditionKind.ChildCompleted, key, childWorkItemId, null, null);
    }

    private static (AwaitConditionKind, string, WorkItemId?, DateTimeOffset?, string?) NormalizeDate(
        string? correlationKey,
        DateTimeOffset? instant)
    {
        if (instant is null)
        {
            throw new ArgumentNullException(nameof(instant));
        }

        DateTimeOffset utcInstant = instant.Value.ToUniversalTime();
        string expectedKey = utcInstant.ToString("O", CultureInfo.InvariantCulture);
        string key = RequireKey(correlationKey ?? expectedKey, nameof(correlationKey));
        if (!StringComparer.Ordinal.Equals(key, expectedKey))
        {
            throw new ArgumentException("Date-reached correlation key must match the instant.", nameof(correlationKey));
        }

        return (AwaitConditionKind.DateReached, key, null, utcInstant, null);
    }

    private static (AwaitConditionKind, string, WorkItemId?, DateTimeOffset?, string?) NormalizeExternal(
        string? correlationKey,
        string? externalCorrelationId)
    {
        string external = RequireKey(externalCorrelationId ?? correlationKey, nameof(externalCorrelationId));
        string key = RequireKey(correlationKey ?? external, nameof(correlationKey));
        if (!StringComparer.Ordinal.Equals(key, external))
        {
            throw new ArgumentException("External-signal correlation key must match the external correlation id.", nameof(correlationKey));
        }

        return (AwaitConditionKind.ExternalSignal, key, null, null, external);
    }

    private static string RequireKey(string? value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }
}
