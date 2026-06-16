using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class LifecycleTransitionMatrixDocTests
{
    private static readonly string[] RequiredStatuses =
    [
        "Created",
        "Assigned",
        "Queued",
        "InProgress",
        "Suspended",
        "Completed",
        "Cancelled",
        "Rejected",
        "Expired",
    ];

    private static readonly string[] RequiredCommands =
    [
        "AssignWorkItem",
        "QueueWorkItem",
        "ClaimWorkItem",
        "SuspendWorkItem",
        "ResumeWorkItem",
        "CompleteWorkItem",
        "CancelWorkItem",
        "RejectWorkItem",
        "ExpireWorkItem",
    ];

    [Fact]
    public void TransitionMatrixDoc_existsAndEnumeratesEveryStatusAndLifecycleCommand()
    {
        string path = RepositoryRoot.PathFromRoot("docs", "lifecycle-transition-matrix.md");
        File.Exists(path).ShouldBeTrue($"The lifecycle transition matrix must exist at '{path}' (AC #6).");

        string content = File.ReadAllText(path);

        // Vacuous-pass guard: assert content was actually discovered before asserting completeness.
        content.ShouldNotBeNullOrWhiteSpace("The lifecycle transition matrix must not be empty.");

        foreach (string status in RequiredStatuses)
        {
            content.ShouldContain(
                status,
                Case.Sensitive,
                $"The transition matrix must enumerate the '{status}' status across all 9 statuses (AC #6).");
        }

        foreach (string command in RequiredCommands)
        {
            content.ShouldContain(
                command,
                Case.Sensitive,
                $"The transition matrix must enumerate the '{command}' lifecycle command (AC #6).");
        }

        // The idempotent no-op and rejection outcomes must both be documented (AC #4).
        content.ShouldContain("NoOp", Case.Insensitive, "The matrix must document the idempotent no-op outcome (AC #4).");
        content.ShouldContain("WorkItemTransitionRejected", Case.Sensitive, "The matrix must document the rejection outcome (AC #4).");
    }
}
