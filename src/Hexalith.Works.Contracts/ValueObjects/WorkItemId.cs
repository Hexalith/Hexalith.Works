using Hexalith.EventStore.Contracts.Identity;

namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record WorkItemId
{
    private const string SampleTenantId = "tenant";
    private const string Domain = "work";

    public WorkItemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = new AggregateIdentity(SampleTenantId, Domain, value).AggregateId;
    }

    public string Value { get; }
}
