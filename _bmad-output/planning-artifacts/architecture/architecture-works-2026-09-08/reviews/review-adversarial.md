# Adversarial Reviewer Gate — Architecture Spine

- **Reviewed:** ARCHITECTURE-SPINE.md, updated 2026-09-12
- **Lens:** construct two independently built units one level down that obey the written ADs yet interoperate incompatibly
- **Verdict:** **FAIL**

The spine is substantially tighter than the prior architecture, but five cross-unit contracts still
permit incompatible implementations. Three can corrupt authority or durable effects; two can lose or
fork derived state. These are architecture holes, not implementation-detail choices.

## Finding Summary

| ID | Tier | Hole | Units that can diverge |
| --- | --- | --- | --- |
| ADV-C1 | Critical | Responsibility authorization has no atomic decision point or trusted context contract | EventStore gateway/actor runtime vs Works adapter/kernel |
| ADV-C2 | Critical | Child-creation evidence and the post-create parent race are not closed | Registry/Reactor vs Work Item aggregate |
| ADV-C3 | Critical | Effect canonicalization and correctness-receipt ownership are incomplete | Reactor/reminder producers vs EventStore target runtime |
| ADV-H1 | High | Rebuild does not fence mixed-version writers or bind the catch-up journal protocol | Rebuild coordinator vs live projection writers |
| ADV-H2 | High | WorkContributionSnapshot is not classified as a domain event, projection message, or internal merge value | Works projection producer vs EventStore merge runtime |

## ADV-C1 — Responsibility authorization has no atomic decision point

**Counterexample pair**

1. The Platform/EventStore team derives an authenticated Party, reads the current executor from an
   authorized read model, checks equality, and then dispatches the ordinary pure command. It considers
   AD-23 satisfied because the acting Party equalled the current Executor at authorization time.
2. The Works team makes each aggregate adapter envelope-aware and compares the trusted actor with the
   rehydrated WorkItemState inside the serialized actor turn. It considers AD-23 satisfied because it
   checks the current Executor at mutation time.

Both choices fit the written ownership and pure-Handle rules, but they disagree after a concurrent
Handoff. The first path can authorize executor A, queue the command, apply a handoff to B, and then let
A mutate the item. The second denies A. That is a security and state-transition fork.

**Evidence**

- AD-08 serializes the aggregate turn, but only discusses claim concurrency
  (ARCHITECTURE-SPINE.md:159-166).
- AD-23 requires equality with the current Executor but does not bind the comparison to the rehydrated
  state and serialized mutation turn (lines 385-403).
- AD-01 names only Handle(state, command), so no trusted command-context seam is bound (lines 82-90).
- Current reality demonstrates the missing seam: most aggregate wrappers accept command and state
  only, while just LinkConversation accepts CommandEnvelope
  (src/Hexalith.Works/WorkItemEventStoreAggregate.cs:31-143).

**Required tightening**

Amend AD-01/AD-23 to bind one EventStore-owned immutable AuthorizedCommandContext populated only from
validated transport/delegation evidence. The Works adapter must receive that context inside the same
serialized actor turn, after rehydration, and perform responsibility/origin admission against that
exact state immediately before Handle. No read model or caller payload may authorize mutation. Bind
the denial class and audit behavior so one implementation cannot append a domain rejection while
another returns an authorization failure.

## ADV-C2 — Child-creation evidence and terminal-parent races are unresolved

**Counterexample pair**

1. The Registry team interprets “matching persisted WorkItemCreated” as matching tenant, child ID, and
   Parent reference. ReservationId and FencingToken are checked immediately before dispatch but are
   not stored in the child event. The Registry attaches on that evidence.
2. The Work Item team interprets “matching” as an exact durable proof. It adds ReservationId,
   FencingToken, and the reserved-payload digest to WorkItemCreated and quarantines evidence lacking an
   exact match.

Both implement AD-21 as written because “matching” has no normative field set. They disagree on
whether the same durable child is Attached. The currently checked contracts illustrate the ambiguous
baseline: WorkItemCreated stores Parent but no reservation or fence
(src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs:8-18).

A second fork remains after valid child creation. AD-21 orders WorkItemCreated, then EdgeAttached,
then the parent's SpawnChild record (lines 347-357). If the parent becomes terminal between reservation
and that final parent command:

- one implementation keeps the Registry edge Attached and schedules terminal-cascade catch-up,
  treating the parent list as a non-authoritative mirror;
- another treats the parent's permanent SpawnChild rejection as a conflict and marks the already
  created child orphaned.

Both outcomes fit the Registry-as-authority rule plus the permitted operator orphan path, but they
produce incompatible tree membership. Current WorkItem state still stores a Parent and a
SpawnedChildWorkItemIds list (src/Hexalith.Works.Contracts/State/WorkItemState.cs:39-45,165-174), so
the mirror's authority cannot remain implicit.

**Required tightening**

