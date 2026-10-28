# Independent review and dispositions

Blind Hunter reviewed the current tracked diff and untracked specification/evidence independently.
The review computed 137,988 changed bytes (134.754 KiB), with finding floor
`min(floor(sqrt(134.754) + 1), 10) = 10`. All ten findings receive in-scope corrections.

| Finding | Verdict and disposition |
| --- | --- |
| The discovery-route refusal does not exercise topic-metadata classification. | Medium, patch: retain discovery and add an unprotected `/work/events` endpoint with real `WithTopic` metadata; require SDK startup refusal. |
| Actor callback classification has no negative control. | Medium, patch: add an unprotected `/actors/...` callback and require startup refusal. |
| Caller counts and domains admit a duplicated/replaced caller identity. | Low, patch: assert the exact caller sequence without relying on set equality or deduplication. Both committed and fixture-copy duplicate-caller mutations must fail. |
| Configured copy directories appear only in negative evidence. | Medium, patch: retain a four-case passing lane selecting a valid configured copy directory. |
| A fixed six-type starter inventory can omit a new live class. | Medium, patch: discover explicit facts across the entire Integration assembly. Discover harness callers from every Integration source file; bind the complete source inventory and refuse direct testing-builder factories outside the shared harness. This source audit does not claim arbitrary C# control-flow analysis or a new live run. |
| Commands without XML bypass exit validation. | Medium, patch: validate every recorded command exit against its expected outcome before processing optional XML. Initial test-development and analyzer failures remain explicitly nonqualifying. |
| Required lanes and exact advertised totals are not enforced. | Medium, patch: require final builds/format and every qualifying lane; enforce 121/6/4/39/1 passing totals, 403/404 broad results and eight 3-pass/1-fail mutation outcomes. |
| Any single mutation failure can qualify, including malformed input. | Medium, patch: require the exact method, Works filename, Shouldly assertion exception, mutated field/scope and expected value diagnostics, with exactly three other configurations passing. |
| Source hashes omit exercised tests/spec/inventory implementation; assembly/dependency fields are not enforced. | Medium, patch: bind the complete Integration source inventory, specification and SDK endpoint-inventory implementation. Verify executed assembly hashes and both dependency gitlinks/checkouts. |
| The accepted broad failure does not establish unchanged baseline inputs. | Medium, patch: compare the failing test and both marker documents byte-for-byte against the canonical baseline; require the exact failing method and both marker-mismatch diagnostics with the unchanged gitlink. |

No review finding is deferred. The failed initial raw-subscription control and the intermediate
CA1062 analyzer failure remain retained as nonqualifying history. The final build keeps all analyzers
and startup guards enabled.

Independent correction recheck: all ten original findings are resolved. The reviewer checked the updated tests, retained XML and provenance checks, and successfully ran verify.py. No original finding remains unresolved.
