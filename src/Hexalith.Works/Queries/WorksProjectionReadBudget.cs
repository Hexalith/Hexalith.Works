using Hexalith.EventStore.Client.Streams;

namespace Hexalith.Works.Queries;

/// <summary>Bounds the complete authorized SDK projection read within the gateway invocation budget.</summary>
internal static class WorksProjectionReadBudget
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    /// <summary>Returns the selected snapshot within the budget while retaining admission for an unfinished provider.</summary>
    internal static async Task<TResult> ReadAsync<TResult>(Func<CancellationToken, Task<TResult>> read, long startedAt, CancellationToken cancellationToken)
    {
        using var deadline = new AuthoritativeStreamReadDeadline(s_timeout, TimeProvider.System, cancellationToken,
            startedAt);
        return await deadline.ReadWithAdmissionAsync(read).ConfigureAwait(false);
    }
}
