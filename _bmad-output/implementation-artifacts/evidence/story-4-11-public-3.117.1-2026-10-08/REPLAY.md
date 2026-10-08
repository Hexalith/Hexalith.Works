# Replay the 3.117.1 proof

Use Python 3, the repository's pinned .NET SDK, network access to NuGet, and the
recorded EventStore checkout `07d1e23a6c5b06bbbb1fc8ddb5174cc3382d3d93` with
the same root-declared source dependencies and package catalog. No existing
`/tmp` directory is required. Run from the Works workspace:

```bash
python3 _bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/replay-proof.py \
  --eventstore-root references/Hexalith.EventStore
```

The recipe creates and prints a fresh directory. It downloads the 14 named
public archives, verifies their retained SHA-256 hashes and nuspec source SHA,
builds the two test fixtures, validates the inventory, and runs the three
isolated package-only consumers. It then copies the source regression fixture,
verifies its exact dependency manifest, replaces its six EventStore SDK DLLs
with the hash-verified public DLLs, and runs the reminder checks. Original
archives and prepared host remain in that new directory alongside logs, XML,
and a ledger of the replay's downloads and preparation operations.

The regression host retains source-built transitive dependencies, including
`Hexalith.Commons.UniqueIds/1.0.0`. This exercises the six public SDK DLLs in
the source test fixture. The independently restored package-only consumers
exercise the public NuGet dependency graph with no project references.

`commands.json` retains the original executed CLI calls. This recipe supplies
the download/hash checks and host preparation that were missing from that
ledger; it does not claim they were separately captured as original commands.
The executed original host's exact dependency manifest is retained as
`public-runtime-dependencies.json` and bound in `public-runtime-bindings.json`.
