using System.Text.Json;
using System.Text.Json.Nodes;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises the pinned SDK's process sweep through a Dapr state-store client with custom JSON options.</summary>
public sealed class WorksProcessSweepSerializationTests
{
    /// <summary>Matching serializers retain the active lease and cursor, even after creating a new Dapr client.</summary>
    [Fact(Explicit = true)]
    public async Task CustomDaprSerializerPreservesProcessSweepClaimsAcrossClients()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var time = new Story48FixedTimeProvider(DateTimeOffset.UnixEpoch.AddYears(50));
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        ServiceProvider BuildServices(JsonSerializerOptions? serializer = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton(_ => WorksProjectionMeasurementEndpoint.CreateClient(serializer));
            services.AddLogging();
            services.AddWorksDomainAdapters(configuration);
            return services.BuildServiceProvider();
        }
        var snakeCase = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        const string cursor = "test-tenant\0work\0test-item";
        ProcessSweepState written;
        string raw;
        using (ServiceProvider services = BuildServices(snakeCase))
        {
            var store = services.GetRequiredService<IReadModelStore>();
            var sweeps = services.GetRequiredService<ProcessSweepStore>();
            ProcessSweepState claim = (await sweeps.AcquireAsync(ct)).ShouldNotBeNull();
            written = await sweeps.UpdateOwnedAsync(claim, current => current with { CatalogAfterKey = cursor, CandidateOffset = 3 }, ct);
            JsonObject record = (await store.GetAsync<JsonObject>("statestore", ProcessStateKeys.Sweep, ct)).Value.ShouldNotBeNull();
            record["catalog_after_key"].ShouldNotBeNull().GetValue<string>().ShouldBe(cursor);
            record.ContainsKey("catalogAfterKey").ShouldBeFalse();
            raw = record.ToJsonString();
        }

        using (ServiceProvider services = BuildServices())
        {
            var incompatible = services.GetRequiredService<ProcessSweepStore>();
            await Should.ThrowAsync<InvalidOperationException>(() => incompatible.AcquireAsync(ct));
        }

        using ServiceProvider restarted = BuildServices(snakeCase);
        var restartedStore = restarted.GetRequiredService<IReadModelStore>();
        var reopened = restarted.GetRequiredService<ProcessSweepStore>();
        JsonObject retained = (await restartedStore.GetAsync<JsonObject>("statestore", ProcessStateKeys.Sweep, ct)).Value.ShouldNotBeNull();
        retained.ToJsonString().ShouldBe(raw);
        ProcessSweepState read = await reopened.GetAsync(ct);
        read.Owner.ShouldBe(written.Owner);
        read.LeaseUntil.ShouldBe(written.LeaseUntil);
        read.Cycle.ShouldBe(written.Cycle);
        read.Generation.ShouldBe(written.Generation);
        read.Fence.ShouldBe(written.Fence);
        read.CatalogAfterKey.ShouldBe(cursor);
        read.CandidateOffset.ShouldBe(3);
        (await reopened.AcquireAsync(ct)).ShouldBeNull();
        await reopened.ReleaseAsync(read, ct);
        ProcessSweepState resumed = (await reopened.AcquireAsync(ct)).ShouldNotBeNull();
        resumed.Fence.ShouldBe(written.Fence + 1);
        resumed.Owner.ShouldNotBe(written.Owner);
        resumed.Generation.ShouldBe(written.Generation);
        resumed.CatalogAfterKey.ShouldBe(cursor);
        resumed.CandidateOffset.ShouldBe(3);
        await reopened.ReleaseAsync(resumed, ct);
        (await reopened.GetAsync(ct)).Owner.ShouldBeNull();
    }
}
