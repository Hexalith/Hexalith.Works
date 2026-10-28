# Five deferred development assurance items

Starting Works revision: `3becf19a7470530c5a68ea3e84b832472769d431` on clean `main`.
No dependency or source gitlink is changed by this batch.

The three historical Story 6.1 tooling items are closed by retirement, not by
re-executing their deleted evidence programs. Replay their exact parent blobs,
retirement absence and current-source absence/ancestry from the workspace:

```bash
python3 _bmad-output/implementation-artifacts/evidence/five-deferred-development-assurance-2026-10-28/verify-retirement.py
```

The script uses only bounded, read-only Git commands, refuses missing owner
initialization or objects, and disables lazy object fetching. `retirement.json`
retains the exact trees, commands and output. It does not qualify the current
PublishedCompatibility runtime or authenticate current public package bytes.

The source drift/local compile items use the reviewed source/package pairs in
`tools/source-debug-dependencies.json`. Run:

```bash
python3 scripts/verify-source-debug.py --artifacts-path /tmp/works-source-debug
python3 -m unittest discover -s scripts/tests
```

The local gate checks indexed root gitlinks, actual checkouts and evaluated Release pins,
then restores/builds the entire `.slnx` in explicit Debug/source mode. It records
Git-visible source content identities and refuses changed snapshots after compilation.
CI runs `--check-only` using Release/package evaluation, without Debug compilation.
Both actual modes pass. The full source gate's 22 commands pass, including restore
and build with zero warnings/errors; both governed source checkouts are clean.

| Fresh suite | Passed |
| --- | ---: |
| Architecture | 404 |
| Unit | 597 |
| Property | 3 |
| Deterministic Integration | 1,043 |
| Python tooling | 112 |
| Total | 2,159 |

All suites have zero failures/skips/unexecuted cases. The final Integration run
follows the final source build; the other test assemblies retain their original
pre-execution timestamps. The focused review suite passes 14 cases. Disabling
pipeline identity admission produces six expected failing controls. Retirement
replays normally and under optimized Python with identical output. Ruff,
actionlint, CRLF-aware whitespace and Conventional Commit validation pass.

`verification.json` records exact commands and assembly hashes; `source-debug`
and `ci-preflight` retain every command/result and compressed raw log. The `.log.gz`
files expand to the original log names referenced by those results. XML/logs from
each suite are also compressed. `ledger-preservation.json` establishes that removing
the ten annotation lines restores the exact original ledger bytes. All eight
independent findings were corrected and independently rechecked; see `review.md`.

From this packet directory, verify retained bytes and the corresponding workspace
sources with:

```bash
sha256sum -c checksums.sha256
sha256sum -c source-hashes.sha256
```

The test assembly commands run each project separately. The deterministic
Integration lane uses `-class- '*SmokeTests' -class- '*Platform*'`; it does not
execute the live Platform facts. No live parity or production admission is claimed.
