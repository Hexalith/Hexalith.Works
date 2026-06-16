using FsCheck;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Shouldly;
using FSharpFuncConvert = Microsoft.FSharp.Core.FuncConvert;

namespace Hexalith.Works.PropertyTests;

public sealed class WorkItemRollUpConvergencePropertyTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly TenantId OtherTenant = new("tenant-beta");
    private static readonly Unit Hour = new("hour");
    private static readonly WorkItemId Parent = new("parent");

    [Fact]
    public void Roll_up_projection_converges_for_generated_tenant_safe_trees_with_permuted_and_duplicate_delivery()
    {
        FsCheck.Gen<int[]> generatedCases = FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 127));
        Arbitrary<int[]> arbitraryCases = FsCheck.Fluent.Arb.ToArbitrary(generatedCases);
        Property property = FsCheck.FSharp.Prop.ForAll(
            arbitraryCases,
            FSharpFuncConvert.FromFunc<int[], bool>(values =>
            {
                RollUpScenario scenario = BuildScenario(values);
                WorkItemRollUpProjection canonicalProjection = Replay(scenario.Canonical);
                WorkItemRollUp expected = canonicalProjection.Get(Tenant, Parent).ShouldNotBeNull();
                WorkItemRollUp actual = Replay(scenario.Delivery).Get(Tenant, Parent).ShouldNotBeNull();

                return SameRollUp(actual, expected)
                    && actual.ChildWorkItemIds.All(id => id.Value.StartsWith("child-", StringComparison.Ordinal))
                    && Replay(scenario.Delivery).Get(OtherTenant, scenario.CollidingForeignChild).ShouldNotBeNull().TenantId == OtherTenant;
            }));

        Check.One(Config.QuickThrowOnFailure, property);
    }

    private static RollUpScenario BuildScenario(IReadOnlyList<int> values)
    {
        int childCount = 1 + Pick(values, 0, 3);
        int grandchildCount = 1 + Pick(values, 1, 2);
        decimal parentEstimate = 5m + Pick(values, 2, 5);

        List<WorkItemRollUpEvent> canonical = [Envelope(Created(Parent, 1, parentEstimate, null, Tenant))];
        List<WorkItemId> children = [.. Enumerable.Range(0, childCount).Select(index => new WorkItemId($"child-{index}"))];

        for (int index = 0; index < children.Count; index++)
        {
            WorkItemId child = children[index];
            decimal estimate = 3m + Pick(values, 3 + index, 7);
            canonical.Add(Envelope(Created(child, 1, estimate, Parent, Tenant)));
            canonical.Add(Envelope(new ReEstimated(child.Value, 2, Tenant, child, estimate + Pick(values, 8 + index, 4), Hour)));
            canonical.Add(Envelope(new ProgressReported(child.Value, 3, Tenant, child, 1m + Pick(values, 13 + index, 3), Hour)));

            if (Pick(values, 18 + index, 5) == 0)
            {
                canonical.Add(Envelope(new WorkItemCompleted(child.Value, 4, Tenant, child)));
            }
        }

        for (int index = 0; index < grandchildCount; index++)
        {
            WorkItemId grandchild = new($"grandchild-{index}");
            decimal estimate = 2m + Pick(values, 24 + index, 5);
            canonical.Add(Envelope(Created(grandchild, 1, estimate, children[0], Tenant)));
            canonical.Add(Envelope(new ProgressReported(grandchild.Value, 2, Tenant, grandchild, 1m + Pick(values, 29 + index, 2), Hour)));
        }

        WorkItemId collidingForeignChild = children[0];
        WorkItemRollUpEvent foreignCollision = Envelope(Created(collidingForeignChild, 1, 99m, Parent, OtherTenant));
        canonical.Add(foreignCollision);

        int[] duplicateIndexes = [.. values.Select(value => Math.Abs(value) % canonical.Count)];
        WorkItemRollUpEvent[] delivery = [.. duplicateIndexes.Select(index => canonical[index]), .. canonical.Reverse<WorkItemRollUpEvent>()];

        return new RollUpScenario([.. canonical], delivery, collidingForeignChild);
    }

    private static bool SameRollUp(WorkItemRollUp actual, WorkItemRollUp expected)
        => actual.TenantId == expected.TenantId
            && actual.WorkItemId == expected.WorkItemId
            && actual.Status == expected.Status
            && actual.Parent == expected.Parent
            && actual.OwnRemaining == expected.OwnRemaining
            && actual.RolledRemaining == expected.RolledRemaining
            && actual.RolledRemainingByUnit.SequenceEqual(expected.RolledRemainingByUnit)
            && actual.ChildWorkItemIds.OrderBy(id => id.Value).SequenceEqual(expected.ChildWorkItemIds.OrderBy(id => id.Value))
            && actual.ChildContributionCount == expected.ChildContributionCount
            && actual.LatestAcceptedSourceSequence == expected.LatestAcceptedSourceSequence;

    private static WorkItemRollUpProjection Replay(IEnumerable<WorkItemRollUpEvent> events)
    {
        WorkItemRollUpProjection projection = new();
        foreach (WorkItemRollUpEvent e in events)
        {
            projection.Project(e);
        }

        return projection;
    }

    private static WorkItemCreated Created(WorkItemId workItemId, long sequence, decimal remaining, WorkItemId? parent, TenantId tenantId)
        => new(
            workItemId.Value,
            sequence,
            tenantId,
            workItemId,
            new Obligation($"obligation-{workItemId.Value}"),
            new WorkItemEffort(remaining, Hour),
            Parent: parent is null ? null : new ParentWorkItemReference(Tenant, parent));

    private static WorkItemRollUpEvent Envelope(object payload)
        => payload switch
        {
            WorkItemCreated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ProgressReported e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ReEstimated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemCompleted e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            _ => throw new ArgumentOutOfRangeException(nameof(payload)),
        };

    private static int Pick(IReadOnlyList<int> values, int index, int modulo)
        => values.Count == 0 ? 0 : Math.Abs(values[index % values.Count]) % modulo;

    private sealed record RollUpScenario(
        WorkItemRollUpEvent[] Canonical,
        WorkItemRollUpEvent[] Delivery,
        WorkItemId CollidingForeignChild);
}
