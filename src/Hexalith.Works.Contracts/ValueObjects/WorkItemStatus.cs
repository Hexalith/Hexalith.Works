using System.Text.Json.Serialization;

namespace Hexalith.Works.Contracts.ValueObjects;

[JsonConverter(typeof(JsonStringEnumConverter<WorkItemStatus>))]
public enum WorkItemStatus
{
    Unknown = 0,
    Created = 1,
}
