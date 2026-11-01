using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Reminders;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Observes the actual Works source without replacing its fold, policy, results or failures.</summary>
internal sealed class WorksProofReminderIntentSource(IReminderIntentSource inner, WorksReminderPhaseDiagnostics diagnostics) : IReminderIntentSource
{
    public async Task<IReadOnlyList<ReminderIntent>> GetCurrentIntentsAsync(ReminderTarget target, CancellationToken cancellationToken = default)
    {
        long sequence = diagnostics.Begin("intent-read", ReminderIdentityCodec.ComputeActorId(target.Tenant, target.Aggregate));
        try
        {
            var result = await inner.GetCurrentIntentsAsync(target, cancellationToken).ConfigureAwait(false);
            diagnostics.End(sequence, null, result?.Count);
            return result!;
        }
        catch (Exception exception)
        {
            diagnostics.End(sequence, exception);
            throw;
        }
    }

    public ReminderCommand TranslateDueIntent(ReminderIntent intent) => inner.TranslateDueIntent(intent);
}
