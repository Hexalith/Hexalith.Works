namespace Hexalith.Works.Queries;

/// <summary>Gateway and trusted-workload policy for SDK Works queries.</summary>
public sealed class WorksQueryOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Works:Query";

    /// <summary>The authenticated EventStore gateway workload allowed to reach SDK query ingress.</summary>
    public string GatewayWorkloadId { get; init; } = "eventstore";

    /// <summary>Exact trusted workloads allowed to request all tenant work.</summary>
    public IReadOnlyList<string> PermittedCoordinatorWorkloads { get; init; } = [];

    /// <summary>The canonical authenticated gateway URI used by the Parties identity client.</summary>
    public string? PartiesGatewayBaseAddress { get; init; }

    /// <summary>The independently provisioned service bearer for authoritative Parties gateway queries.</summary>
    public string? PartiesGatewayBearerToken { get; init; }
}
