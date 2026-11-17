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
    public async Task FiveChildrenUseAtMostFourReadsAndKeepSortedByteIdenticalEffectsAsync()
    {
        var reads = new ControlledReads();
        await using var cleanup = reads.ConfigureAwait(true);
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        reads.Started.Order(StringComparer.Ordinal).ShouldBe(ControlledReads.Sorted.Take(4));
        foreach (string child in new[] { "child-d", "child-c", "child-b" })
        {
            reads.Complete(child);
            await reads.Finished(child).ConfigureAwait(true);
            reads.Started.ShouldNotContain("child-e");
            translation.IsCompleted.ShouldBeFalse();
        }
        reads.Complete("child-a");
        await reads.Entered("child-e").ConfigureAwait(true);
        reads.Peak.ShouldBe(4);
        reads.Complete("child-e");
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
        foreach (string child in ControlledReads.Sorted.Take(4)) reads.Complete(child, child == "child-c" ? failure : null);
        if (failure == "missing")
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        else
            await Should.ThrowAsync<InvalidOperationException>(async () => effects = await translation.ConfigureAwait(true)).ConfigureAwait(true);
        effects.ShouldBeNull();
        reads.Started.ShouldNotContain("child-e");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relationship")]
    [InlineData("corrupt-page")]
    public async Task FifthChildFailureAfterValidatedFirstBatchReturnsNoEffectsAsync(string failure)
    {
        var reads = new ControlledReads();
        await using var cleanup = reads.ConfigureAwait(true);
        IReadOnlyList<ProcessEffect>? effects = null;
        Task<IReadOnlyList<ProcessEffect>> translation = reads.Start();
        await reads.FirstBatchStartedAsync().ConfigureAwait(true);
        foreach (string child in ControlledReads.Sorted.Take(4)) reads.Complete(child);
        await reads.Entered("child-e").ConfigureAwait(true);
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
        reads.Complete("child-c");
        reads.Complete("child-d");
        await reads.Finished("child-b").ConfigureAwait(true);
        translation.IsCompleted.ShouldBeFalse();
        reads.Complete("child-a", firstMissing ? "missing" : "relationship");
        if (firstMissing)
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => translation).ConfigureAwait(true);
        else
            await Should.ThrowAsync<InvalidOperationException>(() => translation).ConfigureAwait(true);
        reads.Started.ShouldNotContain("child-e");
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
        reads.Started.ShouldNotContain("child-e");
        foreach (string child in ControlledReads.Sorted.Take(4)) reads.Complete(child);
        await Task.WhenAll(ControlledReads.Sorted.Take(4).Select(reads.Finished)).ConfigureAwait(true);
        effects.ShouldBeNull();
        reads.Started.ShouldNotContain("child-e");
    }


}
