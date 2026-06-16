using System.Text.Json.Serialization;

namespace Hexalith.Works.Contracts.ValueObjects;

[JsonConverter(typeof(JsonStringEnumConverter<AwaitConditionKind>))]
public enum AwaitConditionKind
{
    ChildCompleted = 1,
    DateReached = 2,
    ExternalSignal = 3,
}
