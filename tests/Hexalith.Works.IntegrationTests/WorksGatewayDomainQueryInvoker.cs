using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Queries;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Connects the real gateway router to the real guarded domain HTTP receiver in isolated security tests.</summary>
internal sealed class WorksGatewayDomainQueryInvoker(Func<QueryEnvelope, CancellationToken, Task<QueryResult>> invoke) : IDomainQueryInvoker
{
    public Task<QueryResult> InvokeAsync(QueryEnvelope query, CancellationToken cancellationToken = default)
        => invoke(query, cancellationToken);
}
