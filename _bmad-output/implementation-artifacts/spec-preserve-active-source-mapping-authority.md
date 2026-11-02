---
title: 'Preserve active source mapping authority over project edges'
type: 'bugfix'
created: '2026-11-02'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Works' inherited Directory.Build.targets forwards its SDK/configuration onto every external project edge, including nested McpCli analyzer edges during an active source mapping. The mapper correctly rejects these AdditionalProperties with HXW006. Let an explicit source mapping retain sole authority by stopping ordinary edge forwarding when source mode or either existing mapping hash is present. Preserve byte-equivalent ordinary Debug/Release forwarding, analyzer target/framework/private metadata and HXW006 refusal of explicit conflicting overrides. No mapper waiver or dependency source edit.

</frozen-after-approval>

## Implementation Notes

Read-only investigation reproduced the exact root-cause metadata and real mapped Debug build. Draft `/tmp/mcpcli-source-mapping-investigation-20261102/mapped-forwarding-boundary.patch` changes only the evaluation ItemGroup and transitive forwarding target conditions. Root owns the change; no McpCli changes. Evidence:16reconciliation controls,12evaluation cases, actual source Abstractions+netstandard2.0 analyzer build0warnings/errors. Existing script regression suite must preserve all8Debug/Release/parallel-diamond lanes and negative control.

## Verification

Run `python3 -m unittest discover -s scripts/tests -p test_transitive_project_reference_globals.py -v` with exact owner targets applied. Retain independent actual mapped owner build and explicit override refusals; no fake CI or nested init. Validate root whitespace and commitlint. One-shot blind review required; human checkpoint waived by user, automated checks retained.

## Review Triage Log

- Blind1 medium/patch: existing maintained tests scrub mapping markers, so deletion of either guard was not protected. Added six actual MSBuild fixture variants across Debug/Release and each activation signal; preserve analyzer ReferenceOutputAssembly=false and PrivateAssets=all.
- Blind2 medium/patch: transitive mapped expansion and reserved-override refusal lacked maintained coverage. The same new fixtures execute real SDK IncludeTransitiveProjectReferences, then the exact signed published source-mapping task; a seventh variant retains explicit conflicting AdditionalProperties and requires fatal HXW006. Tool version comes from the root-declared Platform pin, canonical NuGet archive signature is verified before loading its task closure. No mapper or metadata guard is altered.

The two new methods pass all7graph variants. Original normal-forwarding and parallel-node controls remain unchanged and are rerunning with the applied fix. Read-only investigation already proved the actual mapped McpCli Abstractions/netstandard2.0 analyzer build0warnings/errors and16published mapper controls.

Final applied verification: all5maintained test methods pass57.409s, covering original9graph variants plus7new mapped/override variants; no skips/failures/errors. Actual mapped owner build and mapper refusals remain independent evidence.
