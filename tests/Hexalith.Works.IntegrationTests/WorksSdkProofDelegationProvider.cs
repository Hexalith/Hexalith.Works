using System.Globalization;
using System.Security.Cryptography;

using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Server.Pipeline.Commands;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Issues fresh asymmetric credentials bound to the complete synthetic effect and canonical digest.</summary>
internal sealed class WorksSdkProofDelegationProvider(RsaSecurityKey key, WorksSdkProofIntentRegistry registry, IReadModelStore store) : IReminderDelegationTokenProvider
{
    internal const string Issuer = "https://story415.invalid/synthetic-authority";

    /// <inheritdoc/>
    public async Task<string?> GetDelegationTokenAsync(ReminderDelegationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TrustedEffectSubmission submission = request.Submission;
        EffectIdentity identity = submission.Identity;
        if (request.Workload != "works" || !identity.Tenant.StartsWith("sdk415-", StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic Works issuer refused the workload or tenant.");
        if (identity.EffectKind == EffectKindCatalog.ChildCompletionResume
            && (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + identity.Tenant, cancellationToken).ConfigureAwait(false)).Value?.HoldDelegation == true)
            throw new IOException("Synthetic delegation hold after durable plan, before competing ordinary resume.");
        var descriptor = registry.Resolve(new SubmitCommand(submission.MessageId, identity.Tenant, identity.TargetDomain,
            identity.TargetAggregate, submission.CommandType, submission.CommandPayload, submission.MessageId, request.Workload));
        DateTime now = DateTime.UtcNow;
        var token = new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = "eventstore-gateway", IssuedAt = now, NotBefore = now.AddSeconds(-1), Expires = now.AddMinutes(2),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["effect_id"] = EffectIdentityCodec.ComputeEffectId(identity), ["tenant"] = identity.Tenant,
                ["source_domain"] = identity.SourceDomain, ["source_aggregate"] = identity.SourceAggregate,
                ["source_envelope_sequence"] = identity.SourceEnvelopeSequence.ToString(CultureInfo.InvariantCulture),
                ["effect_kind"] = identity.EffectKind, ["target_domain"] = identity.TargetDomain,
                ["target_aggregate"] = identity.TargetAggregate, ["effect_ordinal"] = identity.Ordinal.ToString(CultureInfo.InvariantCulture),
                ["command_type"] = submission.CommandType,
                ["command_digest"] = EffectIdentityCodec.RenderDigest(SHA256.HashData(descriptor.CanonicalIntent)),
                ["workload"] = request.Workload, ["purpose"] = request.Purpose, ["causation"] = request.CausationId,
            },
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(token);
    }
}
