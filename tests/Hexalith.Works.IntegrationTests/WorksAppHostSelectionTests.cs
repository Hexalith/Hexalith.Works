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

    /// <summary>Only real topology facts need explicit selection; the Platform gate executes each of them.</summary>
    [Fact]
    public void LiveFactsRequireExplicitSelection()
    {
        Type[] types = [typeof(WorksSdkRecoveryPipelineSmokeTests), typeof(WorksPlatformHistoricalPipelineSmokeTests),
            typeof(WorksPlatformHaPipelineSmokeTests), typeof(WorksPlatformProjectionPipelineSmokeTests), typeof(WorksMtlsAuthorizationSmokeTests), typeof(WorksPlatformRetainedHostPreflightTests)];
        string[] live = ["SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery", "SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart",
            "PlatformLifecycleTreeClaimFilteringAndSecuritySurviveRestart", "TwoWorksHostsRecoverLostFiringAfterPrimaryStopsWithoutDuplicateEffect",
            "ActualPlatformSharedRebuildAndConcurrentCatchUpPreserveCompleteFamily", "Mtls_allows_event_store_and_denies_an_unauthorized_caller_at_works_process", "ArchivedTopologyReachesHealthyReadiness"];
        var facts = types.SelectMany(type => type.GetMethods()).Select(method => (method.Name,
            Fact: method.GetCustomAttributes(typeof(FactAttribute), inherit: false).Cast<FactAttribute>().SingleOrDefault()))
            .Where(item => item.Fact is not null).ToArray();
        facts.Where(item => item.Fact!.Explicit).Select(item => item.Name).Order().ShouldBe(live.Order());
        facts.Where(item => !live.Contains(item.Name)).ShouldAllBe(item => !item.Fact!.Explicit);
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
