using System.Text.Json;

using Hexalith.EventStore.Client.Projections;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Runtime;

/// <summary>Reports bounded failure context without exception text, credentials or domain payloads.</summary>
internal static partial class WorksFailureDiagnostics
{
    /// <summary>Records an authorized projection query failure without changing its unavailable response.</summary>
    internal static void QueryFailed(ILogger logger, string queryType, string tenantId, string correlationId, Exception exception)
        => QueryUnavailable(logger, queryType, tenantId, correlationId, Category(exception));

    /// <summary>Records a known validation failure without changing the protected query result.</summary>
    internal static void QueryUnavailable(ILogger logger, string queryType, string tenantId, string correlationId, string category)
    {
        try { ProjectionQueryFailed(logger, queryType, Bound(tenantId), Bound(correlationId), category); }
        catch (Exception) { /* Diagnostics must not replace the fail-closed result. */ }
    }

    /// <summary>Records one incomplete tenant discovery result with a fixed failure category.</summary>
    internal static void DiscoveryIncomplete(ILogger logger, string tenantId, string category)
    {
        try { TenantDiscoveryIncomplete(logger, Bound(tenantId), category); }
        catch (Exception) { /* Diagnostics must not suppress healthy-tenant progress or retry. */ }
    }

    /// <summary>Maps an exception to a finite diagnostic category without retaining private error details.</summary>
    internal static string Category(Exception exception) => exception switch
    {
        OperationCanceledException or TimeoutException
            or SharedProjectionReadUnavailableException { InnerException: OperationCanceledException or TimeoutException } => "timeout",
        SharedProjectionReadUnavailableException or Dapr.DaprException or HttpRequestException or IOException => "dependency-unavailable",
        JsonException => "invalid-document",
        ArgumentException or InvalidOperationException => "invalid-state",
        _ => "unexpected-failure",
    };

    private static string Bound(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return string.Create(Math.Min(value.Length, 128), value, static (destination, source) =>
        {
            for (int index = 0; index < destination.Length; index++)
                destination[index] = char.IsControl(source[index]) || source[index] is '\u2028' or '\u2029'
                    ? '?' : source[index];
        });
    }

    [LoggerMessage(EventId = 4510, Level = LogLevel.Warning,
        Message = "Works projection query {QueryType} unavailable (tenant {TenantId}, correlation {CorrelationId}, category {FailureCategory}).")]
    private static partial void ProjectionQueryFailed(ILogger logger, string queryType, string tenantId, string correlationId, string failureCategory);

    [LoggerMessage(EventId = 4511, Level = LogLevel.Warning,
        Message = "Works tenant discovery incomplete (tenant {TenantId}, category {FailureCategory}); retry retained.")]
    private static partial void TenantDiscoveryIncomplete(ILogger logger, string tenantId, string failureCategory);
}
