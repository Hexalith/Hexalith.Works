using System.ComponentModel;
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
    public void Exclusive_bind_uses_production_listener_configuration_and_detects_an_occupied_port()
    {
        var configuredIpv4Listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            configuredIpv4Listener.Server.ExclusiveAddressUse = false;

            WorksAppHostSmokeHarness.BindPortExclusively(
                configuredIpv4Listener,
                IPAddress.Loopback);

            configuredIpv4Listener.Server.ExclusiveAddressUse.ShouldBeTrue();
        }
        finally
        {
            configuredIpv4Listener.Stop();
        }

        if (Socket.OSSupportsIPv6)
        {
            var configuredIpv6Listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            try
            {
                configuredIpv6Listener.Server.DualMode = true;

                try
                {
                    WorksAppHostSmokeHarness.BindPortExclusively(
                        configuredIpv6Listener,
                        IPAddress.IPv6Loopback);
                }
                catch (SocketException exception) when (
                    WorksAppHostSmokeHarness.IsUnavailableLoopbackAddress(
                        IPAddress.IPv6Loopback,
                        exception.SocketErrorCode))
                {
                    // A kernel-disabled IPv6 loopback is production's inapplicable-address branch. The listener
                    // still passed through the production configuration operation before Start reported it.
                }

                configuredIpv6Listener.Server.DualMode.ShouldBeFalse();
            }
            finally
            {
                configuredIpv6Listener.Stop();
            }
        }

        var occupiedListener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            occupiedListener.Server.ExclusiveAddressUse = true;
            occupiedListener.Start();
            int occupiedPort = ((IPEndPoint)occupiedListener.LocalEndpoint).Port;

            WorksAppHostSmokeHarness.IsPortAvailableForExclusiveBind(IPAddress.Loopback, occupiedPort)
                .ShouldBeFalse();
        }
        finally
        {
            occupiedListener.Stop();
        }
    }

    [Fact]
    public void Scheduler_volume_probe_includes_stopped_containers()
    {
        ProcessStartInfo startInfo = WorksAppHostSmokeHarness.CreateSchedulerVolumeProbeStartInfo();

        startInfo.FileName.ShouldBe("docker");
        startInfo.CreateNoWindow.ShouldBeTrue();
        startInfo.RedirectStandardError.ShouldBeTrue();
        startInfo.RedirectStandardOutput.ShouldBeTrue();
        startInfo.UseShellExecute.ShouldBeFalse();
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
    public async Task Control_plane_wait_retries_an_occupied_production_address_until_it_is_free()
    {
        int delayCount = 0;
        int targetProbeCount = 0;

        await WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
            "test boundary",
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            (address, port) => address.Equals(IPAddress.Loopback) && port == 50001
                ? ++targetProbeCount > 1
                : true,
            static _ => Task.FromResult<IReadOnlyList<string>>([]),
            (_, _) =>
            {
                delayCount++;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        targetProbeCount.ShouldBe(2);
        delayCount.ShouldBe(1);
    }

    [Fact]
    public async Task Control_plane_wait_reports_the_occupied_resource_address_and_port_after_the_budget()
    {
        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
                "test boundary",
                TimeSpan.Zero,
                TimeSpan.Zero,
                static (address, port) => !address.Equals(IPAddress.Loopback) || port != 50001,
                static _ => Task.FromResult<IReadOnlyList<string>>([]),
                static (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        exception.Message.ShouldContain("dapr-sentry on 127.0.0.1:50001");
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
    public async Task Control_plane_wait_does_not_retry_after_unconfirmed_child_termination()
    {
        int probeCount = 0;
        var cleanupFailure = new SchedulerVolumeProbeCleanupException(
            "child termination was not confirmed",
            new TimeoutException("child remained alive"));

        SchedulerVolumeProbeCleanupException exception
            = await Should.ThrowAsync<SchedulerVolumeProbeCleanupException>(
                () => WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
                    "test boundary",
                    TimeSpan.FromSeconds(1),
                    TimeSpan.Zero,
                    static (_, _) => true,
                    _ =>
                    {
                        probeCount++;
                        return Task.FromException<IReadOnlyList<string>>(cleanupFailure);
                    },
                    static (_, _) => Task.CompletedTask,
                    TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probeCount.ShouldBe(1);
        exception.ShouldBeSameAs(cleanupFailure);
    }

    [Fact]
    public async Task Scheduler_volume_probe_returns_trimmed_line_delimited_owners()
    {
        using var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            standardOutput: "  abc scheduler-one  \r\n\r\ndef scheduler-two\n  ");

        IReadOnlyList<string> owners = await WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
            probe,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        owners.ShouldBe(["abc scheduler-one", "def scheduler-two"]);
    }

    [Fact]
    public async Task Scheduler_volume_probe_returns_an_empty_owner_list_for_empty_output()
    {
        using var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true);

        IReadOnlyList<string> owners = await WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
            probe,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        owners.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_an_already_cancelled_caller_after_synchronous_success()
    {
        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();
        using var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true);

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                callerCts.Token)).ConfigureAwait(true);

        exception.CancellationToken.ShouldBe(callerCts.Token);
    }

    [Fact]
    public async Task Scheduler_volume_probe_classifies_a_non_zero_exit()
    {
        using var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            exitCode: 19,
            standardError: "daemon unavailable");

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        exception.Message.ShouldContain("exit 19");
        exception.Message.ShouldContain("daemon unavailable");
    }

    [Fact]
    public async Task Scheduler_volume_probe_classifies_redirected_read_faults_after_a_successful_exit()
    {
        foreach ((bool failStandardOutput, Exception readFailure) in new (bool, Exception)[]
        {
            (true, new IOException("standard output failed")),
            (false, new ObjectDisposedException("standard error")),
        })
        {
            using var probe = new HangingSchedulerVolumeProbe(
                startsExited: true,
                completeReadsImmediately: true,
                standardOutputException: failStandardOutput ? readFailure : null,
                standardErrorException: failStandardOutput ? null : readFailure,
                throwReadsSynchronously: true);

            InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
                () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                    probe,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(100),
                    TestContext.Current.CancellationToken)).ConfigureAwait(true);

            exception.Message.ShouldContain("redirected stream observation failed");
            exception.InnerException.ShouldBeSameAs(readFailure);
        }
    }

    [Fact]
    public async Task Scheduler_volume_probe_classifies_supported_initial_exit_wait_failures()
    {
        foreach (Exception waitFailure in new Exception[]
        {
            new AggregateException(new InvalidOperationException("wait aggregate failed")),
            new InvalidOperationException("wait state failed"),
            new Win32Exception(5, "wait access denied"),
            new NotSupportedException("wait unsupported"),
        })
        {
            using var probe = new HangingSchedulerVolumeProbe(
                waitForExitException: waitFailure);

            InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
                () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                    probe,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(100),
                    TestContext.Current.CancellationToken)).ConfigureAwait(true);

            exception.Message.ShouldContain("process observation failed");
            exception.InnerException.ShouldBeSameAs(waitFailure);
            probe.KillCallCount.ShouldBe(1);
            probe.WaitForExitCallCount.ShouldBe(2);
            probe.CanceledReadCount.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_timeout_when_exit_completes_but_redirected_reads_cancel()
    {
        using var probe = new HangingSchedulerVolumeProbe(startsExited: true);

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.KillCallCount.ShouldBe(0);
        probe.CanceledReadCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(1);
        exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
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
        probe.KillCallCount.ShouldBe(1);
        probe.CanceledReadCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(2);
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
    }

    [Fact]
    public async Task Scheduler_volume_probe_retries_termination_when_the_first_kill_is_ignored()
    {
        using var probe = new HangingSchedulerVolumeProbe(exitAfterKillCallCount: 2);

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(40),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.HasExited.ShouldBeTrue();
        probe.KillCallCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(3);
        exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
    }

    [Fact]
    public async Task Scheduler_volume_probe_rechecks_exit_after_the_second_termination_wait_times_out()
    {
        using var probe = new HangingSchedulerVolumeProbe(
            exitAfterKillCallCount: int.MaxValue,
            exitAfterCanceledWaitCallCount: 3);

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(40),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.HasExited.ShouldBeTrue();
        probe.KillCallCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(3);
        exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
        exception.ToString().ShouldNotContain("did not exit after two bounded termination attempts");
    }

    [Fact]
    public async Task Scheduler_volume_probe_classifies_a_child_that_resists_both_termination_attempts()
    {
        using var probe = new HangingSchedulerVolumeProbe(exitAfterKillCallCount: int.MaxValue);
        var elapsed = Stopwatch.StartNew();

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(40),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        elapsed.Stop();
        probe.HasExited.ShouldBeFalse();
        probe.KillCallCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(3);
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        exception.Message.ShouldContain("cleanup failed");
        exception.ToString().ShouldContain("did not exit after two bounded termination attempts");
    }

    [Fact]
    public async Task Scheduler_volume_probe_classifies_supported_kill_failures()
    {
        foreach (Func<string, Exception> exceptionFactory in new Func<string, Exception>[]
        {
            static message => new AggregateException(new InvalidOperationException(message)),
            static message => new InvalidOperationException(message),
            static message => new Win32Exception(5, message),
            static message => new NotSupportedException(message),
        })
        {
            const string FirstFailure = "first tree kill failed";
            const string FinalFailure = "final tree kill failed";
            Exception firstFailure = exceptionFactory(FirstFailure);
            Exception finalFailure = exceptionFactory(FinalFailure);
            using var probe = new HangingSchedulerVolumeProbe(
                exitAfterKillCallCount: int.MaxValue,
                killExceptions: [firstFailure, finalFailure]);

            InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
                () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                    probe,
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(40),
                    TestContext.Current.CancellationToken)).ConfigureAwait(true);

            probe.KillCallCount.ShouldBe(2);
            probe.WaitForExitCallCount.ShouldBe(3);
            exception.Message.ShouldContain("cleanup failed");
            exception.ToString().ShouldContain(firstFailure.GetType().Name);
            exception.ToString().ShouldContain(FirstFailure);
            exception.ToString().ShouldContain(FinalFailure);
        }
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_timeout_when_redirected_pipes_close_during_cleanup()
    {
        using var probe = new HangingSchedulerVolumeProbe(
            standardOutputException: new IOException("standard output pipe closed"),
            standardErrorException: new ObjectDisposedException("standard error pipe"));

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.KillCallCount.ShouldBe(1);
        probe.CanceledReadCount.ShouldBe(2);
        probe.WaitForExitCallCount.ShouldBe(2);
        exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
    }

    [Fact]
    public async Task Scheduler_volume_probe_bounds_redirected_reads_that_ignore_cancellation()
    {
        using var probe = new HangingSchedulerVolumeProbe(ignoreReadCancellation: true);
        var elapsed = Stopwatch.StartNew();

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(40),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        elapsed.Stop();
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        exception.Message.ShouldContain("cleanup failed");
        exception.ToString().ShouldContain("standard output did not settle");
        exception.ToString().ShouldContain("standard error did not settle");
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_exact_caller_cancellation_after_cleanup()
    {
        using var probe = new HangingSchedulerVolumeProbe();
        using var callerCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                callerCts.Token)).ConfigureAwait(true);

        probe.HasExited.ShouldBeTrue();
        probe.KillCallCount.ShouldBe(1);
        probe.CanceledReadCount.ShouldBe(2);
        exception.CancellationToken.ShouldBe(callerCts.Token);
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_exact_caller_cancellation_when_cleanup_fails()
    {
        using var probe = new HangingSchedulerVolumeProbe(exitAfterKillCallCount: int.MaxValue);
        using var callerCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(40),
                callerCts.Token)).ConfigureAwait(true);

        probe.HasExited.ShouldBeFalse();
        probe.KillCallCount.ShouldBe(2);
        exception.CancellationToken.ShouldBe(callerCts.Token);
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_exact_caller_cancellation_when_a_redirected_read_fails()
    {
        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();
        using var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            standardOutputException: new IOException("standard output failed"));

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
            () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                callerCts.Token)).ConfigureAwait(true);

        exception.CancellationToken.ShouldBe(callerCts.Token);
    }

    [Fact]
    public async Task Scheduler_volume_probe_wrapper_preserves_unconfirmed_disposal_as_non_retryable()
    {
        var cleanupFailure = new SchedulerVolumeProbeCleanupException(
            "child termination was not confirmed",
            new TimeoutException("child remained alive"));
        var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            disposeException: cleanupFailure);

        SchedulerVolumeProbeCleanupException exception
            = await Should.ThrowAsync<SchedulerVolumeProbeCleanupException>(
                () => WorksAppHostSmokeHarness.RunAndDisposeSchedulerVolumeProbeAsync(
                    probe,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(100),
                    TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.DisposeCallCount.ShouldBe(1);
        exception.InnerException.ShouldBeSameAs(cleanupFailure);
    }

    [Fact]
    public async Task Scheduler_volume_probe_wrapper_prefers_exact_caller_cancellation_over_disposal_failure()
    {
        var disposalFailure = new InvalidOperationException("probe disposal failed");
        var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            disposeException: disposalFailure);
        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
            () => WorksAppHostSmokeHarness.RunAndDisposeSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                callerCts.Token)).ConfigureAwait(true);

        probe.DisposeCallCount.ShouldBe(1);
        exception.CancellationToken.ShouldBe(callerCts.Token);
    }

    [Fact]
    public void Process_scheduler_volume_probe_disposal_retries_after_the_first_attempt()
    {
        using var process = new Process();
        int killCount = 0;
        int waitCount = 0;
        bool exited = false;
        var probe = new ProcessSchedulerVolumeProbe(
            process,
            () => exited,
            () => killCount++,
            _ =>
            {
                waitCount++;
                exited = waitCount >= 2;
                return exited;
            },
            static () => { });

        probe.Dispose();

        killCount.ShouldBe(2);
        waitCount.ShouldBe(2);
        exited.ShouldBeTrue();
    }

    [Fact]
    public void Process_scheduler_volume_probe_still_terminates_after_an_initial_state_failure()
    {
        using var process = new Process();
        int hasExitedCallCount = 0;
        int killCount = 0;
        var probe = new ProcessSchedulerVolumeProbe(
            process,
            () => ++hasExitedCallCount == 1
                ? throw new Win32Exception(5, "state unavailable")
                : killCount > 0,
            () => killCount++,
            _ => true,
            static () => { });

        InvalidOperationException exception = Should.Throw<InvalidOperationException>(probe.Dispose);

        killCount.ShouldBe(1);
        exception.ToString().ShouldContain("state unavailable");
    }

    [Fact]
    public void Process_scheduler_volume_probe_aggregates_lifecycle_and_disposal_failures()
    {
        using var process = new Process();
        var lifecycleFailure = new Win32Exception(5, "state unavailable");
        var disposalFailure = new ObjectDisposedException("process handle", "dispose failed");
        int hasExitedCallCount = 0;
        int disposeCallCount = 0;
        var probe = new ProcessSchedulerVolumeProbe(
            process,
            () => ++hasExitedCallCount == 1 ? throw lifecycleFailure : true,
            static () => { },
            static _ => true,
            () =>
            {
                disposeCallCount++;
                throw disposalFailure;
            });

        InvalidOperationException exception = Should.Throw<InvalidOperationException>(probe.Dispose);

        disposeCallCount.ShouldBe(1);
        AggregateException aggregate = exception.InnerException.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions.ShouldContain(lifecycleFailure);
        aggregate.InnerExceptions.ShouldContain(disposalFailure);
    }

    [Fact]
    public void Process_scheduler_volume_probe_accepts_exit_observed_after_the_final_false_wait()
    {
        using var process = new Process();
        int hasExitedCallCount = 0;
        int killCallCount = 0;
        int waitCallCount = 0;
        var probe = new ProcessSchedulerVolumeProbe(
            process,
            () => ++hasExitedCallCount >= 5,
            () => killCallCount++,
            _ =>
            {
                waitCallCount++;
                return false;
            },
            static () => { });

        probe.Dispose();

        killCallCount.ShouldBe(2);
        waitCallCount.ShouldBe(2);
        hasExitedCallCount.ShouldBe(5);
    }

    [Fact]
    public void Process_scheduler_volume_probe_classifies_unconfirmed_termination()
    {
        using var process = new Process();
        int killCount = 0;
        int waitCount = 0;
        var probe = new ProcessSchedulerVolumeProbe(
            process,
            static () => false,
            () => killCount++,
            _ =>
            {
                waitCount++;
                return false;
            },
            static () => { });

        SchedulerVolumeProbeCleanupException exception
            = Should.Throw<SchedulerVolumeProbeCleanupException>(probe.Dispose);

        killCount.ShouldBe(2);
        waitCount.ShouldBe(2);
        exception.ToString().ShouldContain("remained alive after two bounded disposal attempts");
    }

    [Fact]
    public async Task Process_scheduler_volume_probe_disposal_terminates_a_live_child()
    {
        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe")
            : new ProcessStartInfo("/bin/sh");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("ping 127.0.0.1 -n 30 > nul");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("exec sleep 30");
        }

        Process process = Process.Start(startInfo).ShouldNotBeNull();
        using Process cleanupProcess = Process.GetProcessById(process.Id);
        _ = cleanupProcess.SafeHandle;
        try
        {
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => exited.TrySetResult();
            process.HasExited.ShouldBeFalse();

            var probe = new ProcessSchedulerVolumeProbe(process);
            probe.Dispose();

            await exited.Task
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            process.Dispose();
            try
            {
                if (!cleanupProcess.HasExited)
                {
                    cleanupProcess.Kill(entireProcessTree: true);
                    cleanupProcess.WaitForExit(2_000);
                }
            }
            catch (InvalidOperationException)
            {
                // The retained process exited between the state check and the fail-safe termination request.
            }
        }
    }
}
