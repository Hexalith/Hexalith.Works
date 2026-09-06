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
    [Fact]
    public void Missing_required_actor_remoting_member_is_rejected()
    {
        string missingInstant = FrozenRegistration.Replace(
            "<Instant xmlns:a=\"http://schemas.datacontract.org/2004/07/System\"><a:DateTime>2026-09-06T19:30:00Z</a:DateTime><a:OffsetMinutes>0</a:OffsetMinutes></Instant>",
            string.Empty,
            StringComparison.Ordinal);
        var serializer = new DataContractSerializer(typeof(DateReminderRegistration));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(missingInstant));

        _ = Should.Throw<SerializationException>(() => serializer.ReadObject(stream));
    }
}
