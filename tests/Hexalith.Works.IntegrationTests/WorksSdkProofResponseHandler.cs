using System.Net.Http.Json;
using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Effects;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Records the actual committed receipt before simulating a lost HTTP response.</summary>
internal sealed class WorksSdkProofResponseHandler(IReadModelStore store) : DelegatingHandler
{
    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TrustedEffectSubmitRequest? submission = request.Content is null ? null
            : await request.Content.ReadFromJsonAsync<TrustedEffectSubmitRequest>(cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string? tenant = submission?.Submission.Identity.Tenant;
        if (response.IsSuccessStatusCode && tenant?.StartsWith("sdk415-", StringComparison.Ordinal) == true)
        {
            string key = "story415:fail-after-commit:" + tenant;
            ReadModelEntry<WorksSdkProofFaultState> fault = await store.GetAsync<WorksSdkProofFaultState>("statestore", key, cancellationToken).ConfigureAwait(false);
            // Inspect a buffered copy; the real submitter must still deserialize the original response.
            byte[] receiptBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            TrustedEffectResult? receipt = JsonSerializer.Deserialize<TrustedEffectResult>(receiptBytes,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (receipt?.Replayed == true && receipt.EffectId == fault.Value?.CommittedEffectId)
                await ReadModelWritePolicy.UpdateAsync<WorksSdkProofFaultState>(store, "statestore", key,
                    current => current! with { ReplayObserved = true }, maxAttempts: 8, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (fault.Value?.FailNext == true && (fault.Value.EffectKind is null
                || fault.Value.EffectKind == submission!.Submission.Identity.EffectKind))
            {
                if (receipt is not null && await store.TrySaveAsync("statestore", key,
                    new WorksSdkProofFaultState(false, receipt.EffectId) { Disposition = receipt.Disposition }, fault.ETag!, cancellationToken).ConfigureAwait(false))
                {
                    response.Dispose();
                    throw new HttpRequestException("Synthetic response loss after a durable trusted-effect receipt.");
                }
            }
        }
        return response;
    }
}
