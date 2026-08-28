using System.IO;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Identity;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.EventStore.Contracts.Serialization;
using Hexalith.EventStore.Server.Events;
using Hexalith.EventStore.Testing.Fakes;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests.SchemaEvolution;

/// <summary>
/// Freezes the exact options-free concrete JSON bytes written by the pinned EventStore persister for every
/// durable Works v1 payload. This corpus is separate from the camelCase Web-reader compatibility history.
/// </summary>
public sealed class EventPersisterGoldenCorpusTests
{
    private const string AggregateType = "work-item";
    private const string Domain = "work";
    private const string DomainServiceVersion = "v1";

    private static readonly string GoldenDirectory =
        Path.Combine(AppContext.BaseDirectory, "SchemaEvolution", "EventPersisterGolden");

    [Fact]
    public void ExactCorpusMembershipMatchesEveryFrozenV1EventPayloadBidirectionally()
    {
        IReadOnlyDictionary<string, IEventPayload> catalog = CatalogByFileName();
        string[] fixtures = Directory
            .GetFiles(GoldenDirectory, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(GoldenDirectory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        catalog.Count.ShouldBe(23, "The frozen v1 catalog must contain 14 success and 9 rejection event payloads.");
        fixtures.ShouldBe(
            catalog.Keys.Order(StringComparer.Ordinal),
            ignoreOrder: false,
            customMessage: "Exact-corpus filenames must match the frozen v1 event catalog in both directions.");
    }

    [Fact]
    public async Task EveryExactFixtureEqualsTheRealEventPersisterPayloadBytes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        foreach ((string fileName, IEventPayload payload) in CatalogByFileName())
        {
            string path = Path.Combine(GoldenDirectory, fileName);
            File.Exists(path).ShouldBeTrue(path);
            byte[] fixture = await File.ReadAllBytesAsync(path, cancellationToken);

            fixture.Length.ShouldBeGreaterThan(3, $"{fileName} must not be empty.");

            // Compared byte by byte rather than as a sequence: a sequence comparison here would depend on
            // which Shouldly overload binds for IEnumerable<byte>, and a reference comparison can never
            // fail, which would silently disarm the BOM gate.
            (fixture[0] == 0xEF && fixture[1] == 0xBB && fixture[2] == 0xBF).ShouldBeFalse(
                $"{fileName} must not contain a UTF-8 BOM.");
            fixture[^1].ShouldNotBe((byte)'\n', $"{fileName} must not end with a newline.");
            fixture[^1].ShouldNotBe((byte)'\r', $"{fileName} must not end with a carriage return.");
            Encoding.UTF8.GetString(fixture).ShouldNotContain("$type", Case.Sensitive);

            using (JsonDocument document = JsonDocument.Parse(fixture))
            {
                AssertPascalCaseProperties(document.RootElement, fileName);
            }

            JsonSerializer
                .Deserialize(fixture, payload.GetType(), EventStorePayloadSerialization.Options)
                .ShouldBe(payload);

            var stateManager = new InMemoryStateManager();
            var persister = new EventPersister(
                stateManager,
                Substitute.For<ILogger<EventPersister>>(),
                new NoOpEventPayloadProtectionService());
            var identity = new AggregateIdentity(WorkItemV1Catalog.Tenant.Value, Domain, WorkItemV1Catalog.Item.Value);
            EventPersistResult result = await persister.PersistEventsAsync(
                identity,
                AggregateType,
                CommandFor(payload.GetType()),
                new DomainResult([payload]),
                DomainServiceVersion,
                cancellationToken);

            result.PersistedEnvelopes.ShouldHaveSingleItem().Payload.ShouldBe(
                fixture,
                $"{fileName} drifted from the raw options-free bytes written by EventPersister.");
            JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()).ShouldBe(fixture);
        }
    }

    private static IReadOnlyDictionary<string, IEventPayload> CatalogByFileName()
    {
        IEventPayload[] payloads = [.. WorkItemV1Catalog.All.OfType<IEventPayload>()];

        // Assert before ToDictionary: a duplicated catalog sample would otherwise throw an opaque duplicate-key
        // ArgumentException instead of naming the type that broke one-fixture-per-event-type.
        payloads
            .Select(static payload => payload.GetType())
            .Distinct()
            .Count()
            .ShouldBe(
                payloads.Length,
                "Each frozen v1 event type must appear exactly once in the catalog: " +
                string.Join(", ", payloads.Select(static payload => payload.GetType().Name).Order(StringComparer.Ordinal)));

        return payloads.ToDictionary(
            static payload => $"{payload.GetType().Name}.v1.json",
            static payload => payload,
            StringComparer.Ordinal);
    }

    private static CommandEnvelope CommandFor(Type payloadType)
        => new(
            MessageId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            TenantId: WorkItemV1Catalog.Tenant.Value,
            Domain: Domain,
            AggregateId: WorkItemV1Catalog.Item.Value,
            CommandType: payloadType.FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(new { PayloadType = payloadType.Name }),
            CorrelationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            CausationId: null,
            UserId: "golden-corpus-proof",
            Extensions: null);

    private static void AssertPascalCaseProperties(JsonElement element, string fileName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                char.IsUpper(property.Name[0]).ShouldBeTrue(
                    $"{fileName} property '{property.Name}' must use the options-free PascalCase writer form.");
                AssertPascalCaseProperties(property.Value, fileName);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                AssertPascalCaseProperties(item, fileName);
            }
        }
    }
}
