# Five deferred producer/recovery qualification checks — 2026-10-28

This packet supports five historical ledger closures: corrected public projection/runtime producers
(including the reminder-publication alias), the create-only receipt-conflict correction, final-decoder
source Debug restart/callback, owner-selected public 3.126.0, and the twelve-source synthetic profile.
The adjacent spec records the implementation and fresh deterministic validation.

Run these commands from the repository root. The verifier also accepts its absolute path from any directory:

```bash
python3 _bmad-output/implementation-artifacts/evidence/five-deferred-producer-recovery-2026-10-28/verify-history.py
python3 -O _bmad-output/implementation-artifacts/evidence/five-deferred-producer-recovery-2026-10-28/verify-history.py
python3 _bmad-output/implementation-artifacts/evidence/five-deferred-producer-recovery-2026-10-28/verify-history-mutations.py
python3 _bmad-output/implementation-artifacts/evidence/five-deferred-producer-recovery-2026-10-28/verify-fresh.py
python3 _bmad-output/implementation-artifacts/evidence/five-deferred-producer-recovery-2026-10-28/verify-fresh-mutations.py
```

`historical-inputs.json` binds 87 selected retained inputs by SHA-256 without duplicating the old packets.
The verifier parses both canonical results and ledgers, checks the exact nine-package inventories and
named producers, all eleven rows and 44 required proof kinds per run, record hashes from persisted
observations, raw xUnit facts, command exit codes/logs, source Debug restart/callback and the owner decision.
Historical producers are 3.125.1 (`55432c50d4b6f1666b4bd6d4a60bfc263b2d0e0b`) and 3.126.0
(`fcf78d6cafb6c0c9f52dc350a9a09aff54ded288`). Expected Agents-full unavailability remains separate
from the qualifying Works lanes. Every selected xUnit assembly has zero errors, failures, skips and unexecuted tests.

`corrected-producer-ancestry.json` retains twelve Git commit objects obtained read-only from the existing
SDK object store. The verifier recomputes each Git object identity and checks parent continuity from
the retained official 3.125.1 producer to correction `216b790628574d39802adcc8f4fea0d4034ef9f0`.
It needs no initialized SDK checkout, local NuGet cache or external service during replay.

Two historical packaging details remain explicit. The 3.125.1 persisted stream contains two initial
projection-convergence observations; only that named duplication is admitted, and every referenced
record must match its exact recorded hash. The two 3.125.1 source Debug live logs differ from their
original-run hashes in `results.json`; their retained bytes match the original packet manifest.
The verifier binds those packaged forms separately and does not claim original-run log identity.
Both raw live XML files match their original-run hashes and contain the named passing facts.
Both canonical projection records explicitly require concurrent catch-up to source head 4/global position 14,
generation 1→2, the expected changed digest and continued readability of the old generation. The 3.126.0
record additionally requires the actual caught-up checkpoint. Ten source Debug restart phases and the
separate callback phase must exist; the final retained twelve-source lane must match the canonical fixture
hash and complete with twelve retained candidates, zero unresolved work and no capacity excess.

The mutation controls copy only selected inputs to a disposable directory. Valid relocated packets pass
normally and under `python -O`. Altered bytes, a wrong producer, a failed proof, a missing row and a broken
ancestry chain each refuse in both modes. Semantic controls rebind the mutated input's manifest hash so
that they exercise the admission rules as well as the separate byte-integrity control.
The expanded suite refuses eight further semantic mutations in both modes: empty consumer pins,
a bare boolean replacing a newer structured proof, changed restored fleet bytes, missing actual starts,
an invented canonical command, a mismatched row producer, a mismatched source Debug inventory and
package-mode source Debug environment. Both valid relocated controls and all 26 refusal cases pass.

The verifier requires the exact canonical command and consumer-pin inventories, nine Platform starts
plus one identified retained-host start, matching row/public producer identities, and equality of the
recorded snapshot/restored runtime and fleet inventories. The 3.126.0 restored fleet intentionally lacks
the snapshot's raw Redis full-value digest; all sixteen restored fields, including logical Redis contents,
key count, configuration and Scheduler inventory, must equal the snapshot. All 1,070 runtime entries
must compare exactly. These checks compare recorded inventories; they do not reacquire the original fleet.
The source Debug SDK inventory must recompute from canonical source/build inputs under the historical
Debug inventory policy (`src/**/*.cs`, `src/**/*.csproj` and three root build files). This equals the older
complete runtime inventory; the newer canonical inventory additionally binds other runtime inputs.
Recorded source-mode environment values, explicit Debug build arguments and Debug executable paths
are checked alongside the executing XML.

`fresh-verification.json`, five compressed logs and `focused.xml.gz` retain the fresh source Debug checks:
the final build has zero warnings/errors and all **85/85** focused cases pass, including seven profile controls.
`verify-fresh.py` checks source SHA-256 bindings, exact command exits and log hashes, raw XML totals and
the exact seven profile cases and distinct theory argument sets. It needs the recorded source dependencies initialized;
local DLL hashes are provenance only and are not required for replay. The initial build's CA2007 failure
was corrected before the passing build. Formatter verification of the existing smoke file exits 2 with
30 diagnostics on 14 baseline-unchanged lines; formatter verification of the new profile file exits 0.
These outcomes remain separate from the passing build and tests. Final source bindings are sealed; the adjacent spec records all ten review dispositions and their successful independent recheck.

This is offline integrity verification of retained historical records. It does not execute fresh Dapr
workloads, authenticate current external feeds, qualify later SDK source/pins, or open production admission.

Duplicate-argument controls retain the passing totals and rebind the XML hash, then duplicate each of the three theory argument pairs. All six controls refuse across normal and optimized Python; both valid relocated packets pass. The exact 55.1-second five-effect quantum is accepted by the seventh profile case.
