# Story 4.15 closeout review patches — 2026-10-18

This addendum records the four patch findings from the code review of the post-review-patch closeout (`9a7a71b..d8f254e`). Works HEAD was `d8f254e`; SDK gitlink and clean working tree stayed at `f43b121c6c7539487b1f38b3c28508e3ab6264cc`. No commit, push, package repack or dependency-pin change was made. The earlier [final review closeout](../post-review-patch-closeout/implementation.md) remains immutable historical evidence; its `verify.py --current` now reports the files changed here, as intended for a historical packet.

## Changes

- `WorksExpiryEvidence` refuses a `WorkItemRescheduled` before the first `WorkItemCreated`. Previously a witnessed pre-creation reschedule passed `IsValidReplacement(null, witness)`, and a later witnessless creation silently reset the reminder fold to no expiry intent. `WorkItemAggregate` rejects reschedule while `Unknown`, so no real history is refused.
- `RescheduleBeforeCreationRefusesContextAndQuarantinesWithoutCheckpointAdvance` covers a witnessless and a witnessed creation after a witnessed pre-creation reschedule: reader and reminder acquisition refuse with the exact message, and the runner from checkpoint 0 durably quarantines envelope 1 as `invalid-evidence` with checkpoint 0, no plan and no submission. With the guard removed, both cases fail ([mutation log](raw/mutation-guard-removed.log.gz)).
- The scalar-presence `WorkItemRescheduled` rows now place the mutated reschedule after a valid creation, so they keep isolating member presence on valid committed history.
- `docs/eventstore-api-surface-constraints.md` places the reopened store-contract subsection at the end of the producer-history section, so it no longer captures iteration 1–3 paragraphs. The adoption guide, boundary record and constraints document cite the `f43b121c` requalification, closeout and this addendum. The Story 4.16 ledger entry records the third decoder change and both lifecycle refusals.

## Verification

Commands ran sequentially from the Works repository; original output paths were in the session scratchpad and the packet retains every log/XML under `raw/`.

| Lane | Command | Passed | Failed / errors / skipped / not run |
| --- | --- | ---: | --- |
| Debug build | `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1` | 0 warnings, 0 errors | — |
| Focused Debug | `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorksSdkIteration4Tests'` | 95 | 0 / 0 / 0 / 0 |
| Debug integration | `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*'` | **895** | 0 / 0 / 0 / 0 |
| Architecture | build `tests/Hexalith.Works.ArchitectureTests` Debug `-m:1`, then run its built assembly | 269 | 0 / 0 / 0 / 0 |
| Unit | build `tests/Hexalith.Works.UnitTests` Debug `-m:1`, then run its built assembly | 589 | 0 / 0 / 0 / 0 |
| Property | build `tests/Hexalith.Works.PropertyTests` Debug `-m:1`, then run its built assembly | 3 | 0 / 0 / 0 / 0 |
| Release build | `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1 -p:HexalithEventStoreVersion=3.124.1-story415.20261018.1 -p:WorksAllowLocalEventStoreCandidate=true` | 0 warnings, 0 errors | — |
| Release integration | `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*'` | **895** | 0 / 0 / 0 / 0 |

Each integration mode passes both new cases, all 36 scalar-presence cases, the four repeated-creation cases and both creation/reschedule controls. The Debug dependency graph resolves all nine EventStore libraries as projects (source `f43b121c`); Release resolves all nine as unpublished candidate `3.124.1-story415.20261018.1` packages. This does not qualify default public 3.124.0.

Architecture ran after the final document edits. No current-source live restart or callback was rerun; that remains the approved Story 4.16 input. Retained raw artifacts were scanned for private-key PEM blocks, compact JWTs and secret assignments before compression; none were found.

## Bindings

| Source | SHA-256 |
| --- | --- |
| `src/Hexalith.Works/Recovery/WorksExpiryEvidence.cs` | `195649a2c3b1a304b8296c8f1892408ce51b4b6a8c00d3398cce3f8f04ead928` |
| `tests/Hexalith.Works.IntegrationTests/WorksSdkIteration4Tests.cs` | `bed4fbf58842c527e642de8f90133eaa1a8b8f8c4dda84df839ddb78212e0953` |
| `docs/sdk-reminder-process-adoption.md` | `4609c99035ad1f7d12efea4678f77778a170521a4022aa7eed7265fcc5b27b64` |
| `docs/boundary-decision-record.md` | `45e53220c84b59fe6cc3aa2b230ff3b7bba66a4d880a262172612342b5920483` |
| `docs/eventstore-api-surface-constraints.md` | `bba81404e17b0d532c7248798dccc0dfb8f23d0b0254d13e2e6dcc0e072902f3` |
| `_bmad-output/implementation-artifacts/deferred-work.md` | `104b9a0b0ad243b73fa7e0691c33a6f157016bff3bf1e6ae3c842898f875d07f` |
| `global.json` | `d7efcea9b4bbd8e2d7cda75ee6f4ba91b7a05ffa8f93255054516583af529872` |

