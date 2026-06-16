using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class ChildCompletionResumeTranslatorTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId Child = new("child-001");

    [Fact]
    public void ToResumeCommands_emits_parent_resume_intent_for_matching_child_completed_condition()
    {
        WorkItemCompleted childCompleted = new(Child.Value, 7, Tenant, Child);
        AwaitingParent awaitingParent = new(
            Tenant,
            Parent,
            [
                AwaitCondition.ExternalSignal("external-signal"),
                AwaitCondition.ChildCompleted(Child),
            ]);

        IReadOnlyList<ResumeWorkItem> commands =
            ChildCompletionResumeTranslator.ToResumeCommands(childCompleted, [awaitingParent]);

        ResumeWorkItem command = commands.ShouldHaveSingleItem();
        command.TenantId.ShouldBe(Tenant);
        command.WorkItemId.ShouldBe(Parent);
        command.AwaitCondition.ShouldBe(AwaitCondition.ChildCompleted(Child));
    }

    [Fact]
    public void ToResumeCommands_ignores_non_matching_awaiting_parent_input_without_deciding_acceptance()
    {
        WorkItemCompleted childCompleted = new(Child.Value, 7, Tenant, Child);
        AwaitingParent nonMatchingParent = new(
            Tenant,
            Parent,
            [
                AwaitCondition.ExternalSignal("external-signal"),
                AwaitCondition.ChildCompleted(new WorkItemId("other-child-001")),
            ]);

        IReadOnlyList<ResumeWorkItem> commands =
            ChildCompletionResumeTranslator.ToResumeCommands(childCompleted, [nonMatchingParent]);

        commands.ShouldBeEmpty();
        typeof(AwaitingParent).GetProperty("Status").ShouldBeNull("The reactor input must not carry parent state for acceptance decisions.");
    }

    [Fact]
    public void ToResumeCommands_returns_empty_when_no_parents_are_awaiting()
    {
        WorkItemCompleted childCompleted = new(Child.Value, 7, Tenant, Child);

        ChildCompletionResumeTranslator.ToResumeCommands(childCompleted, []).ShouldBeEmpty();
    }

    [Fact]
    public void ToResumeCommands_emits_one_intent_per_matching_parent_and_skips_the_others()
    {
        WorkItemId secondParent = new("parent-002");
        WorkItemId thirdParent = new("parent-003");
        WorkItemCompleted childCompleted = new(Child.Value, 7, Tenant, Child);
        AwaitingParent matchingA = new(Tenant, Parent, [AwaitCondition.ChildCompleted(Child)]);
        AwaitingParent nonMatching = new(
            Tenant,
            secondParent,
            [AwaitCondition.ChildCompleted(new WorkItemId("other-child-001"))]);
        AwaitingParent matchingB = new(
            Tenant,
            thirdParent,
            [AwaitCondition.DateReached(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero)), AwaitCondition.ChildCompleted(Child)]);

        IReadOnlyList<ResumeWorkItem> commands =
            ChildCompletionResumeTranslator.ToResumeCommands(childCompleted, [matchingA, nonMatching, matchingB]);

        commands.Count.ShouldBe(2);
        commands.Select(c => c.WorkItemId).ShouldBe([Parent, thirdParent]);
        commands.ShouldAllBe(c => c.AwaitCondition == AwaitCondition.ChildCompleted(Child));
    }

    [Fact]
    public void ToResumeCommands_is_kind_aware_and_does_not_match_an_external_signal_sharing_the_child_id_text()
    {
        // D3: kind + key is the identity. A parent awaiting an ExternalSignal whose correlation text equals
        // the completed child's id is NOT a child-completion match — the reactor must not fabricate a resume.
        WorkItemCompleted childCompleted = new(Child.Value, 7, Tenant, Child);
        AwaitingParent collidingTextParent = new(Tenant, Parent, [AwaitCondition.ExternalSignal(Child.Value)]);

        ChildCompletionResumeTranslator.ToResumeCommands(childCompleted, [collidingTextParent]).ShouldBeEmpty();
    }
}
