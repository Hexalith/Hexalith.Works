# Replay the 3.117.1 proof

Use Python 3, the repository's pinned .NET SDK, network access to NuGet, and the
recorded EventStore checkout `07d1e23a6c5b06bbbb1fc8ddb5174cc3382d3d93` with
the same root-declared source dependencies and package catalog. The bound test
assembly embeds absolute source paths, so build the fixture from the Works
submodule at `/home/administrator/projects/hexalith/works/references/Hexalith.EventStore`
at that commit. No existing `/tmp` directory is required. Run from the Works
workspace:

```bash
python3 _bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/replay-proof.py \
  --eventstore-root references/Hexalith.EventStore
```

After the replay, restore the pinned submodule commit with the non-recursive
`git submodule update -- references/Hexalith.EventStore`. Never commit the
rewound `07d1e23a` gitlink.

The recipe creates and prints a fresh directory. It downloads the 14 named
public archives with reads bounded by their recorded lengths, verifies their
lengths, retained SHA-256 hashes, and nuspec source SHA, builds the two test
fixtures, validates the inventory, and runs the three isolated package-only
consumers through one test method. It then copies the source regression
fixture and verifies the dependency manifest, test assembly, and retained
source dependency hashes before replacing its six EventStore SDK DLLs with the
hash-verified public DLLs, and runs the reminder checks. Each run's result XML
must report exactly one assembly whose tests all passed, with no failures,
errors, or skips: 1/1 for the package-only method and 227/227 for the reminder
checks. Original archives and the prepared host remain in that new directory
alongside logs, XML, and a ledger of the replay's downloads and preparation
operations.

Run the three focused checks for the current recipe from the evidence directory:

```bash
python3 -m unittest -v test_replay_proof.py
```

The 2026-10-08 parent replay ran the retained
`replay-proof.executed-2026-10-08.py` (SHA-256
`fa11cdd6591cbad4523deff70ed1bc0497f75dc635f6e139685bd6ab989180ef`).
The current `replay-proof.py` adds the checks described above; it has not been
run end to end. The retained replay summaries describe that 2026-10-08 run.
The executed recipe did not hash the test assembly or the retained source
dependency; the parent checked those matches outside it (Story 4.11 spec, C12
record).

The regression host retains source-built transitive dependencies, including
`Hexalith.Commons.UniqueIds/1.0.0`. This exercises the six public SDK DLLs in
the source test fixture. The independently restored package-only consumers
exercise the public NuGet dependency graph with no project references.

`commands.json` retains the original executed CLI calls. This recipe supplies
the download/hash checks and host preparation that were missing from that
ledger; it does not claim they were separately captured as original commands.
The executed original host's exact dependency manifest is retained as
`public-runtime-dependencies.json` and bound in `public-runtime-bindings.json`.
