# Works architecture validation

21 November 2026 · Hexalith.Works `07020c84` · Standalone BMad review

**Verdict: changes required.** All 29 decision IDs are stable, all 26 functional requirements appear in the capability map, and mechanical lint returned zero findings. The three independent reviews produced nine distinct findings: five high, three medium and one low. The high findings affect replay, cross-aggregate ordering, projection reads and which runtime a parity result would qualify. [Reviewed spine](../architecture.md) · [Lint result](lint-spine.json)

## High-priority findings

### H1 · Recognize historical progress completion on an existing overrun

**AD-05 and AD-07.** An item with estimate 4 and Done 7 already has zero Remaining. An accepted positive report preserves Done 7 and emits `ProgressReported` followed by `WorkItemCompleted` in the same act. A reader looking for a transition *from positive Remaining to zero* can misclassify that completion as explicit. The classification determines whether a later correction may reopen the item.

**Resolution.** Specify historical inference using the accepted report and completion event pair that leaves Remaining zero. Exercise that overrun history alongside an explicit completion at zero. After correcting Done below the estimate, only the progress-completed item should reopen. [AD-07, lines 177–187](../architecture.md#L177) · [FR-8](../prds/prd-works-2026-06-14/prd.md#L218) · [Aggregate, line 321](../../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs#L321)

### H2 · Decide when the registry accepts a reservation relative to parent termination

**AD-21, target design.** The parent can supply a valid admission witness, then terminate before the registry commits `EdgeReserved`. The PRD requires refusal for reservations accepted after termination and cascade coverage for those accepted before it. Checking the witness immediately before dispatch leaves the ordering of the two durable acts unresolved.

**Decision needed from Works and EventStore.** Name the durable event that constitutes acceptance and define how both owners enforce that order across retries and restarts. In a conformance case, pause a reservation after witness validation, terminate the parent, then resume delivery. Both owners must reach the same decision and agree on any cascade obligation. [AD-21, lines 440–448](../architecture.md#L440) · [FR-10, concurrent attachment](../prds/prd-works-2026-06-14/prd.md#L248)

### H3 · Bind delayed child Resume to one occurrence of the parent’s wait

**AD-13, AD-21 and AD-26, target integration.** Suppose a date trigger resolves a suspension while a child Resume is in flight. The parent later suspends again on the same child. A unique transport receipt distinguishes duplicate submissions, but a first delivery from the earlier wait can still match the later wait’s condition. The current command carries tenant, item and condition. Acknowledging the correct wait requires knowing which suspension installed it.

**Decision needed from Works and EventStore.** Define where the internal effect carries the intended wait occurrence and how the parent compares it with current state inside its serialized turn. Deliver the old effect for the first time after resuspension, then replay it after restart. The second wait must stay intact. [AD-21, lines 450–455](../architecture.md#L450) · [Resume contract](../../../src/Hexalith.Works.Contracts/Commands/ResumeWorkItem.cs#L11) · [Target handler](../../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs#L235)

### H4 · Identify the catalog that the Works parity run actually consumes

**AD-19 and Stack.** The Works bootstrap links its root Builds checkout into the Platform snapshot and exports it as `HexalithBuildsRoot`. The Platform verifier reads the Works catalog there. It separately materializes a published Builds tuple for its Agents check. The distinction matters on this checkout: the refreshed Works Builds catalog selects Dapr runtime **1.19.2**, while recorded Builds 6.4.4 selects **1.19.1**. Both catalogs select EventStore 5.4.0.

**Resolution.** Name each catalog consumer in AD-19 and record each selected revision or digest in its proof. Check them independently with a deliberately different pair of catalog versions. The bootstrap refusal below prevented a runtime launch in this validation. [Stack, line 677](../architecture.md#L677) · [Works bootstrap](../../../scripts/verify-platform-parity-ci.py#L148) · [Works SDK selection](../../../references/Hexalith.Platform/eng/verify_works_host.py#L1338) · [Agents catalog selection](../../../references/Hexalith.Platform/eng/verify_works_host.py#L1413)

### H5 · Label the proposed projection keys and document the current lookup

**AD-06, AD-15 and AD-16.** AD-06 labels the encoded tenant prefix as a registry migration target. AD-15 presents it inside a decision marked both current and target. Today the query uses a tenant-scoped SDK generation and the logical key `works-runtime-v3:rollup:<item>`. The SDK derives the physical key. A new reader following the literal proposed prefix can miss a roll-up that the current writer has persisted.

**Resolution.** Specify the current lookup through `WorksReadModelKeys` and the SDK coordinator. Label the proposed format as a migration target. Verify that a newly deployed reader opens an existing writer’s selected generation before changing the format. [AD-15, lines 279–291](../architecture.md#L279) · [Works key definitions](../../../src/Hexalith.Works/Projections/WorksReadModelKeys.cs#L16) · [Current query](../../../src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs#L105) · [SDK scope](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/SharedProjectionScope.cs#L31)

## Other findings

| Severity | Decision | Evidence and proposed disposition |
| --- | --- | --- |
| Medium | AD-04 · Child Unit | FR-12 binds the explicit or inherited Unit **at spawn**, even if the child has no estimate. Capture that Unit in the spawn record. If neither parent nor child has a Unit then, let the first estimate supply it. A parent acquiring a Unit later must have no effect on the child’s earlier spawn. [FR-12 truth table](../prds/prd-works-2026-06-14/prd.md#L279) |
| Medium | AD-14 · Executor cardinality | “An item has one ExecutorBinding” would exclude valid unbound creation. State “at most one” and specify which lifecycle states require the binding. Include replay of `WorkItemCreated` with the optional binding absent. [Create contract](../../../src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs#L26) · [FR-1](../prds/prd-works-2026-06-14/prd.md#L130) |
| Medium | AD-03 · Undated items | A compiled probe of the repository comparator sorted equal-priority `a` (undated) before `b` (due 9999-12-31). Null and the latest valid date share a sort key. FR-20 requires every present date to precede an absent one. Label the implementation gap, compare date presence explicitly and check both ID orders. [Comparator](../../../src/Hexalith.Works.Projections/Strategies/WhatsNextOrdering.cs#L42) · [FR-20](../prds/prd-works-2026-06-14/prd.md#L397) |
| Low | AD-02 · Existing IDs | The conventions table promises readability for any existing non-whitespace ID. The SDK also limits the length to 256 and restricts the character set. Use AD-02’s `AggregateIdentity` qualifier in the table. [Conventions](../architecture.md#L650) · [SDK validator](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Identity/AggregateIdentity.cs#L121) |

## Release evidence

Two independent checks stopped before qualification. The source-identity mismatch was already recorded in the spine. Resolving it alone would leave the catalog-reading failure in the parity bootstrap.

| Check | Result on the reviewed workspace |
| --- | --- |
| Debug source identity | `python3 scripts/verify-source-debug.py --check-only` exited 1 on EventStore source revision drift. Works records `8bfc4ca13`, the reviewed manifest names `607072c68`, and the checkout is `7435254cc`. PolymorphicSerializations also differs between the Works gitlink and manifest. [Manifest](../../../tools/source-debug-dependencies.json) |
| Platform parity entry point | `bash scripts/verify-platform-parity-ci.sh --artifacts-path <fresh-directory>` exited 1 with `Parity requires a stable public EventStore version`. The bootstrap searches the literal Works package wrapper. MSBuild imports the property from the Builds catalog. Align the bootstrap and downstream literal-pin checks with the evaluated catalog and retain producer verification. [Bootstrap check](../../../scripts/verify-platform-parity-ci.py#L103) · [Package wrapper](../../../Directory.Packages.props) · [Downstream pin check](../../../references/Hexalith.Platform/eng/verify_works_host.py#L190) |
| Historical qualification | The October cutover packet binds all 11 rows, 44 proof kinds and fleet restore to its recorded EventStore 3.126.0 inputs. For Works `07020c84`, the previously recorded CI result shows failure at Stage 3 unit tests and a skipped parity job. Full application suites and live fleet qualification were outside this document review. [Cutover record](../../../docs/platform-hosting-cutover.md) · [Recorded CI run](https://github.com/Hexalith/Hexalith.Works/actions/runs/44874235428) |

## Review record

The reviewed input was the working-tree spine with SHA-256 `e53447b83e031c5ef39e9f1e443cdf311a7db164d31f43a36bb0b3693a7609ab`. Platform ownership of hosting, SDK ownership of generation selection, and the distinction between implemented queue access and target active-item detail were supported by the inspected sources. Two lenses independently identified the projection-key issue, and the report counts it once. The recommended next architecture update is to settle H2 and H3 with the two owners, then reconcile the other decisions against the cited evidence.

[Rubric review](reviews/review-rubric.md) · [Technology and implementation review](reviews/review-technology-reality.md) · [Adversarial review](reviews/review-adversarial.md)
