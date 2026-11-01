using System.Security.Cryptography;

using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Works.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksFleetQuiescenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyFencesDoNotRaceBootstrapCommitOrConsultAStaleProof(bool prepared)
    {
        var (_, _, state) = Fixture();
        var envelope = RouteCatalogCodec.Decode(state.Committed!.CanonicalCatalog);
        envelope = RouteCatalogCodec.Seal(envelope with { Generation = 2, Routes = envelope.Routes.Select(route => route with { Generation = 2 }).ToArray() });
        var next = state.Committed with { AttemptId = "next", CanonicalCatalog = RouteCatalogCodec.Encode(envelope),
            RootDigest = RouteCatalogCodec.ComputeRootDigest(envelope), PredecessorRootDigest = state.Committed.RootDigest };
        next = next with { TrustProof = RouteCatalogActivationBinding.ComputeDigest(next) };
        var before = state with { Pending = prepared ? next : null, Leases = [] };
        var after = before with { Committed = next, Pending = null, Epoch = 2 };
        var store = Substitute.For<IRouteCatalogStateStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns((before, "before"), (after, "after"));
        var authority = Substitute.For<IRouteCatalogAuthority>();
        string directory = Path.Combine(Path.GetTempPath(), "works-stopped-test-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, WorksFleetQuiescence.ProofName), "deliberately invalid stale proof", TestContext.Current.CancellationToken).ConfigureAwait(true);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { [WorksFleetQuiescence.OwnerEnvironment] = Path.Combine(directory, WorksFleetQuiescence.OwnerName) }).Build();
            using var service = new WorksStoppedFleetMaintenanceService(configuration, store, authority,
                Options.Create(new RouteCatalogHostOptions { Identity = next.RequiredHosts[0] }), new WorksStoppedFleetVerifier(configuration, store));
            await service.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken).ConfigureAwait(true);
            await store.Received(1).ReadAsync(Arg.Any<CancellationToken>()).ConfigureAwait(true);
            await authority.DidNotReceive().ReconcileAsync(Arg.Any<RouteCatalogQuiescenceRequest>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task StopPollingRequiresObservedDisappearanceAndCancellationNeverProvesIt()
    {
        var (_, document, _) = Fixture();
        int observations = 0;
        await WorksFleetQuiescence.WaitStoppedAsync(document.Scope, document.Scope.Boot, document.Scope.PidNamespace,
            () => ++observations < 3 ? document.Scope.Processes.ToDictionary(process => process.Pid) : new Dictionary<int, WorksFleetProcess>(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        observations.ShouldBe(3);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Should.ThrowAsync<OperationCanceledException>(() => WorksFleetQuiescence.WaitStoppedAsync(document.Scope,
            document.Scope.Boot, document.Scope.PidNamespace, () => document.Scope.Processes.ToDictionary(process => process.Pid), deadline.Token)).ConfigureAwait(true);
        document.Fences.Length.ShouldBe(2);
    }

    [Fact]
    public void OnlyConcreteDisappearanceCanIgnoreAProcessReadFailure()
    {
        WorksFleetQuiescence.Disappeared(() => throw new FileNotFoundException()).ShouldBeTrue();
        WorksFleetQuiescence.Disappeared(() => throw new DirectoryNotFoundException()).ShouldBeTrue();
        WorksFleetQuiescence.Disappeared(() => FileAttributes.Directory).ShouldBeFalse();
        Should.Throw<IOException>(() => WorksFleetQuiescence.Disappeared(() => throw new IOException("unknown")));
        Should.Throw<UnauthorizedAccessException>(() => WorksFleetQuiescence.Disappeared(() => throw new UnauthorizedAccessException()));
    }

    [Fact]
    public void OriginalSignedRosterAllowsSequentialExactRemovals()
    {
        var (owner, document, state) = Fixture();
        WorksFleetQuiescence.ValidateDocument(owner, document, state);
        string secondProof = WorksFleetQuiescence.FenceProof(owner, document, state.Leases[1]);
        state = state with { Leases = [state.Leases[1]] };
        WorksFleetQuiescence.ValidateDocument(owner, document, state);
        WorksFleetQuiescence.FenceProof(owner, document, state.Leases[0]).ShouldBe(secondProof);
        WorksFleetQuiescence.ValidateDocument(owner, document, state with { Leases = [] });
        document.Fences.Length.ShouldBe(2);
    }

    [Theory]
    [InlineData("new-fence")]
    [InlineData("replaced-fence")]
    [InlineData("foreign-owner")]
    [InlineData("altered-roster")]
    [InlineData("altered-signature")]
    [InlineData("wrong-epoch")]
    [InlineData("changed-committed")]
    public void ChangedAuthorityOrProofCannotUseOldOwnerRoster(string fault)
    {
        var (owner, document, state) = Fixture();
        if (fault == "new-fence") state = state with { Leases = [.. state.Leases, state.Leases[0] with { LeaseId = "new" }] };
        if (fault == "replaced-fence") state = state with { Leases = [state.Leases[0] with { Sequence = 99 }] };
        if (fault == "foreign-owner") owner = owner with { OwnerId = Guid.NewGuid().ToString("N") };
        if (fault == "altered-roster") document = document with { Fences = [document.Fences[0]] };
        if (fault == "altered-signature") document = document with { Signature = new string('0', 64) };
        if (fault == "wrong-epoch") state = state with { Epoch = state.Epoch + 1 };
        if (fault == "changed-committed") state = state with { Committed = state.Committed! with { Owner = "changed-owner" } };
        Should.Throw<RouteCatalogUnavailableException>(() => WorksFleetQuiescence.ValidateDocument(owner, document, state));
    }

    [Theory]
    [InlineData("old-alive")]
    [InlineData("old-descendant")]
    [InlineData("new-group-member")]
    [InlineData("new-subgroup-same-session")]
    [InlineData("foreign-boot")]
    [InlineData("foreign-namespace")]
    public void EveryOldGroupMustBeActuallyStoppedInTheSameKernelNamespace(string fault)
    {
        var (_, document, _) = Fixture();
        var current = new Dictionary<int, WorksFleetProcess>();
        var scope = document.Scope;
        if (fault == "old-alive") current[10] = scope.Processes[0];
        if (fault == "old-descendant") current[11] = scope.Processes[1];
        if (fault == "new-group-member") current[99] = new(99, 99, 1, 10, 10, "R");
        if (fault == "new-subgroup-same-session") current[99] = new(99, 99, 1, 99, 10, "R");
        Should.Throw<RouteCatalogUnavailableException>(() => WorksFleetQuiescence.RequireStopped(scope,
            fault == "foreign-boot" ? "other" : scope.Boot, fault == "foreign-namespace" ? "other" : scope.PidNamespace, current));
    }

    [Fact]
    public void CompletedOldProcessesAndReusedPidOutsideOwnedGroupsAreQuiescent()
    {
        var (_, document, _) = Fixture();
        var scope = document.Scope;
        var current = new Dictionary<int, WorksFleetProcess>
        {
            [10] = scope.Processes[0] with { State = "Z" },
            [11] = scope.Processes[1] with { Started = 999, Group = 99, Session = 99 },
        };
        WorksFleetQuiescence.RequireStopped(scope, scope.Boot, scope.PidNamespace, current);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaptureRefusesOwnerOrMixedRootSessions(bool inherited)
    {
        var processes = new Dictionary<int, WorksFleetProcess>
        {
            [1] = new(1, 1, 0, inherited ? 10 : 1, inherited ? 10 : 1, "S"),
            [10] = new(10, 10, 1, 10, 10, "R"),
            [11] = new(11, 11, 10, 10, 10, "S"),
        };
        if (!inherited) processes[99] = new(99, 99, 0, 99, 99, "S");
        Should.Throw<InvalidOperationException>(() => WorksFleetQuiescence.ClosedProcesses(inherited ? [10] : [10, 99], processes, 1));
    }

    [Fact]
    public void CaptureClosesAllActualDescendantsIncludingTheirOwnGroups()
    {
        var processes = new Dictionary<int, WorksFleetProcess>
        {
            [1] = new(1, 1, 0, 1, 1, "S"),
            [10] = new(10, 10, 1, 10, 10, "R"),
            [11] = new(11, 11, 10, 11, 11, "S"),
            [12] = new(12, 12, 11, 11, 11, "S"),
        };
        WorksFleetQuiescence.ClosedProcesses([10], processes, 1).Select(process => process.Pid).ShouldBe([10, 11, 12]);
    }

    [Fact]
    public void ActualResourceRootsBindTheEntireIndependentlyOwnedDcpSession()
    {
        var processes = new Dictionary<int, WorksFleetProcess>
        {
            [1] = new(1, 1, 0, 1, 1, "S"),
            [2] = new(2, 2, 1, 1, 1, "S"),
            [100] = new(100, 100, 2, 100, 100, "S"),
            [101] = new(101, 101, 100, 100, 100, "S"),
            [102] = new(102, 102, 100, 100, 100, "S"),
            [103] = new(103, 103, 100, 103, 100, "S"),
        };
        WorksFleetQuiescence.ClosedProcesses([101, 102], processes, 1).Select(process => process.Pid).ShouldBe([100, 101, 102, 103]);
        processes[100] = processes[100] with { Parent = 999 }; // Intentional DCP daemon reparenting does not change snapshot ownership.
        WorksFleetQuiescence.ClosedProcesses([101, 102], processes, 1).Select(process => process.Pid).ShouldBe([100, 101, 102, 103]);
        processes.Remove(100);
        Should.Throw<InvalidOperationException>(() => WorksFleetQuiescence.ClosedProcesses([101, 102], processes, 1));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("foreign-backend")]
    [InlineData("foreign-profile")]
    [InlineData("changed-backend")]
    [InlineData("unsigned")]
    [InlineData("old-process-live")]
    [InlineData("new-fence")]
    public void CaptureRetainsFencesOnlyWithExactSignedFreshlyStoppedOwnerProof(string fault)
    {
        var (owner, document, state) = Fixture();
        byte[] backend = [1, 2, 3];
        document = document with { BackendDigest = Convert.ToHexStringLower(SHA256.HashData(backend)) };
        document = document with { Signature = WorksFleetQuiescence.Sign(owner, document) };
        if (fault == "foreign-backend") owner = owner with { Backend = "127.0.0.1:2" };
        if (fault == "changed-backend") backend = [4];
        if (fault == "unsigned") document = document with { Signature = new string('0', 64) };
        if (fault == "new-fence") state = state with { Leases = [.. state.Leases, state.Leases[0] with { LeaseId = "foreign" }] };
        var processes = fault == "old-process-live" ? document.Scope.Processes.ToDictionary(process => process.Pid) : new Dictionary<int, WorksFleetProcess>();
        void Verify() => WorksRetainedCatalogCustody.RequireStoppedFences(owner, document, state, backend, "127.0.0.1:1",
            fault == "foreign-profile" ? "/foreign/profile.json" : "/private/profile.json", document.Scope.Boot, document.Scope.PidNamespace, processes);
        if (fault == "valid") Verify(); else Should.Throw<Exception>(Verify);
        state.Leases.Count.ShouldBe(fault == "new-fence" ? 3 : 2);
    }

    private static (WorksFleetOwner, WorksStoppedFleetDocument, RouteCatalogActivationState) Fixture()
    {
        var declaration = WorksCatalogDeclarations.Routes.First(route => route.Kind == "query");
        var operation = WorksCatalogDeclarations.Operations.Single(item => item.RouteId == declaration.RouteId);
        var envelope = RouteCatalogCodec.Seal(new(3, 1, "Development", false, [declaration],
            [new(declaration.RouteId, 1, RouteCatalogCodec.ComputeDeclarationDigest(declaration), "works", "query", "v1",
                declaration.CredentialKind, declaration.Operation, false, "fixture", operation.Digest)], [], string.Empty, string.Empty, [operation], []));
        string root = RouteCatalogCodec.ComputeRootDigest(envelope), digest = new('a', 64);
        var activation = new RouteCatalogActivation("first", RouteCatalogCodec.Encode(envelope), root, null,
            [new("eventstore", "eventstore", digest, digest, "policy"), new("works", "works", digest, digest, "policy")], "test-owner", "");
        string binding = RouteCatalogActivationBinding.ComputeDigest(activation);
        activation = activation with { TrustProof = binding };
        var fences = new RouteCatalogDispatchFence[] { new(1, "one", "eventstore", root, 1), new(2, "two", "works", root, 1) };
        var state = new RouteCatalogActivationState(1, activation, null, null, [], 2, fences);
        var owner = new WorksFleetOwner(Guid.NewGuid().ToString("N"), "127.0.0.1:1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "/private/profile.json");
        var scope = new WorksFleetScope("boot", "pid:[123]", root, 1, binding, ["eventstore", "works"],
            [new(10, 10, 1, 10, 10, "R"), new(11, 11, 10, 10, 10, "S")], [10]);
        var document = new WorksStoppedFleetDocument(Guid.NewGuid().ToString("N"), owner.OwnerId, scope, fences, digest, "");
        return (owner, document with { Signature = WorksFleetQuiescence.Sign(owner, document) }, state);
    }
}
