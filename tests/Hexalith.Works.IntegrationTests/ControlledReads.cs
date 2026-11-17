using Hexalith.EventStore.Contracts.Processes;
using System.Collections.Concurrent;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Hexalith.Works.IntegrationTests;

internal sealed class ControlledReads : IAsyncDisposable
{
    internal const string Tenant = "context-tenant";
    private const string Parent = "parent";
    internal static readonly string[] Sorted = ["child-a", "child-b", "child-c", "child-d", "child-e", "child-f", "child-g", "child-h", "child-i"];
    private readonly Dictionary<string, TaskCompletionSource<StreamReadPage>> _gates = Sorted.ToDictionary(child => child,
        _ => new TaskCompletionSource<StreamReadPage>(TaskCreationOptions.RunContinuationsAsynchronously));
    private readonly Dictionary<string, TaskCompletionSource> _entered = Sorted.ToDictionary(child => child,
        _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    private readonly Dictionary<string, TaskCompletionSource> _finished = Sorted.ToDictionary(child => child,
        _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    private readonly CancellationTokenSource _caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly ConcurrentBag<Task<StreamReadPage>> _workers = [];
    private readonly ConcurrentQueue<string> _started = [];
    private readonly WorksProcessTranslator _translator;
    private readonly StreamReadPage _parent;
    private readonly Dictionary<string, StreamReadPage> _children;
    private Task<IReadOnlyList<ProcessEffect>>? _translation;
    private int _active;
    private int _peak;

    internal ControlledReads(int childCount = 9)
    {
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(Parent);
        Children = Sorted.Take(childCount).ToArray();
        string[] scrambled = [.. Children.Reverse()];
        IEventPayload[] history = [new WorkItemCreated(Parent, 1, tenant, parent, new Obligation("parent")),
            .. scrambled.Select((child, index) => (IEventPayload)new ChildSpawned(Parent, index + 2, tenant, parent,
                new WorkItemId(child), new Obligation("child"))), new WorkItemCancelled(Parent, childCount + 2, tenant, parent)];
        _parent = Story48Streams.Page(Tenant, Parent, history);
        _children = Sorted.ToDictionary(child => child, child => ChildPage(child));
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            string child = call.Arg<StreamReadRequest>().AggregateId!;
            if (child == Parent) return Task.FromResult(_parent);
            Task<StreamReadPage> worker = ReadAsync(child); _workers.Add(worker); return worker;
        });
        _translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway,
            Options.Create(new WorksRecoveryOptions { ContextReadTimeout = TimeSpan.FromSeconds(10) })));
        _caller.CancelAfter(TimeSpan.FromSeconds(15));
    }

    internal string[] Children { get; }
    internal string[] Started => _started.ToArray();
    internal int Peak => Volatile.Read(ref _peak);
    internal int Active => Volatile.Read(ref _active);
    internal Task<IReadOnlyList<ProcessEffect>> Start() => _translation = _translator.TranslateAsync(
        new ProcessSource(Tenant, "work", Parent), _parent.Events[^1], _caller.Token).AsTask();
    internal Task Entered(string child) => _entered[child].Task.WaitAsync(TimeSpan.FromSeconds(5), _caller.Token);
    internal Task Finished(string child) => _finished[child].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    internal Task FirstBatchStartedAsync() => Task.WhenAll(Children.Take(8).Select(Entered));
    internal void Cancel() => _caller.Cancel();
    internal void Complete(string child, string? failure = null)
    {
        if (failure == "missing")
            _gates[child].TrySetException(new EventStoreGatewayException(404, "Missing stream", reasonCode: StreamReplayReasonCodes.MissingStream));
        else
        {
            StreamReadPage page = failure == "relationship" ? ChildPage(child, "foreign-parent") : _children[child];
            if (failure == "corrupt-page") page = page with { Events = [page.Events[0] with { MetadataVersion = 99 }] };
            _gates[child].TrySetResult(page);
        }
    }

    private static StreamReadPage ChildPage(string child, string parent = Parent) => Story48Streams.Page(Tenant, child,
        new WorkItemCreated(child, 1, new TenantId(Tenant), new WorkItemId(child), new Obligation("child"),
            Parent: new ParentWorkItemReference(new TenantId(Tenant), new WorkItemId(parent))));

    private async Task<StreamReadPage> ReadAsync(string child)
    {
        int active = Interlocked.Increment(ref _active);
        int peak;
        do { peak = Volatile.Read(ref _peak); } while (peak < active && Interlocked.CompareExchange(ref _peak, active, peak) != peak);
        _started.Enqueue(child); _entered[child].TrySetResult();
        try { return await _gates[child].Task.ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _active); _finished[child].TrySetResult(); }
    }

    public async ValueTask DisposeAsync()
    {
        _caller.Cancel();
        foreach (string child in Sorted) Complete(child);
        if (_translation is not null)
        {
            try { await _translation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        try { await Task.WhenAll(_workers).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (EventStoreGatewayException) { }
        _caller.Dispose();
    }
}
