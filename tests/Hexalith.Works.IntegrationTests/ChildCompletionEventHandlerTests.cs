using System.Text.Json;

using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Recovery.ChildCompletion;
using Hexalith.Works.Runtime;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Verifies live child-completion translation and deterministic resume submission.
/// </summary>
public sealed class ChildCompletionEventHandlerTests
{
    private static readonly TenantId s_tenant = new("tenant-alpha");
    private static readonly WorkItemId s_parent = new("parent-001");
    private static readonly WorkItemId s_child = new("child-001");

    /// <summary>A completion feeds the unchanged translator and submits the resulting resume through the gateway seam.</summary>
    [Fact]
    public async Task Completed_child_resumes_awaiting_parent_with_deterministic_submission()
    {
        IChildCompletionAwaitingParentSource source = Substitute.For<IChildCompletionAwaitingParentSource>();
        source
            .GetAwaitingParentsAsync(Arg.Any<WorkItemCompleted>(), Arg.Any<CancellationToken>())
            .Returns([new AwaitingParent(s_tenant, s_parent, [AwaitCondition.ChildCompleted(s_child)])]);
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var submissions = new List<WorkCommandSubmission>();
        submitter
            .SubmitAsync(Arg.Do<WorkCommandSubmission>(submissions.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = new WorkItemCompletedResumeHandler(source, submitter);
        var completed = new WorkItemCompleted(s_child.Value, 7, s_tenant, s_child);

        await handler.HandleAsync(completed, CreateContext(), TestContext.Current.CancellationToken);
        await handler.HandleAsync(completed, CreateContext(), TestContext.Current.CancellationToken);

        submissions.Count.ShouldBe(2, "at-least-once delivery may reissue; the marker and aggregate Handle make it idempotent");
        submissions.Select(value => value.CorrelationId).Distinct(StringComparer.Ordinal).ShouldHaveSingleItem();
        WorkCommandSubmission submission = submissions[0];
        submission.Tenant.ShouldBe(s_tenant.Value);
        submission.AggregateId.ShouldBe(s_parent.Value);
        submission.CommandType.ShouldBe(nameof(ResumeWorkItem));
        submission.CausationId.ShouldBe(submission.CorrelationId);
        ResumeWorkItem command = submission.Payload.Deserialize<ResumeWorkItem>().ShouldNotBeNull();
        command.TenantId.ShouldBe(s_tenant);
        command.WorkItemId.ShouldBe(s_parent);
        command.AwaitCondition.ShouldBe(AwaitCondition.ChildCompleted(s_child));
    }

    private static EventStoreDomainEventContext CreateContext()
    {
        return new EventStoreDomainEventContext(
            s_tenant.Value,
            s_child.Value,
            "01ARZ3NDEKTSV4RRFFQ69G5FAX",
            7,
            new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero),
            "story-4-7-child-completed");
    }
}
