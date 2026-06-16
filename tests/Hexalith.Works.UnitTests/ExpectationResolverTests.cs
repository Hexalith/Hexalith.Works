using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Ports;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Resolvers;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class ExpectationResolverTests
{
    [Fact]
    public async Task LiteralResolver_returns_the_reference_value_verbatim_without_interpreting_it()
    {
        var resolver = new LiteralExpectationResolver();
        var reference = new ExpectationReference("deliver the onboarding packet");

        Expectation resolved = (await resolver.ResolveAsync(reference, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        // No interpretation in v1: the result echoes the reference value verbatim, unchanged.
        resolved.InterpretedValue.ShouldBe(reference.Value);
    }

    [Fact]
    public async Task LiteralResolver_never_throws_for_a_valid_reference()
    {
        var resolver = new LiteralExpectationResolver();

        Expectation? resolved = await resolver.ResolveAsync(new ExpectationReference("ref-1"), TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
    }

    [Fact]
    public async Task LiteralResolver_is_deterministic_resolving_the_same_reference_twice_yields_equal_values()
    {
        // No clock, RNG, or LLM in v1 (NFR-11): resolving the same reference twice must be deterministic.
        var resolver = new LiteralExpectationResolver();
        var reference = new ExpectationReference("ref-determinism");

        Expectation first = (await resolver.ResolveAsync(reference, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        Expectation second = (await resolver.ResolveAsync(reference, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        second.InterpretedValue.ShouldBe(first.InterpretedValue);
        first.InterpretedValue.ShouldBe(reference.Value);
    }

    [Fact]
    public void LiteralResolver_throws_for_a_null_reference()
    {
        var resolver = new LiteralExpectationResolver();

        // The guard runs synchronously before the ValueTask is produced, so the throw surfaces on invocation.
        Should.Throw<ArgumentNullException>(() =>
        {
            _ = resolver.ResolveAsync(null!, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public void ExpectationReference_trims_its_pointer_and_rejects_a_blank_value()
    {
        new ExpectationReference("  ref-1  ").Value.ShouldBe("ref-1");

        Should.Throw<ArgumentException>(() => new ExpectationReference("   "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExpectationReference_rejects_a_null_or_blank_value(string? value)
        => Should.Throw<ArgumentException>(() => new ExpectationReference(value!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Expectation_rejects_a_null_or_blank_interpreted_value(string? interpretedValue)
        => Should.Throw<ArgumentException>(() => new Expectation(interpretedValue!));

    [Fact]
    public void WorkItem_is_valid_when_obligation_carries_no_expectation_reference()
    {
        var created = new WorkItemCreated(
            "work-001",
            1,
            new TenantId("tenant-alpha"),
            new WorkItemId("work-001"),
            new Obligation("Prepare the work item"));

        created.Obligation.Reference.ShouldBeNull();

        var state = new WorkItemState();
        state.Apply(created);

        state.Status.ShouldBe(WorkItemStatus.Created);
        state.Obligation.ShouldNotBeNull().Reference.ShouldBeNull();
    }

    [Fact]
    public void WorkItem_replays_obligation_expectation_reference_without_materializing_an_interpreted_expectation()
    {
        var reference = new ExpectationReference("expectation-ref-001");
        var created = new WorkItemCreated(
            "work-001",
            1,
            new TenantId("tenant-alpha"),
            new WorkItemId("work-001"),
            new Obligation("Prepare the work item", reference));

        var state = new WorkItemState();
        state.Apply(created);

        state.Status.ShouldBe(WorkItemStatus.Created);
        Obligation obligation = state.Obligation.ShouldNotBeNull();
        obligation.Reference.ShouldBe(reference);
        obligation.Reference.ShouldNotBeNull().Value.ShouldBe("expectation-ref-001");

        // Replayed state exposes the reference only — never a resolved Expectation.
        state.GetType().GetProperties()
            .ShouldNotContain(property => property.PropertyType == typeof(Expectation));
    }
}
