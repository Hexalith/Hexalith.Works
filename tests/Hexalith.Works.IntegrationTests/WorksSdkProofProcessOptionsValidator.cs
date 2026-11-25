using Hexalith.EventStore.DomainService;

using Microsoft.Extensions.Options;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Keeps SDK process option checks except twelve-source freshness at a 4s Platform operation timeout.</summary>
internal sealed class WorksSdkProofProcessOptionsValidator : IValidateOptions<EventStoreProcessOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, EventStoreProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyList<string> failures = [.. options.Validate()
            .Where(static failure => !failure.StartsWith("SupportedCandidateCount cannot fit", StringComparison.Ordinal))];
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
