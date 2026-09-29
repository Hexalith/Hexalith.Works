# Story 4.13 public package inventory — 2026-09-29

These are the published EventStore `3.109.0` packages. Tag `v3.109.0` resolves to
`818e28a8af421994e4f77e66327dc33a8c67ca5f`. All 14 manifest archives were downloaded from
NuGet's public flat container. `git diff --quiet v3.109.0 59ac4a8ed2e2d1c1808d8e5bed06c9b0a504fe10 -- src/Hexalith.EventStore.Contracts src/Hexalith.EventStore.Client`
exited 0. So the published Contracts and Client SDK match current EventStore `main`.
Other `src/` projects differ: 22 files changed between the tag and `main`, including the
erasure and deletion-fence server code. These packages therefore cannot be adopted by
4.11 or 4.15. They do prove the named public trusted-effect SDK for 4.13. They do not
prove production admission, Platform audit or mTLS/ACL, or an AD-28 restore drill.

Commands run from `references/Hexalith.EventStore`. `$DIR` is the session scratch
directory holding the downloads:

```bash
for id in $(python3 -c "import json; [print(p['id']) for p in json.load(open('tools/release-packages.json'))['packages']]"); do
  l=$(echo $id | tr A-Z a-z)
  curl -s -f -o $DIR/$id.3.109.0.nupkg https://api.nuget.org/v3-flatcontainer/$l/3.109.0/$l.3.109.0.nupkg
done
python3 tools/validate-release-packages.py $DIR 3.109.0
EVENTSTORE_PACKAGE_CONTRACT_DIR=$DIR dotnet tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests.dll -class Hexalith.EventStore.Contracts.Tests.Packaging.TrustedEffectPackageContractTests
sha256sum $DIR/*.nupkg
```

The validator accepted exactly 14 manifest packages at `3.109.0`. The named
package-only test passed 1/1 with zero failures or skips. It restored and built 13
isolated library consumers and installed one isolated tool consumer. The Contracts
consumer computed a 52-character `EffectId` and `wrk-<EffectId>` from a synthetic
tuple. The Client consumer submitted synthetic effect facts through
`ITrustedEffectSubmitter` to `/api/v1/trusted-effects` and read a `NoOp` result. Each
consumer's restored assets rejected project references and required the exact package
version. No Works assembly and no gateway status record took part. The combined log
SHA-256 is `a08c3282a45e317f4134f30d44a95c78829942daabdec71085ac825e3a7424af`.

| Manifest package ID | Public nupkg SHA-256 |
| --- | --- |
| `Hexalith.EventStore.Contracts` | `38f8ab2f116b477a2b8588810ce6ef31d8155e6c6c4f4cbd9d2d8bf76e8d0952` |
| `Hexalith.EventStore.Client` | `0cf1b8142d2abc854625984da0ca91b74bec5948209e7dcd926e915e87ab96db` |
| `Hexalith.EventStore.Server` | `c77537fc6a96cb56413652bdee76375551fe2d2ce17a902a954bf6e5eb1016b6` |
| `Hexalith.EventStore.SignalR` | `88f4ebd9c6f0e04eacd0b6d9ab9b8f47720548ca07146b6a800950751ff57049` |
| `Hexalith.EventStore.Testing` | `483f5fcaa876e810505f487f124e961864584bb7e3219d43bb4d6369eff5523a` |
| `Hexalith.EventStore.Testing.Integration` | `68d6a18086bd5202d007badca08375bb7ff58dc078e773493402f50859d9d3f2` |
| `Hexalith.EventStore.Aspire` | `6f75d1323c7c119139e94af824fe0a8b2d0924fcdfdea83b8a71bed04ecfe9e4` |
| `Hexalith.EventStore.ServiceDefaults` | `ea6e05c09fd152d8b1431f4bc6703179e882506a4d8e604ab2d3fda57420b3a7` |
| `Hexalith.EventStore.DomainService` | `d92894ded0cf9279c4b6510adbe4ed0dd3f239c2d4df14a8a18c669c158aa5ac` |
| `Hexalith.EventStore.RestApi.Generators` | `0d76b503d353c4c518f83b0ae5ef0978509b6edd4195385a85267fe7a84fa193` |
| `Hexalith.EventStore.Gateway` | `9c5cbd73b46cfb27b0fb996adf49797acb81e658152622c80e6a50e9c323244f` |
| `Hexalith.EventStore.Admin.Abstractions` | `ed626c1f89dc4da469037acacedad8f5956b4d6830f7b7315752f36d80227824` |
| `Hexalith.EventStore.Admin.Cli` | `67a6613eea5c664e537c647ee841aeadbdda827349b218ebf98afad63e9ee2cf` |
| `Hexalith.EventStore.Admin.Server` | `9d68787cb700f2de270a3dfd05c5404fbd2132f4b2341abb990245bdb3f534f5` |
