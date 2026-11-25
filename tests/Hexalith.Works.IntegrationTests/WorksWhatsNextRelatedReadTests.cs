using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Parties.Client;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.Queries;
using Hexalith.Parties.Contracts.ValueObjects;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>The what's-next query reads related documents only for its candidates and SDK-recorded parking evidence.</summary>
public sealed class WorksWhatsNextRelatedReadTests
{
    private const string Tenant = "related-read";
    private const string Coordinator = "scheduler";
    private const string Actor = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private static readonly string[] s_queued = ["owner-q0", "owner-q1", "owner-q2"];
    private static readonly string[] s_terminal = [.. Enumerable.Range(0, 64).Select(ordinal => FormattableString.Invariant($"owner-t{ordinal:D2}"))];
    private static readonly string[] s_ineligible = [.. s_terminal, "owner-created", "owner-other"];

    /// <summary>Retained terminal and other ineligible members cost no parking or roll-up reads for any caller.</summary>
    /// <param name="executor">Whether the caller is a delegated Executor instead of a permitted coordinator.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IneligibleMembersCostNoRelatedReads(bool executor)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var inner = new InMemoryReadModelStore();
        (SharedProjectionScope scope, long generation) = await SeedAsync(inner, ct);
        var recording = new RecordingReadModelStore(inner);

        QueryResult result = await QueryAsync(new SharedProjectionEpochCoordinator(recording, inner), executor, ct);

