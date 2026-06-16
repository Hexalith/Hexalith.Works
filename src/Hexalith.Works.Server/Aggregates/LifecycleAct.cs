namespace Hexalith.Works.Server.Aggregates;

/// <summary>
/// The lifecycle transition triggers, one per lifecycle command. Used as the column axis of the
/// <see cref="WorkItemLifecycle"/> transition table.
/// </summary>
internal enum LifecycleAct
{
    Assign,
    Queue,
    Claim,
    Suspend,
    Resume,
    Complete,
    Cancel,
    Reject,
    Expire,
}
