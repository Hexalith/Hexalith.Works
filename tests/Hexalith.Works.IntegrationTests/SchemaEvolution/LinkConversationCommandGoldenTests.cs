using System.IO;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Serialization;
using Hexalith.Works.Contracts.Commands;

using Shouldly;

namespace Hexalith.Works.IntegrationTests.SchemaEvolution;

/// <summary>Freezes the exact options-free JSON shape of the additive LinkConversation command.</summary>
public sealed class LinkConversationCommandGoldenTests
{
    private static readonly string GoldenPath = Path.Combine(
        AppContext.BaseDirectory,
        "SchemaEvolution",
        "CommandGolden",
        "LinkConversation.v1.json");

    [Fact]
    public async Task Exact_fixture_matches_the_link_conversation_command_bytes_and_shared_reader()
    {
        File.Exists(GoldenPath).ShouldBeTrue(GoldenPath);
        byte[] fixture = await File.ReadAllBytesAsync(GoldenPath, TestContext.Current.CancellationToken);
        fixture.Length.ShouldBeGreaterThan(3);
        (fixture[0] == 0xEF && fixture[1] == 0xBB && fixture[2] == 0xBF).ShouldBeFalse();
        fixture[^1].ShouldNotBe((byte)'\n');
        fixture[^1].ShouldNotBe((byte)'\r');

        LinkConversation expected = WorkItemV1Catalog.All.OfType<LinkConversation>().ShouldHaveSingleItem();
        JsonSerializer.SerializeToUtf8Bytes(expected, expected.GetType()).ShouldBe(fixture);
        JsonSerializer.Deserialize<LinkConversation>(fixture, EventStorePayloadSerialization.Options).ShouldBe(expected);

        byte[] camelCase = JsonSerializer.SerializeToUtf8Bytes(
            expected,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Encoding.UTF8.GetString(camelCase).ShouldContain("\"tenantId\"", Case.Sensitive);
        JsonSerializer.Deserialize<LinkConversation>(camelCase, EventStorePayloadSerialization.Options).ShouldBe(expected);

        string json = Encoding.UTF8.GetString(fixture);
        json.ShouldNotContain("$type", Case.Sensitive);
        json.ShouldNotContain("message", Case.Insensitive);
        json.ShouldNotContain("participant", Case.Insensitive);
    }
}
