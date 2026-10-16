using System.Reflection;
using System.Text.Json.Serialization;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>The additive expiry member must not change the pre-4.15 compiled shape of the durable records.</summary>
public sealed class WorkItemExpiryContractShapeTests
{
    /// <summary>The records that gained a trailing expiry member in Story 4.15.</summary>
    public static TheoryData<Type> ExpiryRecords => new()
    {
        typeof(CreateWorkItem),
        typeof(RescheduleWorkItem),
        typeof(ExpireWorkItem),
        typeof(WorkItemCreated),
        typeof(WorkItemRescheduled),
        typeof(WorkItemExpired),
    };

    /// <summary>The constructor and deconstruction without the trailing expiry member still exist.</summary>
    [Theory]
    [MemberData(nameof(ExpiryRecords))]
    public void LegacyConstructorAndDeconstructRemain(Type record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ConstructorInfo primary = record.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        Type[] legacyTypes = [.. primary.GetParameters().SkipLast(1).Select(p => p.ParameterType)];
        primary.GetParameters()[^1].ParameterType.ShouldBe(typeof(WorkItemExpirySchedule));

        record.GetConstructor(legacyTypes).ShouldNotBeNull();

        MethodInfo[] deconstructs = [.. record.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "Deconstruct")];
        deconstructs.ShouldContain(m => m.GetParameters().Length == legacyTypes.Length
            && m.GetParameters().All(p => p.IsOut));
    }

    /// <summary>A null expiry member is omitted from serialized bytes, so historical payload bytes are unchanged.</summary>
    [Theory]
    [MemberData(nameof(ExpiryRecords))]
    public void NullExpiryIsOmittedFromSerialization(Type record)
    {
        ArgumentNullException.ThrowIfNull(record);
        PropertyInfo expiry = record.GetProperties().Single(p => p.PropertyType == typeof(WorkItemExpirySchedule));
        expiry.GetCustomAttribute<JsonIgnoreAttribute>()!.Condition.ShouldBe(JsonIgnoreCondition.WhenWritingNull);
    }
}
