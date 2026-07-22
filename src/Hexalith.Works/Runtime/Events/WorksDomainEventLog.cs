using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Runtime.Events;

/// <summary>
/// Source-generated, metadata-only logging for the Works domain-event subscription edge.
/// </summary>
internal static partial class WorksDomainEventLog
{
    /// <summary>Logs an envelope rejected before marker acquisition.</summary>
    [LoggerMessage(
        EventId = 4800,
        Level = LogLevel.Warning,
        Message = "Works domain event was skipped before dispatch. ReasonCode={ReasonCode}.")]
    internal static partial void InvalidEnvelope(ILogger logger, string reasonCode);

    /// <summary>Logs a terminally skipped delivery without including its payload.</summary>
    [LoggerMessage(
        EventId = 4801,
        Level = LogLevel.Warning,
        Message = "Works domain event {EventTypeName} for tenant {TenantId}, work item {WorkItemId}, correlation {CorrelationId} was skipped. ReasonCode={ReasonCode}.")]
    internal static partial void Skipped(
        ILogger logger,
        string eventTypeName,
        string tenantId,
        string workItemId,
        string correlationId,
        string reasonCode);

    /// <summary>Logs a duplicate delivery already covered by the durable marker.</summary>
    [LoggerMessage(
        EventId = 4802,
        Level = LogLevel.Debug,
        Message = "Works domain event {EventTypeName} for tenant {TenantId}, work item {WorkItemId}, correlation {CorrelationId} was already completed.")]
    internal static partial void Duplicate(
        ILogger logger,
        string eventTypeName,
        string tenantId,
        string workItemId,
        string correlationId);

    /// <summary>Logs a marker-store operation that could not complete.</summary>
    [LoggerMessage(
        EventId = 4803,
        Level = LogLevel.Warning,
        Message = "Works domain event marker operation failed for {EventTypeName}, tenant {TenantId}, work item {WorkItemId}, correlation {CorrelationId}. ReasonCode={ReasonCode}.")]
    internal static partial void MarkerFailure(
        ILogger logger,
        string eventTypeName,
        string tenantId,
        string workItemId,
        string correlationId,
        string reasonCode);
}
