namespace Hexalith.Works.IntegrationTests;

/// <summary>A bounded recovery gauge observed on the real OTLP HTTP transport.</summary>
/// <param name="Name">Fixed canonical recovery instrument name.</param>
/// <param name="Value">Finite nonnegative gauge value.</param>
/// <param name="Reason">Optional bounded reason label; source and payload labels are forbidden.</param>
/// <param name="UnixTimeNanoseconds">Exporter observation time.</param>
/// <param name="ReceivedAt">UTC collector receipt time.</param>
internal sealed record WorksPlatformMetricPoint(string Name, double Value, string? Reason, ulong UnixTimeNanoseconds, DateTimeOffset ReceivedAt);
