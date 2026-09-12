# Security and Data-Integrity Architecture Review — 2026-09-08

**Artifact:** `_bmad-output/planning-artifacts/architecture.md` (1197 lines; register AD-01…AD-25 at lines 26–455 is binding).
**Intent:** validate only; no repository file was modified. Evidence was read from the Works tree at HEAD `6e2fb4d` (working tree includes uncommitted Story 4.8 edits) and from the checked-out `references/Hexalith.EventStore` submodule at `7598f67c`.
**Context:** re-validation of the 2026-09-05 gate's SEC-DI-01…07 after the 2026-09-06 register update (AD-20…AD-25) and the 2026-09-06 commit `33a27e2` "Implement mTLS for Dapr Sentry".

**Verdict:** **ACCEPT WITH CONCERNS** — the register now states the missing security invariants as falsifiable, owned rules (SEC-DI-01/02/03 are closed at the invariant level), but four high findings show the new ADs are not yet tight enough for a platform implementer and a Works handler to build compatibly, and one new AD (AD-25) introduces a concrete premature-expiry data-integrity hole; none is a Story 4.9 *entry* blocker, all highs must be dispositioned before Story 4.9 acceptance or before the AD-21/AD-25 stories are drafted, whichever comes first.

## Finding summary

| ID | Severity | Disposition | Title |
|---|---|---|---|
| SEC-01 | High | Discuss | AD-23 delegation context has no shape; today's internal path is a global-admin super-identity, and the sandbox borrows a user identity |
| SEC-02 | High | Discuss | AD-24's exclusive-originator rules are unenforceable by Dapr ACL for pub/sub, actor callbacks, and per-command origin; no app-channel control is named |
| SEC-03 | High | Autofix | AD-25 lets a stale expiry reminder expire a live, rescheduled item (and cascade) — `ExpireWorkItem` carries no due-instant witness |
| SEC-04 | High | Discuss | VAL-H12 revisit condition is too late: irreversible privacy shapes are already persisted and crypto-shredding is an EventStore-level design |
| SEC-05 | Medium | Autofix | AD-23 membership source, identity transport, global-admin bypass, and "result filtering" are ambiguous between EventStore-as-built and the AD text |
| SEC-06 | Medium | Discuss | Sandbox mTLS proves encryption, not attestation; the "dev-only exemption" is comments, not a control; two configs still `defaultAction: allow` |
| SEC-07 | Medium | Autofix | Forged/replayed contribution at the `/project` seam poisons LWW slots; AD-22 fan-out amplifies it; the provenance check has no bound location |
| SEC-08 | Medium | Autofix + Defer | Recovery exhaustion still swallows failures and stays Ready; no AD rule forbids it (SEC-DI-05 residue) |
| SEC-09 | Medium | Autofix | SEC-DI-07 (uniform quarantine contract) was dropped silently — neither adopted nor in the open register |
| SEC-10 | Medium | Discuss | "Audited" is asserted five times with no audit record shape or sink; AD-21 operator repair has no command/event |
| SEC-11 | Medium | Autofix + Defer | Supply chain and secrets posture is silent: root `NuGetAudit=false` overrides the Builds baseline; no CI in repo; broker/Sentry/OIDC/DAPR_API_TOKEN ownership unbound |
| SEC-12 | Low | Defer | Cross-tenant recovery registries and the four-app shared actor store are unchanged (VAL-H09 / SEC-DI-06) |
| SEC-13 | Low | Autofix | "Production ingress is prohibited until live" has no enforcing mechanism |

Counts: **0 critical, 4 high, 7 medium, 2 low**.

## Prior SEC-DI disposition

| Prior item | Status now | Why |
|---|---|---|
| SEC-DI-01 tenant/actor provenance | **Closed at invariant level** by AD-23 (architecture.md:344–368) — owner named (platform host + Hexalith.Tenants), assertion-mismatch deny, minimum floor, negative tests bound to 4.9 R5. Residue: SEC-01, SEC-05, SEC-13. |
| SEC-DI-02 trusted origin | **Closed at invariant level** by AD-24 (architecture.md:370–395) — mTLS/trust-domain/namespace, deny-by-default, exclusive originators, forged-sequence negative test, positive reminder-registration test. Residue: SEC-02, SEC-06, SEC-07. |
| SEC-DI-03 tree authority | **Closed** by AD-21 (architecture.md:272–316) — registry actor is the single authority and concurrency boundary, caller facts demoted to assertions, Reserved→Attached/Released lifecycle, rebuild detects divergent evidence and suppresses dependents. Residue: SEC-10 (repair shape) and SEC-02 (origin restriction of `SpawnChild`). |
| SEC-DI-04 privacy lifecycle | **Open** as VAL-H12 (architecture.md:451). See SEC-04 — the revisit condition needs tightening. |
| SEC-DI-05 recovery fail-safe | **Open** — appears only as AD-20 matrix row R8 (architecture.md:267) and VAL-H06/H07 (445–446); no AD *Rule* forbids the current swallow. See SEC-08. |
| SEC-DI-06 cross-tenant registries | **Open** as VAL-H09 (architecture.md:448); AD-15 explicitly points at it (174–182). See SEC-12. |
| SEC-DI-07 quarantine contract | **Dropped** — not adopted, not in the open register. See SEC-09. |

