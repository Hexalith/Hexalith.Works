namespace Hexalith.Works.Reminders;

/// <summary>The current pending date awaits folded from one aggregate stream, plus the stream watermark.</summary>
/// <param name="Pending">The currently pending date awaits.</param>
/// <param name="LastSequence">The greatest sequence observed in the folded stream, or zero when the stream was empty.</param>
internal sealed record PendingDateAwaitStreamFold(
    IReadOnlyList<PendingDateAwait> Pending,
    long LastSequence);
