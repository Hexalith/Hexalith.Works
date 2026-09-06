# Rubric Walker Review — Architecture Update 2026-09-06

> **Verdict: PASS with concerns.** All five 2026-09-05 critical findings (VAL-C01…C05) and VAL-H01 are now closed by binding AD entries with stable IDs and Binds/Prevents/Rule shape; the autofix items are substantively applied; the open-findings register is honest; the legacy-label map is complete. Three medium residuals (one stale version comment, one leftover universal-key claim, one unbound compensation path in AD-21) and three low residuals remain — none reopens a gate critical.

Target: [`architecture.md`](../../../architecture.md) (1149 lines, updated 2026-09-06).
Baseline gate: [`ARCHITECTURE-VALIDATION.md`](../../../architecture-validation-2026-09-05/ARCHITECTURE-VALIDATION.md) (FAIL, 5 critical).
Source not modified by this review.

---

## 1. Critical-finding closure (VAL-C01…C05, VAL-H01)

### VAL-C01 → AD-20 — CLOSED

`architecture.md:218–260`. The repository is named (`Hexalith.Platform`, :221–223), the accountable owner is named (Platform Maintainer (Hexalith), :224–225), package boundaries are allocated (:226–229), a single Aspire 13.5.x family is bound with the mixed 13.4.6/13.5.3 graph explicitly "retired, not grandfathered" (:230–233), and a conformance lane (`verify-works-host`, :234–237) plus a 10-row seam-by-seam migration matrix (:249–260) exist. The Rule is enforceable and preventive: "Story 4.9 may remove a Works hosting project **only after** the corresponding migration matrix row below is green" with a defined rollback (:241–243). This directly prevents the double-abandonment failure the gate described (:238–240). Body prose reconciled at :450, :554, :985–987, :1102, :1143–1145.

### VAL-C02 → AD-21 — CLOSED (with one medium gap, F3)

`architecture.md:262–286`. An authoritative owner exists: a tenant-scoped, event-sourced Work-Tree Registry aggregate on its own single-writer actor owning every `(tenant, childId) → parentId` edge and deriving ancestry/depth from its own state (:265–268). The gate's five asked bindings are each present: concurrency boundary (registry actor serialization, :278–279), authoritative facts (evaluated inside the registry's `Handle`, :272–274), freshness ("there is no staleness window", :273–274), conflict behavior (exactly one edge accepted, deterministic rejection, :278–279), repair semantics (registry stream authoritative; suppression until audited operator repair, :280–283). The `SpawnChild` permissive-defaults hole is closed by demoting caller facts to revalidated assertions (:274–277, restated :1059). See finding F3 for the unbound reserve-then-reject compensation path.

### VAL-C03 → AD-22 — CLOSED

`architecture.md:288–305`. The stated-seam contradiction is resolved: on child projection delivery, the projection layer resolves the ancestor chain from the AD-21 registry read model and writes into each ancestor's roll-up document via the AD-06 LWW slots. Ownership (generic fan-out lands in the EventStore SDK, R4, :299–300), inputs (:293), ordering (LWW by child envelope sequence, no atomic multi-doc write required, :294–295), failure (unresolvable ancestry ⇒ not acknowledged, redelivered — no partial ack, no silent skip, :295–296), and freshness (:296–298) are all bound. The Rule is falsifiable: "the ordinary delivery path must converge every ancestor without operator action; shared rebuild is for repair and migration, never routine roll-up. FR-11 stands as written" (:304–305). Body prose reconciled at :625, :665, :674, :947.

### VAL-C04 → AD-23 — CLOSED

`architecture.md:307–330`. Every gate ask is bound: authentication (OIDC at the `Hexalith.Platform` ingress; Works never authenticates, :310–312), claim-to-tenant derivation (verified claims via Hexalith.Tenants membership; enforcement owner and truth owner both named, :313–315), assertion-mismatch behavior (denied before dispatch/persistence/query/tenant-existence disclosure, :316–318), service delegation (workload identity + auditable tenant-delegation context, :319–321), and a minimum authorization floor (member / member+filter / audited platform-operator, :322–324). The Rule makes it enforceable: negative tests are Story 4.9 acceptance and "production ingress is prohibited until this contract is live" (:328–330). AD-14 is explicitly subordinated (:163–164). Body prose reconciled at :632.

