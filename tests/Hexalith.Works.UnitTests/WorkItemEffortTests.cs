using Hexalith.Works.Contracts.ValueObjects;

using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>
/// Coverage for the effort model behind AC #3: estimated effort derives Remaining, the
/// Remaining=0 completion rule is only active once work is fully done, and invalid effort
/// inputs are rejected at construction rather than silently coerced.
/// </summary>
public sealed class WorkItemEffortTests
{
    [Fact]
    public void WorkItemEffort_defaults_done_to_zero_and_derives_remaining()
    {
        var effort = new WorkItemEffort(8m, new Unit("hour"));

        effort.Estimated.ShouldBe(8m);
        effort.Done.ShouldBe(0m);
        effort.Remaining.ShouldBe(8m);
    }

    [Theory]
    [InlineData(8, 0, 8)]
    [InlineData(8, 3, 5)]
    [InlineData(8, 8, 0)]
    public void WorkItemEffort_remaining_is_estimated_minus_done(int estimated, int done, int expectedRemaining)
    {
        var effort = new WorkItemEffort(estimated, new Unit("hour"), done);

        effort.Remaining.ShouldBe(expectedRemaining);
    }

    [Fact]
    public void WorkItemEffort_rejects_negative_estimated()
        => Should.Throw<ArgumentOutOfRangeException>(() => new WorkItemEffort(-1m, new Unit("hour")));

    [Fact]
    public void WorkItemEffort_rejects_negative_done()
        => Should.Throw<ArgumentOutOfRangeException>(() => new WorkItemEffort(8m, new Unit("hour"), -1m));

    [Fact]
    public void WorkItemEffort_rejects_done_greater_than_estimated()
        => Should.Throw<ArgumentOutOfRangeException>(() => new WorkItemEffort(8m, new Unit("hour"), 9m));

    [Fact]
    public void WorkItemEffort_rejects_null_unit()
        => Should.Throw<ArgumentNullException>(() => new WorkItemEffort(8m, null!));

    [Fact]
    public void WorkItemEffort_report_accumulates_done_and_re_derives_remaining()
    {
        // AC #1/#2: Report returns a new effort with Done advanced by the delta and Remaining re-derived;
        // chaining reports accumulates without ever storing Remaining.
        WorkItemEffort first = new WorkItemEffort(8m, new Unit("hour")).Report(3m);
        first.Done.ShouldBe(3m);
        first.Remaining.ShouldBe(5m);

        WorkItemEffort second = first.Report(2m);
        second.Done.ShouldBe(5m);
        second.Remaining.ShouldBe(3m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void WorkItemEffort_report_rejects_non_positive_delta(int delta)
        => Should.Throw<ArgumentOutOfRangeException>(() => new WorkItemEffort(8m, new Unit("hour")).Report(delta));
}
