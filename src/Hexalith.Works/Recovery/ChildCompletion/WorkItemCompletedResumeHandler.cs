using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Reactor;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.Recovery.ChildCompletion;

/// <summary>
/// Feeds a consumed child completion through the unchanged pure translator and submits its resume intents.
/// </summary>
internal sealed class WorkItemCompletedResumeHandler(
    IChildCompletionAwaitingParentSource source,
    IWorkCommandSubmitter submitter) : IEventStoreDomainEventHandler<WorkItemCompleted>
{
    private readonly IChildCompletionAwaitingParentSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IWorkCommandSubmitter _submitter = submitter ?? throw new ArgumentNullException(nameof(submitter));

    /// <inheritdoc/>
    public async Task HandleAsync(
        WorkItemCompleted @event,
        EventStoreDomainEventContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<AwaitingParent> parents = await _source
            .GetAwaitingParentsAsync(@event, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<ResumeWorkItem> commands = ChildCompletionResumeTranslator.ToResumeCommands(@event, parents);
        foreach (ResumeWorkItem command in commands)
        {
            await _submitter
                .SubmitAsync(ChildCompletionResume.BuildSubmission(@event, command), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
