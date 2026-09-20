using System.Text.Json;

using Hexalith.EventStore.Admin.Abstractions.Models.Common;
using Hexalith.EventStore.Admin.Abstractions.Models.DeadLetters;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Protects the public EventStore Admin response consumed by Works operators.</summary>
public sealed class EventStoreAdminResponseContractTests
{
    /// <summary>Verifies retained command bytes and their hash never enter the web-default JSON response.</summary>
    [Fact]
    public void DeadLetterPageOmitsRetainedBodyMaterialFromTheWebJsonShape()
    {
        var entry = new DeadLetterEntry(
            "01JWORKS000000000000000001",
            "tenant-operations",
            "work",
            "work-item-operations",
            "01JWORKS000000000000000002",
            "redacted failure",
            DateTimeOffset.Parse("2026-09-20T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            2,
            "Hexalith.Works.Contracts.Commands.CreateWorkItem");
        var page = new PagedResult<DeadLetterEntry>([entry], 1, "next-page");

        JsonElement json = JsonSerializer.SerializeToElement(
            page,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string[] propertyNames = [.. EnumeratePropertyNames(json).Select(static name => name.ToUpperInvariant())];

        propertyNames.ShouldContain("ITEMS");
        propertyNames.ShouldContain("MESSAGEID");
        propertyNames.ShouldNotContain("BODY");
        propertyNames.ShouldNotContain("BODYSHA256");
    }

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (string descendant in EnumeratePropertyNames(property.Value))
                {
                    yield return descendant;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach (string descendant in EnumeratePropertyNames(item))
                {
                    yield return descendant;
                }
            }
        }
    }
}
