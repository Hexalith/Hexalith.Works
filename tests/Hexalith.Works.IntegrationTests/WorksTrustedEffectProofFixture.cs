using System.Text.Json;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Isolated driver controls; its immutable snapshot is not distributed activation evidence.</summary>
internal sealed class WorksTrustedEffectProofFixture
{
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal string Nonce { get; } = Guid.NewGuid().ToString("N");
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal string Session { get; } = Guid.NewGuid().ToString("N");
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal ITrustedEffectSubmitter Submitter { get; } = Substitute.For<ITrustedEffectSubmitter>();
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal IReminderDelegationTokenProvider Delegation { get; } = Substitute.For<IReminderDelegationTokenProvider>();
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal IReadModelStore Store { get; } = Substitute.For<IReadModelStore>();
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal IRouteCatalogAuthority Authority { get; } = Substitute.For<IRouteCatalogAuthority>();
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal Dictionary<string, string?> Values { get; }
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal WorksTrustedEffectProofTarget Target { get; }
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal WorksSdkProofFaultState Fault { get; set; }
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal int Releases { get; private set; }
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal CommittedRouteCatalogSnapshot Snapshot { get; }
    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal RouteCatalogGenerationReadback Generation { get; }

    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal WorksTrustedEffectProofFixture()
    {
        var identity = new EffectIdentity("sdk415-unit", "work", "work-sdk415-child", 4, EffectKindCatalog.ChildCompletionResume,
            "work", "work-sdk415-parent", 0);
        string effect = EffectIdentityCodec.ComputeEffectId(identity), message = EffectIdentityCodec.ComputeMessageId(identity);
        var command = new ResumeWorkItem(new TenantId(identity.Tenant), new WorkItemId(identity.TargetAggregate), AwaitCondition.ChildCompleted(new WorkItemId(identity.SourceAggregate)));
        var submission = new TrustedEffectSubmission(identity, nameof(ResumeWorkItem), JsonSerializer.SerializeToUtf8Bytes(command, JsonSerializerOptions.Web), message, message);
        Target = new(identity.Tenant, identity.SourceAggregate, identity.TargetAggregate, effect);
        Fault = new(false, effect) { Disposition = TrustedEffectDisposition.NoOp, Submission = submission, CausationId = "source-event-4" };
        Values = new()
        {
            [WorksTrustedEffectProofProtocol.SessionKey] = Session, ["WORKS_SDK_PROOF"] = "1", ["WORKS_SDK_PROOF_ROLE"] = "works",
            ["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] = "works", ["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] = "works",
            ["WORKS_PLATFORM_RECOVERY_GRANTS"] = JsonSerializer.Serialize(new[] { new ProcessSource(identity.Tenant, "work", identity.SourceAggregate), new(identity.Tenant, "work", identity.TargetAggregate) }),
        };
        var declaration = WorksIdentityReaderCatalog.Declaration; var operation = WorksIdentityReaderCatalog.Operation;
        var envelope = RouteCatalogCodec.Seal(new(3, 1, "Development", false, [declaration],
            [new(declaration.RouteId, 1, RouteCatalogCodec.ComputeDeclarationDigest(declaration), "parties", "query", "v1",
                declaration.CredentialKind, declaration.Operation, false, "fixture", operation.Digest)], [], string.Empty, string.Empty, [operation], []));
        Snapshot = new(RouteCatalogCodec.Encode(envelope), RouteCatalogCodec.ComputeRootDigest(envelope), 1, localAppId: "works");
        Generation = new(1, 1, Snapshot.RootDigest, envelope.RouteFacetDigest, envelope.IdempotencyFacetDigest, "Development",
            [.. new[] { "eventstore", "eventstore-admin", "eventstore-operations", "works" }.Select(host => new RouteCatalogHostIdentity(host, host, "fixture", "fixture", "fixture"))], true);
        Authority.AcquireAsync(Arg.Any<CancellationToken>()).Returns(_ => new RouteCatalogDispatchLease(Snapshot, () => { Releases++; return Task.CompletedTask; }));
        Authority.ReadbackAsync(Arg.Any<CancellationToken>()).Returns(Generation);
        Store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + identity.Tenant, Arg.Any<CancellationToken>())
            .Returns(_ => new ReadModelEntry<WorksSdkProofFaultState>(Fault, "etag"));
        Delegation.GetDelegationTokenAsync(Arg.Any<ReminderDelegationRequest>(), Arg.Any<CancellationToken>()).Returns("actual-works-delegation");
        Submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>())
            .Returns(new TrustedEffectResult(effect, TrustedEffectDisposition.NoOp, true, null));
    }

    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal WorksProofCallerRequest Request(bool conflict = false) => new(Nonce, Session,
        conflict ? "conflict-child-completion-resume" : "replay-child-completion-resume", JsonSerializer.SerializeToElement(Target), DateTimeOffset.UtcNow.AddSeconds(55));

    /// <summary>Provides the isolated fixture dependency or request used by the driver control.</summary>
    internal WorksTrustedEffectProofService Service()
    {
        var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Development);
        return new(Submitter, Delegation, Store, Authority, new ConfigurationBuilder().AddInMemoryCollection(Values).Build(), environment);
    }
}