Amend AD-21 to:

- prohibit Parent on ordinary CreateWorkItem and require an origin-restricted child-create command;
- persist tenant, parent, child, ReservationId, FencingToken, and a canonical reserved-payload digest
  in the creation evidence, and define exact Registry match rules;
- state that WorkItem Parent/child fields and ChildSpawned are audit/compatibility mirrors only and
  may never enumerate, validate, or own topology;
- choose the terminal-parent race outcome and the linearization point. A safe convergent rule is to
  record parent authorization before child creation, attach only after exact child evidence, and make
  every late Attached edge trigger terminal-ancestor cascade catch-up. If another outcome is chosen,
  bind it just as explicitly.

## ADV-C3 — Effect identity and receipt correctness can fork

**Counterexample pair**

1. The Reactor uses durable CLR discriminator text for effect kind, assigns ordinal by its local
   command enumeration order, and hashes the options-free serialized command bytes.
2. The reminder/process runtime uses a stable logical effect-name enum, assigns ordinal after sorting
   targets, and hashes EventStore's schema-normalized IdempotencyCanonicalIntent.

Both use the AD-26 field order, UTF-8, big-endian lengths/numbers, SHA-256, and Crockford Base32.
However, “canonical” text, effect-kind vocabulary, ordinal allocation, and canonical command digest
are not defined. They therefore produce different EffectIds and delegation JWT bindings for the same
logical effect, or the same EffectId with digests each side considers different. AD-11 and AD-25 also
say only “length-prefixed canonical tuple,” without inheriting AD-26's exact width and numeric encoding
(lines 185-196 and 418-431).

The correctness record has a separate ownership hole. “The target persists a durable receipt” does
not choose between:

- EventStore-owned private target-actor state committed atomically with append/rejection; and
- a Works-owned durable receipt event/state in each target aggregate.

Both satisfy AD-26 literally but create different durable catalogs, transaction boundaries, restore
behavior, and replay authority. A crash between target mutation and a non-atomic receipt write can
re-execute the effect despite the “never lifecycle-pruned” promise (lines 433-447).

**Required tightening**

Bind a single versioned, EventStore-owned effect codec used by Reactor, reminder, recovery, delegation,
gateway, and target admission. Freeze canonical text normalization, effect-kind identifiers, per-family
ordinal derivation, command semantic-intent encoding, digest bytes, and collision behavior. Make AD-11
and AD-25 explicitly reuse that tuple codec. Name the one receipt owner and require receipt plus
success/rejection append to share the target actor's atomic commit boundary; keep the receipt out of
Works' domain-event catalog unless the deliberate decision is the opposite. Golden vectors should be
an admission gate for each catalogued effect family, not the source of an otherwise unstated contract.

## ADV-H1 — Rebuild can lose writes from an old producer

**Counterexample pair**

1. A rebuild coordinator creates the Building epoch while older projection instances continue writing
   the active generation. Only refreshed writers notice the epoch and journal. The coordinator commits
   staging and later drains its journal.
2. Another coordinator refuses to begin until every writer is upgraded and leased into the journal
   protocol, so no writer can update the active generation after the capture watermark.

AD-16 explicitly constrains “refreshed ordinary writers” but says nothing that fences an older writer
(lines 239-253). AD-29's reader-first rollout does not establish a minimum writer protocol or per-write
epoch check. Both coordinators can claim compliance, yet the first drops an acknowledged old-writer
update at manifest promotion.

Even with only refreshed writers, no journal identity, deduplication rule, checkpoint transaction, or
equal-position conflict rule is bound. Different family writers can acknowledge the same envelope
under incompatible journal shapes and drain semantics.

**Required tightening**

Amend AD-16 with a rebuild protocol version and writer lease. BeginBuilding must fail until every
active writer can honor the epoch, and every write must verify the current epoch/lease before
acknowledgement; stale writers fail closed and do not acknowledge. Bind journal ownership and an
immutable key such as tenant + epoch + family + source stream + envelope position, canonical payload
digest, equal-key conflict behavior, and the atomic relationship between journal persistence and the
consumer checkpoint. Promotion/read-stale semantics may remain as written once no unjournaled writer
can succeed.

## ADV-H2 — Contribution snapshots can become two different kinds of durable fact

**Counterexample pair**

1. Works Projections derives WorkContributionSnapshot from each committed source envelope and sends it
   as a non-domain merge input to an EventStore-owned projection SPI.
2. Works Reactor submits a command that appends WorkContributionSnapshot as a durable Work Item event,
   then EventStore consumes that event into the roll-up document.

Both follow “after each accepted state-changing event, Works folds descendant state and emits” and
both let EventStore treat the payload as opaque (AD-22 lines 368-383). They are incompatible:
implementation 2 expands the durable event catalog and state-change sequencing, can create emission
loops, and reconstructs differently from implementation 1. The sourceEnvelopeSequence cannot be known
inside the original pure Handle because EventStore assigns it after persistence, which makes the
placement decision load-bearing.

