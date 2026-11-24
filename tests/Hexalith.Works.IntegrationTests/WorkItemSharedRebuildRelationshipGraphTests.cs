using Hexalith.Works.Projections.SharedRebuild;

using Shouldly;

using Xunit;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises relationship availability at the supported membership depth.</summary>
public sealed class WorkItemSharedRebuildRelationshipGraphTests
{
    /// <summary>Deep complete chains are available, and an unproven final child invalidates every ancestor.</summary>
    /// <param name="missingLastChild">Whether the last member has an unproven child.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepChainAvailabilityDependsOnTheLastChild(bool missingLastChild)
    {
        const int memberCount = 10_000;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string[] members = [.. Enumerable.Range(0, memberCount).Select(static index => $"work-{index:D5}")];
        var graph = new WorkItemSharedRebuildRelationshipGraph();
        for (int index = 1; index < members.Length; index++)
        {
            graph.AddEdge(members[index - 1], members[index]);
        }

        if (missingLastChild)
        {
            graph.AddEdge(members[^1], "missing-child");
        }

        var authoritativeMembers = new HashSet<string>(members, StringComparer.Ordinal) { "independent" };
        IReadOnlySet<string> unavailable = graph.FindUnavailableRolledTotals(authoritativeMembers, cancellationToken);

        if (missingLastChild)
        {
            unavailable.SetEquals(members).ShouldBeTrue();
        }
        else
        {
            unavailable.ShouldBeEmpty();
        }
    }
}
