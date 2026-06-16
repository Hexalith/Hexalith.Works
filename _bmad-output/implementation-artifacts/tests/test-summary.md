# Test Automation Summary — Story 1.4 (Expose Boundary Ports and Decision Record)

## Generated Tests

### API / Contract Tests
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs` — pre-existing Story 1.4 reference-only contract flow: `WorkItemCreated` carries the expectation **reference** (`"value":"expectation-ref-001"`) and never an interpreted `Expectation` (`interpretedValue` absent); a legacy `Obligation` payload with no reference field deserializes as reference-only (additive, backward-compatible).

### E2E Tests
- [x] Browser/UI E2E is **not applicable** for Story 1.4: the story has no UI, MCP, public route, or command-pipeline host surface. The executable end-to-end path for this slice is **port contract → no-LLM resolver → `Expectation`** and **`Obligation` reference → `WorkItemCreated` event → JSON transport shape → replayed state**. These are exercised by the contract-flow and resolver tests below.

### Domain / Port Flow Tests (gaps auto-applied this run)
- [x] `tests/Hexalith.Works.UnitTests/ExpectationResolverTests.cs` — **+5 tests** filling discovered coverage gaps:
  - `LiteralResolver_is_deterministic_resolving_the_same_reference_twice_yields_equal_values` — proves the no-LLM resolver has no clock/RNG (NFR-11): two resolves of the same reference yield equal interpreted values, equal to the reference value verbatim.
  - `LiteralResolver_throws_for_a_null_reference` — critical error case: `ArgumentNullException.ThrowIfNull(reference)` surfaces on invocation.
  - `ExpectationReference_rejects_a_null_or_blank_value` (Theory: `null`, `""`, `"   "`) — strengthens the ctor guard beyond the original whitespace-only case.
  - `Expectation_rejects_a_null_or_blank_interpreted_value` (Theory: `null`, `""`, `"   "`) — previously-untested guard branch on the interpreted-result type.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BoundaryPortTests.cs` — **+1 fitness test**:
  - `NoContractsType_exposesAnInterpretedExpectation` — surface-wide NFR-11/FR-2 guard: reflects over the **entire** Contracts assembly (events, commands, state, value objects) and asserts no type exposes a property typed `Expectation`; only the stable `ExpectationReference` may be persisted. Includes a vacuous-pass guard (asserts types were discovered before asserting absence).

## Coverage

Mapped against the implemented Story 1.4 surface (`Ports/`, `Resolvers/`, `Obligation.Reference`):

- **AC #1 — `IExpectationResolver` + no-LLM impl, work item valid with no resolved Expectation:**
  - Happy path: resolver echoes reference verbatim; resolver never throws for a valid reference. (pre-existing)
  - **Added:** determinism (no clock/RNG), null-reference error case.
  - Work item valid with no reference, and replays with reference-only (no materialized `Expectation`). (pre-existing)
- **AC #2 — `IExecutorRouter` abstraction only:** declared in `Contracts.Ports`; fail-closed reflection test asserts zero kernel implementers (vacuous-pass guarded). (pre-existing)
- **AC #3 — kernel dependency boundary:** `DependencyDirectionTests` + `ScaffoldGovernanceTests` (banned-substring, kernel purity, infra-free csproj) green with the new `Ports/`/`Resolvers/` folders. (pre-existing)
- **AC #4 / #5 — decision record + deferred seams:** `BoundaryDecisionRecordTests` asserts the six modules and four deferred-seam markers exist in `docs/boundary-decision-record.md`. (pre-existing)
- **NFR-11 / FR-2 — interpreted value never persisted:**
  - Event-level: `interpretedValue` never in serialized `WorkItemCreated`; `WorkItemState` exposes no `Expectation` property. (pre-existing)
  - **Added:** surface-wide guard across the whole Contracts assembly + ctor guards on both `Expectation` and `ExpectationReference`.
- Seam-type value-object validation: `ExpectationReference` trims and rejects null/blank; `Expectation` rejects null/blank. **(rejection paths added this run.)**

## Validation

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -v minimal -p:NuGetAudit=false` — **0 warnings, 0 errors** (warnings-as-errors).
- Generated xUnit v3 executables run directly (Microsoft.Testing.Platform named-pipe permissions may block `dotnet test` in this sandbox, per Story 1.2/1.3 pattern):
  - **UnitTests: 60/60** (was 52/52 → +8: 5 new test methods, 3 from Theory expansion)
  - **IntegrationTests: 13/13** (unchanged)
  - **ArchitectureTests: 25/25** (was 24/24 → +1 fitness test)
  - **PropertyTests: 1/1** (unchanged)
  - Total: **99/99**, 0 failures.

## Checklist

- [x] API/contract tests generated.
- [x] E2E/UI tests marked not applicable (Story 1.4 has no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly).
- [x] Tests cover happy path.
- [x] Tests cover critical error cases (null reference, null/blank seam-type values).
- [x] All generated tests run successfully.
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps.
- [x] Tests are independent (no order dependency); new reflection test is vacuous-pass guarded.
- [x] Test summary created with coverage metrics.

## Notes

- This run is **QA gap-filling only** — no production code was changed. Gaps were identified by mapping the five new source types (`Expectation`, `ExpectationReference`, `IExpectationResolver`, `IExecutorRouter`, `LiteralExpectationResolver`) and `Obligation.Reference` against the dev-authored tests; the untested branches were the resolver's null-arg guard, resolver determinism, both seam-type ctor guards, and a surface-wide (vs. single-type) NFR-11 "no persisted Expectation" invariant.
- Both fitness tests added/kept the **vacuous-pass guard** pattern (assert types were discovered before asserting an empty set) to pre-empt adversarial review.
- The new resolver/port narrative avoids the eight banned deferred-domain substrings; `ScaffoldGovernanceTests` banned-substring guard stays green.
