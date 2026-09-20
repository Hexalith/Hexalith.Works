namespace Hexalith.Works.IntegrationTests;

/// <summary>Reports probe cleanup that could not confirm adapter-owned child termination.</summary>
internal sealed class SchedulerVolumeProbeCleanupException : InvalidOperationException
{
    /// <summary>Initializes a probe cleanup exception.</summary>
    /// <param name="message">The actionable cleanup diagnostic.</param>
    /// <param name="innerException">The classified cleanup failure.</param>
    internal SchedulerVolumeProbeCleanupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
