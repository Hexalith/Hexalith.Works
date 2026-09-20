namespace Hexalith.Works.IntegrationTests;

/// <summary>Abstracts the bounded process lifecycle used by the Scheduler-volume ownership probe.</summary>
internal interface ISchedulerVolumeProbe : IDisposable
{
    /// <summary>Gets whether the child process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Gets the child process exit code.</summary>
    int ExitCode { get; }

    /// <summary>Reads redirected standard output.</summary>
    Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken);

    /// <summary>Reads redirected standard error.</summary>
    Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken);

    /// <summary>Waits for the child process to exit.</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Terminates the child process and its descendants.</summary>
    void Kill();
}
