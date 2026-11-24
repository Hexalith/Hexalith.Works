using System.Security.Cryptography;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Target-host selection never silently substitutes the retained rollback AppHost.</summary>
public sealed class WorksAppHostSelectionTests
{
    /// <summary>Unconfigured callers retain their original control-plane ports.</summary>
    [Fact]
    public void DefaultControlPlanePortsArePreserved()
        => WorksAppHostSelection.ResolveControlPlanePorts(null, null, null).ShouldBe((50001, 51005, 51006));

    /// <summary>A complete isolated selection remains exact rather than being normalized.</summary>
    [Fact]
    public void ExplicitControlPlanePortsAreSelected()
        => WorksAppHostSelection.ResolveControlPlanePorts("61001", "61005", "61006").ShouldBe((61001, 61005, 61006));

    /// <summary>Partial, ambiguous, privileged and overlapping selections refuse before topology startup.</summary>
    [Theory]
    [InlineData(null, "61005", "61006")]
    [InlineData("", "61005", "61006")]
    [InlineData(" 61001", "61005", "61006")]
    [InlineData("+61001", "61005", "61006")]
    [InlineData("1023", "61005", "61006")]
    [InlineData("65536", "61005", "61006")]
    [InlineData("65535", "61005", "61006")]
    [InlineData("61001", "65535", "61006")]
    [InlineData("61001", "61005", "65535")]
    [InlineData("61001", "61001", "61006")]
    [InlineData("61001", "61005", "61001")]
    [InlineData("61001", "61005", "61005")]
    public void UnsafeControlPlanePortSelectionIsRefused(string? sentry, string? placement, string? scheduler)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveControlPlanePorts(sentry, placement, scheduler));

    /// <summary>An unselected lane refuses startup because the live host belongs to Platform.</summary>
    [Fact]
    public void UnselectedLaneRequiresPlatformHost()
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(null, null, null));

    /// <summary>Platform parity and standalone measurements are the only opt-in integration tests.</summary>
    [Fact]
    public void LiveFactsRequireExplicitSelection()
    {
        (Type TestClass, string Method)[] platformParity =
        [
            (typeof(WorksSdkRecoveryPipelineSmokeTests), nameof(WorksSdkRecoveryPipelineSmokeTests.SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery)),
            (typeof(WorksSdkRecoveryPipelineSmokeTests), nameof(WorksSdkRecoveryPipelineSmokeTests.SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart)),
            (typeof(WorksPlatformHistoricalPipelineSmokeTests), nameof(WorksPlatformHistoricalPipelineSmokeTests.PlatformLifecycleTreeClaimFilteringAndSecuritySurviveRestart)),
            (typeof(WorksPlatformHaPipelineSmokeTests), nameof(WorksPlatformHaPipelineSmokeTests.TwoWorksHostsRecoverLostFiringAfterPrimaryStopsWithoutDuplicateEffect)),
            (typeof(WorksPlatformProjectionPipelineSmokeTests), nameof(WorksPlatformProjectionPipelineSmokeTests.ActualPlatformSharedRebuildAndConcurrentCatchUpPreserveCompleteFamily)),
            (typeof(WorksMtlsAuthorizationSmokeTests), nameof(WorksMtlsAuthorizationSmokeTests.Mtls_allows_event_store_and_denies_an_unauthorized_caller_at_works_process)),
            (typeof(WorksPlatformRetainedHostPreflightTests), nameof(WorksPlatformRetainedHostPreflightTests.ArchivedTopologyReachesHealthyReadiness)),
            (typeof(WorksPlatformRollbackBinaryTests), nameof(WorksPlatformRollbackBinaryTests.ActualSelectedRuntimeBinariesCaptureWithoutMutation)),
            (typeof(WorksPlatformTelemetryAdmissionTests), nameof(WorksPlatformTelemetryAdmissionTests.ActualPreviewComposesExporterKeysAndRefusesForeignFixtureTelemetry)),
        ];
        (Type TestClass, string Method)[] standaloneMeasurements =
        [
            (typeof(WorksProjectionDeliveryMeasurementTests), nameof(WorksProjectionDeliveryMeasurementTests.OrdinaryDeliveryMeasuresRealStoreCostNearHistoryLimits)),
            (typeof(WorksRecoveryCatalogMeasurementTests), nameof(WorksRecoveryCatalogMeasurementTests.CatalogSweepMeasuresRealStoreCost)),
            (typeof(WorksQueueMeasurementTests), nameof(WorksQueueMeasurementTests.WhatsNextMeasuresRetainedTerminalMembershipCost)),
            (typeof(WorksQueueMeasurementTests), nameof(WorksQueueMeasurementTests.ShardedProjectionReadsMeasureDetailAndRecoveryDeadlines)),
            (typeof(WorksQueueMeasurementTests), nameof(WorksQueueMeasurementTests.WhatsNextMeasuresUnpagedEligibleResponses)),
            (typeof(WorksRealStoreRebuildAbortTests), nameof(WorksRealStoreRebuildAbortTests.AbortedRebuildUnderConcurrentDeliveryKeepsTheServedGenerationOnTheRealStore)),
        ];
        var explicitTests = typeof(WorksAppHostSelectionTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods()
                .Where(method => method.DeclaringType == type)
                .Where(method => method.GetCustomAttributes(typeof(FactAttribute), inherit: false)
                    .Cast<FactAttribute>().Any(attribute => attribute.Explicit))
                .Select(method => (type, method.Name)));
        explicitTests.ShouldBe(platformParity.Concat(standaloneMeasurements), ignoreOrder: true);
    }

    /// <summary>A selected proof cannot reach the shared default providers without its owned SDK scope.</summary>
    [Theory]
    [InlineData(false, "works-sdk416-owned", "/owned/certificates")]
    [InlineData(true, null, "/owned/certificates")]
    [InlineData(true, "foreign-volume", "/owned/certificates")]
    [InlineData(true, "works-sdk416-owned", null)]
    [InlineData(true, "works-sdk416-owned", "relative/certificates")]
    public void MissingOwnedSdkFixtureIsRefusedBeforeStartup(bool configured, string? volume, string? certificates)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSmokeHarness.RequireOwnedPlatformFixture(true, configured, volume, certificates));

    /// <summary>The complete owned scope is accepted before the resource model is built.</summary>
    [Fact]
    public void OwnedSdkFixtureIsAccepted()
        => WorksAppHostSmokeHarness.RequireOwnedPlatformFixture(true, true, "works-sdk416-owned", Path.GetFullPath("owned-certificates"));

    /// <summary>The actual startup entry refuses its missing owner profile before SDK selection or composition.</summary>
    [Fact]
    public async Task MissingCatalogProfileIsRefusedBeforeCompositionInAnIsolatedProcessAsync()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using WorksIsolatedFixtureProcess fixture = await WorksIsolatedFixtureProcess.CreateAsync(token).ConfigureAwait(true);
        var start = fixture.StartInfo("missing-catalog-profile");
        start.Environment["WORKS_PLATFORM_APPHOST_ASSEMBLY"] = Path.Combine(fixture.DirectoryPath, "absent-apphost.dll");
        var result = await WorksIsolatedFixtureProcess.RunAsync(start, token).ConfigureAwait(true);
        result.ExitCode.ShouldBe(0, result.Output);
        result.Output.ShouldContain("missing-profile-refused-before-composition");
    }

    /// <summary>An explicit but absent artifact is a hard failure.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("apphost.dll")]
    public void InvalidArtifactPathCannotFallBack(string path)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(path, "hash", "/tmp"));

    /// <summary>The recorded artifact hash is mandatory even for an existing assembly.</summary>
    [Fact]
    public void MissingHashCannotFallBack()
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(typeof(WorksAppHostSelectionTests).Assembly.Location, null, "/tmp"));

    /// <summary>A changed assembly cannot execute under the verifier's earlier provenance.</summary>
    [Fact]
    public void ChangedArtifactIsRefused()
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(typeof(WorksAppHostSelectionTests).Assembly.Location, new string('0', 64), "/tmp"));

    /// <summary>A correct hash does not turn a test or old-host assembly into the Platform file entry point.</summary>
    [Fact]
    public void NonPlatformAssemblyIsRefusedEvenWithItsCorrectHash()
    {
        string path = typeof(WorksAppHostSelectionTests).Assembly.Location;
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(path, hash, "/tmp"));
    }
}
