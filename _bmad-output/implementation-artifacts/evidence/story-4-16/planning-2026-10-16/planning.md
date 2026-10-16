# Story 4.16 investigation — 2026-10-16

Story 4.15 is accepted against public EventStore 3.123.0. The existing 4.16 draft has been investigated with two read-only specialists. Missing Works verifier, ledger, SDK opt-in wiring, readiness/operations/profile and target-host fixture support are implementation work, not external blockers. All 4.16 execution tasks remain open.

Platform baseline start failed before resources with NU1605: apphost selected EventStore.Aspire3.120.0 and Dapr13.6.0-preview.1.261001-0243 below its composition project's existing catalog selections. As the user's authorized latest-version follow-up, the two inline pins now match the existing catalog: EventStore.Aspire3.123.0 and Dapr13.6.1-beta.935. No catalog dependency or API was changed. The first metadata-reading attempt rejected a conditional property without editing anything; its unchanged verifier failure remains recorded.

After alignment, the exact default command from `/home/administrator/projects/hexalith/platform`:

```bash
./eng/verify-agents-host.sh
```

exits **1** after matching pins and **8 passing tests**:

```text
ERROR: DependencyNotAvailable: EXT-HOST-1. Complete H1-H4 composition, production providers, accepted targets/commands and persisted qualification are unavailable.
```

The explicit scaffold command succeeds, exit **0**, with all eight Python checks and its Debug build:

```bash
./eng/verify-agents-host.sh --mode LocalScaffold --artifacts-path /tmp/story416-platform-scaffold-20261016
```

This does not qualify Full. The current owner contract `docs/ext-host-1-agents-composition.md` explicitly distinguishes these modes and refuses Agents-enabled startup; README and the old 4.16 draft still expect the earlier passing default. The planning question is whether 4.16 preserves the current Full refusal plus a green LocalScaffold check, or waits for the separate complete Agents qualification. No external owner approval or provider evidence is fabricated. The retained 4.13 decision allows synthetic producer proof; real-data admission remains independently closed.

Baseline `aspire describe --apphost apphost.cs --format Json` and exact-target stop report no running AppHost after the failed build. Nothing was deployed, published, committed, staged, pushed or initialized. No Works host was removed. [Exact command/result/hash records](results-manifest.json), [two-line patch](platform-pin-alignment.patch) and [Platform state](platform-baseline.json) are retained.
