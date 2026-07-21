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
    private static readonly Unit Point = new("point");
    private static readonly Unit Day = new("day");
    private static readonly WorkItemId Parent = new("parent");

    [Fact]
    public void Roll_up_projection_converges_for_generated_tenant_safe_trees_with_permuted_and_duplicate_delivery()
    {
        // The delivery order is a genuine random permutation of `canonical ++ duplicates`, drawn from
        // FsCheck's generator space via Gen.Shuffle over the deliverable indexes — not a fixed reversal.
        // BuildScenario is a pure function of `values`, so re-deriving the scenario inside the property
        // sees exactly the deliverable multiset the permutation was generated for. Failures replay from
        // the seed FsCheck prints; no wall clock or ambient randomness is involved.
        FsCheck.Gen<int[]> generatedCases = FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 127));
        FsCheck.Gen<DeliveryCase> generatedDeliveries = FsCheck.Fluent.Gen.SelectMany(
            generatedCases,
            values => FsCheck.Fluent.Gen.Shuffle(Enumerable.Range(0, BuildScenario(values).Deliverable.Length)),
            (values, order) => new DeliveryCase(values, order));
        Arbitrary<DeliveryCase> arbitraryCases = FsCheck.Fluent.Arb.ToArbitrary(generatedDeliveries);
        Property property = FsCheck.FSharp.Prop.ForAll(
            arbitraryCases,
            FSharpFuncConvert.FromFunc<DeliveryCase, bool>(deliveryCase =>
            {
                RollUpScenario scenario = BuildScenario(deliveryCase.Values);
                WorkItemRollUpEvent[] delivery = [.. deliveryCase.Order.Select(index => scenario.Deliverable[index])];
                WorkItemRollUpProjection canonicalProjection = Replay(scenario.Canonical);
                WorkItemRollUp expected = canonicalProjection.Get(Tenant, Parent).ShouldNotBeNull();
                WorkItemRollUp actual = Replay(delivery).Get(Tenant, Parent).ShouldNotBeNull();

                return SameRollUp(actual, expected)
                    && expected.Degraded
                    && expected.ProjectionDiagnostics.Count > 0
                    && actual.ChildWorkItemIds.All(id => id.Value.StartsWith("child-", StringComparison.Ordinal))
                    && Replay(delivery).Get(OtherTenant, scenario.CollidingForeignChild).ShouldNotBeNull().TenantId == OtherTenant;
            }));

        Check.One(Config.QuickThrowOnFailure, property);
    }

    private static RollUpScenario BuildScenario(IReadOnlyList<int> values)
    {
        int childCount = 1 + Pick(values, 0, 3);
        int grandchildCount = 1 + Pick(values, 1, 2);
        decimal parentEstimate = 5m + Pick(values, 2, 5);

        List<WorkItemRollUpEvent> canonical = [Envelope(Created(Parent, 1, parentEstimate, null, Tenant, Hour))];
        List<WorkItemId> children = [.. Enumerable.Range(0, childCount).Select(index => new WorkItemId($"child-{index}"))];

        for (int index = 0; index < children.Count; index++)
        {
            WorkItemId child = children[index];
            decimal estimate = 3m + Pick(values, 3 + index, 7);
            Unit unit = PickUnit(values, index);
            canonical.Add(Envelope(Created(child, 1, estimate, Parent, Tenant, unit)));
            canonical.Add(Envelope(new ReEstimated(child.Value, 2, Tenant, child, estimate + Pick(values, 8 + index, 4), unit)));
            canonical.Add(Envelope(new ProgressReported(child.Value, 3, Tenant, child, 1m + Pick(values, 13 + index, 3), unit)));

            if (index != children.Count - 1 && Pick(values, 18 + index, 5) == 0)
            {
                canonical.Add(Envelope(new WorkItemCompleted(child.Value, 4, Tenant, child)));
            }
        }

        WorkItemId degradedChild = children[^1];
        canonical.Add(Envelope(new ReEstimated(degradedChild.Value, 9, Tenant, degradedChild, 99m, DifferentUnit(PickUnit(values, children.Count - 1)))));

        for (int index = 0; index < grandchildCount; index++)
        {
            WorkItemId grandchild = new($"grandchild-{index}");
            decimal estimate = 2m + Pick(values, 24 + index, 5);
            Unit unit = PickUnit(values, 32 + index);
            canonical.Add(Envelope(Created(grandchild, 1, estimate, children[0], Tenant, unit)));
            canonical.Add(Envelope(new ProgressReported(grandchild.Value, 2, Tenant, grandchild, 1m + Pick(values, 29 + index, 2), unit)));
        }

        WorkItemId collidingForeignChild = children[0];
        WorkItemRollUpEvent foreignCollision = Envelope(Created(collidingForeignChild, 1, 99m, Parent, OtherTenant, Hour));
        canonical.Add(foreignCollision);

        // The deliverable multiset is every canonical fact plus value-derived duplicates, in canonical
        // order; the generated permutation in the test decides the actual delivery order.
        int[] duplicateIndexes = [.. values.Select(value => Math.Abs(value) % canonical.Count)];
        WorkItemRollUpEvent[] deliverable = [.. canonical, .. duplicateIndexes.Select(index => canonical[index])];

        return new RollUpScenario([.. canonical], deliverable, collidingForeignChild);
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
            && actual.LatestAcceptedSourceSequence == expected.LatestAcceptedSourceSequence
            && actual.Degraded == expected.Degraded
            && actual.ProjectionDiagnostics.SequenceEqual(expected.ProjectionDiagnostics);

    private static WorkItemRollUpProjection Replay(IEnumerable<WorkItemRollUpEvent> events)
    {
        WorkItemRollUpProjection projection = new();
        foreach (WorkItemRollUpEvent e in events)
        {
            projection.Project(e);
        }

        return projection;
    }

    private static WorkItemCreated Created(WorkItemId workItemId, long sequence, decimal remaining, WorkItemId? parent, TenantId tenantId, Unit unit)
        => new(
            workItemId.Value,
            sequence,
            tenantId,
            workItemId,
            new Obligation($"obligation-{workItemId.Value}"),
            new WorkItemEffort(remaining, unit),
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

    private static Unit PickUnit(IReadOnlyList<int> values, int index)
        => Pick(values, index, 3) switch
        {
            0 => Hour,
            1 => Point,
            _ => Day,
        };

    private static Unit DifferentUnit(Unit unit)
        => unit == Hour ? Point : Hour;

    private sealed record RollUpScenario(
        WorkItemRollUpEvent[] Canonical,
        WorkItemRollUpEvent[] Deliverable,
        WorkItemId CollidingForeignChild);

    private sealed record DeliveryCase(int[] Values, int[] Order);
}
