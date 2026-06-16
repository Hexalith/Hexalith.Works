using Hexalith.EventStore.Contracts.Identity;

namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record TenantId
{
    private const string Domain = "work";
    private const string SampleAggregateId = "sample";

    public TenantId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = new AggregateIdentity(value, Domain, SampleAggregateId).TenantId;
    }

    public string Value { get; }
}
