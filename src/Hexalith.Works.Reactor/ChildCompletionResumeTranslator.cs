using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Reactor;

public static class ChildCompletionResumeTranslator
{
    public static IReadOnlyList<ResumeWorkItem> ToResumeCommands(
        WorkItemCompleted childCompleted,
        IReadOnlyList<AwaitingParent> awaitingParents)
    {
        ArgumentNullException.ThrowIfNull(childCompleted);
        ArgumentNullException.ThrowIfNull(awaitingParents);

        AwaitCondition completedChild = AwaitCondition.ChildCompleted(childCompleted.WorkItemId);
        List<ResumeWorkItem> commands = [];
        foreach (AwaitingParent parent in awaitingParents)
        {
            ArgumentNullException.ThrowIfNull(parent);
            if (parent.AwaitConditions.Contains(completedChild))
            {
                commands.Add(new ResumeWorkItem(parent.TenantId, parent.WorkItemId, completedChild));
            }
        }

        return commands;
    }
}
