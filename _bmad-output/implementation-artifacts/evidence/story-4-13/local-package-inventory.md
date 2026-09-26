# Story 4.13 local package inventory — 2026-09-26

This is a synthetic local `3.108.1` package check from an uncommitted EventStore tree at baseline HEAD `cdad82781dc3641775b652c7852fc66db3453663`. It is not a published release, a canonical source SHA, owner approval, or production admission evidence. The packages remain in `/tmp/eventstore-story-4-13-final-pack-20260926` for this workspace session.

Commands run from `references/Hexalith.EventStore` after the final code edits:

```bash
python3 tools/pack-release-packages.py /tmp/eventstore-story-4-13-final-pack-20260926 3.108.1
python3 tools/validate-release-packages.py /tmp/eventstore-story-4-13-final-pack-20260926 3.108.1
EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/eventstore-story-4-13-final-pack-20260926 dotnet tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests.dll -class Hexalith.EventStore.Contracts.Tests.Packaging.TrustedEffectPackageContractTests
```

The pack completed without error; the validator reported **14/14 exact manifest packages**; the named package-only test passed **1/1**, with 13 isolated library consumers and one isolated tool consumer.

| Manifest package ID | Local nupkg SHA-256 |
| --- | --- |
| `Hexalith.EventStore.Contracts` | `118a752eab2c3f208901a7da8b73212c6fb7d2f1fa1b9c8f8ce3b7ff52cc3a6c` |
| `Hexalith.EventStore.Client` | `b95c1a94609789be20197dacbe580c2d5520806522c77ac1f22405f5acbb9d08` |
| `Hexalith.EventStore.Server` | `78f995fcf1f4e0121a2e31e6aa1e5877919affede33ba87a65cfd10032e35960` |
| `Hexalith.EventStore.SignalR` | `e7f02046c2e21b2551ca86001577ff64a593267115d4dd31031c88704107790a` |
| `Hexalith.EventStore.Testing` | `e4fe27f683e97971560d2062dcd3b4dea3c297af698dadfdf4e0bd7a7cce5436` |
| `Hexalith.EventStore.Testing.Integration` | `2352b753b9578b35eda428d70fdf5a79bbe58b96ed11e305e6b2cf9c4ea57210` |
| `Hexalith.EventStore.Aspire` | `0ecabe5b43403abe678834725c407b7327c6106118bf0755ea633cc8482bd2d5` |
| `Hexalith.EventStore.ServiceDefaults` | `e842d244a35d56fdad0138ab6c4eb02abf62d78f8202d8901edf28d78ebb5c9d` |
| `Hexalith.EventStore.DomainService` | `ffeaa9322e5858490197ae4f9ba247e1decab275a1617c8744b1c886817bb9d6` |
| `Hexalith.EventStore.RestApi.Generators` | `dad62261a000a21f6db6b051ed07b65602061c925640bf1142a27764c7cbcd80` |
| `Hexalith.EventStore.Gateway` | `8602198391761a73c054eae41d6ec7b0fa670b465d8df942ea589668b8a952c2` |
| `Hexalith.EventStore.Admin.Abstractions` | `d9adecdb34824cc3cd38899a7ff6e7eab721bfb429fea0b3eb57c2381343a21d` |
| `Hexalith.EventStore.Admin.Cli` | `a56cca96997fa3805543a089c834a29c435c53a1077445537361b91d052e0534` |
| `Hexalith.EventStore.Admin.Server` | `96358e643e6c2f089d4e85dc3b64a8b8b1e7675cdd7b0503a95b0d894f134880` |
