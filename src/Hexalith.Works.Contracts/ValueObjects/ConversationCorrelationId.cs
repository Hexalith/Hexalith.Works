namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record ConversationCorrelationId
{
    public ConversationCorrelationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
