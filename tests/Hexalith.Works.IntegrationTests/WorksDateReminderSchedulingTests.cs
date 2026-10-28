using System.Reflection;
using System.Text.Json;

using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Executes SDK scheduling against real Works intents after earlier operations consume wall-clock time.</summary>
public sealed class WorksDateReminderSchedulingTests
{
    private const string Tenant = "tenant-date-scheduling";
    private const string ActorType = "WorksDateSchedulingProof";
    private static readonly DateTimeOffset s_start = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Assembly s_sdk = typeof(EventStoreReminderOptions).Assembly;

    /// <summary>Actual SDK submissions replay an existing occurrence receipt and admit a later same-date suspension independently.</summary>
    [Fact]
    public async Task SameDateOccurrencesHaveDistinctSdkSubmissionsAndStableReceiptReplay()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "same-date-occurrences";
        var target = new ReminderTarget(Tenant, "work", item);
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(s_start);
        var store = new InMemoryReadModelStore();
        AwaitCondition date = AwaitCondition.DateReached(s_start.AddMinutes(-1));
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [item] = Page(item, date.Instant!.Value),
        };
        var calls = new List<(TrustedEffectSubmission Submission, TrustedEffectContext Context)>();
        int receiptWrites = 0;
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                TrustedEffectSubmission submission = call.Arg<TrustedEffectSubmission>();
                calls.Add((submission, call.Arg<TrustedEffectContext>()));
                string effectId = EffectIdentityCodec.ComputeEffectId(submission.Identity);
                string key = "synthetic-target-receipt:" + effectId;
                TrustedEffectResult? prior = (await store.GetAsync<TrustedEffectResult>("statestore", key, ct).ConfigureAwait(false)).Value;
                if (prior is not null) return prior with { Replayed = true };
                var receipt = new TrustedEffectResult(effectId, TrustedEffectDisposition.Success, false, null);
                await store.SaveAsync("statestore", key, receipt, ct).ConfigureAwait(false);
                receiptWrites++;
                return receipt;
            });
        IReminderDelegationTokenProvider delegation = Substitute.For<IReminderDelegationTokenProvider>();
        delegation.GetDelegationTokenAsync(Arg.Any<ReminderDelegationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("synthetic-delegation"));
        WorksReminderSchedulerProxy scheduler = Scheduler();
        for (int occurrence = 0; occurrence < 2; occurrence++)
        {
            if (occurrence == 1)
            {
                pages[item] = Story48Streams.Page(Tenant, item,
                    new WorkItemCreated(item, 1, new TenantId(Tenant), new WorkItemId(item), new Obligation("scheduler timing")),
                    new WorkItemSuspended(item, 2, new TenantId(Tenant), new WorkItemId(item), [date]),
                    new WorkItemResumed(item, 3, new TenantId(Tenant), new WorkItemId(item), date),
                    new WorkItemSuspended(item, 4, new TenantId(Tenant), new WorkItemId(item), [date]));
            }
            // Every operation receives newly composed SDK services over retained witness and target-receipt storage.
            for (int replay = 0; replay < 2; replay++)
            {
                using ServiceProvider provider = Services(store, clock, pages, submitter: submitter, delegation: delegation);
                using IServiceScope scope = provider.CreateScope();
                ReminderIntent intent = (await scope.ServiceProvider.GetRequiredService<IReminderIntentSource>()
                    .GetCurrentIntentsAsync(target, ct)).ShouldHaveSingleItem();
                ReminderConvergenceResult result = await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct);
                result.Submitted.ShouldBe(1);
                result.Unresolved.ShouldBe(0);
                result.Quarantined.ShouldBe(0);
                (TrustedEffectSubmission submission, TrustedEffectContext context) = calls[^1];
                submission.Identity.SourceEnvelopeSequence.ShouldBe(occurrence == 0 ? 2 : 4);
                submission.Identity.Ordinal.ShouldBe(intent.Ordinal);
                submission.MessageId.ShouldBe("wrk-" + EffectIdentityCodec.ComputeEffectId(submission.Identity));
                submission.IdempotencyKey.ShouldBe(submission.MessageId);
                submission.CommandType.ShouldBe(nameof(ResumeWorkItem));
                ResumeWorkItem command = JsonSerializer.Deserialize<ResumeWorkItem>(submission.CommandPayload).ShouldNotBeNull();
                command.TenantId.ShouldBe(new TenantId(Tenant));
                command.WorkItemId.ShouldBe(new WorkItemId(item));
                command.AwaitCondition.ShouldBe(date);
                context.CausationId.ShouldBe(intent.SourceMessageId);
                context.Workload.ShouldBe("works");
                context.Purpose.ShouldBe("works.date-resume");
                string effectId = EffectIdentityCodec.ComputeEffectId(submission.Identity);
                store.Snapshot<TrustedEffectResult>("statestore", "synthetic-target-receipt:" + effectId)
                    .ShouldNotBeNull().Disposition.ShouldBe(TrustedEffectDisposition.Success);
                AssertSubmittedAudit(store, item, ReminderIdentityCodec.ComputeReminderName(intent), effectId, replay == 1);
            }
        }
        calls.Count.ShouldBe(4);
        calls[0].Submission.MessageId.ShouldBe(calls[1].Submission.MessageId);
        calls[2].Submission.MessageId.ShouldBe(calls[3].Submission.MessageId);
        calls[2].Submission.MessageId.ShouldNotBe(calls[0].Submission.MessageId);
        receiptWrites.ShouldBe(2);
        scheduler.Held.ShouldBeEmpty();
    }

    /// <summary>An old occurrence callback cannot submit after a later same-date suspension has its own retained witness.</summary>
    [Fact]
    public async Task OldOccurrenceCallbackPreservesCurrentSameDateWitness()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "stale-date-callback";
        var target = new ReminderTarget(Tenant, "work", item);
        DateTimeOffset now = s_start;
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => now);
        var store = new InMemoryReadModelStore();
        DateTimeOffset due = s_start.AddMinutes(1);
        AwaitCondition date = AwaitCondition.DateReached(due);
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal) { [item] = Page(item, due) };
        WorksReminderSchedulerProxy scheduler = Scheduler();
        var submissions = new List<TrustedEffectSubmission>();
        ITrustedEffectSubmitter submitter = SuccessSubmitter(store, submissions);
        IReminderDelegationTokenProvider delegation = Delegation();
        string oldName;
        using (ServiceProvider provider = Services(store, clock, pages, submitter: submitter, delegation: delegation))
        using (IServiceScope scope = provider.CreateScope())
        {
            (await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct)).Armed.ShouldBe(1);
            oldName = scheduler.Held.ShouldHaveSingleItem();
        }
        pages[item] = Story48Streams.Page(Tenant, item,
            new WorkItemCreated(item, 1, new TenantId(Tenant), new WorkItemId(item), new Obligation("scheduler timing")),
            new WorkItemSuspended(item, 2, new TenantId(Tenant), new WorkItemId(item), [date]),
            new WorkItemResumed(item, 3, new TenantId(Tenant), new WorkItemId(item), date),
            new WorkItemSuspended(item, 4, new TenantId(Tenant), new WorkItemId(item), [date]));
        string currentName;
        using (ServiceProvider provider = Services(store, clock, pages, submitter: submitter, delegation: delegation))
        using (IServiceScope scope = provider.CreateScope())
        {
            ReminderConvergenceResult result = await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct);
            result.Armed.ShouldBe(1);
            result.Cancelled.ShouldBe(1);
            currentName = scheduler.Held.ShouldHaveSingleItem();
            currentName.ShouldNotBe(oldName);
        }
        string actor = ReminderIdentityCodec.ComputeActorId(Tenant, item);
        string stateKey = "eventstore:reminders:v1:" + ActorType + ":item:" + actor;
        byte[] currentWitness = store.SnapshotRaw("statestore", stateKey).ShouldNotBeNull();
        now = due;
        using (ServiceProvider provider = Services(store, clock, pages, submitter: submitter, delegation: delegation))
        using (IServiceScope scope = provider.CreateScope())
        {
            object coordinator = scope.ServiceProvider.GetRequiredService(SdkType("ReminderCoordinator"));
            (await InvokeAsync(coordinator, "HandleCallbackAsync", actor, oldName, scheduler, ct)).ShouldBeNull();
            submissions.ShouldBeEmpty();
            scheduler.Held.ShouldHaveSingleItem().ShouldBe(currentName);
            store.SnapshotRaw("statestore", stateKey).ShouldBe(currentWitness);
            object disposition = await InvokeAsync(coordinator, "HandleCallbackAsync", actor, currentName, scheduler, ct);
            ((ReminderDisposition)disposition).ShouldBe(ReminderDisposition.Submitted);
        }
        TrustedEffectSubmission submission = submissions.ShouldHaveSingleItem();
        submission.Identity.SourceEnvelopeSequence.ShouldBe(4);
        JsonSerializer.Deserialize<ResumeWorkItem>(submission.CommandPayload).ShouldNotBeNull().AwaitCondition.ShouldBe(date);
        AssertSubmittedAudit(store, item, currentName, EffectIdentityCodec.ComputeEffectId(submission.Identity), false);
        scheduler.Held.ShouldBeEmpty();
    }

    /// <summary>An earlier registration or repair crossing another date submits the due work while retaining independent future witnesses.</summary>
    /// <param name="reconcile">Whether to execute the indexed reconciliation pass rather than ordinary convergence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierArmCrossingDeadlineSubmitsDueWorkAndRetainsFutureWitnesses(bool reconcile)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        DateTimeOffset now = s_start;
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => now);
        var store = new InMemoryReadModelStore();
        DateTimeOffset future = s_start.AddHours(1);
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            ["crossed-first"] = reconcile
                ? Page("crossed-first", s_start.AddMinutes(1), future)
                : Page("crossed-first", s_start.AddMinutes(1), s_start.AddSeconds(90), future),
        };
        if (reconcile) pages["crossed-second"] = Page("crossed-second", s_start.AddMinutes(1), future);
        var schedulers = pages.Keys.ToDictionary(item => item, _ => Scheduler(), StringComparer.Ordinal);
        var intents = new List<ReminderIntent>();
        var submissions = new List<TrustedEffectSubmission>();
        ITrustedEffectSubmitter submitter = SuccessSubmitter(store, submissions);
        IReminderDelegationTokenProvider delegation = Delegation();
        using (ServiceProvider provider = Services(store, clock, pages, submitter: submitter, delegation: delegation))
        {
            foreach (string item in pages.Keys)
            {
                using IServiceScope scope = provider.CreateScope();
                var target = new ReminderTarget(Tenant, "work", item);
                intents.AddRange(await scope.ServiceProvider.GetRequiredService<IReminderIntentSource>().GetCurrentIntentsAsync(target, ct));
                if (reconcile)
                    (await ConvergeAsync(scope.ServiceProvider, target, schedulers[item], ct)).Armed.ShouldBe(2);
            }
        }
        string? firstArmName = null;
        foreach (WorksReminderSchedulerProxy scheduler in schedulers.Values)
        {
            scheduler.Held.Clear();
            scheduler.Arms.Clear();
            scheduler.AfterArm = () =>
            {
                if (firstArmName is not null) return;
                firstArmName = scheduler.Arms[^1].Name;
                now = now.AddMinutes(2);
            };
        }
        using (ServiceProvider provider = Services(store, clock, pages, reconcile ? schedulers : null, submitter, delegation))
        {
            if (reconcile)
            {
                object pass = await InvokeAsync(provider.GetRequiredService(SdkType("ReminderReconciler")), "RunPassAsync", ct);
                Property<int>(pass, "Candidates").ShouldBe(2);
                Property<int>(pass, "Submitted").ShouldBe(submissions.Count);
                Property<int>(pass, "Incomplete").ShouldBe(0);
                Property<int>(pass, "Unresolved").ShouldBe(0);
            }
            else
            {
                using IServiceScope scope = provider.CreateScope();
                ReminderConvergenceResult result = await ConvergeAsync(scope.ServiceProvider,
                    new ReminderTarget(Tenant, "work", "crossed-first"), schedulers["crossed-first"], ct);
                result.Submitted.ShouldBe(submissions.Count);
                result.Unresolved.ShouldBe(0);
            }
        }
        firstArmName.ShouldNotBeNull();
        now.ShouldBe(s_start.AddMinutes(2));
        ReminderIntent[] expectedSubmissions = [.. intents.Where(intent => intent.DueUtc < now
            && ReminderIdentityCodec.ComputeReminderName(intent) != firstArmName)];
        expectedSubmissions.ShouldNotBeEmpty();
        submissions.Count.ShouldBe(expectedSubmissions.Length);
        var submittedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (TrustedEffectSubmission submission in submissions)
        {
            ReminderIntent intent = expectedSubmissions.Single(candidate => candidate.TargetAggregate == submission.Identity.TargetAggregate
                && candidate.Ordinal == submission.Identity.Ordinal);
            string name = ReminderIdentityCodec.ComputeReminderName(intent);
            _ = submittedNames.Add(name);
            submission.Identity.SourceEnvelopeSequence.ShouldBe(2);
            submission.MessageId.ShouldBe("wrk-" + EffectIdentityCodec.ComputeEffectId(submission.Identity));
            JsonSerializer.Deserialize<ResumeWorkItem>(submission.CommandPayload).ShouldNotBeNull().AwaitCondition.ShouldNotBeNull().Instant.ShouldBe(intent.DueUtc);
            AssertSubmittedAudit(store, intent.TargetAggregate, name, EffectIdentityCodec.ComputeEffectId(submission.Identity), false);
        }
        foreach (string item in pages.Keys)
        {
            DateTimeOffset[] retained = [.. intents.Where(intent => intent.TargetAggregate == item
                && !submittedNames.Contains(ReminderIdentityCodec.ComputeReminderName(intent))).Select(intent => intent.DueUtc)];
            retained.ShouldContain(future);
            _ = AssertWitnesses(store, item, retained);
            string futureName = ReminderIdentityCodec.ComputeReminderName(intents.Single(intent => intent.TargetAggregate == item && intent.DueUtc == future));
            schedulers[item].Held.ShouldContain(futureName);
        }
    }

    /// <summary>A later date uses the remaining time after an earlier scheduler registration completes.</summary>
    /// <param name="elapsedSeconds">The time consumed by the first registration.</param>
    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    public async Task EachSteadyStateRegistrationUsesItsOwnClock(int elapsedSeconds)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "several-dates";
        var target = new ReminderTarget(Tenant, "work", item);
        DateTimeOffset now = s_start;
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => now);
        var store = new InMemoryReadModelStore();
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [item] = Page(item, s_start.AddMinutes(5), s_start.AddMinutes(10)),
        };
        WorksReminderSchedulerProxy scheduler = Scheduler();
        scheduler.AfterArm = () =>
        {
            if (scheduler.Arms.Count == 1) now = now.AddSeconds(elapsedSeconds);
        };
        using (ServiceProvider provider = Services(store, clock, pages))
        using (IServiceScope scope = provider.CreateScope())
        {
            IReadOnlyList<ReminderIntent> intents = await scope.ServiceProvider.GetRequiredService<IReminderIntentSource>()
                .GetCurrentIntentsAsync(target, ct);
            ReminderConvergenceResult result = await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct);
            result.Armed.ShouldBe(2);
            result.Submitted.ShouldBe(0);
            result.Unresolved.ShouldBe(0);
            result.Quarantined.ShouldBe(0);
            scheduler.Arms.Count.ShouldBe(2);
            for (int index = 0; index < scheduler.Arms.Count; index++)
            {
                (string name, TimeSpan dueTime) = scheduler.Arms[index];
                ReminderIntent intent = intents.Single(candidate => ReminderIdentityCodec.ComputeReminderName(candidate) == name);
                DateTimeOffset operationTime = index == 0 ? s_start : s_start.AddSeconds(elapsedSeconds);
                dueTime.ShouldBe(intent.DueUtc - operationTime);
            }
        }

        byte[] persisted = AssertWitnesses(store, item, s_start.AddMinutes(5), s_start.AddMinutes(10));
        // Reconstruct the entire SDK composition over the same persisted store and retained scheduler.
        using (ServiceProvider restarted = Services(store, clock, pages))
        using (IServiceScope scope = restarted.CreateScope())
        {
            (await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct)).Armed.ShouldBe(0);
        }
        scheduler.Arms.Count.ShouldBe(2);
        AssertWitnesses(store, item, s_start.AddMinutes(5), s_start.AddMinutes(10)).ShouldBe(persisted);
    }

    /// <summary>A reconciliation pass accounts for time spent repairing an earlier target's lost reminder.</summary>
    [Fact]
    public async Task ReconciliationUsesRemainingDelayAcrossRetainedTargets()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        DateTimeOffset now = s_start;
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => now);
        var store = new InMemoryReadModelStore();
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            ["first-item"] = Page("first-item", s_start.AddMinutes(10)),
            ["second-item"] = Page("second-item", s_start.AddMinutes(10)),
        };
        var schedulers = pages.Keys.ToDictionary(item => item, _ => Scheduler(), StringComparer.Ordinal);
        using (ServiceProvider provider = Services(store, clock, pages))
        {
            foreach (string item in pages.Keys)
            {
                using IServiceScope scope = provider.CreateScope();
                (await ConvergeAsync(scope.ServiceProvider, new ReminderTarget(Tenant, "work", item), schedulers[item], ct))
                    .Armed.ShouldBe(1);
                _ = AssertWitnesses(store, item, s_start.AddMinutes(10));
            }
        }

        var repairs = new List<TimeSpan>();
        foreach (WorksReminderSchedulerProxy scheduler in schedulers.Values)
        {
            scheduler.Held.Clear();
            scheduler.Arms.Clear();
            scheduler.AfterArm = () =>
            {
                repairs.Add(scheduler.Arms[^1].DueTime);
                if (repairs.Count == 1) now = now.AddMinutes(2);
            };
        }
        using (ServiceProvider restarted = Services(store, clock, pages, schedulers))
        {
            object reconciler = restarted.GetRequiredService(SdkType("ReminderReconciler"));
            object pass = await InvokeAsync(reconciler, "RunPassAsync", ct);
            Property<int>(pass, "Candidates").ShouldBe(2);
            Property<int>(pass, "Armed").ShouldBe(2);
            Property<int>(pass, "Submitted").ShouldBe(0);
            Property<int>(pass, "Unresolved").ShouldBe(0);
            Property<int>(pass, "Quarantined").ShouldBe(0);
            Property<int>(pass, "Incomplete").ShouldBe(0);
        }
        repairs.ShouldBe([TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(8)]);
        foreach (string item in pages.Keys)
        {
            schedulers[item].Held.Count.ShouldBe(1);
            _ = AssertWitnesses(store, item, s_start.AddMinutes(10));
        }
    }

    /// <summary>A slow lookup during repair clamps an elapsed due instant to zero without changing its witness.</summary>
    /// <param name="lookupSeconds">The time consumed by the scheduler lookup.</param>
    /// <param name="remainingSeconds">The remaining relative delay, clamped at zero.</param>
    [Theory]
    [InlineData(30, 90)]
    [InlineData(180, 0)]
    public async Task LostReminderLookupUsesFreshClockAndClampsElapsedDelay(int lookupSeconds, int remainingSeconds)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "lookup-latency";
        var target = new ReminderTarget(Tenant, "work", item);
        DateTimeOffset now = s_start;
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => now);
        var store = new InMemoryReadModelStore();
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [item] = Page(item, s_start.AddMinutes(2)),
        };
        WorksReminderSchedulerProxy scheduler = Scheduler();
        using (ServiceProvider provider = Services(store, clock, pages))
        using (IServiceScope scope = provider.CreateScope())
        {
            (await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct)).Armed.ShouldBe(1);
        }
        byte[] persisted = AssertWitnesses(store, item, s_start.AddMinutes(2));
        scheduler.Held.Clear();
        scheduler.BeforeLookup = () => now = now.AddSeconds(lookupSeconds);
        using (ServiceProvider restarted = Services(store, clock, pages))
        using (IServiceScope scope = restarted.CreateScope())
        {
            ReminderConvergenceResult result = await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct);
            result.Armed.ShouldBe(1);
            result.Submitted.ShouldBe(0);
            result.Unresolved.ShouldBe(0);
        }
        scheduler.Arms.Count.ShouldBe(2);
        scheduler.Arms[^1].DueTime.ShouldBe(TimeSpan.FromSeconds(remainingSeconds));
        AssertWitnesses(store, item, s_start.AddMinutes(2)).ShouldBe(persisted);
    }

    /// <summary>A persistent arm failure retains its witness without starving a sibling, then repairs through indexed recovery.</summary>
    [Fact]
    public async Task FailedArmDoesNotStarveSiblingAndRepairsAfterReconstruction()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "isolated-arm-failure";
        var target = new ReminderTarget(Tenant, "work", item);
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(s_start);
        var store = new InMemoryReadModelStore();
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [item] = Page(item, s_start.AddMinutes(1), s_start.AddMinutes(2)),
        };
        WorksReminderSchedulerProxy scheduler = Scheduler();
        string failedName;
        string siblingName;
        using (ServiceProvider provider = Services(store, clock, pages))
        using (IServiceScope scope = provider.CreateScope())
        {
            ReminderIntent[] intents = [.. await scope.ServiceProvider.GetRequiredService<IReminderIntentSource>()
                .GetCurrentIntentsAsync(target, ct)];
            failedName = ReminderIdentityCodec.ComputeReminderName(intents[0]);
            siblingName = ReminderIdentityCodec.ComputeReminderName(intents[1]);
            scheduler.ArmFailure = name => name == failedName ? new IOException("private-scheduler-detail") : null;
            ReminderConvergenceResult result = await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct);
            result.Armed.ShouldBe(1);
            result.Unresolved.ShouldBe(1);
        }
        scheduler.Held.ShouldHaveSingleItem().ShouldBe(siblingName);
        JsonElement failed = Witness(store, item, failedName);
        failed.GetProperty("status").GetString().ShouldBe("Pending");
        failed.GetProperty("lastReasonCode").GetString().ShouldBe("arm-failed");
        string sibling = Witness(store, item, siblingName).GetRawText();
        var schedulers = new Dictionary<string, WorksReminderSchedulerProxy> { [item] = scheduler };
        for (int pass = 0; pass < 3; pass++)
        {
            using ServiceProvider restarted = Services(store, clock, pages, schedulers);
            object result = await InvokeAsync(restarted.GetRequiredService(SdkType("ReminderReconciler")), "RunPassAsync", ct);
            Property<int>(result, "Unresolved").ShouldBe(1);
            HealthCheckResult health = await HealthAsync(restarted, ct);
            health.Status.ShouldBe(HealthStatus.Degraded);
            health.Data["passCompleted"].ShouldBe(true);
            health.Data["unresolved"].ShouldBe(1);
            Witness(store, item, failedName).GetProperty("lastReasonCode").GetString().ShouldBe("arm-failed");
            Witness(store, item, siblingName).GetRawText().ShouldBe(sibling);
            scheduler.Held.ShouldHaveSingleItem().ShouldBe(siblingName);
        }
        scheduler.ArmFailure = null;
        using (ServiceProvider restarted = Services(store, clock, pages, schedulers))
        {
            object result = await InvokeAsync(restarted.GetRequiredService(SdkType("ReminderReconciler")), "RunPassAsync", ct);
            Property<int>(result, "Armed").ShouldBe(1);
            Property<int>(result, "Unresolved").ShouldBe(0);
            (await HealthAsync(restarted, ct)).Status.ShouldBe(HealthStatus.Healthy);
        }
        scheduler.Held.Order(StringComparer.Ordinal).ShouldBe(new[] { failedName, siblingName }.Order(StringComparer.Ordinal));
        scheduler.Arms.Count.ShouldBe(2);
        _ = AssertWitnesses(store, item, s_start.AddMinutes(1), s_start.AddMinutes(2));
    }

    /// <summary>A due submission fault emits digest-only diagnostics and retains one retry while its sibling completes.</summary>
    [Fact]
    public async Task DueNowSubmissionFailureRetainsWitnessAndBoundedDiagnosticsAcrossRecovery()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "due-submission-failure";
        const string canary = "private-submission-token-and-payload";
        var receiptWrites = new Dictionary<string, int>(StringComparer.Ordinal);
        DateTimeOffset now = s_start;
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => now);
        var store = new InMemoryReadModelStore();
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [item] = Page(item, s_start.AddMinutes(1), s_start.AddMinutes(2)),
        };
        var target = new ReminderTarget(Tenant, "work", item);
        WorksReminderSchedulerProxy scheduler = Scheduler();
        var logs = new WorksReminderRecoveryLogCapture();
        var submissions = new List<TrustedEffectSubmission>();
        bool failing = true;
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                TrustedEffectSubmission submission = call.Arg<TrustedEffectSubmission>();
                submissions.Add(submission);
                ResumeWorkItem command = JsonSerializer.Deserialize<ResumeWorkItem>(submission.CommandPayload).ShouldNotBeNull();
                if (failing && command.AwaitCondition.ShouldNotBeNull().Instant == s_start.AddMinutes(1))
                    throw new HttpRequestException(canary, new IOException(canary));
                string effectId = EffectIdentityCodec.ComputeEffectId(submission.Identity);
                string key = "synthetic-target-receipt:" + effectId;
                TrustedEffectResult? prior = store.Snapshot<TrustedEffectResult>("statestore", key);
                if (prior is not null) return prior with { Replayed = true };
                var receipt = new TrustedEffectResult(effectId, TrustedEffectDisposition.Success, false, null);
                await store.SaveAsync("statestore", key, receipt, call.Arg<CancellationToken>()).ConfigureAwait(false);
                receiptWrites[effectId] = receiptWrites.GetValueOrDefault(effectId) + 1;
                return receipt;
            });
        IReminderDelegationTokenProvider delegation = Delegation();
        using (ServiceProvider initial = Services(store, clock, pages, submitter: submitter, delegation: delegation))
        using (IServiceScope scope = initial.CreateScope())
            (await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct)).Armed.ShouldBe(2);
        now = s_start.AddMinutes(3);
        var schedulers = new Dictionary<string, WorksReminderSchedulerProxy> { [item] = scheduler };
        using (ServiceProvider restarted = Services(store, clock, pages, schedulers, submitter, delegation, logs))
        {
            object result = await InvokeAsync(restarted.GetRequiredService(SdkType("ReminderReconciler")), "RunPassAsync", ct);
            Property<int>(result, "Candidates").ShouldBe(1);
            Property<int>(result, "Submitted").ShouldBe(1);
            Property<int>(result, "Unresolved").ShouldBe(1);
            Property<int>(result, "Incomplete").ShouldBe(0);
        }
        var warning = logs.Entries.Single(entry => entry.EventId == 200219);
        warning.Exception.ShouldBeNull();
        warning.Properties.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(new[] { "ActorId", "ReminderName", "ExceptionType", "{OriginalFormat}" }.Order(StringComparer.Ordinal));
        warning.Properties["ActorId"].ShouldBe(ReminderIdentityCodec.ComputeActorId(Tenant, item));
        warning.Properties["ExceptionType"].ShouldBe(nameof(HttpRequestException));
        string name = (string)warning.Properties["ReminderName"]!;
        var retryLog = logs.Entries.Single(entry => entry.EventId == 200203);
        retryLog.Properties["ActorId"].ShouldBe(warning.Properties["ActorId"]);
        retryLog.Properties["ReminderName"].ShouldBe(name);
        retryLog.Properties["ReasonCode"].ShouldBe("submission-uncertain");
        retryLog.Properties["Attempts"].ShouldBe(1);
        JsonElement retained = Witness(store, item, name);
        retained.GetProperty("status").GetString().ShouldBe("Retrying");
        retained.GetProperty("lastReasonCode").GetString().ShouldBe("submission-uncertain");
        retained.GetProperty("attempts").GetInt32().ShouldBe(1);
        scheduler.Held.ShouldHaveSingleItem().ShouldBe(name);
        foreach (var entry in logs.Entries)
        {
            entry.Exception.ShouldBeNull();
            foreach (string value in entry.Properties.Values.Select(value => value?.ToString() ?? string.Empty).Append(entry.Message))
            {
                value.ShouldNotContain(canary);
                value.ShouldNotContain(Tenant);
                value.ShouldNotContain(item);
            }
        }
        submissions.Count.ShouldBe(2);
        failing = false;
        now = now.AddSeconds(31);
        using (ServiceProvider restarted = Services(store, clock, pages, schedulers, submitter, delegation))
        {
            object result = await InvokeAsync(restarted.GetRequiredService(SdkType("ReminderReconciler")), "RunPassAsync", ct);
            Property<int>(result, "Submitted").ShouldBe(2);
            Property<int>(result, "Unresolved").ShouldBe(0);
        }
        // The synthetic source still reports both awaits, so convergence also replays the sibling receipt.
        TrustedEffectSubmission[] retries = [.. submissions.Where(submission =>
            JsonSerializer.Deserialize<ResumeWorkItem>(submission.CommandPayload)!.AwaitCondition.ShouldNotBeNull().Instant == s_start.AddMinutes(1))];
        retries.Length.ShouldBe(2);
        AssertSameSubmission(retries[0], retries[1]);
        AssertSubmittedAudit(store, item, name, EffectIdentityCodec.ComputeEffectId(retries[0].Identity), false);
        TrustedEffectSubmission[] siblings = [.. submissions.Where(submission =>
            JsonSerializer.Deserialize<ResumeWorkItem>(submission.CommandPayload)!.AwaitCondition.ShouldNotBeNull().Instant == s_start.AddMinutes(2))];
        siblings.Length.ShouldBe(2);
        AssertSameSubmission(siblings[0], siblings[1]);
        string siblingEffect = EffectIdentityCodec.ComputeEffectId(siblings[0].Identity);
        string siblingName;
        using (ServiceProvider provider = Services(store, clock, pages, submitter: submitter, delegation: delegation))
        using (IServiceScope scope = provider.CreateScope())
            siblingName = ReminderIdentityCodec.ComputeReminderName((await scope.ServiceProvider.GetRequiredService<IReminderIntentSource>()
                .GetCurrentIntentsAsync(target, ct)).Single(intent => intent.DueUtc == s_start.AddMinutes(2)));
        AssertSubmittedAudit(store, item, siblingName, siblingEffect, true);
        receiptWrites.Count.ShouldBe(2);
        receiptWrites.Values.ShouldAllBe(count => count == 1);
        store.SnapshotRaw("statestore", StateKey(item)).ShouldBeNull();
        scheduler.Held.ShouldBeEmpty();
    }

    /// <summary>Uncertain receipts and failed cancellation retain a fired witness until a durable replay is safely released.</summary>
    /// <param name="failure">The uncertain response or scheduler boundary.</param>
    /// <param name="disposition">The authoritative target disposition, including rejection and no-op.</param>
    [Theory]
    [InlineData("lost-response", TrustedEffectDisposition.Success)]
    [InlineData("wrong-effect", TrustedEffectDisposition.Success)]
    [InlineData("invalid-disposition", TrustedEffectDisposition.Success)]
    [InlineData("missing-receipt", TrustedEffectDisposition.Success)]
    [InlineData("cancel-failed", TrustedEffectDisposition.Success)]
    [InlineData("cancel-response-lost", TrustedEffectDisposition.Success)]
    [InlineData("audit-unavailable", TrustedEffectDisposition.Success)]
    [InlineData("lost-response", TrustedEffectDisposition.Rejection)]
    [InlineData("lost-response", TrustedEffectDisposition.NoOp)]
    public async Task FiredReminderWaitsForDurableReceiptAndSchedulerReleaseAfterRestart(string failure, TrustedEffectDisposition disposition)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "uncertain-fired-reminder";
        DateTimeOffset now = s_start;
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => now);
        var store = new InMemoryReadModelStore();
        var auditStore = new WorksReminderAuditFaultStore(store);
        var pages = new Dictionary<string, StreamReadPage>(StringComparer.Ordinal) { [item] = Page(item, s_start.AddMinutes(1)) };
        var target = new ReminderTarget(Tenant, "work", item);
        WorksReminderSchedulerProxy scheduler = Scheduler();
        var submissions = new List<TrustedEffectSubmission>();
        bool failing = true;
        int receiptWrites = 0;
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                TrustedEffectSubmission submission = call.Arg<TrustedEffectSubmission>();
                submissions.Add(submission);
                string effectId = EffectIdentityCodec.ComputeEffectId(submission.Identity);
                string key = "synthetic-target-receipt:" + effectId;
                TrustedEffectResult? prior = store.Snapshot<TrustedEffectResult>("statestore", key);
                var receipt = prior is null ? new TrustedEffectResult(effectId, disposition, false, null) : prior with { Replayed = true };
                if (prior is null)
                {
                    await store.SaveAsync("statestore", key, receipt, call.Arg<CancellationToken>()).ConfigureAwait(false);
                    receiptWrites++;
                }
                if (!failing) return receipt;
                return failure switch
                {
                    "lost-response" => throw new HttpRequestException("synthetic lost response"),
                    "wrong-effect" => receipt with { EffectId = "wrong-effect" },
                    "invalid-disposition" => receipt with { Disposition = (TrustedEffectDisposition)99 },
                    "missing-receipt" => null!,
                    _ => receipt,
                };
            });
        IReminderDelegationTokenProvider delegation = Delegation();
        string name;
        using (ServiceProvider initial = Services(store, clock, pages, submitter: submitter, delegation: delegation, reminderStore: auditStore))
        using (IServiceScope scope = initial.CreateScope())
        {
            (await ConvergeAsync(scope.ServiceProvider, target, scheduler, ct)).Armed.ShouldBe(1);
            name = scheduler.Held.ShouldHaveSingleItem();
        }
        byte[] registrationAudit = store.SnapshotRaw("statestore", StateKey(item) + ":disposition:" + name).ShouldNotBeNull();
        now = s_start.AddMinutes(1);
        scheduler.CancelFailure = failure == "cancel-failed" ? new IOException("synthetic cancellation failure") : null;
        scheduler.CancelResponseFailure = failure == "cancel-response-lost" ? new IOException("synthetic lost cancellation response") : null;
        auditStore.RejectAudit = failure == "audit-unavailable";
        string actor = ReminderIdentityCodec.ComputeActorId(Tenant, item);
        using (ServiceProvider restarted = Services(store, clock, pages, submitter: submitter, delegation: delegation, reminderStore: auditStore))
        using (IServiceScope scope = restarted.CreateScope())
            ((ReminderDisposition)await InvokeAsync(scope.ServiceProvider.GetRequiredService(SdkType("ReminderCoordinator")),
                "HandleCallbackAsync", actor, name, scheduler, ct)).ShouldBe(ReminderDisposition.Retrying);
        JsonElement retained = Witness(store, item, name);
        retained.GetProperty("status").GetString().ShouldBe("Retrying");
        retained.GetProperty("lastReasonCode").GetString().ShouldBe(failure switch
        {
            "lost-response" => "submission-uncertain",
            "cancel-failed" or "cancel-response-lost" => "cancel-failed",
            "audit-unavailable" => "audit-unavailable",
            _ => "receipt-mismatch",
        });
        retained.GetProperty("attempts").GetInt32().ShouldBe(1);
        scheduler.Held.ShouldHaveSingleItem().ShouldBe(name);
        scheduler.Arms[^1].DueTime.ShouldBe(TimeSpan.FromSeconds(30));
        auditStore.RefusedAuditWrites.ShouldBe(failure == "audit-unavailable" ? 1 : 0);
        scheduler.LostCancellationResponses.ShouldBe(failure == "cancel-response-lost" ? 1 : 0);
        if (failure == "audit-unavailable")
            store.SnapshotRaw("statestore", StateKey(item) + ":disposition:" + name).ShouldBe(registrationAudit);
        byte[] pending = store.SnapshotRaw("statestore", StateKey(item)).ShouldNotBeNull();
        now = now.AddSeconds(30).AddTicks(-1);
        using (ServiceProvider restarted = Services(store, clock, pages, submitter: submitter, delegation: delegation, reminderStore: auditStore))
        using (IServiceScope scope = restarted.CreateScope())
            ((ReminderDisposition)await InvokeAsync(scope.ServiceProvider.GetRequiredService(SdkType("ReminderCoordinator")),
                "HandleCallbackAsync", actor, name, scheduler, ct)).ShouldBe(ReminderDisposition.Retrying);
        submissions.Count.ShouldBe(1);
        store.SnapshotRaw("statestore", StateKey(item)).ShouldBe(pending);
        failing = false;
        scheduler.CancelFailure = null;
        scheduler.CancelResponseFailure = null;
        auditStore.RejectAudit = false;
        now = now.AddTicks(1);
        using (ServiceProvider restarted = Services(store, clock, pages, submitter: submitter, delegation: delegation, reminderStore: auditStore))
        using (IServiceScope scope = restarted.CreateScope())
            ((ReminderDisposition)await InvokeAsync(scope.ServiceProvider.GetRequiredService(SdkType("ReminderCoordinator")),
                "HandleCallbackAsync", actor, name, scheduler, ct)).ShouldBe(ReminderDisposition.Submitted);
        submissions.Count.ShouldBe(2);
        AssertSameSubmission(submissions[0], submissions[1]);
        receiptWrites.ShouldBe(1);
        store.Snapshot<TrustedEffectResult>("statestore", "synthetic-target-receipt:" + EffectIdentityCodec.ComputeEffectId(submissions[0].Identity))
            .ShouldNotBeNull().Disposition.ShouldBe(disposition);
        using JsonDocument audit = JsonDocument.Parse(store.SnapshotRaw("statestore", StateKey(item) + ":disposition:" + name).ShouldNotBeNull());
        audit.RootElement.GetProperty("disposition").GetString().ShouldBe("Submitted");
        audit.RootElement.GetProperty("targetDisposition").GetString().ShouldBe(disposition.ToString());
        audit.RootElement.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        store.SnapshotRaw("statestore", StateKey(item)).ShouldBeNull();
        scheduler.Held.ShouldBeEmpty();
    }

    private static void AssertSameSubmission(TrustedEffectSubmission original, TrustedEffectSubmission replay)
    {
        replay.Identity.ShouldBe(original.Identity);
        replay.MessageId.ShouldBe(original.MessageId);
        replay.IdempotencyKey.ShouldBe(original.IdempotencyKey);
        replay.CommandType.ShouldBe(original.CommandType);
        replay.CommandPayload.ShouldBe(original.CommandPayload);
    }

    private static Task<HealthCheckResult> HealthAsync(IServiceProvider services, CancellationToken ct)
        => services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(registration => registration.Name == "eventstore-reminders-unresolved").Factory(services)
            .CheckHealthAsync(new HealthCheckContext(), ct);

    private static string StateKey(string item)
        => "eventstore:reminders:v1:" + ActorType + ":item:" + ReminderIdentityCodec.ComputeActorId(Tenant, item);

    private static JsonElement Witness(InMemoryReadModelStore store, string item, string name)
    {
        using JsonDocument document = JsonDocument.Parse(store.SnapshotRaw("statestore", StateKey(item)).ShouldNotBeNull());
        return document.RootElement.GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("reminderName").GetString() == name).Clone();
    }

    private static ServiceProvider Services(InMemoryReadModelStore store, TimeProvider clock,
        IReadOnlyDictionary<string, StreamReadPage> pages,
        IReadOnlyDictionary<string, WorksReminderSchedulerProxy>? schedulers = null,
        ITrustedEffectSubmitter? submitter = null, IReminderDelegationTokenProvider? delegation = null,
        WorksReminderRecoveryLogCapture? logs = null, IReadModelStore? reminderStore = null)
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => pages[call.Arg<StreamReadRequest>().AggregateId!]);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EventStore:Reminders:ActorTypeName"] = ActorType,
            ["APP_API_TOKEN"] = "synthetic-callback-token",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(builder =>
        {
            if (logs is not null) builder.AddProvider(logs);
        });
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        services.AddSingleton(environment);
        services.AddSingleton<IReadModelStore>(reminderStore ?? store);
        services.AddSingleton<IReadModelConditionalEraser>(store);
        services.AddSingleton(clock);
        services.AddWorksReminderAndCascadeRecovery(configuration);
        // Substitute only the public external boundaries after the production registration has run.
        services.RemoveAll<IEventStoreGatewayClient>();
        services.AddSingleton(gateway);
        IReminderTargetCatalog catalog = Substitute.For<IReminderTargetCatalog>();
        catalog.ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessSourcePage([], null));
        services.RemoveAll<IReminderTargetCatalog>();
        services.AddSingleton(catalog);
        services.RemoveAll<ITrustedEffectSubmitter>();
        services.AddSingleton(submitter ?? Substitute.For<ITrustedEffectSubmitter>());
        if (delegation is not null) services.AddSingleton(delegation);
        if (schedulers is not null)
        {
            IReminderRegistrar registrar = Substitute.For<IReminderRegistrar>();
            ServiceProvider? composed = null;
            registrar.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                using IServiceScope scope = composed!.CreateScope();
                ReminderTarget target = call.Arg<ReminderTarget>();
                return await ConvergeAsync(scope.ServiceProvider, target, schedulers[target.Aggregate], call.Arg<CancellationToken>())
                    .ConfigureAwait(false);
            });
            services.RemoveAll<IReminderRegistrar>();
            services.AddSingleton(registrar);
            composed = services.BuildServiceProvider();
            AssertProductionOptions(composed);
            return composed;
        }
        ServiceProvider provider = services.BuildServiceProvider();
        AssertProductionOptions(provider);
        return provider;
    }

    private static void AssertProductionOptions(IServiceProvider services)
    {
        EventStoreReminderOptions options = services.GetRequiredService<IOptions<EventStoreReminderOptions>>().Value;
        options.ActorTypeName.ShouldBe(ActorType);
        options.Workload.ShouldBe("works");
        options.Purposes[EffectKindCatalog.DateResume].ShouldBe("works.date-resume");
        options.Purposes[EffectKindCatalog.Expiry].ShouldBe("works.expiry");
    }

    private static StreamReadPage Page(string item, params DateTimeOffset[] dates)
        => Story48Streams.Page(Tenant, item,
            new WorkItemCreated(item, 1, new TenantId(Tenant), new WorkItemId(item), new Obligation("scheduler timing")),
            new WorkItemSuspended(item, 2, new TenantId(Tenant), new WorkItemId(item),
                [.. dates.Select(AwaitCondition.DateReached)]));

    private static WorksReminderSchedulerProxy Scheduler()
        => (WorksReminderSchedulerProxy)DispatchProxy.Create(SdkType("IReminderScheduler"), typeof(WorksReminderSchedulerProxy));

    private static async Task<ReminderConvergenceResult> ConvergeAsync(IServiceProvider services, ReminderTarget target,
        WorksReminderSchedulerProxy scheduler, CancellationToken cancellationToken)
    {
        object coordinator = services.GetRequiredService(SdkType("ReminderCoordinator"));
        return (ReminderConvergenceResult)await InvokeAsync(coordinator, "ConvergeAsync",
            ReminderIdentityCodec.ComputeActorId(target.Tenant, target.Aggregate), target, scheduler, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object> InvokeAsync(object instance, string method, params object[] arguments)
    {
        var operation = (Task)instance.GetType().GetMethod(method)!.Invoke(instance, arguments)!;
        await operation.ConfigureAwait(false);
        return operation.GetType().GetProperty("Result")!.GetValue(operation)!;
    }

    private static Type SdkType(string name) => s_sdk.GetType("Hexalith.EventStore.DomainService." + name, throwOnError: true)!;

    private static T Property<T>(object instance, string name) => (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;

    private static IReminderDelegationTokenProvider Delegation()
    {
        IReminderDelegationTokenProvider provider = Substitute.For<IReminderDelegationTokenProvider>();
        provider.GetDelegationTokenAsync(Arg.Any<ReminderDelegationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("synthetic-delegation"));
        return provider;
    }

    private static ITrustedEffectSubmitter SuccessSubmitter(InMemoryReadModelStore store, ICollection<TrustedEffectSubmission> submissions)
    {
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                TrustedEffectSubmission submission = call.Arg<TrustedEffectSubmission>();
                submissions.Add(submission);
                var receipt = new TrustedEffectResult(EffectIdentityCodec.ComputeEffectId(submission.Identity), TrustedEffectDisposition.Success, false, null);
                await store.SaveAsync("statestore", "synthetic-target-receipt:" + receipt.EffectId, receipt, call.Arg<CancellationToken>())
                    .ConfigureAwait(false);
                return receipt;
            });
        return submitter;
    }

    private static void AssertSubmittedAudit(InMemoryReadModelStore store, string item, string reminderName, string effectId, bool replayed)
    {
        string actor = ReminderIdentityCodec.ComputeActorId(Tenant, item);
        string key = "eventstore:reminders:v1:" + ActorType + ":item:" + actor + ":disposition:" + reminderName;
        using JsonDocument document = JsonDocument.Parse(store.SnapshotRaw("statestore", key).ShouldNotBeNull());
        JsonElement audit = document.RootElement;
        audit.GetProperty("tenant").GetString().ShouldBe(Tenant);
        audit.GetProperty("actorId").GetString().ShouldBe(actor);
        audit.GetProperty("subject").GetString().ShouldBe(reminderName);
        audit.GetProperty("disposition").GetString().ShouldBe("Submitted");
        audit.GetProperty("effectId").GetString().ShouldBe(effectId);
        audit.GetProperty("targetDisposition").GetString().ShouldBe("Success");
        audit.GetProperty("replayed").GetBoolean().ShouldBe(replayed);
    }

    private static byte[] AssertWitnesses(InMemoryReadModelStore store, string item, params DateTimeOffset[] dates)
    {
        string actor = ReminderIdentityCodec.ComputeActorId(Tenant, item);
        byte[] bytes = store.SnapshotRaw("statestore", "eventstore:reminders:v1:" + ActorType + ":item:" + actor).ShouldNotBeNull();
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        root.GetProperty("tenant").GetString().ShouldBe(Tenant);
        root.GetProperty("domain").GetString().ShouldBe("work");
        root.GetProperty("aggregate").GetString().ShouldBe(item);
        JsonElement[] entries = [.. root.GetProperty("entries").EnumerateArray()];
        entries.Length.ShouldBe(dates.Length);
        entries.Select(entry => entry.GetProperty("dueUtc").GetDateTimeOffset()).Order().ShouldBe(dates.Order());
        entries.Select(entry => entry.GetProperty("reminderName").GetString()).Distinct().Count().ShouldBe(dates.Length);
        foreach (JsonElement entry in entries)
        {
            entry.GetProperty("sourceSequence").GetInt64().ShouldBe(2);
            entry.GetProperty("scheduleRevision").GetInt64().ShouldBe(2);
            entry.GetProperty("status").GetString().ShouldBe("Armed");
            entry.GetProperty("sourceMessageId").GetString().ShouldNotBeNullOrWhiteSpace();
            entry.GetProperty("attempts").GetInt32().ShouldBe(0);
        }
        root.GetProperty("quarantine").GetArrayLength().ShouldBe(0);
        return bytes;
    }
}
