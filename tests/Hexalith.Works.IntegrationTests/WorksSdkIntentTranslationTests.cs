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
        page = page with { Events = [page.Events[0] with { MessageId = "mixed-create" },
            page.Events[1] with { MessageId = "mixed-suspension" }] };
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

    /// <summary>Current translation requires the exact await; the SDK retained plan owns receipt replay after consumption.</summary>
    [Fact]
    public async Task ChildResumeRequiresCurrentExactAwaitWhileSdkOwnsReceiptReplay()
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
            .Returns(call => call.Arg<StreamReadRequest>().AggregateId == child.Value ? childPage : parentPage);
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
    [Fact]
    public async Task ExpiredParentMapsChildrenToCascadeExpiry()
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
            .Returns(call => call.Arg<StreamReadRequest>().AggregateId == parent.Value ? parentPage
                : Story48Streams.Page(Tenant, call.Arg<StreamReadRequest>().AggregateId!,
                    new WorkItemCreated(call.Arg<StreamReadRequest>().AggregateId!, 1, tenant,
                        new WorkItemId(call.Arg<StreamReadRequest>().AggregateId!), new Obligation("child"),
                        Parent: new ParentWorkItemReference(tenant, parent))));
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
}
