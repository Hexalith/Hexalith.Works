using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic coverage for the shared live-AppHost resource-settling probes.
/// </summary>
public sealed class WorksAppHostSmokeHarnessTests
{
    private const string SchedulerVolume = "works-sdk416-harness-probe";
    private static readonly (int Sentry, int Placement, int Scheduler) SelectedPorts = (61001, 61005, 61006);

    /// <summary>Both lifecycle boundaries wait on the selected ports and the caller's owned Scheduler volume.</summary>
    /// <param name="boundary">The start or teardown diagnostic boundary.</param>
    [Theory]
    [InlineData("startup boundary")]
    [InlineData("teardown boundary")]
    public async Task SelectedControlPlaneAndOwnedSchedulerVolumeMustBothBeReleased(string boundary)
    {
        bool released = false;
        int delays = 0;
        var observedPorts = new HashSet<(IPAddress Address, int Port)>();
        var observedVolumes = new List<string>();
        await WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
            boundary, SelectedPorts, SchedulerVolume, TimeSpan.FromSeconds(1), TimeSpan.Zero,
            (address, port) =>
            {
                observedPorts.Add((address, port));
                return released || port != SelectedPorts.Placement;
            },
            (volume, _) =>
            {
                observedVolumes.Add(volume);
                return Task.FromResult<IReadOnlyList<string>>(released ? [] : ["proof-scheduler"]);
            },
            (_, _) =>
            {
                released = true;
                delays++;
                return Task.CompletedTask;
            }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        delays.ShouldBe(1);
        observedVolumes.ShouldBe([SchedulerVolume, SchedulerVolume]);
        observedPorts.ShouldBe(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }
            .SelectMany(address => new[] { SelectedPorts.Sentry, SelectedPorts.Placement, SelectedPorts.Scheduler }
                .Select(port => (address, port))), ignoreOrder: true);
    }

    /// <summary>A Platform boundary without a selected proof volume probes its ports without invoking Docker.</summary>
    [Fact]
    public async Task MissingProofVolumeSkipsOnlyTheSchedulerOwnershipProbe()
    {
        int portProbes = 0;
        await WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
            "prerequisite", SelectedPorts, null, TimeSpan.Zero, TimeSpan.Zero,
            (_, _) => { portProbes++; return true; },
            static (_, _) => throw new InvalidOperationException("No Docker probe is permitted without an owned volume."),
            static (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken).ConfigureAwait(true);
        portProbes.ShouldBe(6);
    }

    /// <summary>Sidecar readiness refuses the historically unsafe runtime and accepts the qualified floor numerically.</summary>
    /// <param name="runtimeVersion">The runtime advertised by the sidecar.</param>
    /// <param name="accepted">Whether it reaches the qualified minimum.</param>
    [Theory]
    [InlineData("1.18.3", false)]
    [InlineData("1.18.6", false)]
    [InlineData("1.18.7", true)]
    [InlineData("1.18.10", true)]
    public async Task ActorReadinessRequiresTheQualifiedDaprRuntime(string runtimeVersion, bool accepted)
    {
        using var handler = new WorksSdkHttpEvidenceHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                runtimeVersion, actors = new[] { new { type = "WorksSdkReminderActor" } },
                actorRuntime = new { runtimeStatus = "RUNNING", hostReady = true, placement = "connected" },
                scheduler = new { connected_addresses = new[] { "localhost:61006" } },
            }),
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1/") };
        if (accepted)
            await WorksAppHostTestReadiness.WaitForWorksActorRuntimeAsync(client, TestContext.Current.CancellationToken).ConfigureAwait(true);
        else
        {
            InvalidOperationException refusal = await Should.ThrowAsync<InvalidOperationException>(() =>
                WorksAppHostTestReadiness.WaitForWorksActorRuntimeAsync(client, TestContext.Current.CancellationToken)).ConfigureAwait(true);
            refusal.Message.ShouldContain("[startup/readiness]");
            refusal.Message.ShouldContain("runtime 1.18.7 or newer is required");
        }
    }

    /// <summary>Real redirected pipe reads settle when the production probe terminates its child.</summary>
    /// <param name="cancelCaller">Whether the caller cancels instead of the probe budget expiring.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealRedirectedProbeReadsSettleAfterTimeoutOrCallerCancellation(bool cancelCaller)
    {
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows()
            ? "echo output-ready & echo error-ready 1>&2 & ping 127.0.0.1 -n 60 > nul"
            : "echo output-ready; echo error-ready >&2; exec sleep 60");
        using Process process = Process.Start(startInfo).ShouldNotBeNull();
        using var adapter = new ProcessSchedulerVolumeProbe(process);
        CancellationToken testToken = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(testToken);

        (await process.StandardOutput.ReadLineAsync(testToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), testToken).ConfigureAwait(true)).ShouldNotBeNull().Trim().ShouldBe("output-ready");
        (await process.StandardError.ReadLineAsync(testToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), testToken).ConfigureAwait(true)).ShouldNotBeNull().Trim().ShouldBe("error-ready");
        process.HasExited.ShouldBeFalse();

        var probe = new ObservedSchedulerVolumeProbe(adapter);
        Task<IReadOnlyList<string>> run = WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
            probe,
            cancelCaller ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(2),
            caller.Token);
        Task<string> outputRead = probe.OutputRead.ShouldNotBeNull();
        Task<string> errorRead = probe.ErrorRead.ShouldNotBeNull();
        if (cancelCaller)
        {
            outputRead.IsCompleted.ShouldBeFalse();
            errorRead.IsCompleted.ShouldBeFalse();
            caller.Cancel();
            OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(10), testToken)).ConfigureAwait(true);
            exception.CancellationToken.ShouldBe(caller.Token);
        }
        else
        {
            TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(10), testToken)).ConfigureAwait(true);
            exception.Message.ShouldContain("Docker did not inspect the Scheduler volume owner");
        }

        process.HasExited.ShouldBeTrue();
        outputRead.IsCompleted.ShouldBeTrue();
        errorRead.IsCompleted.ShouldBeTrue();
        foreach (Task<string> read in new[] { outputRead, errorRead })
        {
            if (read.IsCanceled)
            {
                await Should.ThrowAsync<OperationCanceledException>(() => read).ConfigureAwait(true);
            }
            else
            {
                (await read.ConfigureAwait(true)).ShouldBeEmpty();
            }
        }
    }

    /// <summary>Controlled connection completion preserves caller precedence without timing-dependent network failures.</summary>
    /// <param name="scenario">The connection/deadline race to exercise.</param>
    [Theory]
    [InlineData("timeout")]
    [InlineData("timeout-success")]
    [InlineData("caller")]
    [InlineData("both")]
    [InlineData("caller-success")]
    [InlineData("caller-socket")]
    [InlineData("foreign-cancellation")]
    [InlineData("pre-cancelled")]
    public async Task TcpPrerequisiteProbePreservesDeadlineAndCallerRaces(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        using var caller = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken connectionToken = default;
        int invocations = 0;
        if (scenario == "pre-cancelled")
        {
            caller.Cancel();
        }

        Task<bool> run = WorksAppHostSmokeHarness.IsPortReachableAsync(token =>
        {
            invocations++;
            connectionToken = token;
            return completion.Task;
        }, caller.Token, timeout.Token);
        if (scenario.StartsWith("caller", StringComparison.Ordinal) || scenario == "both")
        {
            caller.Cancel();
        }
        if (scenario.StartsWith("timeout", StringComparison.Ordinal) || scenario == "both")
        {
            timeout.Cancel();
        }

        if (scenario.EndsWith("success", StringComparison.Ordinal))
        {
            completion.SetResult();
        }
        else if (scenario == "caller-socket")
        {
            completion.SetException(new SocketException((int)SocketError.ConnectionRefused));
        }
        else if (scenario == "foreign-cancellation")
        {
            completion.SetException(new OperationCanceledException(foreign.Token));
        }
        else
        {
            completion.SetCanceled(connectionToken);
        }

        if (scenario.StartsWith("timeout", StringComparison.Ordinal))
        {
            (await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).ShouldBeFalse();
        }
        else
        {
            OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ConfigureAwait(true);
            exception.CancellationToken.ShouldBe(scenario == "foreign-cancellation" ? foreign.Token : caller.Token);
        }

        invocations.ShouldBe(scenario == "pre-cancelled" ? 0 : 1);
        if (invocations > 0)
        {
            connectionToken.CanBeCanceled.ShouldBeTrue();
            connectionToken.IsCancellationRequested.ShouldBe(scenario != "foreign-cancellation");
        }
    }

    /// <summary>The production timer cancels a pending connection while the caller remains active.</summary>
    [Fact]
    public async Task TcpPrerequisiteProbeAppliesItsOwnTimerToAPendingConnection()
    {
        WorksAppHostSmokeHarness.TcpProbeWait.ShouldBe(TimeSpan.FromSeconds(2));
        CancellationToken observed = default;
        int invocations = 0;
        var elapsed = Stopwatch.StartNew();
        bool reachable = await WorksAppHostSmokeHarness.IsPortReachableAsync(SelectedPorts.Sentry, (port, token) =>
        {
            invocations++;
            port.ShouldBe(SelectedPorts.Sentry);
            observed = token;
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);

        reachable.ShouldBeFalse();
        invocations.ShouldBe(1);
        observed.IsCancellationRequested.ShouldBeTrue();
        elapsed.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1900));
    }

    /// <summary>A cancelled caller must leave the TCP prerequisite as cancellation, even when the port is reachable.</summary>
    [Fact]
    public async Task TcpPrerequisiteProbePreservesCallerCancellation()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var caller = new CancellationTokenSource();
            caller.Cancel();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await Should.ThrowAsync<OperationCanceledException>(() =>
                WorksAppHostSmokeHarness.IsPortReachableAsync(port, caller.Token)).ConfigureAwait(true);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>The production TCP prerequisite accepts a listener on an owned loopback port.</summary>
    [Fact]
    public async Task TcpPrerequisiteProbeRecognizesReachableLoopback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            (await WorksAppHostSmokeHarness.IsPortReachableAsync(port, TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).ShouldBeTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>A bound socket that does not listen owns the test port while refusing TCP connections.</summary>
    [Fact]
    public async Task TcpPrerequisiteProbeReportsUnavailableLoopback()
    {
        using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reserved.LocalEndPoint!).Port;

        (await WorksAppHostSmokeHarness.IsPortReachableAsync(port, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBeFalse();
    }

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
        (bool exclusiveAddressUse, bool? dualMode) ipv4Settings
            = WorksAppHostSmokeHarness.BindPortExclusively(IPAddress.Loopback, 0);

        ipv4Settings.exclusiveAddressUse.ShouldBeTrue();
        ipv4Settings.dualMode.ShouldBeNull();

        if (Socket.OSSupportsIPv6)
        {
            try
            {
                (bool exclusiveAddressUse, bool? dualMode) ipv6Settings
                    = WorksAppHostSmokeHarness.BindPortExclusively(IPAddress.IPv6Loopback, 0);

                ipv6Settings.exclusiveAddressUse.ShouldBeTrue();
                ipv6Settings.dualMode.ShouldBe(false);
            }
            catch (SocketException exception) when (
                WorksAppHostSmokeHarness.IsUnavailableLoopbackAddress(
                    IPAddress.IPv6Loopback,
                    exception.SocketErrorCode))
            {
                // A kernel-disabled IPv6 loopback is production's inapplicable-address branch. The production
                // overload was exercised through Start; configuration itself remains covered by the listener seam.
                TcpListener? configuredIpv6Listener = null;
                try
                {
                    configuredIpv6Listener = new TcpListener(IPAddress.IPv6Loopback, 0);
                    configuredIpv6Listener.Server.DualMode = true;
                    try
                    {
                        WorksAppHostSmokeHarness.BindPortExclusively(
                            configuredIpv6Listener,
                            IPAddress.IPv6Loopback);
                    }
                    catch (SocketException bindException) when (
                        WorksAppHostSmokeHarness.IsUnavailableLoopbackAddress(
                            IPAddress.IPv6Loopback,
                            bindException.SocketErrorCode))
                    {
                    }

                    configuredIpv6Listener.Server.ExclusiveAddressUse.ShouldBeTrue();
                    configuredIpv6Listener.Server.DualMode.ShouldBeFalse();
                }
                catch (SocketException configurationException) when (
                    WorksAppHostSmokeHarness.IsUnavailableLoopbackAddress(
                        IPAddress.IPv6Loopback,
                        configurationException.SocketErrorCode))
                {
                }
                finally
                {
                    configuredIpv6Listener?.Stop();
                }
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
        ProcessStartInfo startInfo = WorksAppHostSmokeHarness.CreateSchedulerVolumeProbeStartInfo(SchedulerVolume);

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
            "volume=" + SchedulerVolume,
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
            SelectedPorts,
            SchedulerVolume,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            static (_, _) => true,
            (_, _) => ++probeCount == 1
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
            SelectedPorts,
            SchedulerVolume,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            (address, port) => address.Equals(IPAddress.Loopback) && port == SelectedPorts.Sentry
                ? ++targetProbeCount > 1
                : true,
            static (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
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
                SelectedPorts,
                SchedulerVolume,
                TimeSpan.Zero,
                TimeSpan.Zero,
                static (address, port) => !address.Equals(IPAddress.Loopback) || port != SelectedPorts.Sentry,
                static (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
                static (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        exception.Message.ShouldContain("dapr-sentry on 127.0.0.1:61001");
    }

    [Fact]
    public async Task Control_plane_wait_reports_a_persistent_docker_probe_failure_after_the_budget()
    {
        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => WorksAppHostSmokeHarness.WaitForControlPlaneResourcesReleasedAsync(
                "test boundary",
                SelectedPorts,
                SchedulerVolume,
                TimeSpan.Zero,
                TimeSpan.Zero,
                static (_, _) => true,
                static (_, _) => Task.FromException<IReadOnlyList<string>>(
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
                    SelectedPorts,
                    SchedulerVolume,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.Zero,
                    static (_, _) => true,
                    (_, _) =>
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

        exception.Message.ShouldBe(
            "Docker could not inspect the Scheduler volume owner (exit 19): daemon unavailable.");
        exception.Message.ShouldNotContain("process observation failed");
        exception.InnerException.ShouldBeNull();
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
    public async Task Scheduler_volume_probe_classifies_indexed_termination_wait_and_state_failures()
    {
        foreach (bool failTerminationWait in new[] { true, false })
        {
            Exception cleanupFailure = failTerminationWait
                ? new Win32Exception(5, "termination wait failed")
                : new NotSupportedException("termination state failed");
            using var probe = new HangingSchedulerVolumeProbe(
                waitForExitExceptions: failTerminationWait
                    ? new Dictionary<int, Exception> { [2] = cleanupFailure }
                    : null,
                hasExitedExceptions: failTerminationWait
                    ? null
                    : new Dictionary<int, Exception> { [1] = cleanupFailure });

            InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
                () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                    probe,
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(40),
                    TestContext.Current.CancellationToken)).ConfigureAwait(true);

            probe.KillCallCount.ShouldBe(1);
            probe.WaitForExitCallCount.ShouldBe(2);
            probe.HasExitedCallCount.ShouldBe(failTerminationWait ? 2 : 1);
            exception.Message.ShouldContain("cleanup failed");
            exception.InnerException.ShouldBeSameAs(cleanupFailure);
        }
    }

    [Fact]
    public async Task Scheduler_volume_probe_preserves_exact_caller_cancellation_over_indexed_cleanup_failures()
    {
        foreach (bool failTerminationWait in new[] { true, false })
        {
            Exception cleanupFailure = failTerminationWait
                ? new Win32Exception(5, "termination wait failed")
                : new NotSupportedException("termination state failed");
            using var probe = new HangingSchedulerVolumeProbe(
                waitForExitExceptions: failTerminationWait
                    ? new Dictionary<int, Exception> { [2] = cleanupFailure }
                    : null,
                hasExitedExceptions: failTerminationWait
                    ? null
                    : new Dictionary<int, Exception> { [1] = cleanupFailure });
            using var callerCts = new CancellationTokenSource();
            callerCts.Cancel();

            OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
                () => WorksAppHostSmokeHarness.RunSchedulerVolumeProbeAsync(
                    probe,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(40),
                    callerCts.Token)).ConfigureAwait(true);

            probe.KillCallCount.ShouldBe(1);
            probe.WaitForExitCallCount.ShouldBe(2);
            probe.HasExitedCallCount.ShouldBe(failTerminationWait ? 2 : 1);
            exception.CancellationToken.ShouldBe(callerCts.Token);
        }
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
    public async Task Scheduler_volume_probe_wrapper_classifies_an_ordinary_disposal_failure()
    {
        var disposalFailure = new ObjectDisposedException("probe", "probe disposal failed");
        var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            disposeException: disposalFailure);

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            () => WorksAppHostSmokeHarness.RunAndDisposeSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.DisposeCallCount.ShouldBe(1);
        exception.Message.ShouldContain("classified cleanup failure");
        exception.InnerException.ShouldBeSameAs(disposalFailure);
    }

    [Fact]
    public async Task Scheduler_volume_probe_wrapper_preserves_probe_and_ordinary_disposal_failures()
    {
        var observationFailure = new IOException("standard output failed");
        var disposalFailure = new ObjectDisposedException("probe", "probe disposal failed");
        var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            standardOutputException: observationFailure,
            throwReadsSynchronously: true,
            disposeException: disposalFailure);

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            () => WorksAppHostSmokeHarness.RunAndDisposeSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.DisposeCallCount.ShouldBe(1);
        AggregateException aggregate = exception.InnerException.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions.Count.ShouldBe(2);
        InvalidOperationException classifiedProbeFailure
            = aggregate.InnerExceptions[0].ShouldBeOfType<InvalidOperationException>();
        classifiedProbeFailure.InnerException.ShouldBeSameAs(observationFailure);
        aggregate.InnerExceptions[1].ShouldBeSameAs(disposalFailure);
    }

    [Fact]
    public async Task Scheduler_volume_probe_wrapper_preserves_a_non_zero_probe_failure_when_disposal_succeeds()
    {
        var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            exitCode: 19,
            standardError: "daemon unavailable");

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            () => WorksAppHostSmokeHarness.RunAndDisposeSchedulerVolumeProbeAsync(
                probe,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        probe.DisposeCallCount.ShouldBe(1);
        exception.Message.ShouldBe(
            "Docker could not inspect the Scheduler volume owner (exit 19): daemon unavailable.");
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task Scheduler_volume_probe_wrapper_returns_the_successful_owner_list_and_disposes_the_probe()
    {
        var probe = new HangingSchedulerVolumeProbe(
            startsExited: true,
            completeReadsImmediately: true,
            standardOutput: "abc scheduler-one\ndef scheduler-two");

        IReadOnlyList<string> owners = await WorksAppHostSmokeHarness.RunAndDisposeSchedulerVolumeProbeAsync(
            probe,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        owners.ShouldBe(["abc scheduler-one", "def scheduler-two"]);
        probe.DisposeCallCount.ShouldBe(1);
    }

    [Fact]
    public void Process_scheduler_volume_probe_disposal_retries_when_the_first_wait_is_false_with_bounded_waits()
    {
        using var process = new Process();
        int killCount = 0;
        int waitCount = 0;
        bool exited = false;
        var waitDurations = new List<int>();
        var probe = new ProcessSchedulerVolumeProbe(
            process,
            () => exited,
            () => killCount++,
            milliseconds =>
            {
                waitDurations.Add(milliseconds);
                waitCount++;
                exited = waitCount >= 2;
                return exited;
            },
            static () => { });

        probe.Dispose();

        killCount.ShouldBe(2);
        waitCount.ShouldBe(2);
        waitDurations.ShouldBe([5_000, 5_000]);
        exited.ShouldBeTrue();
    }

    [Fact]
    public void Process_scheduler_volume_probe_classifies_supported_kill_delegate_failures_and_still_disposes()
    {
        foreach (Func<string, Exception> exceptionFactory in new Func<string, Exception>[]
        {
            static message => new AggregateException(new InvalidOperationException(message)),
            static message => new InvalidOperationException(message),
            static message => new Win32Exception(5, message),
            static message => new NotSupportedException(message),
        })
        {
            using var process = new Process();
            int disposeCallCount = 0;
            int killCallCount = 0;
            var waitDurations = new List<int>();
            var probe = new ProcessSchedulerVolumeProbe(
                process,
                static () => false,
                () => throw exceptionFactory($"kill {++killCallCount} failed"),
                milliseconds =>
                {
                    waitDurations.Add(milliseconds);
                    return false;
                },
                () => disposeCallCount++);

            SchedulerVolumeProbeCleanupException exception
                = Should.Throw<SchedulerVolumeProbeCleanupException>(probe.Dispose);

            killCallCount.ShouldBe(2);
            waitDurations.ShouldBe([5_000, 5_000]);
            disposeCallCount.ShouldBe(1);
            exception.ToString().ShouldContain("kill 1 failed");
            exception.ToString().ShouldContain("kill 2 failed");
        }
    }

    [Fact]
    public void Process_scheduler_volume_probe_classifies_supported_wait_delegate_failures_and_still_disposes()
    {
        foreach (Func<string, Exception> exceptionFactory in new Func<string, Exception>[]
        {
            static message => new AggregateException(new InvalidOperationException(message)),
            static message => new InvalidOperationException(message),
            static message => new Win32Exception(5, message),
            static message => new NotSupportedException(message),
        })
        {
            using var process = new Process();
            int disposeCallCount = 0;
            int killCallCount = 0;
            var waitDurations = new List<int>();
            var probe = new ProcessSchedulerVolumeProbe(
                process,
                static () => false,
                () => killCallCount++,
                milliseconds =>
                {
                    waitDurations.Add(milliseconds);
                    throw exceptionFactory($"wait {waitDurations.Count} failed");
                },
                () => disposeCallCount++);

            SchedulerVolumeProbeCleanupException exception
                = Should.Throw<SchedulerVolumeProbeCleanupException>(probe.Dispose);

            killCallCount.ShouldBe(2);
            waitDurations.ShouldBe([5_000, 5_000]);
            disposeCallCount.ShouldBe(1);
            exception.ToString().ShouldContain("wait 1 failed");
            exception.ToString().ShouldContain("wait 2 failed");
        }
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
        var cleanupHandle = cleanupProcess.SafeHandle;
        Exception? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            process.HasExited.ShouldBeFalse();

            var probe = new ProcessSchedulerVolumeProbe(process);
            probe.Dispose();

            await cleanupProcess.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            cleanupProcess.HasExited.ShouldBeTrue();
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            process.Dispose();
            try
            {
                // Always route the retained handle through the same injectable, two-attempt adapter whose
                // false-first-wait, bounded duration, exception classification, and exhaustion paths are pinned
                // above. The closed-handle assertion makes this real-child fact exercise the fail-safe call even
                // when the child already exited after the primary adapter disposed it.
                var cleanupProbe = new ProcessSchedulerVolumeProbe(cleanupProcess);
                cleanupProbe.Dispose();
                cleanupHandle.IsClosed.ShouldBeTrue();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }

        if (primaryFailure is not null)
        {
            if (cleanupFailure is not null)
            {
                TestContext.Current.SendDiagnosticMessage(
                    "Real-child fail-safe cleanup also failed after the primary test failure: {0}",
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }
}
