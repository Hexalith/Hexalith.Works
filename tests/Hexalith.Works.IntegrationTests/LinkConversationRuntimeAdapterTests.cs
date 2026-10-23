using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.EventStore.Contracts.Serialization;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;

using Shouldly;

using KernelAggregate = Hexalith.Works.Server.Aggregates.WorkItemAggregate;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Exercises the WorkItem aggregate's EventStore reflection adapters, including their aggregate-wide
/// reserved-tenant guard and LinkConversation identity validation and state rehydration.
/// </summary>
public sealed class LinkConversationRuntimeAdapterTests
{
    private const string Domain = "work";

    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly ConversationCorrelationId Conversation = new("conversation-456");
    private static readonly ConversationCorrelationId ConflictingConversation = new("conversation-789");

    /// <summary>The canonical fifteen-command catalog, labelled for individual theory diagnostics.</summary>
    public static TheoryData<string, Polymorphic> CommandFixtures
    {
        get
        {
            var data = new TheoryData<string, Polymorphic>();
            Polymorphic[] commands = WorkItemV1Catalog.All
                .Where(value => value.GetType().Namespace == typeof(CreateWorkItem).Namespace)
                .ToArray();
            commands.Length.ShouldBe(15);
            foreach (Polymorphic command in commands)
            {
                data.Add(command.GetType().Name, command);
            }

            return data;
        }
    }

    /// <summary>
    /// The fourteen ordinary adapters paired with malformed envelope fields that only
    /// <see cref="LinkConversation"/> is responsible for validating.
    /// </summary>
    public static TheoryData<string, Polymorphic, string, string> OrdinaryMalformedEnvelopeFixtures
    {
        get
        {
            var data = new TheoryData<string, Polymorphic, string, string>();
            Polymorphic[] commands = WorkItemV1Catalog.All
                .Where(value => value.GetType().Namespace == typeof(CreateWorkItem).Namespace
                    && value is not LinkConversation)
                .ToArray();
            commands.Length.ShouldBe(14);
            foreach (Polymorphic command in commands)
            {
                data.Add($"{command.GetType().Name}-domain", command, "not valid!", Item.Value);
                data.Add($"{command.GetType().Name}-aggregate", command, Domain, "not valid!");
            }

            return data;
        }
    }

    /// <summary>
    /// The fourteen ordinary adapters paired with tenant values that the data-contract path can deserialize
    /// without running <see cref="CommandEnvelope"/>'s constructor validation.
    /// </summary>
    public static TheoryData<string, Polymorphic, string?> OrdinaryMalformedEnvelopeTenantFixtures
    {
        get
        {
            var data = new TheoryData<string, Polymorphic, string?>();
            Polymorphic[] commands = WorkItemV1Catalog.All
                .Where(value => value.GetType().Namespace == typeof(CreateWorkItem).Namespace
                    && value is not LinkConversation)
                .ToArray();
            commands.Length.ShouldBe(14);
            (string Name, string? Value)[] malformedTenants =
            [
                ("null", null),
                ("empty", string.Empty),
                ("over-length", new string('a', 65)),
                ("non-ascii", "ténant"),
                ("invalid-shape", "not valid!"),
            ];
            foreach (Polymorphic command in commands)
            {
                foreach ((string name, string? value) in malformedTenants)
                {
                    data.Add($"{command.GetType().Name}-{name}", command, value);
                }
            }

            return data;
        }
    }

    [Fact]
    public async Task ProcessAsync_reflection_dispatch_links_rehydrated_unlinked_work()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        DomainServiceCurrentState current = CurrentState(
            new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link runtime work")));

        DomainResult result = await aggregate.ProcessAsync(CommandFor(command), current, TestContext.Current.CancellationToken).ConfigureAwait(true);

        ConversationLinked linked = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ConversationLinked>();
        result.IsSuccess.ShouldBeTrue();
        linked.AggregateId.ShouldBe(Item.Value);
        linked.Sequence.ShouldBe(2);
        linked.ConversationCorrelationId.ShouldBe(Conversation);
    }

