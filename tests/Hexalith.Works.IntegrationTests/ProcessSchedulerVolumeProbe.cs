using System.ComponentModel;
using System.Diagnostics;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Adapts a child process to the Scheduler-volume probe lifecycle.</summary>
internal sealed class ProcessSchedulerVolumeProbe : ISchedulerVolumeProbe
{
    private const int DisposalTerminationWaitMilliseconds = 5_000;

    private readonly Action _dispose;
    private readonly Func<bool> _hasExited;
    private readonly Action _kill;
    private readonly Process _process;
    private readonly Func<int, bool> _waitForExit;

    /// <summary>Initializes the adapter for a real Docker child process.</summary>
    /// <param name="process">The Docker child process.</param>
    internal ProcessSchedulerVolumeProbe(Process process)
        : this(
            process,
            () => process.HasExited,
            () => process.Kill(entireProcessTree: true),
            process.WaitForExit,
            process.Dispose)
    {
    }

    /// <summary>Initializes the adapter with injectable disposal lifecycle operations.</summary>
    /// <param name="process">The process supplying the normal probe streams and exit task.</param>
    /// <param name="hasExited">Returns whether disposal has confirmed child exit.</param>
    /// <param name="kill">Requests child-tree termination.</param>
    /// <param name="waitForExit">Waits the supplied number of milliseconds for child exit.</param>
    /// <param name="dispose">Releases the process handle.</param>
    internal ProcessSchedulerVolumeProbe(
        Process process,
        Func<bool> hasExited,
        Action kill,
        Func<int, bool> waitForExit,
        Action dispose)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(hasExited);
        ArgumentNullException.ThrowIfNull(kill);
        ArgumentNullException.ThrowIfNull(waitForExit);
        ArgumentNullException.ThrowIfNull(dispose);

        _process = process;
        _hasExited = hasExited;
        _kill = kill;
        _waitForExit = waitForExit;
        _dispose = dispose;
    }

    /// <inheritdoc />
    public bool HasExited => _process.HasExited;

    /// <inheritdoc />
    public int ExitCode => _process.ExitCode;

    /// <inheritdoc />
    public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken)
        => _process.StandardOutput.ReadToEndAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
        => _process.StandardError.ReadToEndAsync(cancellationToken);

    /// <inheritdoc />
    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => _process.WaitForExitAsync(cancellationToken);

    /// <inheritdoc />
    public void Kill() => _process.Kill(entireProcessTree: true);

    /// <inheritdoc />
    public void Dispose()
    {
        var terminationFailures = new List<Exception>();
        bool exited = TryGetHasExited(terminationFailures);
        if (!exited)
        {
            TryKill(terminationFailures);
            exited = WaitForExit(terminationFailures);
            if (!exited)
            {
                TryKill(terminationFailures);
                exited = WaitForExit(terminationFailures);
            }
        }

        if (!exited)
        {
            exited = TryGetHasExited(terminationFailures);
        }

        if (!exited)
        {
            terminationFailures.Add(
                new TimeoutException(
                    "The Docker Scheduler-volume probe remained alive after two bounded disposal attempts."));
        }

        try
        {
            _dispose();
        }
        catch (Exception exception)
        {
            terminationFailures.Add(exception);
        }

        if (terminationFailures.Count > 0)
        {
            Exception classifiedFailure = terminationFailures.Count == 1
                ? terminationFailures[0]
                : new AggregateException(terminationFailures);
            if (!exited)
            {
                throw new SchedulerVolumeProbeCleanupException(
                    "Disposing the Docker Scheduler-volume probe could not confirm child termination.",
                    classifiedFailure);
            }

            throw new InvalidOperationException(
                "Disposing the Docker Scheduler-volume probe encountered a classified cleanup failure.",
                classifiedFailure);
        }
    }

    private bool WaitForExit(List<Exception> terminationFailures)
    {
        try
        {
            return TryGetHasExited(terminationFailures)
                || _waitForExit(DisposalTerminationWaitMilliseconds)
                || TryGetHasExited(terminationFailures);
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            terminationFailures.Add(exception);
            return false;
        }
    }

    private void TryKill(List<Exception> terminationFailures)
    {
        try
        {
            _kill();
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            if (exception is not InvalidOperationException || !TryGetHasExited(terminationFailures))
            {
                terminationFailures.Add(exception);
            }
        }
    }

    private bool TryGetHasExited(List<Exception> terminationFailures)
    {
        try
        {
            return _hasExited();
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            terminationFailures.Add(exception);
            return false;
        }
    }
}
