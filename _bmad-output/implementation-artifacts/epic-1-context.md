# Epic 1 Context: Builder-Ready Work Item Kernel

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Give Hexalith builders a clean, buildable, pure event-sourced Works foundation that can create a tenant-scoped Work Item, retain only coordination facts and sibling-module references, link an optional Conversation at creation or later, and expose explicit extension ports and ownership boundaries without importing deferred UI, routing, LLM, cost, security, or platform-hosting concerns into the kernel.

## Stories

- Story 1.1: Set Up Initial Project from Starter Template
- Story 1.2: Create a Tenant-Scoped Work Item
- Story 1.3: Reference Sibling Modules Without Copying Data
- Story 1.4: Expose Boundary Ports and Decision Record
- Story 1.5: Link a Conversation After Work Item Creation

## Requirements & Constraints

- Creation requires a `TenantId`, an edge-assigned `WorkItemId`, and a non-empty human-readable Obligation. It may also carry initial Effort and Unit, Schedule, parent reference, Executor Binding, Expectation reference, or Conversation correlation ID. An unestimated item is valid; its Remaining value is undefined until estimated and it is not implicitly complete.
- Successful creation emits `WorkItemCreated` and replays to Status `Created` under canonical identity `{tenant}:work:{workItemId}`. Invalid creation is a domain rejection, and a `DomainResult` must never mix success and rejection payloads.
- Works owns coordination facts only. Parties, Conversations, Tenants, persistence/events, and ID generation remain owned by their sibling modules and are represented by stable reference value objects. Do not denormalize party profiles, contact data, conversation messages, tenant profiles, envelope data, or ID-generation details into commands, events, state, or read models.
- A Conversation reference is optional. `LinkConversation` may establish the first link on a non-terminal item and emits `ConversationLinked`; a retry with the same ID is a no-op, including after terminal closure. A conflicting replacement is rejected without changing the original link, and a new first link on a terminal or nonexistent item is rejected. Works is not a comment store.
- Provide `IExpectationResolver` with a no-LLM implementation; a Work Item remains valid without a resolved interpretation. Provide `IExecutorRouter` only as an unwired abstraction. No v1 LLM, routing, scoring, escalation, cost-governance, or security behavior may be introduced behind these seams.
- Maintain a tracked boundary decision record explaining what Works owns versus references for Parties, Conversations, EventStore, Tenants, Commons, and PolymorphicSerializations.
- Tenant isolation applies to every command, identity, state key, projection key, query, and log scope. Tests must cover cross-tenant negative paths rather than relying only on key prefixes.
- Public contracts evolve additively and remain serialization-tolerant. The completed v1 catalog has 15 commands, 15 state-changing success events, and 10 rejection events. Register every durable command and event with `Hexalith.PolymorphicSerializations`, introduce no `V2` event types, and protect previously persisted payloads with golden-payload contract tests.

## Technical Decisions

- Use the Hexalith domain-module layout and central build configuration: `.slnx`, central package versions, nullable C#, warnings as errors, and focused xUnit v3 tests. Treat `global.json` and the central build/package files as the only version authority; project files must not carry inline package versions.
- The target Works-owned set is `Contracts`, `Server`, `Projections`, pure `Reactor` translators, the canonical minimal `Hexalith.Works` EventStore domain-service executable, `Testing`, and focused test projects. Do not add Works-owned AppHost, Aspire, ServiceDefaults, Dapr-component, delivery, scheduling, subscription, UI, MCP, portal, security, or production channel-adapter projects.
- Enforce direct-to-contract dependencies: `Server → Contracts`, `Projections → Contracts`, and `Reactor → Contracts`; `Projections` never references `Server`. Testing references the kernel, the minimal host references the EventStore SDK and inward units, `Contracts` stays low-dependency, and no kernel project references an adapter or infrastructure implementation.
- Model the aggregate as pure `Handle(state, command) → DomainResult/events` plus in-memory-only `Apply(event)`. It reads no clock, RNG, I/O, Dapr, or external service. IDs come from the edge via Commons. EventStore owns persist-then-publish, Dapr ETag concurrency, canonical envelope sequencing, and envelope metadata; Works returns payloads only.
- State-changing Works events carry payload `(AggregateId, Sequence)`, where `Sequence` is the state-changing ordinal. EventStore envelope `SequenceNumber` remains the canonical persisted stream and delivery position; actor and timestamp also come from the envelope. Rejection payloads keep their context-only shape and apply as state no-ops.
- Use `PartyId`, `ConversationCorrelationId`, and `TenantId` reference value objects resolved on demand. The domain must not require sibling implementation clients or servers in `Contracts`.
- Name commands imperatively and events in the past tense, without `Command` or `Event` suffixes; use sealed records, file-scoped namespaces, and one public type per file. Architecture-fitness tests enforce purity and dependency boundaries.

## UX & Interaction Patterns

v1 is headless and ships no production end-user surface. Keep creation channel-neutral so future email, chatbot, MCP, CLI, or web capture can converge on one Work Item with Obligation and Tenant as the only required inputs; do not introduce a multi-step interaction requirement or embed conversation content. Raw acts and Conversation links must remain suitable for a future unified history without turning Works into a dialogue store.

## Cross-Story Dependencies

Story 1.1 establishes the buildable project and dependency boundaries required by all later stories. Story 1.2 establishes identity, creation, replay, and initial state; Story 1.3 supplies the reference-only contracts used by creation and later linking; Story 1.4 fixes the extension seams and ownership record; Story 1.5 builds on the created aggregate and Conversation reference to add backward-compatible late linking. Its terminal-state rules share the lifecycle status model developed in Epic 2. The historical scaffold included Works-owned AppHost and ServiceDefaults projects; do not remove them as part of Epic 1 work—Story 4.9 owns removal after an equivalent platform topology is proven.