## High findings

### SEC-01 — [high] — AD-23 delegation context has no shape; the as-built internal path is a global-admin super-identity

**Issue.** AD-23 requires internal originators (reactor, reminder callbacks, reminder registrar, replay, recovery) to act under "an authenticated workload identity plus an explicit, auditable tenant-delegation context, never a borrowed user identity" (architecture.md:356–359). Nothing says what the delegation context *is* (a claim? a header? a field on `SubmitCommandRequest`? an RFC 8693 `act` claim?), who mints it, what it scopes (one tenant, one aggregate, one causation id?), or where it is persisted so it can be audited. A platform implementer and the Works `IWorkCommandSubmitter` (R11) cannot build this compatibly from the text.

**Attack/failure scenario.** As built, an internal caller admitted by `Authentication:DaprInternal:AllowedCallers` is minted `sub = system:<appId>` **and `global_admin=true`**; `ClaimsTenantValidator` short-circuits to *Allowed for any tenant* for global admins. So the Works host — via a reminder firing, a cascade replay, or a compromised recovery loop — can submit any command to any tenant with no delegation evidence; the persisted envelope `UserId` becomes `system:works`. In the Keycloak-enabled sandbox path the Works service is instead given the **tenant-A user's** client credentials, i.e., exactly the "borrowed user identity" AD-23 forbids, and every date-resume for every tenant would be persisted as that user.

**Source.** architecture.md:356–359 (delegation), 360–362 (floor), 678 (prose restatement).

**Corroborating evidence.**
- `references/Hexalith.EventStore/src/Hexalith.EventStore/Authentication/DaprInternalAuthenticationHandler.cs:21–38` — header `dapr-caller-app-id` → allow-list → `sub=system:<app>` + `global_admin=true`.
- `references/Hexalith.EventStore/src/Hexalith.EventStore/Authorization/ClaimsTenantValidator.cs:23–24` — global admin ⇒ `Allowed` for any tenant.
- `references/Hexalith.EventStore/src/Hexalith.EventStore/Controllers/CommandsController.cs:111–122` — `request.Tenant` passed straight into `SubmitCommand`.
- `src/Hexalith.Works.AppHost/Program.cs:108` (`AllowedCallers__0=works`), `:224–242` (Works gets `tenant-a-user` client credentials), `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs:39–65` (gateway client via sidecar, `DAPR_API_TOKEN`).
- `src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs:23–30` — submission carries `Tenant` as a plain field, no delegation token.
- `references/Hexalith.EventStore/src/Hexalith.EventStore/Authorization/DualPrincipalClaimsHelper.cs:14–17` — an RFC 8693 `act.sub` dual-principal helper already exists on the query path and is the obvious seed for the missing shape.

**Suggested disposition.** Discuss (before the AD-21 registry story and the AD-25 expiry story are drafted, since both add internal originators). Minimum text to bind in AD-23: (a) the delegation context is a per-submission, tenant-scoped assertion `{workloadId, tenantId, causationId, purpose}` carried on the R11 submission contract and persisted on the envelope; (b) an internal workload identity is **never** a global administrator — `DaprInternal` callers get a tenant-delegation scope, not `global_admin`; (c) the sandbox must not use user client-credentials for the Works service; (d) negative test: a DaprInternal caller submitting to a tenant outside its delegation is denied.

### SEC-02 — [high] — AD-24's exclusive-originator rules cannot be expressed in Dapr access control; the app channel is unprotected and no app-level check is named

