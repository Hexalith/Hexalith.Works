using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic coverage for the shared live-AppHost resource-settling probes.
/// </summary>
public sealed class WorksAppHostSmokeHarnessTests
{
    [Fact]
    public void Ipv6_bind_failures_are_classified_by_the_production_port_probe()
    {
        WorksAppHostSmokeHarness.IsPortAvailableForExclusiveBind(
                IPAddress.IPv6Loopback,
                50001,
                static (_, _) => throw new SocketException((int)SocketError.AddressFamilyNotSupported))
            .ShouldBeTrue();
        WorksAppHostSmokeHarness.IsPortAvailableForExclusiveBind(
                IPAddress.IPv6Loopback,
                50001,
                static (_, _) => throw new SocketException((int)SocketError.AddressNotAvailable))
            .ShouldBeTrue();
        WorksAppHostSmokeHarness.IsPortAvailableForExclusiveBind(
                IPAddress.IPv6Loopback,
                50001,
                static (_, _) => throw new SocketException((int)SocketError.ProtocolNotSupported))
            .ShouldBeTrue();
        WorksAppHostSmokeHarness.IsPortAvailableForExclusiveBind(
                IPAddress.IPv6Loopback,
                50001,
                static (_, _) => throw new SocketException((int)SocketError.ProtocolFamilyNotSupported))
            .ShouldBeTrue();
        WorksAppHostSmokeHarness.IsPortAvailableForExclusiveBind(
                IPAddress.IPv6Loopback,
                50001,
                static (_, _) => throw new SocketException((int)SocketError.AddressAlreadyInUse))
            .ShouldBeFalse();
        WorksAppHostSmokeHarness.IsPortAvailableForExclusiveBind(
                IPAddress.Loopback,
                50001,
                static (_, _) => throw new SocketException((int)SocketError.AddressFamilyNotSupported))
            .ShouldBeFalse();
    }

    [Fact]
    public void Scheduler_volume_probe_includes_stopped_containers()
    {
        ProcessStartInfo startInfo = WorksAppHostSmokeHarness.CreateSchedulerVolumeProbeStartInfo();

        startInfo.FileName.ShouldBe("docker");
        startInfo.ArgumentList.ToArray().ShouldBe(
        [
            "ps",
            "--all",
            "--filter",
            "volume=hexalith-works-dapr-scheduler",
            "--format",
            "{{.ID}} {{.Names}}",
        ]);
    }

    [Fact]
    public async Task Control_plane_wait_retries_a_transient_docker_probe_failure()
    {
        int probeCount = 0;

        await WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
            "test boundary",
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            static (_, _) => true,
            _ => ++probeCount == 1
                ? Task.FromException<IReadOnlyList<string>>(new TimeoutException("probe timed out"))
                : Task.FromResult<IReadOnlyList<string>>([]),
            static (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        probeCount.ShouldBe(2);
    }

    [Fact]
    public async Task Control_plane_wait_reports_a_persistent_docker_probe_failure_after_the_budget()
    {
        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
                "test boundary",
                TimeSpan.Zero,
                TimeSpan.Zero,
                static (_, _) => true,
                static _ => Task.FromException<IReadOnlyList<string>>(
                    new InvalidOperationException("docker unavailable")),
                static (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        exception.Message.ShouldContain("scheduler volume ownership probe unavailable");
        exception.Message.ShouldContain("docker unavailable");
    }

    [Fact]
    public async Task Scheduler_volume_probe_terminates_and_observes_a_timed_out_process()
    {
        using var probe = new HangingSchedulerVolumeProbe();
        var elapsed = Stopwatch.StartNew();

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        elapsed.Stop();
        probe.KillCalled.ShouldBeTrue();
        probe.CanceledReadCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(2);
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_timeout_when_redirected_pipes_close_during_cleanup()
    {
        using var probe = new HangingSchedulerVolumeProbe(
            new IOException("standard output pipe closed"),
            new ObjectDisposedException("standard error pipe"));

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.KillCalled.ShouldBeTrue();
        probe.CanceledReadCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(2);
        exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
    }

    private sealed class HangingSchedulerVolumeProbe : ISchedulerVolumeProbe
    {
        private readonly Exception? _standardErrorCleanupException;
        private readonly Exception? _standardOutputCleanupException;
        private volatile bool _killed;
        private int _canceledReadCount;
        private int _waitForExitCallCount;

        public HangingSchedulerVolumeProbe(
            Exception? standardOutputCleanupException = null,
            Exception? standardErrorCleanupException = null)
        {
            _standardOutputCleanupException = standardOutputCleanupException;
            _standardErrorCleanupException = standardErrorCleanupException;
        }

        public bool HasExited => _killed;

        public int ExitCode => 0;

        public bool KillCalled { get; private set; }

        public int CanceledReadCount => _canceledReadCount;

        public int WaitForExitCallCount => _waitForExitCallCount;

        public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken)
            => WaitForReadCancellationAsync(cancellationToken, _standardOutputCleanupException);

        public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
            => WaitForReadCancellationAsync(cancellationToken, _standardErrorCleanupException);

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waitForExitCallCount);
            if (_killed)
            {
                return;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        public void Kill()
        {
            KillCalled = true;
            _killed = true;
        }

        public void Dispose()
        {
        }

        private async Task<string> WaitForReadCancellationAsync(
            CancellationToken cancellationToken,
            Exception? cleanupException)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledReadCount);
                if (cleanupException is not null)
                {
                    throw cleanupException;
                }

                throw;
            }
        }
    }
}