        // A coordinator sees every eligible item; an Executor sees queued work and its own assignment only.
        string[] expected = executor ? ["owner-mine", .. s_queued] : ["owner-mine", "owner-other", .. s_queued];
        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        result.GetPayload().Deserialize<WhatsNextItem[]>(JsonSerializerOptions.Web).ShouldNotBeNull()
            .Select(item => item.WorkItemId.Value).Order(StringComparer.Ordinal).ShouldBe(expected.Order(StringComparer.Ordinal));
        string[] relatedReads = [.. RelatedKeys(scope, generation, s_queued.Concat(s_ineligible).Append("owner-mine"))
            .Where(recording.Keys.Contains).Order(StringComparer.Ordinal)];
        // Only the returned candidates' parking and roll-up documents are read. A generation-stable
        // ReadMany retry rereads that whole set together, so hits are a positive multiple of the set.
        relatedReads.ShouldBe([.. RelatedKeys(scope, generation, expected).Order(StringComparer.Ordinal)]);
        int relatedHits = recording.Keys.Count(relatedReads.Contains);
        relatedHits.ShouldBeGreaterThanOrEqualTo(relatedReads.Length);
        (relatedHits % relatedReads.Length).ShouldBe(0);
    }

    /// <summary>
    /// Candidate documents still refuse or degrade the queue; an ineligible member's documents no longer affect it.
    /// </summary>
    /// <param name="scenario">The document change applied to the selected generation.</param>
    /// <param name="outcome">The expected lifecycle, and for success the returned queued items.</param>
    [Theory]
    [InlineData("candidate-rollup-corrupt", "unavailable")]
    [InlineData("candidate-rollup-mismatched", "unavailable")]
    [InlineData("candidate-parking-corrupt", "unavailable")]
    [InlineData("candidate-parked", "degraded:owner-q0,owner-q2")]
    [InlineData("terminal-rollup-corrupt", "current:owner-q0,owner-q1,owner-q2")]
    [InlineData("terminal-parking-corrupt", "current:owner-q0,owner-q1,owner-q2")]
    public async Task OnlyCandidateDocumentsCanRefuseOrDegradeTheQueue(string scenario, string outcome)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(outcome);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var inner = new InMemoryReadModelStore();
        (SharedProjectionScope scope, long generation) = await SeedAsync(inner, ct);
        string owner = scenario.StartsWith("candidate", StringComparison.Ordinal) ? s_queued[1] : s_terminal[0];
        var corrupt = new WorksRuntimeDocument("corrupt", JsonSerializer.SerializeToElement(new { corrupt = true }));
        (string key, WorksRuntimeDocument document) = scenario switch
        {
            "candidate-rollup-mismatched" => (WorksReadModelKeys.RuntimeRollUpKey(owner), WorksRuntimeDocument.From(new WorkItemRollUp(
                new TenantId(Tenant), new WorkItemId(owner), WorkItemStatus.Cancelled, null, null, null, [], [], 2))),
            "candidate-parked" => (WorksReadModelKeys.RuntimeParkingKey(owner), WorksRuntimeDocument.From(new WorkItemProjectionParking { Parked = true })),
            _ when scenario.EndsWith("parking-corrupt", StringComparison.Ordinal) => (WorksReadModelKeys.RuntimeParkingKey(owner), corrupt),
            _ => (WorksReadModelKeys.RuntimeRollUpKey(owner), corrupt),
        };
        await inner.SaveAsync(WorksReadModelKeys.StateStoreName, Physical(scope, generation, key), document, ct);

        QueryResult result = await QueryAsync(new SharedProjectionEpochCoordinator(inner, inner), executor: false, ct);

        QueryResponseMetadata metadata = result.Metadata.ShouldNotBeNull();
        if (outcome == "unavailable")
        {
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldBe("projection-unavailable");
            metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
            return;
        }

        result.Success.ShouldBeTrue(result.ErrorMessage);
        string[] parts = outcome.Split(':');
        metadata.Lifecycle.ShouldBe(parts[0] == "degraded" ? ProjectionLifecycleState.Degraded : ProjectionLifecycleState.Current);
        if (parts[0] == "degraded") metadata.WarningCodes.ShouldNotBeNull().ShouldContain("works-runtime-parked");
        result.GetPayload().Deserialize<WhatsNextItem[]>(JsonSerializerOptions.Web).ShouldNotBeNull()
            .Select(item => item.WorkItemId.Value).Where(id => id.StartsWith("owner-q", StringComparison.Ordinal))
            .ShouldBe(parts[1].Split(','));
    }

    private static async Task<QueryResult> QueryAsync(SharedProjectionEpochCoordinator coordinator, bool executor, CancellationToken ct)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        parties.ResolvePartyIdentityAsync(Tenant, Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>()).Returns(Identity());
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = [Coordinator] }));
        QueryEnvelope query = executor
            ? new QueryEnvelope(Tenant, "work", s_queued[0], WhatsNextQueryHandler.WhatsNextQueryType,
                JsonSerializer.SerializeToUtf8Bytes(new { partyId = "party-mine" }), "related-read", Actor, null, true, null, Actor, "eventstore", true, null, null)
            : new QueryEnvelope(Tenant, "work", s_queued[0], WhatsNextQueryHandler.WhatsNextQueryType,
                [], "related-read", Coordinator, null, false, null, null, Coordinator, false, null, null);
        return await new WhatsNextQueryHandler(policy, coordinator).ExecuteAsync(query, ct).ConfigureAwait(false);
    }

    private static PartyIdentityResult Identity()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var custody = new IdentityHistoryCustodyEvidence("policy", "party-actor-history-v1", now.AddDays(1), 1, "custody", true, true, true);
        var binding = new HumanActorBindingEvidence(Tenant, "party-mine", Actor, 1, 1, now.AddMinutes(-1), now.AddHours(1), "source", "provenance", custody);
        return new PartyIdentityResult(PartyIdentityOutcome.Resolved,
            new PartyIdentityEvidence(1, Tenant, "party-mine", PartyIdentityClassification.Human, true, false, false, 2, now, "observation", binding));
    }

    private static async Task<(SharedProjectionScope Scope, long Generation)> SeedAsync(InMemoryReadModelStore store, CancellationToken ct)
    {
        const string writer = "works-runtime-v3-writer";
        var scope = new SharedProjectionScope(WorksReadModelKeys.StateStoreName, Tenant, "work", WorksReadModelKeys.RuntimeFamily, [writer]);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        _ = await coordinator.RegisterWriterAsync(scope, writer, ct).ConfigureAwait(false);
        var tenantId = new TenantId(Tenant);
        var histories = new List<WorkItemSharedRebuildAggregateHistory>();
        foreach (string owner in s_queued.Concat(s_ineligible).Append("owner-mine"))
        {
            var item = new WorkItemId(owner);
            var events = new List<ProjectionEventDto> { Event(new WorkItemCreated(owner, 1, tenantId, item, new Obligation("Related read")), 1) };
            if (s_queued.Contains(owner)) events.Add(Event(new WorkItemQueued(owner, 2, tenantId, item), 2));
            else if (s_terminal.Contains(owner)) events.Add(Event(new WorkItemCancelled(owner, 2, tenantId, item), 2));
            else if (owner is "owner-mine" or "owner-other")
                events.Add(Event(new WorkItemAssigned(owner, 2, tenantId, item, new ExecutorBinding(
                    new PartyId(owner == "owner-mine" ? "party-mine" : "party-other"), Channel.Cli, AuthorityLevel.Contribute)), 2));
            histories.Add(new WorkItemSharedRebuildAggregateHistory(owner, [.. events]));
        }

        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily, "related-read", "related-read-inventory");
        var plan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, new WorkItemSharedRebuildCandidateState(histories), ct);
        _ = await coordinator.BeginAsync(scope, identity.OperationId, identity.CatalogFingerprint,
            histories.ToDictionary(history => history.AggregateId, history => history.Events[^1].GlobalPosition), ct).ConfigureAwait(false);
        _ = await coordinator.StageAsync(scope, identity.OperationId, plan.Operations, ct,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(false);
        await coordinator.CommitAsync(scope, identity.OperationId, ct).ConfigureAwait(false);
        return (scope, (await coordinator.GetStatusAsync(scope, ct).ConfigureAwait(false)).ActiveGeneration);

        static ProjectionEventDto Event(IEventPayload payload, long sequence)
            => new(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
                "json", sequence, DateTimeOffset.UnixEpoch, "related-read", GlobalPosition: sequence);
    }

    private static IEnumerable<string> RelatedKeys(SharedProjectionScope scope, long generation, IEnumerable<string> owners)
        => owners.SelectMany(owner => new[]
        {
            Physical(scope, generation, WorksReadModelKeys.RuntimeParkingKey(owner)),
            Physical(scope, generation, WorksReadModelKeys.RuntimeRollUpKey(owner)),
        });

    private static string Physical(SharedProjectionScope scope, long generation, string key)
        => FormattableString.Invariant($"shared-generation:{scope.ComputeHash()}:{generation}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}");

    /// <summary>Records every physical key the query reads.</summary>
    private sealed class RecordingReadModelStore(InMemoryReadModelStore inner) : IReadModelStore
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _keys = new();

        internal IReadOnlyCollection<string> Keys => _keys;

        public Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
        {
            _keys.Enqueue(key);
            return inner.GetAsync<T>(storeName, key, cancellationToken);
        }

        public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
            => inner.SaveAsync(storeName, key, value, cancellationToken);

        public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
            => inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
    }
}
