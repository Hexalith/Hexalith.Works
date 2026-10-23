using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Processes;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Works emits stable SDK effects from committed envelope provenance and exact domain relationships.</summary>
public sealed class WorksSdkIntentTranslationTests
{
    private const string Tenant = "tenant-415";
    private static readonly DateTimeOffset s_due = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Several current dates have distinct roles, and same-date re-suspension has a new committed identity.</summary>
    [Fact]
    public async Task AllCurrentDatesAndExpiryHaveCommittedWitnesses()
    {
        const string item = "reminder-item";
        var tenant = new TenantId(Tenant);
        var id = new WorkItemId(item);
        var expiry = new WorkItemExpirySchedule(s_due.AddDays(1),
            ReminderIdentityCodec.ComputeScheduleToken(Tenant, item, s_due.AddDays(1), 1), 1);
        IEventPayload[] facts = [new WorkItemCreated(item, 1, tenant, id, new Obligation("proof"), Expiry: expiry),
            new WorkItemSuspended(item, 2, tenant, id, [AwaitCondition.DateReached(s_due), AwaitCondition.DateReached(s_due.AddHours(1))])];
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                StreamReadPage page = Story48Streams.Page(Tenant, item, facts);
                string[] messages = ["committed-create", "committed-first-suspension", "committed-resume", "committed-second-suspension", "committed-reschedule"];
                return page with { Events = [.. page.Events.Select((fact, index) => fact with { MessageId = messages[index] })] };
            });
        var source = new WorksReminderIntentSource(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));
        var target = new ReminderTarget(Tenant, "work", item);

        IReadOnlyList<ReminderIntent> first = await source.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken);
        first.Count.ShouldBe(3);
        first.Where(intent => intent.Kind == EffectKindCatalog.DateResume).Select(intent => intent.Ordinal).ShouldBe([0L, 1L]);
        first.Single(intent => intent.Kind == EffectKindCatalog.Expiry).SourceMessageId.ShouldBe("committed-create");
        first.Where(intent => intent.Kind == EffectKindCatalog.DateResume).Select(intent => intent.SourceMessageId)
            .ShouldBe(["committed-first-suspension", "committed-first-suspension"]);
        first.Single(intent => intent.Kind == EffectKindCatalog.Expiry).SourceSequence.ShouldBe(1);
        first.Where(intent => intent.Kind == EffectKindCatalog.DateResume).All(intent => intent.SourceSequence == 2).ShouldBeTrue();

        facts = [.. facts, new WorkItemResumed(item, 3, tenant, id, AwaitCondition.DateReached(s_due)),
            new WorkItemSuspended(item, 4, tenant, id, [AwaitCondition.DateReached(s_due)])];
        ReminderIntent repeated = (await source.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken)).Single(intent => intent.Kind == EffectKindCatalog.DateResume);
        repeated.SourceSequence.ShouldBe(4);
        repeated.SourceMessageId.ShouldBe("committed-second-suspension");
        ReminderIdentityCodec.ComputeReminderName(repeated).ShouldNotBe(ReminderIdentityCodec.ComputeReminderName(first.Single(intent => intent.Kind == EffectKindCatalog.DateResume && intent.Ordinal == 0)));
        var replacement = new WorkItemExpirySchedule(s_due.AddDays(2),
            ReminderIdentityCodec.ComputeScheduleToken(Tenant, item, s_due.AddDays(2), 2), 2);
        facts = [.. facts, new WorkItemRescheduled(item, 5, tenant, id, new WorkItemSchedule(), Expiry: replacement)];
        var rescheduled = await source.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken);
        rescheduled.Single(intent => intent.Kind == EffectKindCatalog.Expiry).SourceMessageId.ShouldBe("committed-reschedule");
        rescheduled.Single(intent => intent.Kind == EffectKindCatalog.Expiry).SourceSequence.ShouldBe(5);
        rescheduled.Single(intent => intent.Kind == EffectKindCatalog.Expiry).ScheduleRevision.ShouldBe(2);
        rescheduled.Single(intent => intent.Kind == EffectKindCatalog.DateResume).SourceMessageId.ShouldBe("committed-second-suspension");
    }

    /// <summary>Only date awaits become resume reminders; other await kinds cannot replace the independent expiry witness.</summary>
    [Fact]
    public async Task MixedAwaitSuspensionEmitsOnlyExactDateAndIndependentExpiry()
    {
        const string item = "mixed-await-item";
        var tenant = new TenantId(Tenant);
        var id = new WorkItemId(item);
        AwaitCondition date = AwaitCondition.DateReached(s_due);
        var expiry = new WorkItemExpirySchedule(s_due.AddDays(1),
            ReminderIdentityCodec.ComputeScheduleToken(Tenant, item, s_due.AddDays(1), 1), 1);
        StreamReadPage page = Story48Streams.Page(Tenant, item,
            new WorkItemCreated(item, 1, tenant, id, new Obligation("mixed awaits"), Expiry: expiry),
            new WorkItemSuspended(item, 2, tenant, id,
                [AwaitCondition.ChildCompleted(new WorkItemId("child")), date, AwaitCondition.ExternalSignal("external-proof")]));
        page = page with
        {
            Events = [page.Events[0] with { MessageId = "mixed-create" },
            page.Events[1] with { MessageId = "mixed-suspension" }]
        };
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(page);
        var source = new WorksReminderIntentSource(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));

        IReadOnlyList<ReminderIntent> intents = await source.GetCurrentIntentsAsync(new ReminderTarget(Tenant, "work", item),
            TestContext.Current.CancellationToken);
        intents.Count.ShouldBe(2);
        ReminderIntent resume = intents.Single(intent => intent.Kind == EffectKindCatalog.DateResume);
        resume.DueUtc.ShouldBe(s_due);
        resume.Ordinal.ShouldBe(0);
        resume.SourceSequence.ShouldBe(2);
        resume.ScheduleRevision.ShouldBe(2);
        resume.SourceMessageId.ShouldBe("mixed-suspension");
        ReminderCommand translated = source.TranslateDueIntent(resume);
        translated.CommandType.ShouldBe(nameof(ResumeWorkItem));
        ResumeWorkItem command = JsonSerializer.Deserialize<ResumeWorkItem>(translated.Payload)!;
        command.TenantId.ShouldBe(tenant);
        command.WorkItemId.ShouldBe(id);
        command.AwaitCondition.ShouldBe(date);
        ReminderIntent expires = intents.Single(intent => intent.Kind == EffectKindCatalog.Expiry);
        expires.DueUtc.ShouldBe(expiry.EffectiveExpiryUtc);
        expires.SourceSequence.ShouldBe(1);
        expires.SourceMessageId.ShouldBe("mixed-create");
        expires.ScheduleRevision.ShouldBe(1);
        ReminderCommand translatedExpiry = source.TranslateDueIntent(expires);
        translatedExpiry.CommandType.ShouldBe(nameof(ExpireWorkItem));
        JsonSerializer.Deserialize<ExpireWorkItem>(translatedExpiry.Payload)!.ExpiryWitness.ShouldBe(expiry);
    }

    /// <summary>Requeue retains the exact committed expiry witness; terminal rejection removes every reminder intent.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectedAssignmentRetainsExpiryOnlyWhenRequeued(bool requeue)
    {
        const string item = "requeued-expiry-item";
        var tenant = new TenantId(Tenant);
        var id = new WorkItemId(item);
        var expiry = new WorkItemExpirySchedule(s_due,
            ReminderIdentityCodec.ComputeScheduleToken(Tenant, item, s_due, 1), 1);
        StreamReadPage page = Story48Streams.Page(Tenant, item,
            new WorkItemCreated(item, 1, tenant, id, new Obligation("requeue proof"), Expiry: expiry),
            new WorkItemQueued(item, 2, tenant, id),
            new WorkItemAssigned(item, 3, tenant, id, new(new("party"), Channel.Mcp, AuthorityLevel.Contribute)),
            new WorkItemRejected(item, 4, tenant, id, requeue));
        page = page with { Events = [page.Events[0] with { MessageId = "committed-expiry-create" }, .. page.Events.Skip(1)] };
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(page);
        var source = new WorksReminderIntentSource(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));
        StreamReadPage rejectedHistory = page;
        page = page with { Events = [.. page.Events.Take(3)], Metadata = page.Metadata with { EventCount = 3, LastSequenceReturned = 3, LatestSequence = 3 } };
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(_ => page);
        var target = new ReminderTarget(Tenant, "work", item);
        ReminderIntent original = (await source.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        page = rejectedHistory;
        IReadOnlyList<ReminderIntent> intents = await source.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken);
        if (!requeue)
        {
            intents.ShouldBeEmpty();
            return;
        }
        ReminderIntent intent = intents.ShouldHaveSingleItem();
        ReminderIdentityCodec.ComputeReminderName(intent).ShouldBe(ReminderIdentityCodec.ComputeReminderName(original));
        intent.Payload.ShouldBe(original.Payload);
        intent.Ordinal.ShouldBe(0);
        intent.Kind.ShouldBe(EffectKindCatalog.Expiry);
        intent.Tenant.ShouldBe(Tenant);
        intent.TargetDomain.ShouldBe("work");
        intent.TargetAggregate.ShouldBe(item);
        intent.SourceDomain.ShouldBe("work");
        intent.SourceAggregate.ShouldBe(item);
        intent.SourceSequence.ShouldBe(1);
        intent.SourceMessageId.ShouldBe("committed-expiry-create");
        intent.ScheduleRevision.ShouldBe(expiry.Revision);
        intent.DueUtc.ShouldBe(expiry.EffectiveExpiryUtc);
        intent.PayloadType.ShouldBe(nameof(ExpireWorkItem));
        ReminderCommand translated = source.TranslateDueIntent(intent);
        translated.CommandType.ShouldBe(nameof(ExpireWorkItem));
        translated.Payload.ShouldBe(intent.Payload);
        ExpireWorkItem command = JsonSerializer.Deserialize<ExpireWorkItem>(translated.Payload)!;
        command.TenantId.ShouldBe(tenant);
        command.WorkItemId.ShouldBe(id);
        command.ExpiryWitness.ShouldBe(expiry);
    }

    /// <summary>Current translation requires the exact await; the SDK retained plan owns receipt replay after consumption.</summary>
    /// <param name="pageSize">The maximum contextual envelopes per gateway page.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(256)]
    public async Task ChildResumeRequiresCurrentExactAwaitWhileSdkOwnsReceiptReplay(int pageSize)
    {
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId("parent");
        var child = new WorkItemId("child");
        AwaitCondition condition = AwaitCondition.ChildCompleted(child);
        StreamReadPage childPage = Story48Streams.Page(Tenant, child.Value,
            new WorkItemCreated(child.Value, 1, tenant, child, new Obligation("child"), Parent: new ParentWorkItemReference(tenant, parent)),
            new WorkItemCompleted(child.Value, 2, tenant, child));
        StreamReadEvent completion = childPage.Events[1];
        IEventPayload[] parentFacts = [new WorkItemCreated(parent.Value, 1, tenant, parent, new Obligation("parent")),
            new ChildSpawned(parent.Value, 2, tenant, parent, child, new Obligation("child")),
            new WorkItemSuspended(parent.Value, 3, tenant, parent, [condition])];
        StreamReadPage parentPage = Story48Streams.Page(Tenant, parent.Value, parentFacts);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => SlicePage(call.Arg<StreamReadRequest>().AggregateId == child.Value ? childPage : parentPage,
                call.Arg<StreamReadRequest>(), pageSize));
        var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));
        var source = new ProcessSource(Tenant, "work", child.Value);

        ProcessEffect before = (await translator.TranslateAsync(source, completion, TestContext.Current.CancellationToken)).Single();
        parentPage = Story48Streams.Page(Tenant, parent.Value,
            [.. parentFacts, new WorkItemResumed(parent.Value, 4, tenant, parent, condition)]);
        parentPage = parentPage with { Events = [.. parentPage.Events.Take(3), parentPage.Events[3] with { CausationId = completion.MessageId }] };
        (await translator.TranslateAsync(source, completion, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        before.Kind.ShouldBe(EffectKindCatalog.ChildCompletionResume);
        before.TargetAggregate.ShouldBe(parent.Value);
        before.Ordinal.ShouldBe(0);
        JsonSerializer.Deserialize<ResumeWorkItem>(before.CommandPayload)!.AwaitCondition.ShouldBe(condition);
    }

    /// <summary>Expired parents map each direct child to delegated cascade expiry without a scheduled witness.</summary>
    /// <param name="pageSize">The maximum contextual envelopes per gateway page.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(256)]
    public async Task ExpiredParentMapsChildrenToCascadeExpiry(int pageSize)
    {
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId("expired-parent");
        WorkItemId[] children = [new("child-a"), new("child-b")];
        StreamReadPage parentPage = Story48Streams.Page(Tenant, parent.Value,
            new WorkItemCreated(parent.Value, 1, tenant, parent, new Obligation("parent")),
            new ChildSpawned(parent.Value, 2, tenant, parent, children[0], new Obligation("a")),
            new ChildSpawned(parent.Value, 3, tenant, parent, children[1], new Obligation("b")),
            new WorkItemExpired(parent.Value, 4, tenant, parent));
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => SlicePage(call.Arg<StreamReadRequest>().AggregateId == parent.Value ? parentPage
                : Story48Streams.Page(Tenant, call.Arg<StreamReadRequest>().AggregateId!,
                    new WorkItemCreated(call.Arg<StreamReadRequest>().AggregateId!, 1, tenant,
                        new WorkItemId(call.Arg<StreamReadRequest>().AggregateId!), new Obligation("child"),
                        Parent: new ParentWorkItemReference(tenant, parent))), call.Arg<StreamReadRequest>(), pageSize));
        var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));
        IReadOnlyList<ProcessEffect> effects = await translator.TranslateAsync(new ProcessSource(Tenant, "work", parent.Value),
            parentPage.Events[^1], TestContext.Current.CancellationToken);
        effects.Select(effect => effect.TargetAggregate).ShouldBe(children.Select(child => child.Value));
        foreach (ProcessEffect effect in effects)
        {
            effect.Kind.ShouldBe(EffectKindCatalog.CascadeExpire);
            effect.Ordinal.ShouldBe(0);
            effect.TargetDomain.ShouldBe("work");
            effect.CommandType.ShouldBe(nameof(ExpireWorkItem));
            ExpireWorkItem command = JsonSerializer.Deserialize<ExpireWorkItem>(effect.CommandPayload)!;
            command.TenantId.ShouldBe(tenant);
            command.WorkItemId.Value.ShouldBe(effect.TargetAggregate);
            command.ExpiryWitness.ShouldBeNull();
            using JsonDocument payload = JsonDocument.Parse(effect.CommandPayload);
            payload.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, nameof(ExpireWorkItem.ExpiryWitness), StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        }
    }

    /// <summary>Committed source position, rather than payload sequence, governs every deterministic target identity.</summary>
    [Fact]
    public async Task TerminalTranslationUsesEnvelopeAndRetainsPlanAfterChildTerminates()
    {
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId("parent");
        var child = new WorkItemId("child");
        StreamReadPage parentPage = Story48Streams.Page(Tenant, parent.Value,
            new WorkItemCreated(parent.Value, 1, tenant, parent, new Obligation("parent")),
            new ChildSpawned(parent.Value, 2, tenant, parent, child, new Obligation("child")),
            new Hexalith.Works.Contracts.Events.Rejections.WorkItemTransitionRejected(tenant, parent, WorkItemStatus.Created, "invalid"),
            new WorkItemCancelled(parent.Value, 3, tenant, parent));
        StreamReadPage childPage = Story48Streams.Page(Tenant, child.Value,
            new WorkItemCreated(child.Value, 1, tenant, child, new Obligation("child"), Parent: new ParentWorkItemReference(tenant, parent)));
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<StreamReadRequest>().AggregateId == parent.Value ? parentPage : childPage);
        var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));
        var source = new ProcessSource(Tenant, "work", parent.Value);
        StreamReadEvent terminal = parentPage.Events[3];
        ProcessEffect first = (await translator.TranslateAsync(source, terminal, TestContext.Current.CancellationToken)).Single();
        childPage = Story48Streams.Page(Tenant, child.Value,
            new WorkItemCreated(child.Value, 1, tenant, child, new Obligation("child"), Parent: new ParentWorkItemReference(tenant, parent)),
            new WorkItemCancelled(child.Value, 2, tenant, child));
        ProcessEffect replay = (await translator.TranslateAsync(source, terminal, TestContext.Current.CancellationToken)).Single();
        replay.CommandPayload.ShouldBe(first.CommandPayload);
        terminal.SequenceNumber.ShouldBe(4);
        ((WorkItemCancelled)WorksEventDecoder.Decode(terminal.EventTypeName, terminal.Payload)!).Sequence.ShouldBe(3);
    }
    /// <summary>Both recovery paths refuse corrupt contextual evidence and recover on a later complete read.</summary>
    /// <param name="cascade">Whether to qualify parent cancellation or child-completion recovery.</param>
    /// <param name="failure">The page, payload or relationship corruption to inject.</param>
    [Theory]
    [InlineData(true, "domain")]
    [InlineData(false, "domain")]
    [InlineData(true, "aggregate")]
    [InlineData(false, "aggregate")]
    [InlineData(true, "payload-tenant")]
    [InlineData(false, "payload-tenant")]
    [InlineData(true, "payload-aggregate")]
    [InlineData(false, "payload-aggregate")]
    [InlineData(true, "malformed")]
    [InlineData(false, "malformed")]
    [InlineData(true, "unknown")]
    [InlineData(false, "unknown")]
    [InlineData(true, "relationship")]
    [InlineData(false, "relationship")]
    [InlineData(true, "malformed-spawn")]
    [InlineData(false, "malformed-spawn")]
    public async Task InvalidRecoveryContextCannotProduceEffects(bool cascade, string failure)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId("parent");
        var child = new WorkItemId("child");
        var firstChild = new WorkItemId("a-child");
        IEventPayload terminal = cascade
            ? new WorkItemCancelled(parent.Value, 4, tenant, parent)
            : new WorkItemSuspended(parent.Value, 3, tenant, parent, [AwaitCondition.ChildCompleted(child)]);
        IEventPayload[] parentFacts = cascade ? [
            new WorkItemCreated(parent.Value, 1, tenant, parent, new Obligation("parent")),
            new ChildSpawned(parent.Value, 2, tenant, parent, firstChild, new Obligation("first child")),
            new ChildSpawned(parent.Value, 3, tenant, parent, child, new Obligation("child")), terminal] : [
            new WorkItemCreated(parent.Value, 1, tenant, parent, new Obligation("parent")),
            new ChildSpawned(parent.Value, 2, tenant, parent, child, new Obligation("child")), terminal];
        StreamReadPage parentPage = Story48Streams.Page(Tenant, parent.Value, parentFacts);
        StreamReadPage firstChildPage = Story48Streams.Page(Tenant, firstChild.Value,
            new WorkItemCreated(firstChild.Value, 1, tenant, firstChild, new Obligation("first child"),
                Parent: new ParentWorkItemReference(tenant, parent)));
        StreamReadPage childPage = Story48Streams.Page(Tenant, child.Value,
            new WorkItemCreated(child.Value, 1, tenant, child, new Obligation("child"),
                Parent: new ParentWorkItemReference(tenant, parent)),
            new WorkItemCompleted(child.Value, 2, tenant, child));
        StreamReadPage related = cascade ? childPage : parentPage;
        StreamReadPage invalid = related;
        if (failure is "domain" or "aggregate")
            invalid = failure == "domain" ? related with { Domain = "foreign" } : related with { AggregateId = "foreign" };
        else if (failure == "unknown")
            invalid = related with
            {
                Events = [.. related.Events.Take(related.Events.Count - 1),
                related.Events[^1] with { EventTypeName = "FutureWorksFact" }]
            };
        else if (failure != "malformed-spawn")
        {
            int index = failure == "relationship" && !cascade ? 1 : 0;
            var node = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<JsonElement>(related.Events[index].Payload))!.AsObject();
            switch (failure)
            {
                case "payload-tenant": node["TenantId"] = JsonSerializer.SerializeToNode(new TenantId("foreign")); break;
                case "payload-aggregate": node["AggregateId"] = "foreign"; break;
                case "malformed": node.Remove("Obligation"); break;
                case "relationship" when cascade:
                    node["Parent"] = JsonSerializer.SerializeToNode(new ParentWorkItemReference(tenant, new WorkItemId("other-parent")));
                    break;
                case "relationship": node["ChildWorkItemId"] = JsonSerializer.SerializeToNode(new WorkItemId("other-child")); break;
            }
            invalid = related with
            {
                Events = [.. related.Events.Select((envelope, ordinal) => ordinal == index
                ? envelope with { Payload = JsonSerializer.SerializeToUtf8Bytes(node) } : envelope)]
            };
        }
        StreamReadPage invalidParent = parentPage;
        if (failure == "malformed-spawn")
        {
            int spawnIndex = cascade ? 2 : 1;
            var spawn = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<JsonElement>(parentPage.Events[spawnIndex].Payload))!.AsObject();
            spawn.Remove("ChildWorkItemId");
            invalidParent = parentPage with
            {
                Events = [.. parentPage.Events.Select((envelope, ordinal) => ordinal == spawnIndex
                    ? envelope with { Payload = JsonSerializer.SerializeToUtf8Bytes(spawn) } : envelope)],
            };
        }
        bool corrected = false;
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.Arg<StreamReadRequest>();
            request.Tenant.ShouldBe(Tenant);
            request.Domain.ShouldBe("work");
            StreamReadPage selected = request.AggregateId == parent.Value ? parentPage
                : request.AggregateId == firstChild.Value ? firstChildPage : childPage;
            if (!corrected)
            {
                if (failure == "malformed-spawn" && request.AggregateId == parent.Value) selected = invalidParent;
                else if (request.AggregateId == related.AggregateId) selected = invalid;
            }
            return SlicePage(selected, request, pageSize: 1);
        });
        var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));
        var source = new ProcessSource(Tenant, "work", cascade ? parent.Value : child.Value);
        StreamReadEvent trigger = cascade ? parentPage.Events[^1] : childPage.Events[^1];
        IReadOnlyList<ProcessEffect>? effects = null;

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            effects = await translator.TranslateAsync(source, trigger, ct).ConfigureAwait(false));
        effects.ShouldBeNull();
        if (cascade && failure != "malformed-spawn")
            await gateway.Received(1).ReadWorkloadStreamAsync(
                Arg.Is<StreamReadRequest>(request => request.AggregateId == firstChild.Value), Arg.Any<CancellationToken>());

        corrected = true;
        IReadOnlyList<ProcessEffect> recovered = await translator.TranslateAsync(source, trigger, ct);
        recovered.Count.ShouldBe(cascade ? 2 : 1);
        if (cascade) recovered.Select(effect => effect.TargetAggregate).ShouldBe(new[] { child.Value, firstChild.Value }.Order(StringComparer.Ordinal));
        ProcessEffect effect = recovered.Single(effect => effect.TargetAggregate == (cascade ? child.Value : parent.Value));
        effect.Kind.ShouldBe(cascade ? EffectKindCatalog.CascadeCancel : EffectKindCatalog.ChildCompletionResume);
        effect.TargetDomain.ShouldBe("work");
        effect.TargetAggregate.ShouldBe(cascade ? child.Value : parent.Value);
        effect.Ordinal.ShouldBe(EffectKindCatalog.PrimaryTargetOrdinal);
        effect.CommandType.ShouldBe(cascade ? nameof(CancelWorkItem) : nameof(ResumeWorkItem));
        if (cascade)
        {
            CancelWorkItem command = JsonSerializer.Deserialize<CancelWorkItem>(effect.CommandPayload).ShouldNotBeNull();
            command.TenantId.ShouldBe(tenant);
            command.WorkItemId.ShouldBe(child);
        }
        else
        {
            ResumeWorkItem command = JsonSerializer.Deserialize<ResumeWorkItem>(effect.CommandPayload).ShouldNotBeNull();
            command.TenantId.ShouldBe(tenant);
            command.WorkItemId.ShouldBe(parent);
            command.AwaitCondition.ShouldBe(AwaitCondition.ChildCompleted(child));
        }
    }

    /// <summary>Returns a real exclusive range from complete evidence, preserving committed envelope metadata.</summary>
    private static StreamReadPage SlicePage(StreamReadPage complete, StreamReadRequest request, int pageSize)
    {
        request.ContinuationToken.ShouldBeNull();
        StreamReadEvent[] selected = [.. complete.Events.Where(envelope => envelope.SequenceNumber > request.FromSequence).Take(pageSize)];
        long last = selected.Length == 0 ? request.FromSequence : selected[^1].SequenceNumber;
        return complete with
        {
            Events = selected,
            Metadata = complete.Metadata with
            {
                FromSequence = request.FromSequence,
                LastSequenceReturned = selected.Length == 0 ? null : last,
                EventCount = selected.Length,
                IsTruncated = last < complete.Metadata.LatestSequence,
            },
        };
    }

}
