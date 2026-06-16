# Works Boundary Decision Record

**Status:** Accepted — Epic 1 (Story 1.4).
**Tracks:** FR-21 (reference siblings, never copy them), FR-22 (expose module ports as
abstractions), FR-23 (produce the boundary decision record), AR-18 (ports realization), AR-19
(boundary decision record). Aligned with NFR-5 (domain purity) and NFR-11 (natural-language-is-data
boundary).

## Purpose

Works is the **coordination** bounded context. It owns the facts required to coordinate work —
the obligation, the executor binding (a reference value object), effort and schedule, parent/work
references, the tenant-scoped identity, an optional conversation correlation, and status — and it
references every other concern by stable ID, resolved on demand. It never denormalizes a copy of a
sibling's data into its own events or state.

This record makes the boundary **falsifiable**: it enumerates, per sibling module, what Works
**owns** versus what it **references**, explains **why**, and names the four capability **seams**
that are deliberately deferred and are **not v1 behavior**. Architecture-fitness tests assert this
file exists and mentions every module and seam below.

## Ownership principle

Works owns *coordination facts* only. Each sibling module remains the **system of record** for its
own concern. A reference is a stable identifier or correlation ID that is resolved on demand — never
a denormalized copy. This is the FR-21 no-copy rule: copying sibling data would create a second
source of truth that drifts, leak another context's payload into Works events, and couple Works to
schemas it does not own.

## Owns vs. references, per module

| Module | Works references (system of record stays in the sibling) | Works does **not** own |
| --- | --- | --- |
| **Parties** | Executor / actor **identity** via `PartyId` (a Works-owned reference value object holding the stable party id). | Party profiles, display names, contact details, party lifecycle. |
| **Conversations** | Optional **dialogue** correlation via `ConversationCorrelationId`. | Conversation content, messages, comments, threads. |
| **EventStore** | **Persistence**: event append/replay, envelopes, and aggregate identity (`AggregateIdentity`) come from EventStore.Contracts. | The storage substrate, envelope/metadata shape, concurrency mechanism, online-rebuild checkpointing. |
| **Tenants** | **Isolation**: every Works fact is tenant-scoped via `TenantId`; cross-tenant parent references are rejected at the writer. | Tenant registration, tenant profile, tenant lifecycle/onboarding. |
| **Commons** | **ID generation** and shared primitives — supplied at the edge, outside the aggregate. | The identifier-generation algorithm itself; the kernel never generates IDs. |
| **PolymorphicSerializations** | **Payload (de)serialization** of event/command contracts in the wider ecosystem. | The serialization registry/policy. Works-owned reference value objects (`PartyId`, `ExpectationReference`) are plain `System.Text.Json` records and need no polymorphic registration. |

### Why Works owns coordination facts but not these concerns

- **Identity → Parties.** Who performs work is a party fact. Works stores only the reference
  (`PartyId`); it never copies a party profile, so a renamed or deactivated party is resolved live
  from Parties, not from a stale Works copy.
- **Dialogue → Conversations.** The conversation around a work item is dialogue, owned by
  Conversations. Works carries only a correlation id, so message content never lands in a Works event
  and the natural-language-is-data boundary (NFR-11) holds.
- **Persistence → EventStore.** Append/replay/envelopes are an infrastructure concern. The Works
  kernel stays pure (NFR-5): no clock, generated id, I/O, or Dapr in command handling or replay — the
  command pipeline owns that decision in a later story.
- **Isolation → Tenants.** Tenancy is the isolation boundary. Works scopes every fact by `TenantId`
  and treats a foreign-tenant parent as a distinct reference rather than coercing it.
- **ID generation → Commons.** Deterministic replay forbids the kernel from minting ids; ids are
  supplied at the edge via Commons so `Handle`/`Apply` stay deterministic.
- **Payload serialization → PolymorphicSerializations.** Cross-module payload polymorphism is owned
  centrally; Works does not re-implement it.

Counter-metrics **SM-C1** (do not grow the kernel) and **SM-C2** (do not over-fit to deferred themes)
are binding: this story ships the named seams, not the machinery behind them.

## Preserved deferred seams — explicitly NOT v1 behavior

These four capabilities have **named seams** so future themes attach without changing the kernel's
ownership model. None of them is implemented or wired in v1.

1. **AI-inferred expectations (expectation resolution).** Seam: `IExpectationResolver` +
   `ExpectationReference` (`Hexalith.Works.Contracts.Ports`). v1 ships **only** a no-LLM literal
   resolver (`LiteralExpectationResolver` in Server) that interprets nothing. The interpreted
   `Expectation` is resolved on demand and **never** stored in an event, command, or replayed state.
   This port is the future **prompt-injection boundary** (NFR-11): interpreted natural language is
   data, never trusted input. **Not v1 behavior:** any AI/LLM interpretation.

2. **Executor routing / selection (and escalation).** Seam: `IExecutorRouter`
   (`Hexalith.Works.Contracts.Ports`), **abstraction only**. No implementation, routing/selection
   engine, scoring model, or escalation policy ships in v1 — a fitness test asserts zero
   implementers in the Works kernel. This is **Theme 4**, which will also own claim/queue semantics
   (single-claim-wins, the tenant "what's next" queue). **Not v1 behavior:** routing, claiming queued
   work, escalation.

3. **Cost meter / spend governance.** Seam: a cost-ready `Meter` reuse, **Theme 5**. Cost metering,
   spend caps, and cost burn-down/governance are not modeled in v1; no cost type is referenced by the
   kernel. **Not v1 behavior:** any cost meter or spend-governance enforcement.

4. **Trust / security hardening.** Seam: carried-not-enforced security facts — signed links, step-up
   authentication, audit query, and `AuthorityLevel` enforcement — **Theme 6**. `AuthorityLevel` is
   carried on `ExecutorBinding` today but is **not enforced**. **Not v1 behavior:** authority
   enforcement, signed links, step-up auth, or audit querying.

## Notes and cross-references

- Dependency direction is enforced by fitness tests: `Contracts` references only
  `EventStore.Contracts`; `Server`, `Projections`, and `Reactor` reference inward to `Contracts`
  only. The new `Ports/` (Contracts) and `Resolvers/` (Server) folders add no forbidden reference.
- Hexalith libraries are consumed as `ProjectReference` to the checked-out sibling source, never as
  NuGet `PackageReference` (see `CLAUDE.md`). Story 1.4 introduced no new sibling reference.
- EventStore API-surface constraints from Story 1.1 (ETag-based concurrency, checkpoint-per-aggregate
  online rebuild) are unchanged by this story; it adds no command-pipeline wiring.
- Sources: `epics.md` FR-2/FR-21/FR-22/FR-23, NFR-5/NFR-11, SM-C1/SM-C2; `architecture.md` AR-18
  (ports realization), AR-19 (boundary decision record), Architectural Boundaries, Deferred Decisions.