The same paragraph leaves the compile-time port direction unclear: AD-21 names a Works-owned
IWorkTreeTopologyReader while also assigning EventStore the generic relationship adapter
(lines 359-364), and AD-18 prohibits reusable runtime from pointing outward into Works
(lines 268-278).

**Required tightening**

Amend AD-22/AD-18 to classify WorkContributionSnapshot as a deterministic, non-domain projection
message derived only from a persisted EventStore envelope; it must never be appended to Work Item or
Registry streams and must not advance WorkItem payload Sequence. EventStore should own the generic
merge/relationship SPI, Works Projections should own the domain snapshot and pure fold, and the Works
executable should be the only composition adapter between them. Bind the source-envelope identity and
retry/quarantine contract at that adapter.

## Acceptance Condition

The adversarial verdict can pass after the spine closes all five choices above with stable AD
amendments or explicitly defers them behind a gate that prevents independent implementation. At
minimum, ADV-C1 through ADV-C3 must be resolved before implementation stories are considered safe to
run in parallel.

## Recheck — 2026-09-12

**Verdict:** **FAIL — no Critical findings remain; two High findings remain.**

The revision closes ADV-C1, the terminal-parent and durable-evidence portions of ADV-C2, ADV-C3's
effect codec and receipt ownership/atomicity, ADV-H1, and ADV-H2. The following two lower-level
counterexamples still survive.

### ADV-H3 — The reservation payload digest has no shared codec

**Counterexample pair:** the Registry hashes the options-free serialized EdgeReserved child payload,
while the Work Item adapter hashes EventStore's schema-normalized CreateWorkItem semantic intent.
Both persist and compare a “reserved-payload digest” exactly as AD-21 requires, but the same authorized
child never attaches because the byte contract is unnamed. AD-26's EventStore codec explicitly owns
only AD-11/25/26 digests, not the AD-21 digest.

**Closure:** State that the AD-21 reserved-payload digest is the AD-26 codec's semantic command digest
for the complete token-bearing CreateWorkItem intent, including an immutable descriptor version, and
that EdgeReserved, delegation, WorkItemCreated, and attachment validation reuse those exact bytes.

### ADV-H4 — Child-wait suspension lost its mutation and authorization owner

AD-21 now makes the post-attachment SpawnChild bookkeeping act state-neutral in every parent status.
That closes the terminal-parent race, but EdgeReserved still carries the complete child-create payload,
FR-14..FR-16 remain bound, and AD-10 still owns child-completion translation.

**Counterexample pair:** the Registry/Reactor unit interprets the reserved
SuspendParentUntilChildCompletes intent as a separate system Suspend effect after attachment. The Work
Item/EventStore unit obeys AD-23's exhaustive origin matrix and rejects that effect because Suspend is
responsibility-bound to the current Executor and no registry/Reactor Suspend purpose is allowed; its
SpawnChild handler also cannot create the await because AD-21 forbids changing Status or
AwaitConditions. Both units follow their local AD contract, but the integrated parent never suspends
and child-completion Resume rejects.

**Closure:** Bind one path. Either make the exact-token SpawnChild bookkeeping act atomically add the
ChildCompleted await and suspend only when the reserved intent requested it and the current parent
state permits it, with an explicit state-race outcome for all other states; or introduce a distinct
origin-restricted SuspendForChild command, authorize its named Reactor purpose in AD-23, and define its
idempotent race behavior. The “every parent status without changing Status or AwaitConditions” rule
must be narrowed to the terminal bookkeeping case if the first option is chosen.

## Focused Final Recheck — 2026-09-12

**Verdict:** **PASS — no Critical or High adversarial findings remain in AD-21/23/26.**

- **ADV-H3 closed:** AD-21 now requires the durable reserved-payload digest to be produced by the
  single versioned AD-26 codec and matched as part of the exact creation evidence. Registry,
  delegation, creation, and attachment can no longer independently choose digest bytes.
- **ADV-H4 closed:** the exact-token SpawnChild bookkeeping act now emits ChildSpawned in every parent
  status, suspends an InProgress parent or unions the child condition into an already Suspended parent
  when the reserved flag is true, preserves terminal state, and durably schedules Cancelled/Expired
  cascade catch-up before acknowledgement. AD-23 separately authorizes the registry-originated
  Create/SpawnChild pair, so no unlisted system Suspend path is required.
- **Prior Critical contracts remain closed:** responsibility checks occur after rehydration within the
  serialized actor turn; exact reservation/fence evidence owns attachment; terminal-parent races have
  one convergent outcome; and EventStore exclusively owns the target inbox with receipt/disposition
  atomic with target persistence.

No pair of independently built lower-level units following the revised AD-21, AD-23, and AD-26 was
found that could choose incompatible topology, authorization, effect identity, or receipt authority.
