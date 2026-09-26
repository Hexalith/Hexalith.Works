# Story 4.13 clean-source package inventory — 2026-09-26

This synthetic local package set was built from the clean EventStore commit
`048c8930900af12f97e9aa3a1e4193399b8d880b` at version `3.108.1`.
`git status --porcelain=v1` returned no changes in EventStore before and after
packing. This identifies an immutable source candidate; it does not establish a
published release, production admission, accountable data-owner approval, or an
AD-28 restore drill. The packages are in
`/tmp/eventstore-story-4-13-agent-pack-20260926` for this workspace session.

Commands run from `references/Hexalith.EventStore`:

```bash
git rev-parse HEAD
git status --porcelain=v1
python3 tools/pack-release-packages.py /tmp/eventstore-story-4-13-agent-pack-20260926 3.108.1
python3 tools/validate-release-packages.py /tmp/eventstore-story-4-13-agent-pack-20260926 3.108.1
EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/eventstore-story-4-13-agent-pack-20260926 dotnet tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests.dll -class Hexalith.EventStore.Contracts.Tests.Packaging.TrustedEffectPackageContractTests
sha256sum /tmp/eventstore-story-4-13-agent-pack-20260926/*.nupkg
```

Packing completed without error. The validator accepted exactly 14 manifest
packages. The named package-only test passed 1/1 using 13 isolated library
consumers and one isolated tool consumer.

| Manifest package ID | Local nupkg SHA-256 |
| --- | --- |
| `Hexalith.EventStore.Contracts` | `b7359327503a584c3689fbdfa7c0212735da1dd229e2952ffc7748a36f40b840` |
| `Hexalith.EventStore.Client` | `01da81514fa2b886eeeddab37537bce7635bbcdfd83f4c8fe76d0ed3b7a5ee5d` |
| `Hexalith.EventStore.Server` | `95512641a6d94269eb306feca9dbc9df60a162a44b1f22e2601a3b7a59bd4bb4` |
| `Hexalith.EventStore.SignalR` | `5bbd123d572bc88a5322589144c11c45b9fe20ee2df42d56d56e88a6109ff47e` |
| `Hexalith.EventStore.Testing` | `33fbf06ed0d9ca5f3392172fcb0dfa243bf827a3c6baca93036e65285f54b3d8` |
| `Hexalith.EventStore.Testing.Integration` | `99c553e90078b3e6ef8bf093758909c04d6eb7e4721e3e9df46bef5949423b53` |
| `Hexalith.EventStore.Aspire` | `1a3acfb09cc0421b755d7c3008e0b4b422e8e6cd97a8d423bd50a691e76cd5b8` |
| `Hexalith.EventStore.ServiceDefaults` | `88dbcaa65673a337e4ad697361ab7391aaefe32295c235f366c49676291b38b4` |
| `Hexalith.EventStore.DomainService` | `08daeacc744d21d2d1ae486590c2564879813c9d312ca9eb1da07af749430349` |
| `Hexalith.EventStore.RestApi.Generators` | `132ca42d75f2a05f10e245ca71cd0b1b9a7bcfd0f25d8c396338e2f65b419df6` |
| `Hexalith.EventStore.Gateway` | `5bd5ebc54bff91e170002822c7af6b83d926a5c54749e993e68d3e61d7564524` |
| `Hexalith.EventStore.Admin.Abstractions` | `d7b3fb91c808a556fdd569be2195a901c444a9a640c2f9f5198a26c958c835e6` |
| `Hexalith.EventStore.Admin.Cli` | `074ddae5d71bed5530cd025bc2a0226a3fa8fcf92c0268b16d0cd83e40cc07a4` |
| `Hexalith.EventStore.Admin.Server` | `94833e1bb0e9277a906fb7586c87d831f5b6d397cfd6e74dd774c4c228dfd748` |
