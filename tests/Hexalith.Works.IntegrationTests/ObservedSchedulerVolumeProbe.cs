namespace Hexalith.Works.IntegrationTests;

/// <summary>Retains the real adapter's pipe tasks so tests can verify their terminal state after cleanup.</summary>
/// <param name="inner">The real process adapter supplying every lifecycle operation.</param>
internal sealed class ObservedSchedulerVolumeProbe(ProcessSchedulerVolumeProbe inner) : ISchedulerVolumeProbe
{
    /// <summary>Gets the original standard-output read task.</summary>
    internal Task<string>? OutputRead { get; private set; }

    /// <summary>Gets the original standard-error read task.</summary>
    internal Task<string>? ErrorRead { get; private set; }

    /// <inheritdoc />
    public bool HasExited => inner.HasExited;

    /// <inheritdoc />
    public int ExitCode => inner.ExitCode;

    /// <inheritdoc />
    public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken)
        => OutputRead = inner.ReadStandardOutputAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
        => ErrorRead = inner.ReadStandardErrorAsync(cancellationToken);

    /// <inheritdoc />
    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => inner.WaitForExitAsync(cancellationToken);

    /// <inheritdoc />
    public void Kill() => inner.Kill();

    /// <inheritdoc />
    public void Dispose() => inner.Dispose();
}