### VAL-C05 → AD-24 — CLOSED

`architecture.md:332–350`. Full trusted-origin bind: mTLS with declared trust domain/namespace, deny-by-default access control and network policy (:335–337), broker TLS + ACLs (:338), exclusive originators for `work.events`, reminder callbacks, and replay/rebuild/quarantine (:339–341), and the key preventive rule that identity-field equality is "necessary but never sufficient — authenticated provenance is required before an envelope `SequenceNumber` is trusted" (:342–344), which addresses the forged-high-sequence poisoning scenario verbatim (:345–346). Negative migration tests are named concretely (direct route, spoofed app ID, wrong trust domain, unauthorized publish, forged sequence, :347–348) and the sandbox exemption is explicit, named, and recorded in the platform repository (:349–350). Body prose reconciled at :633.

### VAL-H01 → AD-25 — CLOSED

`architecture.md:352–371`. Trigger owner (reminder subsystem; registered/cancelled/rescheduled mechanically from lifecycle events, never by the kernel, :355–358), deterministic tenant-inclusive identity (:359–360), idempotent firing against the per-state transition table (:361–362), missed-firing recovery via the AD-11 indexed protocol under the R6 durability bindings (:363–364), and policy authority (platform-host typed options; kernel never reads configuration, :365–367). The Rule keeps `Handle` clock-free and declares "FR-10 stands unchanged — no correct-course pass is required" (:370–371). Body prose reconciled at :653, :655–656, :1063–1064.

**Section verdict: 6/6 closed.** Every entry has a stable ID, Binds/Prevents/Rule, and a rule falsifiable enough to prevent the specific divergence the gate described.

## 2. Autofix closure

- **VAL-H02 dependency graph — CLOSED.** AD-18 (`:196–206`) states `Server → Contracts`, `Projections → Contracts`, `Reactor → Contracts`, and "Projections does not reference Server"; the allowlist in `DependencyDirectionTests.cs` is declared machine truth. The legacy chain `Contracts ← Server ← Projections` survives only inside AD-18's own Prevents clause as the quoted defect (:201–203). Body prose is consistent everywhere it recurs (:720–727, :928–931).
- **VAL-H05 version authority — CLOSED with residue (F1).** AD-19 (`:208–216`) makes `global.json` + central packages the only authority, splits Dapr runtime from Dapr .NET SDK, and forbids "current" version claims in prose. The June table is relabeled "historical snapshot … not implementation pins" with every row marked superseded (:572–588). Residue: the directory-tree comment at :849 still asserts `SDK 10.0.301` in a section presented as the current target structure — see F1.
- **VAL-M02 pub/sub ordering — CLOSED.** The absolute "not ordered" phrasing is gone (grep: zero hits). AD-09 (:111–119) and body prose (:640) now say ordering is "component- and configuration-specific and never a portable contract."
- **VAL-M03 turn-lock vs ETag — CLOSED with residue (F4).** AD-08 (:97–109) makes the actor turn lock the normal loser mechanism and the ETag save the fallback, with tests required for both paths; reconciled at :480, :602, :639, :790–795, :829–831. Residue: three late-document summaries still use ETag-primary shorthand — see F4.

## 3. Internal consistency

Grep sweep results (`designated platform`, `10.0.301`, `1.18.4`, `3.2.2`, `rc.3`, `13.4.6`, `not ordered`, `ETag-backed`, `Contracts ← Server`):

