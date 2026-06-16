using System.Text.Json.Serialization;

namespace Hexalith.Works.Contracts.ValueObjects;

[JsonConverter(typeof(JsonStringEnumConverter<AuthorityLevel>))]
public enum AuthorityLevel
{
    Unknown = 0,
    Read = 1,
    Contribute = 2,
    Coordinate = 3,
    Administer = 4,
}
