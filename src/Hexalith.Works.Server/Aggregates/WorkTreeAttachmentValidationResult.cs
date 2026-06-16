using Hexalith.EventStore.Contracts.Events;

namespace Hexalith.Works.Server.Aggregates;

public sealed record WorkTreeAttachmentValidationResult(
    bool IsAccepted,
    IRejectionEvent? Rejection,
    int ResultingDepth)
{
    public static WorkTreeAttachmentValidationResult Accepted(int resultingDepth)
        => new(true, null, resultingDepth);

    public static WorkTreeAttachmentValidationResult Rejected(IRejectionEvent rejection, int resultingDepth)
        => new(false, rejection, resultingDepth);
}