| Hit | Assessment |
|---|---|
| :202 `Contracts ← Server ← Projections` | Quoted inside AD-18 Prevents — correct usage |
| :222 "designated platform/host repository" | Immediately follows the name in AD-20 — fine |
| :232 `13.4.6` | Describes the retired scaffold being upgraded — fine |
| :580–585 `10.0.301`, `1.18.4`, `3.2.2`, `rc.3` | Inside the explicitly historical, per-row-superseded table — fine per AD-19 |
| :849 `SDK 10.0.301` | **Stale — F1** (current-target tree comment) |
| :667, :725, :976, :1072 "designated platform host/repository" (unnamed) | Now referential to AD-20 but still nameless — F5 |
| :266 "ETag-backed save" (AD-21) | Describes the persistence mechanism under a single-writer actor — acceptable |
| :480, :792 "ETag-backed … fallback" | Correct per AD-08 |
| :999, :1026, :1073 "ETag-backed optimistic concurrency" | **ETag-primary shorthand — F4** |
| `not ordered` | Zero hits — clean |

Additional sweep: :938 restates the retired universal key-derivation claim — **F2**. No leftover "mutually compatible/current" claims; no live 10.0.300/1.18.5/rc.5 assertions. The precedence clause ("where prose and this register disagree, the register wins", :34–35, restated :1133–1134) contains all residuals but does not excuse them.

## 4. Deferred-item safety

The open-findings register (`:391–407`) is honest: it lists exactly the gate residue this update does not close — VAL-H03 (product fork, routed through `bmad-correct-course`, revisit before Epic 4 closure), VAL-H06→R7, VAL-H07→R6, VAL-H08→R4/AD-16, VAL-H09 (universal-claim replacement during R3/R6/R7), VAL-H10 (EventStore submission seam, before Theme 3 or R3), VAL-H11 (before the Story 1.5 catalog change), VAL-H12 (before production data), VAL-M01 (lane repair, next test-infrastructure story), VAL-M04 (wording corrected, tests owed). Each row has a status and a concrete revisit condition, and the preamble correctly distinguishes 4.9 entry from 4.9 acceptance (:393–394). Cross-references from the retained ADs back into the register (AD-02→H10 :55, AD-03→H03 :62–64, AD-11→R6 :138–141, AD-15→H09 :172–174, AD-16→H08 :183–186) keep the deferrals visible at the point of use. Weakness: the preamble promises "an owner-of-record" per item, but the table carries no owner column — matrix-bound rows inherit owners via AD-20, while H09/H10/H11/H12/M01 have a place but no named person/role (F6).

## 5. Legacy-label map

`architecture.md:373–389`. All 21 legacy labels are mapped: A1–A5 → AD-02/03/04/05/06; B1–B3 → AD-08/09/07; C1–C4 → AD-10/11/12/13; D1/D2 → AD-14/15 (disambiguated with parentheticals); E1/E2 → AD-16/17; D-1…D-4 → AD-10/08/12/09. The colliding `D-n` vs `Dn` families the gate flagged (VAL-H04) are explicitly called out and disambiguated (:375–377). Complete and unambiguous — PASS.

## 6. AD-21/AD-22 vs retained invariants

- **AD-10 mechanical reactor:** the attach decision is made in the registry's pure `Handle`; the reactor only translates the edge-reserved event into `SpawnChild` (:269–271). No conditional a pure `Handle` could not have produced — consistent.
- **Projection purity:** the relationship-lookup + fan-out capability lands in the EventStore SDK (R4); Works keeps only the pure contribution/merge strategy (:254, :299–300). Consistent with :726–727 and :885–890.
- **B3/AD-07 authority split:** AD-22 writes roll-up documents only; rolled-Remaining remains an eventually consistent projection of distinct type, and nothing in AD-21/AD-22 lets a projection flip status (:94–95, :626). Consistent.
- **Per-hop tenant equality:** the registry is tenant-scoped and binds tenant closure inside its `Handle` (:265, :272–274); AD-15 retains the per-hop assertion (:169–171) and :674 couples AD-22's fan-out to the AD-21 read model. Consistent. (v1 has no reparent command, so stale-ancestry-after-move is out of scope.)

No contradiction found.

---

## Findings

### F1 — MEDIUM — Stale SDK version asserted in the current-target directory tree
`architecture.md:849` — `├── global.json  # SDK 10.0.301, rollForward latestPatch, MTP runner`. The Project Structure section is presented as the current target (not a historical snapshot), so this comment violates AD-19's own rule ("this document never states a version as 'current'", :215–216) and is precisely the VAL-H05 defect class. Fix: drop the number ("SDK pinned per AD-19").

