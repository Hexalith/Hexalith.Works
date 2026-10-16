using System.Security.Cryptography;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Server.Commands;
using Hexalith.EventStore.Server.Pipeline.Commands;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Fixture-only canonical semantic registry restricted to synthetic Works effects.</summary>
internal sealed class WorksSdkProofIntentRegistry : IIdempotencyIntentAdapterRegistry
{
    /// <inheritdoc/>
    public TrustedIdempotencyDescriptor Resolve(SubmitCommand command)
    {
        if (!command.Tenant.StartsWith("sdk415-", StringComparison.Ordinal) || command.Domain != "work"
            || !command.AggregateId.StartsWith("work-sdk415-", StringComparison.Ordinal)
            || command.CommandType is not ("ResumeWorkItem" or "ExpireWorkItem" or "CancelWorkItem"))
            throw new InvalidOperationException("Synthetic Works authority cannot admit this target.");
        return new TrustedIdempotencyDescriptor("story415-synthetic", command.CommandType, 1,
            SHA256.HashData(command.Payload), IdempotencyReplayRetentionTier.Mutation);
    }
}
