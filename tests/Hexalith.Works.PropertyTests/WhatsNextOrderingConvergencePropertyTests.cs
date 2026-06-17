using FsCheck;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Shouldly;
using FSharpFuncConvert = Microsoft.FSharp.Core.FuncConvert;

namespace Hexalith.Works.PropertyTests;

/// <summary>
/// Story 4.4 (Task 9 / AC #2/#5): for any generated set of items and any permutation + duplication of
/// their delivery, the tenant what's-next queue converges to the same ordered list (order-tolerant), the
/// ordering is a strict total order (every adjacent pair compares strictly less — no two distinct items
/// compare equal under the full comparator including the id tiebreak), and a colliding foreign-tenant item
/// never leaks across tenants. Falsifiable value beyond the fixed-case Task 4 matrix: random permutations.
/// FsCheck wiring mirrors <c>WorkItemRollUpConvergencePropertyTests</c>.
/// </summary>
public sealed class WhatsNextOrderingConvergencePropertyTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly TenantId OtherTenant = new("tenant-beta");
    private static readonly ExecutorBinding Binding =
        new(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Coordinate);

    [Fact]
    public void What_s_next_queue_converges_and_is_a_total_order_under_permuted_and_duplicate_delivery()
    {
        FsCheck.Gen<int[]> generatedCases = FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 127));
        Arbitrary<int[]> arbitraryCases = FsCheck.Fluent.Arb.ToArbitrary(generatedCases);
        Property property = FsCheck.FSharp.Prop.ForAll(
            arbitraryCases,
            FSharpFuncConvert.FromFunc<int[], bool>(values =>
            {
                Scenario scenario = BuildScenario(values);
                IReadOnlyList<WhatsNextItem> expected = Replay(scenario.Canonical).WhatsNext(Tenant);
                IReadOnlyList<WhatsNextItem> actual = Replay(scenario.Delivery).WhatsNext(Tenant);

                return SameOrder(expected, actual)
                    && IsStrictTotalOrder(actual)
                    && actual.All(item => item.TenantId == Tenant)
                    && Replay(scenario.Delivery).WhatsNext(OtherTenant).All(item => item.TenantId == OtherTenant);
            }));

        Check.One(Config.QuickThrowOnFailure, property);
    }

    private static Scenario BuildScenario(IReadOnlyList<int> values)
    {
        int itemCount = 2 + Pick(values, 0, 5);
        List<WorkItemRollUpEvent> canonical = [];

        for (int index = 0; index < itemCount; index++)
        {
            WorkItemId id = new($"item-{index:D2}");
            WorkItemSchedule schedule = new(PickPriority(values, index), PickDueDate(values, index));
            canonical.Add(Envelope(Created(Tenant, id, schedule)));

            switch (Pick(values, 10 + index, 4))
            {
                case 0:
                    canonical.Add(Envelope(new WorkItemQueued(id.Value, 2, Tenant, id)));
                    break;
                case 1:
                    canonical.Add(Envelope(new WorkItemAssigned(id.Value, 2, Tenant, id, Binding)));
                    break;
                case 2:
                    canonical.Add(Envelope(new WorkItemQueued(id.Value, 2, Tenant, id)));
                    canonical.Add(Envelope(new WorkItemClaimed(id.Value, 3, Tenant, id, Binding)));  // not eligible
                    break;
                default:
                    break;  // stays Created (not eligible)
            }
        }

        // Colliding foreign-tenant item: same inner id as Tenant's item-00, eligible, different tenant.
        WorkItemId colliding = new("item-00");
        canonical.Add(Envelope(Created(OtherTenant, colliding, new WorkItemSchedule(Priority.Critical, new DateOnly(2026, 1, 1)))));
        canonical.Add(Envelope(new WorkItemQueued(colliding.Value, 2, OtherTenant, colliding)));

        int[] duplicateIndexes = [.. values.Select(value => Math.Abs(value) % canonical.Count)];
        WorkItemRollUpEvent[] delivery = [.. duplicateIndexes.Select(index => canonical[index]), .. canonical.Reverse<WorkItemRollUpEvent>()];

        return new Scenario([.. canonical], delivery);
    }

    private static bool SameOrder(IReadOnlyList<WhatsNextItem> expected, IReadOnlyList<WhatsNextItem> actual)
        => expected.Select(item => item.WorkItemId.Value).SequenceEqual(actual.Select(item => item.WorkItemId.Value))
            && expected.Select(item => item.Status).SequenceEqual(actual.Select(item => item.Status))
            && expected.Select(item => item.Priority).SequenceEqual(actual.Select(item => item.Priority))
            && expected.Select(item => item.DueDate).SequenceEqual(actual.Select(item => item.DueDate));

    private static bool IsStrictTotalOrder(IReadOnlyList<WhatsNextItem> ordered)
    {
        for (int index = 1; index < ordered.Count; index++)
        {
            if (WhatsNextOrdering.Instance.Compare(ordered[index - 1], ordered[index]) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static WhatsNextQueueProjection Replay(IEnumerable<WorkItemRollUpEvent> events)
    {
        WhatsNextQueueProjection projection = new();
        foreach (WorkItemRollUpEvent e in events)
        {
            _ = projection.Project(e);
        }

        return projection;
    }

    private static WorkItemCreated Created(TenantId tenant, WorkItemId workItemId, WorkItemSchedule schedule)
        => new(
            workItemId.Value,
            1,
            tenant,
            workItemId,
            new Obligation($"obligation-{workItemId.Value}"),
            Schedule: schedule);

    private static WorkItemRollUpEvent Envelope(IEventPayload payload)
        => payload switch
        {
            WorkItemCreated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemQueued e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemAssigned e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemClaimed e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            _ => throw new ArgumentOutOfRangeException(nameof(payload)),
        };

    private static int Pick(IReadOnlyList<int> values, int index, int modulo)
        => values.Count == 0 ? 0 : Math.Abs(values[index % values.Count]) % modulo;

    private static Priority? PickPriority(IReadOnlyList<int> values, int index)
        => Pick(values, 20 + index, 6) switch
        {
            0 => null,
            1 => Priority.Unknown,
            2 => Priority.Critical,
            3 => Priority.High,
            4 => Priority.Normal,
            _ => Priority.Low,
        };

    private static DateOnly? PickDueDate(IReadOnlyList<int> values, int index)
        => Pick(values, 40 + index, 4) switch
        {
            0 => null,
            1 => new DateOnly(2026, 7, 1),
            2 => new DateOnly(2026, 8, 15),
            _ => new DateOnly(2026, 9, 30),
        };

    private sealed record Scenario(WorkItemRollUpEvent[] Canonical, WorkItemRollUpEvent[] Delivery);
}
