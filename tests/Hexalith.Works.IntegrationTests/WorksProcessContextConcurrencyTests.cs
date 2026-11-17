using System.Collections.Concurrent;
using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Processes;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Controls real contextual reads to qualify bounded, deterministic all-or-nothing cascade translation.</summary>
[Collection("SDK discovery admission")]
public sealed class WorksProcessContextConcurrencyTests
{
    [Fact]
    public async Task NineChildrenUseAtMostEightReadsAndKeepSortedByteIdenticalEffectsAsync()
    {
        var reads = new ControlledReads();
        await using var cleanup = reads.ConfigureAwait(true);
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        reads.Started.Order(StringComparer.Ordinal).ShouldBe(ControlledReads.Sorted.Take(8));
        foreach (string child in ControlledReads.Sorted.Take(8).Skip(1).Reverse())
        {
            reads.Complete(child);
            await reads.Finished(child).ConfigureAwait(true);
            reads.Started.ShouldNotContain("child-i");
            translation.IsCompleted.ShouldBeFalse();
        }
        reads.Complete("child-a");
        await reads.Entered("child-i").ConfigureAwait(true);
        reads.Peak.ShouldBe(8);
        reads.Complete("child-i");
        IReadOnlyList<ProcessEffect> effects = await translation.ConfigureAwait(true);
        effects.Select(effect => effect.TargetAggregate).ShouldBe(ControlledReads.Sorted);
        foreach (ProcessEffect effect in effects)
        {
            effect.Kind.ShouldBe(EffectKindCatalog.CascadeCancel);
            effect.TargetDomain.ShouldBe("work");
            effect.Ordinal.ShouldBe(0);
            effect.CommandType.ShouldBe(nameof(CancelWorkItem));
            effect.CommandPayload.ShouldBe(JsonSerializer.SerializeToUtf8Bytes(new CancelWorkItem(
                new TenantId(ControlledReads.Tenant), new WorkItemId(effect.TargetAggregate))));
        }
    }

    [Fact]
    public async Task FiveChildrenStartTogetherBeforeAnyChildCompletesAsync()
    {
        var reads = new ControlledReads(childCount: 5);
        await using var cleanup = reads.ConfigureAwait(true);
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        reads.Started.Order(StringComparer.Ordinal).ShouldBe(reads.Children);
        reads.Peak.ShouldBe(5);
        translation.IsCompleted.ShouldBeFalse();
        foreach (string child in reads.Children.Reverse()) reads.Complete(child);
        (await translation.ConfigureAwait(true)).Select(effect => effect.TargetAggregate).ShouldBe(reads.Children);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relationship")]
    [InlineData("corrupt-page")]
    public async Task FailedChildEvidenceReleasesNoEffectsOrLaterBatchAsync(string failure)
    {
        var reads = new ControlledReads();
        await using var cleanup = reads.ConfigureAwait(true);
        IReadOnlyList<ProcessEffect>? effects = null;
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        foreach (string child in ControlledReads.Sorted.Take(8)) reads.Complete(child, child == "child-c" ? failure : null);
        if (failure == "missing")
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        else
            await Should.ThrowAsync<InvalidOperationException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        effects.ShouldBeNull();
        reads.Started.ShouldNotContain("child-i");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relationship")]
    [InlineData("corrupt-page")]
    public async Task NinthChildFailureAfterValidatedFirstBatchReturnsNoEffectsAsync(string failure)
    {
        var reads = new ControlledReads();
        await using var cleanup = reads.ConfigureAwait(true);
        IReadOnlyList<ProcessEffect>? effects = null;
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        foreach (string child in ControlledReads.Sorted.Take(8)) reads.Complete(child);
        await reads.Entered("child-i").ConfigureAwait(true);
        translation.IsCompleted.ShouldBeFalse();
        reads.Complete("child-i", failure);
        if (failure == "missing")
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        else
            await Should.ThrowAsync<InvalidOperationException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        effects.ShouldBeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relationship")]
    [InlineData("corrupt-page")]
    public async Task FifthChildFailureAfterFourCompletedReadsReturnsNoEffectsAsync(string failure)
    {
        var reads = new ControlledReads(childCount: 5);
        await using var cleanup = reads.ConfigureAwait(true);
        IReadOnlyList<ProcessEffect>? effects = null;
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        foreach (string child in reads.Children.Take(4)) reads.Complete(child);
        await Task.WhenAll(reads.Children.Take(4).Select(reads.Finished)).ConfigureAwait(true);
        translation.IsCompleted.ShouldBeFalse();
        reads.Complete("child-e", failure);
        if (failure == "missing")
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        else
            await Should.ThrowAsync<InvalidOperationException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        effects.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstSortedFailureWinsOverScrambledCompletionOrderAsync(bool firstMissing)
    {
        var reads = new ControlledReads();
        await using var cleanup = reads.ConfigureAwait(true);
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        reads.Complete("child-b", firstMissing ? "corrupt-page" : "missing");
        foreach (string child in ControlledReads.Sorted.Take(8).Skip(2)) reads.Complete(child);
        await reads.Finished("child-b").ConfigureAwait(true);
        translation.IsCompleted.ShouldBeFalse();
        reads.Complete("child-a", firstMissing ? "missing" : "relationship");
        if (firstMissing)
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => translation).ConfigureAwait(true);
        else
            await Should.ThrowAsync<InvalidOperationException>(() => translation).ConfigureAwait(true);
        reads.Started.ShouldNotContain("child-i");
    }

    [Fact]
    public async Task CancellationStopsLaterBatchesAndLateProviderCompletionCannotReleaseEffectsAsync()
    {
        var reads = new ControlledReads();
        await using var cleanup = reads.ConfigureAwait(true);
        IReadOnlyList<ProcessEffect>? effects = null;
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        reads.Complete("child-a", "missing");
        await reads.Finished("child-a").ConfigureAwait(true);
        reads.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        reads.Started.ShouldNotContain("child-i");
        foreach (string child in ControlledReads.Sorted.Take(8)) reads.Complete(child);
        await Task.WhenAll(ControlledReads.Sorted.Take(8).Select(reads.Finished)).ConfigureAwait(true);
        effects.ShouldBeNull();
        reads.Started.ShouldNotContain("child-i");
    }


}
