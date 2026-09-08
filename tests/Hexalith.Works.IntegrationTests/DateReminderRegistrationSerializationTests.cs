using System.Runtime.Serialization;

using Hexalith.Works.Reminders;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Guards the data-contract boundary used by Dapr actor remoting.</summary>
public sealed class DateReminderRegistrationSerializationTests
{
    private const string FrozenRegistration =
        "<DateReminderRegistration xmlns=\"http://schemas.datacontract.org/2004/07/Hexalith.Works.Reminders\" xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">"
        + "<TenantId>tenant-reminder</TenantId>"
        + "<WorkItemId>work-reminder</WorkItemId>"
        + "<Instant xmlns:a=\"http://schemas.datacontract.org/2004/07/System\">"
        + "<a:DateTime>2026-09-06T19:30:00Z</a:DateTime><a:OffsetMinutes>0</a:OffsetMinutes></Instant>"
        + "<CorrelationKey>2026-09-06T19:30:00.0000000+00:00</CorrelationKey>"
        + "<DueTimeMilliseconds>12345.5</DueTimeMilliseconds>"
        + "</DateReminderRegistration>";

    /// <summary>The actor request payload retains every field across DataContractSerializer.</summary>
    [Fact]
    public void Registration_round_trips_through_the_actor_remoting_serializer_contract()
    {
        var expected = new DateReminderRegistration(
            "tenant-reminder",
            "work-reminder",
            new DateTimeOffset(2026, 9, 6, 19, 30, 0, TimeSpan.Zero),
            "2026-09-06T19:30:00.0000000+00:00",
            12_345.5);
        var serializer = new DataContractSerializer(typeof(DateReminderRegistration));

        using var stream = new MemoryStream();
        serializer.WriteObject(stream, expected);
        stream.Position = 0;

        DateReminderRegistration actual = serializer.ReadObject(stream)
            .ShouldBeOfType<DateReminderRegistration>();
        actual.ShouldBe(expected);
    }

    /// <summary>Previously emitted member names and order remain readable after later implementation refactors.</summary>
    [Fact]
    public void Frozen_actor_remoting_payload_reads_forward()
    {
        var serializer = new DataContractSerializer(typeof(DateReminderRegistration));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(FrozenRegistration));

        DateReminderRegistration actual = serializer.ReadObject(stream)
            .ShouldBeOfType<DateReminderRegistration>();

        actual.ShouldBe(new DateReminderRegistration(
            "tenant-reminder",
            "work-reminder",
            new DateTimeOffset(2026, 9, 6, 19, 30, 0, TimeSpan.Zero),
            "2026-09-06T19:30:00.0000000+00:00",
            12_345.5));
    }

    /// <summary>A truncated payload fails closed instead of silently scheduling from default values.</summary>
    /// <param name="omittedElement">The exact required element removed from the frozen payload.</param>
    /// <remarks>
    /// Every member is <c>IsRequired = true</c>, so every one of them must fail closed — proving it for
    /// <c>Instant</c> alone would leave four members free to silently default (an empty tenant/work-item id, a
    /// blank correlation key, or a zero due time that fires immediately) if their metadata ever regressed.
    /// </remarks>
    [Theory]
    [InlineData("<TenantId>tenant-reminder</TenantId>")]
    [InlineData("<WorkItemId>work-reminder</WorkItemId>")]
    [InlineData("<Instant xmlns:a=\"http://schemas.datacontract.org/2004/07/System\"><a:DateTime>2026-09-06T19:30:00Z</a:DateTime><a:OffsetMinutes>0</a:OffsetMinutes></Instant>")]
    [InlineData("<CorrelationKey>2026-09-06T19:30:00.0000000+00:00</CorrelationKey>")]
    [InlineData("<DueTimeMilliseconds>12345.5</DueTimeMilliseconds>")]
    public void Missing_required_actor_remoting_member_is_rejected(string omittedElement)
    {
        string truncated = FrozenRegistration.Replace(omittedElement, string.Empty, StringComparison.Ordinal);
        truncated.ShouldNotBe(FrozenRegistration, "The omitted element must actually appear in the frozen payload.");
        var serializer = new DataContractSerializer(typeof(DateReminderRegistration));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(truncated));

        _ = Should.Throw<SerializationException>(() => serializer.ReadObject(stream));
    }
}
