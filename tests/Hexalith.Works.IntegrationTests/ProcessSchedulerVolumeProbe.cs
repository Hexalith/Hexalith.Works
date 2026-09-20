using System.Diagnostics;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Adapts a child process to the Scheduler-volume probe lifecycle.</summary>
/// <param name="process">The Docker child process.</param>
internal sealed class ProcessSchedulerVolumeProbe(Process process) : ISchedulerVolumeProbe
{
    /// <inheritdoc />
    public bool HasExited => process.HasExited;

    /// <inheritdoc />
    public int ExitCode => process.ExitCode;

    /// <inheritdoc />
    public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken)
        => process.StandardOutput.ReadToEndAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
        => process.StandardError.ReadToEndAsync(cancellationToken);

    /// <inheritdoc />
    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => process.WaitForExitAsync(cancellationToken);

    /// <inheritdoc />
    public void Kill() => process.Kill(entireProcessTree: true);

    /// <inheritdoc />
    public void Dispose() => process.Dispose();
}