### F2 — MEDIUM — Retired universal key-derivation claim survives in Data boundaries
`architecture.md:938` — "event streams + state keys + projection keys all under `{tenant}:work:{id}`" restates the universal derivation the same document explicitly retires (:711–714: "The **universal-derivation claim is retired**… generation-based projection keys, hashed reminder identities…") and that the open register keeps open as VAL-H09 (:402). The register-wins clause contains it, but a builder reading only §Architectural Boundaries would re-adopt the retired rule. Fix: reword to canonical logical identity + pointer to VAL-H09.

### F3 — MEDIUM — AD-21 reserve-then-reject compensation path unbound
`architecture.md:269–271, 280–283`. The protocol is reserve edge in the registry, then reactor drives `SpawnChild` on the parent — but the behavior when the parent subsequently domain-rejects `SpawnChild` (e.g., terminal parent) is not bound: no edge-release/compensating event is specified, only the generic "registry authoritative + suppress until audited operator repair" branch. As written, every ordinary parent-side rejection becomes an operator-repair event. A one-line rule (mechanical release of a reserved edge on parent rejection, or parent-state revalidated in the registry `Handle`) closes it. Does not reopen VAL-C02 — single-parent/acyclicity/serialization are sound — but the partial-failure path will diverge across implementers.

### F4 — LOW — ETag-primary shorthand survives in three late-document summaries
`architecture.md:999` ("ETag-backed optimistic concurrency"), `:1026` ("concurrency (ETag-backed atomic save, single-claim-wins)"), `:1073` ("resolved: single-aggregate under ETag-backed optimistic concurrency"). Each omits the turn-lock-primary framing AD-08 binds (:100–105). The register wins, but these are the drift seeds VAL-M03 targeted.

### F5 — LOW — Four unnamed "designated platform host" references remain
`architecture.md:667, 725, 976, 1072`. Post-AD-20 these resolve unambiguously, but the gate's C01 complaint was exactly this phrasing; substituting "`Hexalith.Platform` (AD-20)" removes the last ambiguity for a reader landing mid-document.

### F6 — LOW — Open-findings register promises owners it does not name
`architecture.md:393` claims "each has an owner-of-record", but the table (:396–407) has no owner column; H09/H10/H11/H12/M01 name work and revisit conditions but no accountable role, unlike the matrix-bound rows that inherit the AD-20 owner.

---

## Good-spine checklist

| Dimension | 2026-09-05 | Now | Assessment |
|---|---|---|---|
| Real divergence points fixed | Fail | **Pass** | AD-20…AD-25 close C01–C05 + H01 with named owners, protocols, and falsifiable rules; residuals are wording, not divergence |
| Rules enforceable and preventive | Fail | **Pass** | Stable AD-01…AD-25 with Binds/Prevents/Rule; register-wins precedence; proof-before-removal and acceptance-gated rules |
| Deferred items safe | Concern | **Pass (minor)** | Honest register with revisit conditions; per-row owners implicit only (F6) |
| Named technology current | Fail | **Pass (minor)** | AD-19 authority + historical table; one stale tree comment (F1) |
| Brownfield ratification | Fail | **Pass** | AD-18 matches machine truth; AD-08/09 match live behavior; universal-key claim retired (one leftover, F2) |
| Spec capability coverage | Concern | **Pass** | FR-10 delivered by AD-25; FR-11 by AD-22 ("FR-11 stands as written"); FR-20 honestly held open as a product fork (H03) |
| Inherited constraints honored | Fail | **Pass** | Domain/platform boundary now executable: named repo, owner, allocation matrix, rollback |
| Structural dimensions complete | Concern | **Concern** | Deployment ownership, security trust, and expiry now bound; privacy lifecycle (H12), idempotency (H10), schema matrix (H11) remain deferred with conditions |

**Score: 7 of 8 pass, 1 concern (was 0 of 8).** Verdict: **PASS with concerns** — no critical or high findings; 3 medium (F1–F3), 3 low (F4–F6). Recommended before the next full gate re-run: apply F1/F2 as one-line edits and bind the F3 compensation rule inside AD-21.