    [Fact]
    public async Task ProcessAsync_rehydrates_the_authoritative_link_for_retry_and_conflict_precedence()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        DomainServiceCurrentState current = CurrentState(
            new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link runtime work")),
            new ConversationLinked(Item.Value, 2, Tenant, Item, Conversation));

        DomainResult retry = await aggregate.ProcessAsync(
            CommandFor(new LinkConversation(Tenant, Item, Conversation)),
            current, TestContext.Current.CancellationToken).ConfigureAwait(true);
        DomainResult conflict = await aggregate.ProcessAsync(
            CommandFor(new LinkConversation(Tenant, Item, ConflictingConversation)),
            current, TestContext.Current.CancellationToken).ConfigureAwait(true);

        retry.IsNoOp.ShouldBeTrue();
        retry.Events.ShouldBeEmpty();
        WorkItemConversationLinkRejected rejected = conflict.Events
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkItemConversationLinkRejected>();
        conflict.IsRejection.ShouldBeTrue();
        rejected.ExistingConversationCorrelationId.ShouldBe(Conversation);
        rejected.ProposedConversationCorrelationId.ShouldBe(ConflictingConversation);
    }

    [Fact]
    public async Task ProcessAsync_fails_closed_when_envelope_and_payload_identities_differ()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        CommandEnvelope mismatched = CommandFor(command) with { AggregateId = "work-002" };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(mismatched, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("does not match its command envelope", Case.Sensitive);
    }

    [Fact]
    public async Task ProcessAsync_fails_closed_when_envelope_domain_differs()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        CommandEnvelope mismatched = CommandFor(command) with { Domain = "party" };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(mismatched, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("does not match its command envelope", Case.Sensitive);
    }

    [Fact]
    public async Task ProcessAsync_fails_closed_when_envelope_tenant_differs()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        CommandEnvelope mismatched = CommandFor(command) with { TenantId = "tenant-beta" };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(mismatched, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("does not match its command envelope", Case.Sensitive);
    }

    /// <summary>Missing and explicit-null required fields fail through the real reflection dispatcher.</summary>
    [Theory]
    [InlineData("TenantId", false)]
    [InlineData("TenantId", true)]
    [InlineData("WorkItemId", false)]
    [InlineData("WorkItemId", true)]
    [InlineData("ConversationCorrelationId", false)]
    [InlineData("ConversationCorrelationId", true)]
    public async Task ProcessAsyncRefusesMissingOrNullRequiredLinkFields(string field, bool explicitNull)
    {
        var aggregate = new WorkItemEventStoreAggregate();
        CommandEnvelope valid = CommandFor(new LinkConversation(Tenant, Item, Conversation));
        JsonObject payload = JsonNode.Parse(valid.Payload).ShouldNotBeNull().AsObject();
        if (explicitNull)
        {
            payload[field] = null;
        }
        else
        {
            payload.Remove(field).ShouldBeTrue();
        }

        CommandEnvelope envelope = valid with { Payload = JsonSerializer.SerializeToUtf8Bytes(payload) };
        DomainServiceCurrentState current = CurrentState(
            new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link runtime work")));
        string before = JsonSerializer.Serialize(current);

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(envelope, current, TestContext.Current.CancellationToken));

        if (field == nameof(LinkConversation.ConversationCorrelationId))
        {
            exception.InnerException.ShouldBeOfType<ArgumentNullException>().ParamName.ShouldBe($"command.{field}");
        }
        else
        {
            exception.InnerException.ShouldBeOfType<InvalidOperationException>()
                .Message.ShouldContain("does not match its command envelope", Case.Sensitive);
        }

        JsonSerializer.Serialize(current).ShouldBe(before);
        DomainResult healthy = await aggregate.ProcessAsync(valid, current, TestContext.Current.CancellationToken);
        healthy.Events.ShouldHaveSingleItem().ShouldBeOfType<ConversationLinked>()
            .ConversationCorrelationId.ShouldBe(Conversation);
    }

    /// <summary>Persisted event payload identity must be checked during SDK state rehydration.</summary>
    [Theory]
    [InlineData("tenant")]
    [InlineData("item")]
    [InlineData("aggregate")]
    [InlineData("null-tenant")]
    [InlineData("null-item")]
    [InlineData("null-aggregate")]
    [InlineData("empty-aggregate")]
    [InlineData("missing-tenant")]
    [InlineData("missing-item")]
    [InlineData("missing-aggregate")]
    public async Task ProcessAsyncRefusesForeignConversationReplayBeforeHandling(string defect)
    {
        ArgumentNullException.ThrowIfNull(defect);
        var aggregate = new WorkItemEventStoreAggregate();
        var created = new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link runtime work"));
        var valid = new ConversationLinked(Item.Value, 2, Tenant, Item, ConflictingConversation);
        ConversationLinked malformed = defect switch
        {
            "tenant" => valid with { TenantId = new TenantId("tenant-beta") },
            "item" => valid with { WorkItemId = new WorkItemId("work-002") },
            "aggregate" => valid with { AggregateId = "work-002" },
            "null-tenant" => valid with { TenantId = null! },
            "null-item" => valid with { WorkItemId = null! },
            "null-aggregate" => valid with { AggregateId = null! },
            "empty-aggregate" => valid with { AggregateId = string.Empty },
            "missing-tenant" or "missing-item" or "missing-aggregate" => valid,
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        EventEnvelope persisted = Event(malformed, 2);
        if (defect.StartsWith("missing-", StringComparison.Ordinal))
        {
            JsonObject payload = JsonNode.Parse(persisted.Payload).ShouldNotBeNull().AsObject();
            string field = defect switch
            {
                "missing-tenant" => nameof(ConversationLinked.TenantId),
                "missing-item" => nameof(ConversationLinked.WorkItemId),
                _ => nameof(ConversationLinked.AggregateId),
            };
            payload.Remove(field).ShouldBeTrue();
            persisted = new EventEnvelope(persisted.Metadata, JsonSerializer.SerializeToUtf8Bytes(payload), null);
        }

        CommandEnvelope command = CommandFor(new LinkConversation(Tenant, Item, Conversation));
        foreach (bool fromSnapshot in new[] { false, true })
        {
            var snapshot = new WorkItemState();
            snapshot.Apply(created);
            var current = new DomainServiceCurrentState(
                SnapshotState: fromSnapshot ? snapshot : null,
                Events: fromSnapshot ? [persisted] : [Event(created, 1), persisted],
                LastSnapshotSequence: fromSnapshot ? 1 : 0,
                CurrentSequence: 2);
            string before = JsonSerializer.Serialize(current);

            Exception exception = await Should.ThrowAsync<Exception>(
                () => aggregate.ProcessAsync(command, current, TestContext.Current.CancellationToken));

            exception.GetBaseException().ShouldBeOfType<InvalidOperationException>()
                .Message.ShouldContain("identity", Case.Sensitive);
            JsonSerializer.Serialize(current).ShouldBe(before);
            // Retrying the unchanged corrupt history must remain refused; history repair is an operator concern.
            await Should.ThrowAsync<Exception>(
                () => aggregate.ProcessAsync(command, current, TestContext.Current.CancellationToken));
        }

        // A separate healthy history proves that refusal does not poison the reusable adapter instance.
        DomainResult healthy = await aggregate.ProcessAsync(command, CurrentState(created), TestContext.Current.CancellationToken);
        healthy.Events.ShouldHaveSingleItem().ShouldBeOfType<ConversationLinked>()
            .ConversationCorrelationId.ShouldBe(Conversation);
    }

    /// <summary>An established snapshot can supply identity for a valid conversation tail without replaying creation.</summary>
    [Fact]
    public async Task ProcessAsyncAcceptsConversationTailAfterEstablishedSnapshot()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var snapshot = new WorkItemState();
        snapshot.Apply(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Snapshot work")));
        var current = new DomainServiceCurrentState(
            SnapshotState: snapshot,
            Events: [Event(new ConversationLinked(Item.Value, 2, Tenant, Item, Conversation), 2)],
            LastSnapshotSequence: 1,
            CurrentSequence: 2);

        DomainResult retry = await aggregate.ProcessAsync(
            CommandFor(new LinkConversation(Tenant, Item, Conversation)), current, TestContext.Current.CancellationToken);
        DomainResult conflict = await aggregate.ProcessAsync(
            CommandFor(new LinkConversation(Tenant, Item, ConflictingConversation)), current, TestContext.Current.CancellationToken);

        retry.IsNoOp.ShouldBeTrue();
        retry.Events.ShouldBeEmpty();
        conflict.IsRejection.ShouldBeTrue();
        conflict.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemConversationLinkRejected>()
            .ExistingConversationCorrelationId.ShouldBe(Conversation);
    }

    [Fact]
    public async Task ProcessAsync_refuses_reserved_tenant_create_before_persist()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new CreateWorkItem(new TenantId(WorksReadModelKeys.ReservedTenantId), Item, "Must not persist");
        CommandEnvelope ordinaryEnvelope = CommandForCreate(command) with { TenantId = Tenant.Value };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(ordinaryEnvelope, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain(WorksReadModelKeys.ReservedTenantId, Case.Sensitive);
        exception.InnerException.Message.ShouldContain("registry", Case.Sensitive);
    }

    [Fact]
    public async Task ProcessAsync_refuses_canonical_reserved_tenant_from_mixed_case_envelope()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new CreateWorkItem(Tenant, Item, "Must not persist");
        CommandEnvelope mixedCaseReserved = CommandForCreate(command) with { TenantId = "TENANTS" };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(mixedCaseReserved, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain(WorksReadModelKeys.ReservedTenantId, Case.Sensitive);
        exception.InnerException.Message.ShouldContain("registry", Case.Sensitive);
    }

    /// <summary>Every reflection-dispatched wrapper refuses a reserved envelope before kernel delegation.</summary>
    [Theory]
    [MemberData(nameof(CommandFixtures))]
    public async Task ProcessAsync_refuses_reserved_envelope_tenant_for_every_command(
        string commandName,
        Polymorphic command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _ = commandName;
        var aggregate = new WorkItemEventStoreAggregate();
        foreach (string tenant in new[] { WorksReadModelKeys.ReservedTenantId, "TENANTS", "TeNaNtS" })
        {
            CommandEnvelope reserved = CommandFor(command, tenant);

            TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
                () => aggregate.ProcessAsync(reserved, currentState: null));

            exception.InnerException.ShouldBeOfType<InvalidOperationException>()
                .Message.ShouldContain(WorksReadModelKeys.ReservedTenantId, Case.Sensitive);
            exception.InnerException.Message.ShouldContain("registry", Case.Sensitive);
        }
    }

    /// <summary>Every wrapper also refuses a reserved payload despite an ordinary envelope tenant.</summary>
    [Theory]
    [MemberData(nameof(CommandFixtures))]
    public async Task ProcessAsyncRefusesReservedPayloadTenantForEveryCommand(string commandName, Polymorphic command)
    {
        ArgumentNullException.ThrowIfNull(command);
        commandName.ShouldBe(command.GetType().Name);
        var aggregate = new WorkItemEventStoreAggregate();
        CommandEnvelope valid = CommandFor(command, Tenant.Value);
        foreach (string tenant in new[] { WorksReadModelKeys.ReservedTenantId, "TENANTS", "TeNaNtS" })
        {
            JsonObject payload = JsonNode.Parse(valid.Payload).ShouldNotBeNull().AsObject();
            payload[nameof(LinkConversation.TenantId)].ShouldNotBeNull()["Value"] = tenant;
            CommandEnvelope reserved = valid with { Payload = JsonSerializer.SerializeToUtf8Bytes(payload) };

            TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
                () => aggregate.ProcessAsync(reserved, currentState: null));

            exception.InnerException.ShouldBeOfType<InvalidOperationException>()
                .Message.ShouldContain(WorksReadModelKeys.ReservedTenantId, Case.Sensitive);
            exception.InnerException.Message.ShouldContain("registry", Case.Sensitive);
        }
    }

    /// <summary>Envelope-aware reflection dispatch preserves each pure-kernel result for an ordinary tenant.</summary>
    [Theory]
    [MemberData(nameof(CommandFixtures))]
    public async Task ProcessAsync_preserves_kernel_results_for_every_ordinary_command(
        string commandName,
        Polymorphic command)
    {
        ArgumentNullException.ThrowIfNull(command);
        commandName.ShouldBe(command.GetType().Name);
        var aggregate = new WorkItemEventStoreAggregate();
        DomainResult expected = InvokeKernel(command);

        DomainResult actual = await aggregate.ProcessAsync(
            CommandFor(command, Tenant.Value),
            currentState: null, TestContext.Current.CancellationToken);

        actual.IsSuccess.ShouldBe(expected.IsSuccess);
        actual.IsRejection.ShouldBe(expected.IsRejection);
        actual.IsNoOp.ShouldBe(expected.IsNoOp);
        actual.Events.ShouldBe(expected.Events);
    }

    /// <summary>
    /// The reserved-tenant guard reads only the envelope tenant; malformed unrelated identity fields remain the
    /// responsibility of the one adapter that explicitly requires full identity equality.
    /// </summary>
    [Theory]
    [MemberData(nameof(OrdinaryMalformedEnvelopeFixtures))]
    public async Task ProcessAsync_preserves_ordinary_kernel_results_with_malformed_unrelated_envelope_fields(
        string caseName,
        Polymorphic command,
        string envelopeDomain,
        string envelopeAggregateId)
    {
        ArgumentNullException.ThrowIfNull(command);
        caseName.ShouldNotBeNullOrWhiteSpace();
        var aggregate = new WorkItemEventStoreAggregate();
        DomainResult expected = InvokeKernel(command);
        CommandEnvelope malformed = CommandFor(command, Tenant.Value) with
        {
            Domain = envelopeDomain,
            AggregateId = envelopeAggregateId,
        };

        DomainResult actual = await aggregate.ProcessAsync(malformed, currentState: null, TestContext.Current.CancellationToken);

        actual.IsSuccess.ShouldBe(expected.IsSuccess);
        actual.IsRejection.ShouldBe(expected.IsRejection);
        actual.IsNoOp.ShouldBe(expected.IsNoOp);
        actual.Events.ShouldBe(expected.Events);
    }

    /// <summary>
    /// Data-contract deserialization can bypass envelope identity validation; ordinary adapters deliberately
    /// preserve kernel results for those malformed tenant values while <see cref="LinkConversation"/> remains
    /// the sole full-identity adapter.
    /// </summary>
    [Theory]
    [MemberData(nameof(OrdinaryMalformedEnvelopeTenantFixtures))]
    public async Task ProcessAsync_preserves_ordinary_kernel_results_with_data_contract_malformed_envelope_tenants(
        string caseName,
        Polymorphic command,
        string? envelopeTenantId)
    {
        ArgumentNullException.ThrowIfNull(command);
        caseName.ShouldNotBeNullOrWhiteSpace();
        var aggregate = new WorkItemEventStoreAggregate();
        DomainResult expected = InvokeKernel(command);
        CommandEnvelope malformed = DataContractRoundTripWithTenant(
            CommandFor(command, Tenant.Value),
            envelopeTenantId);
        malformed.TenantId.ShouldBe(envelopeTenantId);

        DomainResult actual = await aggregate.ProcessAsync(malformed, currentState: null, TestContext.Current.CancellationToken);

        actual.IsSuccess.ShouldBe(expected.IsSuccess);
        actual.IsRejection.ShouldBe(expected.IsRejection);
        actual.IsNoOp.ShouldBe(expected.IsNoOp);
        actual.Events.ShouldBe(expected.Events);
    }

    private static CommandEnvelope CommandForCreate(CreateWorkItem command)
        => new(
            MessageId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            TenantId: command.TenantId.Value,
            Domain: Domain,
            AggregateId: command.WorkItemId.Value,
            CommandType: typeof(CreateWorkItem).FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command),
            CorrelationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            CausationId: null,
            UserId: "test-user",
            Extensions: null);

    private static CommandEnvelope CommandFor(LinkConversation command)
        => new(
            MessageId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            TenantId: command.TenantId.Value,
            Domain: Domain,
            AggregateId: command.WorkItemId.Value,
            CommandType: typeof(LinkConversation).FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command),
            CorrelationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            CausationId: null,
            UserId: "test-user",
            Extensions: null);

    private static CommandEnvelope CommandFor(Polymorphic command, string envelopeTenantId)
    {
        Type commandType = command.GetType();
        return new CommandEnvelope(
            MessageId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            TenantId: envelopeTenantId,
            Domain: Domain,
            AggregateId: Item.Value,
            CommandType: commandType.FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command, commandType),
            CorrelationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            CausationId: null,
            UserId: "test-user",
            Extensions: null);
    }

    private static CommandEnvelope DataContractRoundTripWithTenant(CommandEnvelope envelope, string? tenantId)
    {
        var serializer = new DataContractSerializer(typeof(CommandEnvelope));
        var serialized = new MemoryStream();
        serializer.WriteObject(serialized, envelope);
        serialized.Position = 0;
        XDocument document = XDocument.Load(serialized);
        XElement tenantElement = document
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, nameof(CommandEnvelope.TenantId), StringComparison.Ordinal));
        XNamespace instanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";
        if (tenantId is null)
        {
            tenantElement.RemoveNodes();
            tenantElement.SetAttributeValue(instanceNamespace + "nil", true);
        }
        else
        {
            tenantElement.SetAttributeValue(instanceNamespace + "nil", null);
            tenantElement.Value = tenantId;
        }

        using var mutated = new MemoryStream();
        using (var writer = new StreamWriter(mutated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
        {
            document.Save(writer, SaveOptions.DisableFormatting);
        }

        mutated.Position = 0;
        return serializer.ReadObject(mutated).ShouldBeOfType<CommandEnvelope>();
    }

    private static DomainResult InvokeKernel(Polymorphic command)
    {
        MethodInfo handler = typeof(KernelAggregate)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Handle"
                && method.GetParameters() is [ParameterInfo commandParameter, _]
                && commandParameter.ParameterType == command.GetType());
        return handler.Invoke(null, [command, null]).ShouldBeOfType<DomainResult>();
    }

    private static DomainServiceCurrentState CurrentState(params IEventPayload[] events)
        => new(
            SnapshotState: null,
            Events: [.. events.Select((payload, index) => Event(payload, index + 1))],
            LastSnapshotSequence: 0,
            CurrentSequence: events.Length);

    private static EventEnvelope Event(IEventPayload payload, int position)
        => new(
            new EventMetadata(
                MessageId: $"event-{position}",
                AggregateId: Item.Value,
                AggregateType: "work-item",
                TenantId: Tenant.Value,
                Domain: Domain,
                SequenceNumber: position,
                GlobalPosition: position,
                Timestamp: new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero),
                CorrelationId: "corr-1",
                CausationId: "cause-1",
                UserId: "test-user",
                DomainServiceVersion: "v1",
                EventTypeName: payload.GetType().FullName!,
                MetadataVersion: 1,
                SerializationFormat: "json"),
            JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            null);
}