**Issue.** AD-24 binds "reminder callbacks are accepted only from the Works actor identity", "actor reminder register/cancel/reschedule accepted only from the Works service's own workload identity", "`SpawnChild` accepted at the gateway only from the reactor's workload identity", and "no direct workload HTTP reachability to subscription, actor, projection, replay, or admin routes" (architecture.md:372–384). Dapr's access-control policy matches `appId` + `trustDomain` + `namespace` + `operations{name,httpVerb,action}` and applies **only to service invocation** into the *called* app's sidecar (docs: https://docs.dapr.io/operations/configuration/invoke-allowlist/). It does not govern: (1) pub/sub deliveries the local sidecar pushes to `/work/events`; (2) actor reminder/timer callbacks the local sidecar pushes to `/actors/{type}/{id}/method/remind/{name}`; (3) anything that reaches the app port directly; (4) *which command type* a caller may submit. The AD does not say which mechanism carries each rule, so R3/R6/R10/R11 implementers will pick different ones.

**Attack/failure scenario.** Any process that can reach the Works app port (a sidecar-less pod in the namespace, a debug port-forward, a misrouted ingress) can `POST /work/events` with a self-consistent envelope (`Domain=work`, valid ULID `MessageId`, matching tenant/aggregate ids) and trigger cascade cancel/expire, child-resume, or reminder registration handlers; or `PUT /actors/DateReminderActor/{id}/method/remind/{name}` to fire an arbitrary registered reminder early. The processor's identity checks only prove attacker-controlled fields agree. Dapr's own app-channel control for this — the `dapr-api-token` header (https://docs.dapr.io/operations/security/app-api-token/) — is validated nowhere in Works or in the EventStore DomainService SDK (only in `EventStore.Operations`).

**Source.** architecture.md:372–384 (bindings), 262 (R3 "AD-24 negative tests"), 269 (R10).

**Corroborating evidence.**
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml:19–50` — policies cover `/process`, `/query`, `/project`, `/project/rebuild/shared/v1`, `/replay-state` (from `eventstore`) and `/work/events` (from `eventstore-operations`); nothing can cover pub/sub or actor callbacks.
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:16–29` — subscription route "intentionally carries no additional caller authentication"; `:70–82` maps mismatch/invalid to HTTP 200.
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:132–152` — envelope-vs-payload equality only, then trusted `EventStoreDomainEventContext` (incl. `SequenceNumber`) is handed to cascade/resume/reminder handlers (`src/Hexalith.Works/Runtime/WorksHost.cs:68–82`).
- `src/Hexalith.Works/Runtime/WorksHost.cs:137` (`MapActorsHandlers()`), `src/Hexalith.Works/Reminders/DateReminderActor.cs:52–77` — `ReceiveReminderAsync` submits `ResumeWorkItem` from stored state with no caller check.
- `grep -rn "dapr-api-token|APP_API_TOKEN" src tests` → only `DAPR_API_TOKEN` for outbound; `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs` — zero occurrences of `RequireAuthorization|UseAuthentication|dapr-api-token|dapr-caller-app-id`; `references/Hexalith.EventStore/src/Hexalith.EventStore.Operations/Security/DaprAppChannelTokenMiddleware.cs:11–13` is the only app-channel token guard in the ecosystem.
- Per-command origin ("`SpawnChild` only from the reactor identity") is a gateway-side `(caller, commandType)` authorization — EventStore has an `IRbacValidator` seam (`Pipeline/AuthorizationBehavior.cs:80–96`) but no binding names it.

**Suggested disposition.** Discuss. Amend AD-24 to state per rule *where* it is enforced: sidecar ACL (service-invocation routes, expressible), broker producer/consumer ACL + component `publishingScopes` (topic origin), **app-channel token (`dapr-api-token`) validated by the domain-service SDK on every route + network policy that admits only the local sidecar** (pub/sub delivery, actor callbacks, direct reachability), and **gateway RBAC keyed by `(workload identity, command type)`** (`SpawnChild`, replay, rebuild). Add the app-channel token middleware to the EventStore DomainService SDK (AD-20 rule: generic capability lands in the SDK first). Extend the 4.9 negative suite with: direct `POST /work/events` without the app token, and direct actor `remind` invocation.

### SEC-03 — [high] — AD-25 stale expiry reminder can expire a live, rescheduled item and cascade-cancel its subtree

**Issue.** AD-25 makes expiry a privileged side effect ("firing emits `ExpireWorkItem`… firing is idempotent" against *terminal* state, architecture.md:397–414). Idempotency against terminal state is the wrong invariant: the dangerous case is a reminder that fires for a Due Date the item no longer has. AD-25 says reminders are "rescheduled mechanically from lifecycle events", but cancellation of the *old* reminder is a separate best-effort actor call, the reminder name is a function of `(tenant, item, correlationKey)` so a reschedule creates a *second* reminder rather than replacing the first (architecture.md:760–762), and `Handle` — by design clock-free — cannot tell "this fire is stale". Nothing in AD-25 requires `ExpireWorkItem` to carry the due instant it was registered for, or `Handle` to reject a mismatch.

**Attack/failure scenario.** Item due 2026-10-01 → expiry reminder R1 registered. Operator reschedules to 2026-12-01 → R2 registered; unregister of R1 fails (best-effort, logged) or the reconciliation pass re-registers R1 from a lagging index. R1 fires on 2026-10-01 → `ExpireWorkItem` → item `Expired` although not due → `TerminalCascadeTranslator` cancels/expires every descendant; the checkpointed cascade makes this durable and "recoverable". No forgery is needed; this is an ordinary at-least-once race. The same pattern also lets a replayed/forged expiry-reminder callback (SEC-02) expire any live item, because the domain has no witness to refuse.

**Source.** architecture.md:397–414 (AD-25), 760–762 (reminder naming), 151–157 (AD-12 advisory-until-fired), 137–149 (AD-11).

**Corroborating evidence.**
- `src/Hexalith.Works/Reminders/DateReminderActor.cs:79–90` — `TryUnregisterAsync` swallows failures ("safe because the resume is idempotent" — true for resume, false for expire).
- `src/Hexalith.Works/Reminders/DateReminderName.cs:35–42` — name keyed by correlation key (the instant), so a new instant is a new reminder, not a replacement.
- `src/Hexalith.Works/Reminders/DateReminderReconciler.cs:96–137` — re-registers from the index; a stale index entry re-arms a stale reminder.
- `docs/lifecycle-transition-matrix.md` is authoritative for the per-state expire table (AD-17) — the current `ExpireWorkItem` shape is not shown to carry a due instant.

**Suggested disposition.** Autofix in AD-25: (a) `ExpireWorkItem` carries `DueInstant` (the value the reminder was registered for); (b) `Handle` rejects with a domain rejection when `state.DueDate != command.DueInstant` (pure, no clock) — a stale/foreign fire is a no-op with evidence; (c) the same witness rule for `ResumeWorkItem(DateReached(instant))` is already implicit via `AwaitCondition` matching — state it explicitly; (d) add the "reschedule then stale fire" scenario to the R6 acceptance list. This closes both the race and the replay/forgery variant without touching AD-12.

### SEC-04 — [high] — VAL-H12's revisit condition ("before production data is admitted") is too late for an immutable, additive-only store

**Issue.** The only privacy rules are "never log payloads/personal data" (architecture.md:478, 847–848) and the open VAL-H12 row (451). Meanwhile AD-01 binds additive, serialization-tolerant evolution with "every event ever produced must remain backward-compatibly deserializable" (494). Erasure in such a store is only possible by **crypto-shredding**, which requires per-tenant (or per-subject) key envelopes applied *at persistence*, and that is an EventStore-envelope-level capability, not a Works one. Every durable type shipped before that decision bakes plaintext free text into a shape that cannot be migrated without a `V2` the architecture forbids.

**Attack/failure scenario.** A tenant offboards or a user exercises erasure. `WorkItemCreated.Obligation.Description` (free text), `ProgressReported.Note`, `WorkItemAssigned` executor references, and the envelope `UserId` (= OIDC `sub`) are persisted verbatim in the aggregate stream, the shared-rebuild candidate state, the read models, and the subscriber/command dead-letter queues (retention "manual today"). There is no key to destroy, no field classification, no defined retention, so the only options are a hard delete of the tenant stream (breaks AD-01/AD-16 rebuild guarantees) or non-compliance.

**Source.** architecture.md:451 (VAL-H12), 478, 494, 782–786 ("raw act, verbatim"), 847–848.

**Corroborating evidence.**
- `src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs` — `Description` free text; `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs:8–18`; `src/Hexalith.Works.Contracts/Events/ProgressReported.cs:15` (`Note`).
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/WorkItemCreated.v1.json:1` — golden corpus proves verbatim plaintext persistence.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPersister.cs:104–121` — envelope `UserId` = JWT `sub`.
- `docs/operations/subscriber-dead-letter-operator.md:62–76` — "Retention is manual today"; archiving does not remove the record.
- Positive: reminder names are SHA-256-derived (`DateReminderName.cs:56–61`) and read-model keys carry only tenant/item ids (`WorksReadModelKeys.cs:26–46`) — no PII in keys.

**Suggested disposition.** Discuss (owner: Platform Maintainer + EventStore). Tighten the VAL-H12 revisit condition to: **before the next durable-type catalog addition ships (Story 1.5, AD-21 registry types, AD-25 `ExpireWorkItem`) and before any non-synthetic data enters any shared environment including staging.** Minimum decisions to record: field classification for the 40 existing types; whether free-text fields are field-level encrypted with tenant keys (EventStore SDK capability) or replaced by opaque references; DLQ/rebuild-candidate/read-model retention; and that envelope `UserId` is a pseudonymous subject id.

## Medium findings

### SEC-05 — [medium] — AD-23 membership source, identity transport, global-admin semantics, and "result filtering" are ambiguous against EventStore-as-built

**Issue.** (a) AD-23 says the tenant is "derived from verified claims mapped through **Hexalith.Tenants** membership" (350–352); EventStore actually validates the request's `Tenant` against a JWT `eventstore:tenant` claim (or `tenants`/`tenant_id`/`tid`), consults no Tenants membership store, and offers an optional `ITenantValidatorActor` that is off by default. Two implementers will build two different membership truths with different revocation latency. (b) Where the derived identity travels is unbound: as built it travels as *body fields* (`SubmitCommand.Tenant/UserId` → envelope → `ProjectionRequest.TenantId` / `QueryEnvelope.TenantId`) and the domain service trusts them only because its sidecar admits app-id `eventstore`; AD-23 never says "never headers, only SDK request contracts". (c) `global_admin` bypasses the tenant check entirely and is not mentioned in the floor. (d) "queries ⇒ tenant member plus query-side result filtering" (360–361) does not say filtering *by what* — Works filters by tenant equality only and leaves a per-user predicate seam unused; a platform implementer could read it as per-user visibility.

**Attack/failure scenario.** A user removed from a tenant in Hexalith.Tenants keeps issuing commands until the token expires (claims-based path), or is denied immediately (actor path) — divergent behavior across hosts; a Works query handler and a platform query filter disagree on whether an assignee-only view is required.

**Source.** architecture.md:350–362, 678.

**Corroborating evidence.** `references/Hexalith.EventStore/src/Hexalith.EventStore/Authorization/ClaimsTenantValidator.cs:23–51`; `.../Authentication/EventStoreClaimsTransformation.cs:68–76`; `.../Authorization/ActorTenantValidator.cs:39–49` + `EventStoreAuthorizationOptions.cs:15` (null ⇒ claims); `.../Controllers/QueriesController.cs:58–62, 85–104`; `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs:51–56, 102–105`; `src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs:51–55`; `src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs:21–34` (optional predicate, never supplied). Does Works still trust envelope `TenantId`? **Yes — by design**: the SDK contract makes the gateway the sole enforcement point; that is acceptable only if AD-23 says so and SEC-02's app-channel control exists.

**Suggested disposition.** Autofix wording: bind membership truth to *one* of {token claims issued from Tenants; live `ITenantValidatorActor` backed by Tenants} with a stated revocation bound; state that the domain service receives identity only through SDK request contracts from the gateway and never re-derives from headers; state that `global_admin` is a break-glass operator role subject to the AD-23 audit rule and never granted to workload identities (ties to SEC-01); define result filtering as tenant-equality per returned item, no per-user visibility in v1.

### SEC-06 — [medium] — Sandbox mTLS proves transport encryption, not identity attestation; the dev-only exemption is comments, not a control

**Issue.** Commit `33a27e2` composes a self-hosted Sentry, TLS placement/scheduler, per-sidecar deny-by-default ACLs — real progress on AD-24. But: (a) the AppHost injects the **issuer certificate and private key** (`DAPR_CERT_CHAIN`, `DAPR_CERT_KEY`) into *every* sidecar's environment, and self-hosted Sentry issues a workload certificate for whatever app-id a daprd claims — so any local process can obtain a valid `works` or `eventstore` SPIFFE identity; a "spoofed app ID" negative test passes in this topology for the wrong reason (verify against the Sentry validator docs, https://docs.dapr.io/operations/security/mtls/). (b) `accesscontrol.yaml` (EventStore sidecar) and `accesscontrol.eventstore-admin.yaml` still declare `defaultAction: allow` — an unnamed relaxation. (c) AD-24 requires relaxations to be "explicitly named … recorded in the `Hexalith.Platform` repository"; today the relaxations live in YAML comments inside the Works AppHost, and nothing machine-checks the production profile. (d) The declared production trust domain/namespace values are unnamed (sandbox uses control-plane `localhost`, workload `public`/`default`).

**Attack/failure scenario.** Story 4.9 R10 runs the AD-24 negative suite on the sandbox profile, all tests pass, and production ships with an ACL whose `trustDomain: public` matches every workload and whose `defaultAction: allow` on the EventStore sidecar admits any sidecar in the mesh.

**Source.** architecture.md:372–374, 391–395, 269 (R10).

**Corroborating evidence.** `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:170–181` (issuer key into every sidecar env), `:47–56` (`--trust-domain=localhost`); `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.yaml:18–19`, `accesscontrol.eventstore-admin.yaml:16–18` (`defaultAction: allow`); `accesscontrol.works.yaml:8–9` ("Local development only"); `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs:235–290` — asserts args and wiring only; no negative (spoof/wrong-domain/direct-route) test exists in the repo (`grep -rli "spoof|wrong trust|forged" tests` → none).

**Suggested disposition.** Discuss. AD-24 should require: the negative suite runs on an *attested* topology (Kubernetes or JWKS Sentry validator), never self-hosted; a machine-readable exemption manifest (list of relaxed controls) whose absence in the production profile is asserted by a test (e.g. "no `defaultAction: allow` in any production Configuration"); the production trust domain and namespace as named values in `Hexalith.Platform`.

### SEC-07 — [medium] — A forged or replayed descendant contribution poisons ancestor LWW slots; the provenance check location is unbound

**Issue.** AD-24 says "authenticated provenance is required before an envelope `SequenceNumber` is trusted as canonical" (385–386) but not *where* that check lives. The `/project` handler receives the events **in the request body** (`ProjectionRequest.Events[].SequenceNumber`) and feeds `dto.SequenceNumber` straight into the roll-up `Accept` LWW; the only provenance is the sidecar ACL admitting app-id `eventstore`. Under AD-22 a single accepted delivery writes into *every* ancestor's document, so one forged high-sequence contribution is amplified up the tree and — because LWW ignores lower sequences — silently suppresses all later genuine contributions until a shared rebuild.

**Attack/failure scenario.** With SEC-02's app-channel gap, `POST /project` with `SequenceNumber: 9_000_000` and `ProgressReported{DoneDelta: huge}` for a leaf; every ancestor's rolled-Remaining collapses to 0 and stays there; "what's next" ordering and the claimable pool are derived from the same read models. A replayed genuine event is harmless (idempotent) — the hazard is forgery, not replay.

**Source.** architecture.md:82–95 (AD-06), 318–341 (AD-22), 385–386.

**Corroborating evidence.** `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:124–191` (`request.Events`, `dto.SequenceNumber`); `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:44–59, 122–127`; `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs:55–68` (identity equality only — correct, but not provenance); `accesscontrol.works.yaml:34–36`.

**Suggested disposition.** Autofix wording in AD-24/AD-22: the provenance requirement is satisfied **only** by (sidecar ACL admitting `eventstore` on `/project`) **and** (app-channel token — SEC-02); the R4 fan-out seam must additionally verify that the delivered `SequenceNumber` does not exceed the stream head it can re-read from the gateway (a cheap witness) *or* re-read the events from the authoritative stream rather than trusting the body. Add "forged high `SequenceNumber` via `/project`" to the R4 negative scenarios (the current AD-24 list names the subscription path only).

### SEC-08 — [medium] — Recovery exhaustion still swallows failures and leaves the host Ready; no AD rule forbids it

**Issue.** The prior gate's SEC-DI-05 asked for an autofix invariant. The update placed it only in matrix row R8 (267: "exhaustion ⇒ durable evidence + degraded readiness") and the open register (445–446). No AD *Rule* forbids fail-open recovery, so a Story 4.9 implementer is free to port the current behavior.

**Attack/failure scenario.** Startup cascade recovery throws (state store unavailable for 10 s); the service logs a warning and continues; `/alive` stays healthy; the Dapr app health check keeps routing; no retry occurs until the next restart. The log text claims "It will be retried at-least-once", which is false for the startup passes. A tenant with an incomplete cascade or a lost date reminder is stranded indefinitely with no alert.

**Source.** architecture.md:267 (R8), 137–149 (AD-11 rule), 445–446.

**Corroborating evidence.** `src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:29–32` (catch-all, warning, return); `src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs:93–96` (per-entry swallow); `src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:37–61` (5 attempts then return); `src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs:20, 48` (feature switch, default 5); `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs:32–36` (misleading message); `src/Hexalith.Works/Runtime/WorksHost.cs` — no readiness contribution from recovery; `src/Hexalith.Works.AppHost/Program.cs:138, 173` (`/alive` only).

**Is it safe to leave open until 4.9?** Yes for the sandbox (no production ingress, idempotent passes, restart re-runs), **provided** the rule is bound now so R7/R8 cannot inherit the swallow.

**Suggested disposition.** Autofix (add to AD-11 Rule or a new AD-20 Rule): "Exhausted reminder/cascade/checkpoint recovery must (1) leave durable unresolved-work evidence, (2) mark a named readiness signal degraded for the affected tenant/capability, (3) retry on a durable schedule independent of restart, (4) never log 'will be retried' unless a scheduled retry exists." Defer enforcement to Story 4.9 R7/R8 acceptance (already the register's condition).

### SEC-09 — [medium] — SEC-DI-07 (uniform quarantine contract) was dropped without disposition

**Issue.** The 2026-09-05 gate rated a cross-surface quarantine rule Medium/Autofix. The 2026-09-06 update neither adopted it nor listed it in the open register; "quarantine" appears only as an operator-role noun (362, 380). The incompatible defaults remain: the subscription route acknowledges unknown/mismatched/invalid deliveries with HTTP 200 and marks them complete, while the `/project` dispatcher parks after N failures under a tenant-scoped key, and the shared-rebuild path marks incomplete evidence.

**Attack/failure scenario.** A malformed-but-state-affecting event (schema drift after a Story 1.5 catalog change) is acknowledged as processed on the subscription path — the marker store records completion, the DLQ never sees it — while the projection path parks the aggregate. Cascade/resume side effects are silently lost with no durable evidence.

**Source.** architecture.md:436–455 (register has no SEC-DI-07 row), 450 (VAL-H11 is adjacent but about compatibility, not disposition).

**Corroborating evidence.** `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:70–82`; `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:115–126, 304–314`; `src/Hexalith.Works/Projections/WorksReadModelKeys.cs:45–46` (parking key = partial quarantine evidence); `src/Hexalith.Works/Runtime/WorksProjectionOptions.cs`.

**Suggested disposition.** Autofix: add a register row (VAL-H13 or fold into VAL-H11) with the prior gate's rule text — unknown/malformed *state-affecting* input is never acknowledged as applied; durable tenant-scoped quarantine with source hash, reason, sequence; reactor emits no side effect; replay is an audited operator action. Bind to R3 acceptance.

### SEC-10 — [medium] — "Audited" is asserted without an audit record shape or sink; AD-21 repair has no command/event

**Issue.** AD-23 ("individually audited", 362), AD-21 ("audited operator repair", 307) and the prose (678) rely on auditing; nothing defines the record (who/what/tenant/target/correlation/outcome/time), the sink (an EventStore stream? a platform audit log?), immutability, or retention. `grep -rin audit src/` returns nothing; the only "audit trail" is structured logging (`WorksRecoveryLog`), which is mutable, sampled, and retention-free. AD-21 also makes the registry event-sourced but describes repair as an operator action outside the catalog — a repair that is not a registry command/event would be a direct state mutation, violating AD-01.

**Attack/failure scenario.** An operator "repairs" a divergent subtree by editing registry state; roll-up suppression lifts; there is no event in the registry stream, so a later rebuild (AD-16) reproduces the divergence or, worse, replays the pre-repair topology.

**Source.** architecture.md:303–309, 360–362, 380–381, 678.

**Corroborating evidence.** `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` (Information/Warning logs only); `docs/operations/subscriber-dead-letter-operator.md:22–25, 89` (replay is JWT-protected "Admin policy" but records no audit shape).

**Suggested disposition.** Discuss (owner Platform, R8). Bind: every operator action (rebuild/replay/quarantine disposition/registry repair) is (a) a command in the owning catalog producing an event that carries the AD-23 operator identity and delegation context, and (b) mirrored to a platform audit sink with a stated retention. Revisit before the first operator action is exposed from the Platform lane (Story 4.9 R4/R8).

### SEC-11 — [medium] — Supply chain and secrets posture is silent in the register; the Works root disables NuGet audit

**Issue.** The register covers version authority (AD-19) but says nothing about vulnerability auditing, CI gates, container images, or where broker credentials, Sentry issuer material, OIDC client secrets, and `DAPR_API_TOKEN` live. As built: the Works root `Directory.Build.props` sets `NuGetAudit=false`, overriding the Hexalith.Builds baseline (`NuGetAudit=true`, `NuGetAuditMode=all`); there is no `.github/workflows` directory in the repo; no Dockerfile/`ContainerBaseImage` (image posture is entirely Platform's, unstated); Redis has an empty password literal in components; the dev JWT signing key is a source literal (Development-only by contract); Sentry issuer material is kept outside the repo (good) but its rotation policy is only `workloadCertTTL: 24h`.

**Attack/failure scenario.** A vulnerable transitive package (transitive pinning is on, which is good) ships unnoticed because the audit is disabled at the root and no CI lane runs `dotnet list package --vulnerable`; a platform implementer moves the broker password into an environment variable instead of a Dapr secret store because no AD says otherwise.

**Source.** architecture.md:216–224 (AD-19), 226–270 (AD-20 — no secrets/supply-chain row), 1035–1038.

**Corroborating evidence.** `Directory.Build.props:15` (`<NuGetAudit>false</NuGetAudit>`) vs `references/Hexalith.Builds/Hexalith.Build.props:31–32`; `Directory.Packages.props:3–4`; `src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml:23–24`, `pubsub.yaml:30–31`; `src/Hexalith.Works.AppHost/Program.cs:254–256` (dev key literal), `:224–234` (per-run random password — good); `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:235–262` (certs forced outside the repo — good); `sentry.yaml:8`.

**Suggested disposition.** Autofix: remove the `NuGetAudit=false` override (or record the justification and a CI compensating gate) — this is a Works-repo change, not an architecture change. Defer the rest to AD-20 with a new matrix row R12 "Secrets and supply chain: Dapr secret-store component for broker/OIDC credentials, Sentry CA rotation, image provenance, vulnerability gate in `verify-works-host`" — revisit at Story 4.9 R10 acceptance.

## Low findings

### SEC-12 — [low] — Cross-tenant recovery registries and the shared actor store are unchanged (VAL-H09 / SEC-DI-06)

**Issue.** Still open and correctly listed (448). The mTLS commit did not touch it. New observation: the Works reminder actor persists `DateReminderRegistration{TenantId, WorkItemId, Instant}` in the single actor state store scoped to four app-ids, and the global pending-await registry drives a startup pass that scans every tenant sequentially with a bounded attempt budget — one tenant's unreadable stream marks the pass incomplete (partial results are still acted on, which is good), and a poisoned registry entry costs a gateway stream read per listed aggregate under the global-admin internal identity (SEC-01).

**Source.** architecture.md:174–182 (AD-15), 448, 756–762.

**Corroborating evidence.** `src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml:27–31`; `src/Hexalith.Works/Projections/WorksReadModelKeys.cs:48–78` (reserved-tenant guard is a real fix for the key collision); `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:45–135`; `src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:20–22, 60–80`.

**Suggested disposition.** Defer (already governed). Safe revisit condition stands: Story 4.9 design review, with the added requirement that the namespace table names per-app-id key prefixes and the store/ACL that enforces them.

### SEC-13 — [low] — "Production ingress is prohibited until this contract is live" is a policy statement, not a control

**Issue.** AD-23's Rule (366–368) and prose (678) prohibit production ingress until enforcement and negative tests are live, but nothing enforces it: no startup fail-closed check, no environment gate in Platform, no fitness test. EventStore already refuses symmetric signing keys outside Development, which is the pattern to reuse.

**Source.** architecture.md:366–368, 678.

**Corroborating evidence.** `src/Hexalith.Works.AppHost/Program.cs:246–269` (Development-only symmetric path); no equivalent gate for AD-23/AD-24 controls.

**Suggested disposition.** Autofix: bind the prohibition to a mechanism — the Platform production profile refuses to start unless the AD-23/AD-24 conformance lane result is recorded (or, minimally, unless `DaprInternal` callers carry delegation scopes and the app-channel token is configured).

## Positive controls retained

- AD-21/AD-22 as written are a sound authority model: single-writer registry actor, assertions-not-facts on `SpawnChild`, Reserved/Attached/Released lifecycle, `Attached`-only enumeration, freshness witness for ancestry resolution (architecture.md:272–341). The tenant-scoped registry makes cross-tenant edges impossible by construction (child ids are minted inside the same tenant's registry stream).
- Roll-up delivery identity checks fail closed on malformed shapes and are mutation-tested (`WorkItemRollUpTenantIsolation.cs:55–68`); reminder names are hashed, tenant-inclusive, and payload-free (`DateReminderName.cs`); read-model keys carry no PII; the reserved-tenant guard closes a real registry-overwrite hole on both ingress paths (`WorksDomainEventProcessor.cs:52–60`, `WorkItemProjectionDispatcher.cs:111–120`).
- The self-hosted Sentry composition fails closed on missing credentials and forces certificate material outside the repository (`DaprSelfHostedMtls.cs:184–262`); per-sidecar ACLs are now deny-by-default for `works` and `eventstore-operations`; pub/sub scopes and protected topics are explicit (`pubsub.yaml:32–39`).

## Gate note

Accept the register as the binding contract. Before Story 4.9 acceptance (and before the AD-21 registry and AD-25 expiry stories are drafted), disposition SEC-01, SEC-02, SEC-03; tighten the VAL-H12 revisit condition (SEC-04) now because every catalog addition makes it costlier. The autofix items (SEC-03, SEC-05, SEC-07, SEC-08, SEC-09, SEC-11 root override, SEC-13) are text-level and can land in the same register pass.
