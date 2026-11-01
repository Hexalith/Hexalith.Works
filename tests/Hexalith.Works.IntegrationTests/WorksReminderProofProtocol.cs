using System.Text.Json;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Reminders;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Closed, private test command for the actual Works reminder registrar.</summary>
internal static class WorksReminderProofProtocol
{
    internal const string DirectoryKey = "WORKS_SDK_PROOF_REMINDER_DIRECTORY";
    internal const string SessionKey = "WORKS_SDK_PROOF_REMINDER_SESSION";

    internal static ReminderTarget Validate(WorksProofCallerRequest request, string nonce, string session,
        DateTimeOffset now, IReadOnlyList<ProcessSource> grants)
    {
        if (!Guid.TryParseExact(nonce, "N", out _) || request.Nonce != nonce || request.Session != session
            || request.Command != "converge-reminder-twice" || request.ExpiresAt <= now || request.ExpiresAt > now.AddSeconds(60)
            || request.Payload.ValueKind != JsonValueKind.Object
            || !request.Payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "Aggregate", "Domain", "Tenant" }, StringComparer.Ordinal))
            throw new InvalidOperationException("The private reminder request is stale, foreign or malformed.");
        var target = request.Payload.Deserialize<ReminderTarget>() ?? throw new InvalidOperationException("A reminder target is required.");
        var source = new ProcessSource(target.Tenant, target.Domain, target.Aggregate);
        source.Validate();
        if (target.Domain != "work" || grants.Count(grant => grant == source) != 1)
            throw new InvalidOperationException("The reminder source is outside the exact fixture inventory.");
        return target;
    }
}
