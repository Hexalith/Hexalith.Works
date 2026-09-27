# Story 4.13 deletion-fence local package inventory — 2026-09-26

This synthetic `3.108.1` package set was built after the deletion-fence edits from
an uncommitted EventStore tree at baseline HEAD
`f54d7ea502ae0f8f853eac4b2baebb8efbb8407b`. That commit does not identify
the package source tree. These packages are local checks, not a published release,
owner approval, or AD-28 restore-drill evidence. The files are in
`/tmp/eventstore-story-4-13-fence-pack-20260926` for this workspace session.

Commands run from `references/Hexalith.EventStore`:

```bash
python3 tools/pack-release-packages.py /tmp/eventstore-story-4-13-fence-pack-20260926 3.108.1
python3 tools/validate-release-packages.py /tmp/eventstore-story-4-13-fence-pack-20260926 3.108.1
EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/eventstore-story-4-13-fence-pack-20260926 dotnet tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests.dll -class Hexalith.EventStore.Contracts.Tests.Packaging.TrustedEffectPackageContractTests
sha256sum /tmp/eventstore-story-4-13-fence-pack-20260926/*.nupkg
```

Packing exited 0, the validator accepted exactly 14 manifest packages, and the
named package-only test passed 1/1 using 13 isolated library consumers and one
isolated tool consumer.

| Manifest package ID | Local nupkg SHA-256 |
| --- | --- |
| `Hexalith.EventStore.Admin.Abstractions` | `c326aabcc15730995fa8d3cdb56ed45437a737d790311ff97b4f330f63e108bf` |
| `Hexalith.EventStore.Admin.Cli` | `58d2a1bc076178031031d985c70f80d5a7acc331a6461dd5abb885a7851ac981` |
| `Hexalith.EventStore.Admin.Server` | `f004f466fd4b3fcc05faf6248c940a3976e9e0b892e467565c6e962aad72888b` |
| `Hexalith.EventStore.Aspire` | `f9d705214a1fa0a513fc80f7571d6533b2c39281a6dd1773fcfad015acaf9d6f` |
| `Hexalith.EventStore.Client` | `469607b7eefaa2bbb83765a1647868f7394485c7b114ff6c25de4e5ea94b0285` |
| `Hexalith.EventStore.Contracts` | `1c8f97184fcea9b75bd794cda5f2bdce69e842d69b0724c82961a98dceaddb8f` |
| `Hexalith.EventStore.DomainService` | `52d04b1c29364581b8bd2df0bb96cbc166374004ca44a2ff64d1c2f59400ba0b` |
| `Hexalith.EventStore.Gateway` | `089ec8fbc48cd7aa00068dcb27172843056e2d90fc1566530494eaa68953ca4c` |
| `Hexalith.EventStore.RestApi.Generators` | `adf30f14b15f9bdb27647dc522869e7e6edccb24f6cbb9963ff4ae8d0bf62c04` |
| `Hexalith.EventStore.Server` | `d950f4f24c18c44f469c3dc993486dacb420e1640e1e53723e7057889beaae98` |
| `Hexalith.EventStore.ServiceDefaults` | `efd347d6ecc7503f33b642612c7325119b751fa9cb7f25313a4d5060585e93b9` |
| `Hexalith.EventStore.SignalR` | `029f69b28085f4f9f1693c2df521be9b14eabefc3c5344ffbdaf0460aab8bec8` |
| `Hexalith.EventStore.Testing` | `0139eae55414a1cd43fcf4c84a8bb061d488d9d51eda998ecbddf3c8a335d53f` |
| `Hexalith.EventStore.Testing.Integration` | `59b2f3faf198795ee417f5ed6dd07b8ec3e8ac4895b5084eda6a345c7445126f` |
