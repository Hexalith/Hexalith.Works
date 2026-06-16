using Hexalith.EventStore.Contracts.Identity;

namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record PartyId
{
    private const string SampleTenantId = "tenant";
    private const string Domain = "party";

    public PartyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = new AggregateIdentity(SampleTenantId, Domain, value).AggregateId;
    }

    public string Value { get; }
}
