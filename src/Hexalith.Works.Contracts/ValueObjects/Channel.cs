using System.Text.Json.Serialization;

namespace Hexalith.Works.Contracts.ValueObjects;

/// <summary>
/// Closed v1 catalog of executor channels, serialized by name. This is intentionally a closed
/// enum: an unrecognized channel string fails deserialization rather than degrading silently, so
/// extending the catalog is an additive, versioned change. <see cref="Unknown"/> is a
/// deserialization sentinel only and is rejected by <see cref="ExecutorBinding"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Channel>))]
public enum Channel
{
    Unknown = 0,
    Mcp = 1,
    Cli = 2,
    Chatbot = 3,
    Email = 4,
}
