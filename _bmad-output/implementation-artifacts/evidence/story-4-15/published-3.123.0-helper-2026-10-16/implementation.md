# Published EventStore 3.123.0 helper verification — 2026-10-16

The existing `scripts/verify-story415-sdk-candidate.sh` now follows the configured SDK selection. Stable versions use only NuGet.org for the initial isolated restore. Each restored SDK archive must match an independent official flat-container download by SHA-256. Those verified public archives supply the temporary upstream consumer probes and selected assembly for the unchanged-binary ABI comparison. Prereleases retain their local candidate-only SDK source mapping and exact immutable archive comparisons. NuGet.Config still uses an XML writer; fresh package and HTTP caches are inside the disposable fixture. All builds/restores target temporary consumer projects, never repository projects.

The adoption document identifies published 3.123.0 and its producer commit, documents both helper modes, and labels prior `.10` source/package/live/restore proof as historical. Only the package contract test's misleading class XML summary changed; no assertions or behavior changed.

Command from the Works root:

```bash
env PYTHONDONTWRITEBYTECODE=1 bash scripts/verify-story415-sdk-candidate.sh
```

Result: exit 0 in **16.52 seconds** at configured **3.123.0**. The host consumer persisted its accepted family contents/checkpoint and retained conflict readiness across reconstruction. All six SDK archives in its dependency graph matched independent official NuGet archives. Fresh-cache public Contracts, Client and DomainService API consumers passed, and the unchanged baseline 3.122.0 consumer passed with only the selected Contracts assembly substituted. Every build had zero warnings/errors.

The same updated helper also passed in **14.93 seconds** from a separate physical temporary checkout fixture containing `&`, double quotes and single quotes, selecting unchanged `.10` solely in that fixture. Its cold host/API/ABI probes and archive comparisons all passed. All nine historical selected `.10` archives still match the original provenance manifest; the earlier special-character fixture and evidence were preserved.

[Exact command arguments, log/archive/file hashes and fixture path](results.json) bind both runs. `bash -n` and CRLF-aware diff checks pass. No repository project restore/build, packing, publication, commit, status change or dependency selection change was performed in this dispatch. Root owns current full repository verification and final acceptance.
