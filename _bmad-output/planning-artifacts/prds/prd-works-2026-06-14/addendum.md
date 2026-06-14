---
title: "Hexalith.Works PRD — Addendum (downstream depth)"
status: final
created: 2026-06-14
updated: 2026-06-14
---

# Addendum — Hexalith.Works PRD

Technical-how depth that supports the PRD but belongs to the **architecture / solution-design phase** rather than the PRD's capability narrative. This addendum carries the **inherited substrate constraints in detail** and a **non-binding event/port sketch** to hand to the architect. For context that lives elsewhere: the brief-level competitive landscape, why-now sources, and the foundation action plan are in `../briefs/brief-works-2026-06-14/addendum.md`; the source-of-record ideation is `../../brainstorming/brainstorming-session-2026-06-14-0910.md` (44 ideas / 6 themes).

## Inherited substrate constraints (Hexalith ecosystem)

These bound the v1 requirements; the architecture phase makes them concrete. Source: `Hexalith.Projects/_bmad-output/project-context.md` and the umbrella `CLAUDE.md`.

- **Platform:** .NET 10 (`global.json` pins SDK `10.0.300`, `rollForward: latestPatch`); C# nullable + implicit usings + warnings-as-errors; central NuGet package management via `Directory.Packages.props` (versions there, not inline).
- **Infrastructure abstraction:** Dapr is the *only* permitted infrastructure abstraction in domain services — no direct Redis/PostgreSQL/Cosmos/broker clients in Contracts/Client/domain packages.
- **EventStore foundation:** canonical identity `{tenant}:{domain}:{aggregateId}` — derive actor IDs, state keys, topics, projection keys, SignalR groups, and log scopes from it. EventStore owns event envelope metadata; Works returns event *payloads* only and must never populate/spoof envelope fields. Event flow is **persist-then-publish**.
- **Domain purity:** aggregate `Handle(...)` is pure → returns domain results/events; projection/state `Apply(...)` mutates only in-memory state. Domain rejections are events implementing `IRejectionEvent`; infrastructure failures are exceptions/dead-letter paths. A `DomainResult` never mixes success and rejection payloads.
- **Schema evolution:** additive and serialization-tolerant only; **no `V2` event types**; every event ever produced must remain backward-compatibly deserializable; tolerate unknown-but-additive fields. `System.Text.Json` conventions; `Hexalith.PolymorphicSerializations` for event/command payloads.
- **Naming:** file-scoped namespaces under `Hexalith.*`; commands are imperative with no `Command` suffix (e.g., `CreateWorkItem`); events are past-tense with no `Event` suffix (e.g., `WorkItemCreated`); prefer sealed records; async methods `Async`-suffixed; private fields `_camelCase`; interfaces `I`-prefixed.
- **Package layout:** `Contracts` (events/commands/models — low-dependency, no infra) · `Server` (domain behavior) · `Projections` (read side) · `Client` (consumer integration, if needed) · `Aspire`/`AppHost` (topology) · `Testing` (reusable test utilities). Dependency direction strict and machine-checkable; Contracts stay low-dependency.
- **Testing:** xUnit (match the surrounding module's major version), Shouldly assertions, NSubstitute mocks; Tier-1 tests pure (no Dapr/Aspire/network/containers); EventStore/Tenants testing fakes/builders before new doubles; tenant-isolation and rejection paths need negative-path tests.
- **Repo discipline:** `works` is an umbrella repo; only root submodules are initialized (never `--recursive`, never nested submodules). Works should contain domain code only; the Aspire host is the one acceptable technical component here.

## Non-binding event / port sketch (for the architect)

Illustrative only — the PRD's FRs are the contract; these shapes are a starting point, not a decision.

**v1 Domain Event catalog (FR-7):** `WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `ProgressReported`, `ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`, `WorkItemResumed`, `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`. Each carries the verbatim Raw Act; acting-Party identity + timestamp come from the binding + EventStore envelope. Theme 4 adds a `WorkItemRouted`-style event additively (no V2, no reshape).

**Executor Binding value object (FR-17/19):** `ExecutorBinding(PartyId, Channel, AuthorityLevel)` — `Channel` an extensible enum/value (MCP, CLI, Chatbot, Email, …); `AuthorityLevel` the proposed ordered set `{ Read, Contribute, Coordinate, Administer }` (carried-not-enforced in v1).

**Burn-Down (FR-3, cost-ready):** a `Meter(Unit, Estimated, Done)` with derived `Remaining`; v1 instantiates one Effort meter, and Theme 5 adds a parallel Cost meter reusing the same roll-up. Roll-Up (FR-11) is a projection: `rolledRemaining(item) = item.Remaining + Σ rolledRemaining(child)`.

**Await-Condition (FR-14):** a discriminated value `{ ChildCompleted(childId) | DateReached(instant) | ExternalSignal(correlationId) }`; a Suspended item may hold a *set* of these and resumes on the first match. Resume is a `ResumeWorkItem(correlationKey)` command (not a port) raised by adapters — child-completion internally, a timer adapter for dates, an external adapter for Theme 3 signals — keeping `Handle` clock-free (FR-15).

**Ports (FR-22):**
- `IExpectationResolver` — `Expectation Resolve(WorkItemState state)`; v1 ships a no-LLM implementation returning a structured/empty default. Theme 3 supplies an AI-inferring adapter.
- `IExecutorRouter` — abstraction only in v1 (no implementation wired); Theme 4 supplies routing/escalation adapters.

**Reference Value Objects (FR-21):** correlation IDs only — `PartyId` (Parties), `ConversationId` (Conversations), `TenantId` (Tenants), aggregate/ID helpers (Commons). No denormalized copies.

## Deferred-theme mechanism depth

Beyond the PRD §12 roadmap table, the brainstorm extraction captured per-theme mechanism detail (constrained-safe generation, confidence-gated auto-apply, start-cheap-escalate ↔ budget-degrade as one ladder run both ways, single-use bound expiring idempotent links, consent/residency-before-cost routing, cost-caps-as-DoS-guard). These are recorded in the session file and the brief addendum; they are *not* v1 requirements and are surfaced here only so the architect preserves the named v1 seams (ports, raw-act events, cost-ready burn-down, AuthorityLevel) that make them buildable additively.
