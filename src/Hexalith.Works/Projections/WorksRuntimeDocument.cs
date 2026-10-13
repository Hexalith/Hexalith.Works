using System.Text.Json;

namespace Hexalith.Works.Projections;

/// <summary>A typed JSON document shared by heterogeneous SDK generation reads.</summary>
/// <param name="Kind">The exact read-model type name.</param>
/// <param name="Value">The immutable serialized read-model value.</param>
public sealed record WorksRuntimeDocument(string Kind, JsonElement Value)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>Captures a read-model value in the common generation document shape.</summary>
    public static WorksRuntimeDocument From<TValue>(TValue value) where TValue : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return new WorksRuntimeDocument(typeof(TValue).FullName!, JsonSerializer.SerializeToElement(value, s_json));
    }

    /// <summary>Returns the value only when the stored type is the exact requested type.</summary>
    public TValue As<TValue>() where TValue : class
    {
        if (!string.Equals(Kind, typeof(TValue).FullName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Works runtime document has an unexpected type.");
        }

        return Value.Deserialize<TValue>(s_json)
            ?? throw new InvalidOperationException("The Works runtime document has an empty value.");
    }
}
