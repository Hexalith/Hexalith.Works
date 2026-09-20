namespace Hexalith.Works.IntegrationTests;

/// <summary>Configurable deterministic probe for Scheduler-volume process lifecycle tests.</summary>
internal sealed class HangingSchedulerVolumeProbe : ISchedulerVolumeProbe
{
    private readonly bool _completeReadsImmediately;
    private readonly Exception? _disposeException;
    private readonly int _exitAfterCanceledWaitCallCount;
    private readonly int _exitAfterKillCallCount;
    private readonly bool _ignoreReadCancellation;
    private readonly Queue<Exception> _killExceptions;
    private readonly Exception? _standardErrorException;
    private readonly string _standardError;
    private readonly Exception? _standardOutputException;
    private readonly string _standardOutput;
    private readonly bool _throwReadsSynchronously;
    private readonly Exception? _waitForExitException;
    private volatile bool _hasExited;
    private int _canceledReadCount;
    private int _disposeCallCount;
    private int _killCallCount;
    private int _waitForExitExceptionConsumed;
    private int _waitForExitCallCount;

    /// <summary>Initializes a configurable probe.</summary>
    internal HangingSchedulerVolumeProbe(
        bool startsExited = false,
        bool completeReadsImmediately = false,
        int exitAfterKillCallCount = 1,
        int exitCode = 0,
        string standardOutput = "",
        string standardError = "",
        Exception? standardOutputException = null,
        Exception? standardErrorException = null,
        IEnumerable<Exception>? killExceptions = null,
        bool ignoreReadCancellation = false,
        bool throwReadsSynchronously = false,
        Exception? waitForExitException = null,
        int exitAfterCanceledWaitCallCount = int.MaxValue,
        Exception? disposeException = null)
    {
        _hasExited = startsExited;
        _completeReadsImmediately = completeReadsImmediately;
        _disposeException = disposeException;
        _exitAfterCanceledWaitCallCount = exitAfterCanceledWaitCallCount;
        _exitAfterKillCallCount = exitAfterKillCallCount;
        ExitCode = exitCode;
        _standardOutput = standardOutput;
        _standardError = standardError;
        _standardOutputException = standardOutputException;
        _standardErrorException = standardErrorException;
        _killExceptions = new Queue<Exception>(killExceptions ?? []);
        _ignoreReadCancellation = ignoreReadCancellation;
        _throwReadsSynchronously = throwReadsSynchronously;
        _waitForExitException = waitForExitException;
    }

    /// <inheritdoc />
    public bool HasExited => _hasExited;

    /// <inheritdoc />
    public int ExitCode { get; }

    /// <summary>Gets how many redirected reads observed cancellation.</summary>
    public int CanceledReadCount => _canceledReadCount;

    /// <summary>Gets how many termination attempts were made.</summary>
    public int KillCallCount => _killCallCount;

    /// <summary>Gets how many disposal attempts were made.</summary>
    public int DisposeCallCount => _disposeCallCount;

    /// <summary>Gets how many exit waits were started.</summary>
    public int WaitForExitCallCount => _waitForExitCallCount;

    /// <inheritdoc />
    public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken)
        => StartRead(cancellationToken, _standardOutput, _standardOutputException);

    /// <inheritdoc />
    public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
        => StartRead(cancellationToken, _standardError, _standardErrorException);

    /// <inheritdoc />
    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waitForExitCallCount);
        if (_waitForExitException is not null
            && Interlocked.Exchange(ref _waitForExitExceptionConsumed, 1) == 0)
        {
            throw _waitForExitException;
        }

        if (_hasExited)
        {
            return;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (_waitForExitCallCount >= _exitAfterCanceledWaitCallCount)
        {
            _hasExited = true;
            throw;
        }
    }

    /// <inheritdoc />
    public void Kill()
    {
        int killCallCount = Interlocked.Increment(ref _killCallCount);
        if (_killExceptions.TryDequeue(out Exception? exception))
        {
            throw exception;
        }

        if (killCallCount >= _exitAfterKillCallCount)
        {
            _hasExited = true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCallCount);
        if (_disposeException is not null)
        {
            throw _disposeException;
        }
    }

    private Task<string> StartRead(
        CancellationToken cancellationToken,
        string value,
        Exception? exception)
    {
        if (_throwReadsSynchronously && exception is not null)
        {
            throw exception;
        }

        return ReadAsync(cancellationToken, value, exception);
    }

    private async Task<string> ReadAsync(
        CancellationToken cancellationToken,
        string value,
        Exception? exception)
    {
        if (_completeReadsImmediately)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return value;
        }

        if (_ignoreReadCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return string.Empty;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return string.Empty;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _canceledReadCount);
            if (exception is not null)
            {
                throw exception;
            }

            throw;
        }
    }
}
