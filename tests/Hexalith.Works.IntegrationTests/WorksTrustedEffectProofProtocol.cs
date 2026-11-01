using System.Text.Json;
using Hexalith.EventStore.Client.Processes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Closed IPC commands for the original persisted child-completion effect; supplies no transport identity.</summary>
internal static class WorksTrustedEffectProofProtocol
{
    /// <summary>The actual primary Works process's owner-private mailbox.</summary>
    internal const string DirectoryKey = "WORKS_SDK_PROOF_TRUSTED_EFFECT_DIRECTORY";
    /// <summary>The selected composition's private session.</summary>
    internal const string SessionKey = "WORKS_SDK_PROOF_TRUSTED_EFFECT_SESSION";
    /// <summary>The existing effect purpose; it cannot be selected by a request.</summary>
    internal const string Purpose = "works.child-completion-resume";

    /// <summary>Validates nonce, lifetime and the complete fixed payload before any authority or store access.</summary>
    internal static WorksTrustedEffectProofTarget Validate(WorksProofCallerRequest request, string nonce, string session,
        DateTimeOffset now, IReadOnlyList<ProcessSource> grants)
    {
        if (!Guid.TryParseExact(nonce, "N", out _) || request.Nonce != nonce || request.Session != session
            || request.Command is not ("replay-child-completion-resume" or "conflict-child-completion-resume")
            || request.ExpiresAt <= now || request.ExpiresAt > now.AddSeconds(60)
            || request.Payload.ValueKind != JsonValueKind.Object
            || !request.Payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "EffectId", "Source", "Target", "Tenant" }, StringComparer.Ordinal)
            || grants.Count is < 1 or > 64 || grants.Distinct().Count() != grants.Count)
            throw new InvalidOperationException("The private effect request is stale, foreign or malformed.");
        foreach (var grant in grants) grant.Validate();
        if (grants.Any(grant => grant.Domain != "work")) throw new InvalidOperationException("The fixture inventory contains a foreign domain.");
        var target = request.Payload.Deserialize<WorksTrustedEffectProofTarget>()
            ?? throw new InvalidOperationException("The persisted effect coordinates are required.");
        var source = new ProcessSource(target.Tenant, "work", target.Source);
        var receiver = new ProcessSource(target.Tenant, "work", target.Target);
        source.Validate(); receiver.Validate();
        if (!target.Tenant.StartsWith("sdk415-", StringComparison.Ordinal)
            || !target.Source.StartsWith("work-sdk415-", StringComparison.Ordinal) || !target.Target.StartsWith("work-sdk415-", StringComparison.Ordinal)
            || target.EffectId is not { Length: 52 } || target.EffectId.Any(character => !"0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(character))
            || grants.Count(grant => grant == source) != 1 || grants.Count(grant => grant == receiver) != 1)
            throw new InvalidOperationException("The effect is outside the exact configured fixture inventory.");
        return target;
    }
}
