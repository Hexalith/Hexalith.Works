using System.Text.Json.Serialization;

namespace Hexalith.Works.Contracts.ValueObjects;

[JsonConverter(typeof(JsonStringEnumConverter<Priority>))]
public enum Priority
{
    Unknown = 0,
    Critical = 1,
    High = 2,
    Normal = 3,
    Low = 4,
}
