# Story 4.9 acceptance-tool review corrections

These offline successors correct the five supplemental-tool findings. The sealed
[parent acceptance tools](../ci-build-parent-acceptance-2026-10-26/README.md),
[hosted qualification packet](../ci-build-hosted-qualification-2026-10-26/README.md),
and their native source bytes remain unchanged. These corrections do not modify
canonical source, published code, workflow, assertions, runtime inputs or prior
qualification results. Story acceptance and remaining reviews belong to the parent.

- [audit-hosted.py](audit-hosted.py) writes a new report and matrix. Missing,
  nonpassing and duplicate hosted methods receive `Missing`, their actual result,
  or `Ambiguous`, respectively. Matrix-wide success requires all audit checks to
  pass. Early refusal records coverage as `NotEvaluated`.
- The audit verifies the actual evaluator against its recorded canonical source
  hash and every recorded verifier/helper against its helper hash before import.
  It imports private copies of those verified bytes, replaces cached helper
  modules for that scope, and restores the prior import state afterwards.
- [acceptance_support.py](acceptance_support.py) verifies each original ZIP hash
  and directly compares every retained extracted file with its ZIP member,
  refusing missing, extra or changed extraction bytes. Evaluation uses the same
  verified in-memory ZIP bytes or private copies rather than rereading extraction
  paths. The summary also binds the build log to its original verified run ZIP.
- [download-artifact.py](download-artifact.py) records missing/null API digest
  metadata as `unavailable`, with `declaredDigestMatched: null`. Only a supplied
  matching digest receives `matched`/`true`; supplied mismatches still refuse.
  The downloader was tested offline and was not invoked against GitHub.
- [build-packet.py](build-packet.py) validates retained same-head CI, Commitlint,
  CodeQL and required-job responses, derives ordinary counts from TRX case records
  while checking counters and expected coverage, matches ordinary exclusions to
  canonical passes, and checks compilation/zero-warning/zero-error claims against
  the native build log. Native API/log inputs must agree with the pinned sealed
  hosted packet bindings. A failed check produces a failed JSON report and no
  success README. The summary is deliberately limited to supported claims.

[Focused controls](test_acceptance_tools.py) pass **24/24**, both normally and with
`python3 -O`. They include a missing hosted method, failed/duplicate coverage,
evaluator and helper mismatches before import, a poisoned module cache, tampered
extraction with unchanged original ZIP, missing/matching/mismatching API digests,
missing/failed ancillary evidence, incorrect TRX counts, unsupported compilation
claims, and refusal without publishing a success summary. Exact commands, exits
and log hashes are in [checks.json](checks.json).

The corrected tools also replayed only the retained evidence:

- [Hosted audit](results/hosted-audit/hosted-audit.json) and
  [matrix](results/hosted-audit/matrix-audit.json): passed.
- [Derived summary](results/summary/README.md): passed; all claims have retained
  native support. This is no new live/full qualification or workflow rerun.
- [Missing-method control](results/negative-missing-hosted/hosted-audit.json):
  expected exit 1. Its [matrix](results/negative-missing-hosted/matrix-audit.json)
  records the injected method as `Missing` and refuses row/matrix success.
  Only a new copied control matrix was changed.

All outputs use new directories and refuse to overwrite existing destinations.
The [preservation check](preserved-inputs-after.json) confirms all 224 selected
sealed packet/native files match their [before hashes](preserved-inputs-before.json).
[Tool lineage](tool-lineage.json) identifies each sealed original and its native
source. The [manifest](manifest.json) binds every successor file. There were no
network operations, live fleets, dependency changes, staging or publication.

Run the focused controls from the Works root:

```bash
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s _bmad-output/implementation-artifacts/evidence/story-4-9/review-acceptance-tools-2026-10-27 \
  -p test_acceptance_tools.py -v
```

The replay scripts accept explicit input/output paths; see `--help`. Evidence
defaults refer to preserved native roots. The evaluator defaults to the declared
Platform `eng` directory and is admitted only after recorded-hash checks.