| Artifact | Compressed SHA-256 | Original SHA-256 | Original bytes |
| --- | --- | --- | ---: |
| `raw/architecture-build.log.gz` | `bf27706dc7433040ef843f27abdf1b3024bd546e41c145c963fa35b16c535cc0` | `2c24b8afd959dc9f5b852d57edb4024fcf1938e297009411bd9369c7aee2eead` | 2743 |
| `raw/architecture.log.gz` | `26650b975ade004aa7138b14fda98c1a6b4ea616d05980e923774e41d340375e` | `0d7ba0ebc5b7de32967ed6e277ae41923f567687dee7959b6b01f63c9d13257a` | 535 |
| `raw/architecture.xml.gz` | `cb2d36c5883f6e63746ad1f4dd42680b7740ae805c53dcb3641a0ed9ef0de987` | `655f7081cfd33fa5878d535014eb88c9e1791f6fb314c0301260b2056ed5d12c` | 188522 |
| `raw/debug-build.log.gz` | `9ec6eea44ab1d119d8701f03df700f6e679add4636506b5dce776898140506fd` | `ce202fd6739bc99ad841570801c8e0f3fcaa7debab7e2a1b84ac991f0b93afb9` | 5988 |
| `raw/debug-focused.log.gz` | `98a4f75fc747adaf07e4afb3668a3852e6aafd172b0bc325faa68c7cfd7782c8` | `fcfabd4f0726faaf5b6b4d45bdc4befbbaff303cfd3b34f97f20cb3040999039` | 528 |
| `raw/debug-focused.xml.gz` | `9017f890fdb1e94843a901a1ecfd0a188ba8483cff4f481c9d02b4ff637ba287` | `cbabdc9fbb5bac23b242372e16c432c47a92bc7d125375b51cba41f3cf067eb4` | 62295 |
| `raw/debug-integration.log.gz` | `fde8dc7b9b898966767090ddd5a95ecec6a9200bea0c81b14ca4e0819f09cf84` | `1d9fcc50dfd38adb8f57a184f6578c819e7f0884bf7049e8d6a0701999761630` | 36313 |
| `raw/debug-integration.xml.gz` | `5a2f2544517ba8ba14adea38c41231654a7f6810629a638d634e369da864c7e1` | `a5c5103d623290e1d5818f31d65c711673946324cd3dd65e71d58494f4dc0f58` | 644876 |
| `raw/mutation-guard-removed.log.gz` | `223f2969272be397116fbf7cb7acaab55b48670adae76e4e84d2ba2e0ab9b028` | `ddf342daf66e80d9a1fbbbc581528287afa11219951c9301faef556a22e0507a` | 5836 |
| `raw/property-build.log.gz` | `7b7f84b661e2f16dc884f38d66f863e984f47a1c9356791b365595dc5d07ae51` | `0fb8335c1f7dacf094d9db30a39df1c0bc34faba7c343daf3dd3e807f3dce8be` | 2727 |
| `raw/property.log.gz` | `a3a499b5fa179d29a0894dc4d47a0db60cf66797d73ce24e2e6c8f56626f5772` | `18e4f06c54ecf1d52238ef2c73dd1cb8ddeccae94e9d3a9b19065bffc19a0d63` | 578 |
| `raw/property.xml.gz` | `9a8134c7c92c4342fa12a9be92577685afed4f5f51c1e9731e0fb60cd37b4372` | `0181f8dffd8b5d36a5a758ecda6d975968bb4b42868f6d0bfad8d9cffde5d051` | 3960 |
| `raw/release-build.log.gz` | `bb3512d097a741f05ab6e7b978bb2edf8724127beb11ad5702150ad205483199` | `fd8da262c5f20f33802ecb24ca5ad06c3da7a2f01487f63ffab6e55d5f5293f6` | 2979 |
| `raw/release-integration.log.gz` | `10955faa255222bf39a466ba65deec18de655b3b6be33675a999295c3b2a0d9c` | `d80afd599884367fba25947f6f083cee1689c97672bef2f3c5ad6377ab335d3c` | 36324 |
| `raw/release-integration.xml.gz` | `fc32445e73c453633995fea772becb0614ed0ff116795fb8640a9b0f51b27b61` | `872cba9471344a14583cb379de700b927c1d82287a595720bd44dff6b91f5902` | 644771 |
| `raw/unit-build.log.gz` | `e1e42953445e2f11c9a595717c4d1bed61aa03fa9ebd8f9eb6f3f69b09526126` | `7762c505289a14dece850129c1ac3e3025bba307f88144f25599eeb8daf4df47` | 2976 |
| `raw/unit.log.gz` | `64382ad6981c19dc52605bfbf0bf3752f6d71235957812ca27c4802f84a0206f` | `72856b6856a92d1498238b17ebf906aa1966a96fc2f32a06abc94116926ec3d5` | 494 |
| `raw/unit.xml.gz` | `bde761c42e6188fca6d9ab93be4d557fb8de6f940466b4980d7448188516e4cc` | `35e2f5cc1961aab7f66d524baf2713d15f8e66e6e04843db46347a8a79de7751` | 373517 |
