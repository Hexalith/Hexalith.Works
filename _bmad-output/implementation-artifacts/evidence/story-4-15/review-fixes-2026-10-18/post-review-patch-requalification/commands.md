# Exact deterministic commands

Commands ran sequentially from the Works repository. Original temporary output paths are provenance; the packet retains the referenced logs/XML under `raw/`.

- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1` — exit 1; 8.239 seconds.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1` — exit 0; 7.543 seconds.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorksSdkIteration4Tests' -result-xml /tmp/story415-requalification-51am5Z/debug-focused.xml` — exit 0; 1.211 seconds.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/story415-requalification-51am5Z/debug-integration.xml` — exit 0; 18.422 seconds.
- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Debug -m:1` — exit 0; 3.031 seconds.
- `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/story415-requalification-51am5Z/architecture.xml` — exit 0; 11.03 seconds.
- `dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj --configuration Debug -m:1` — exit 0; 2.742 seconds.
- `dotnet tests/Hexalith.Works.UnitTests/bin/Debug/net10.0/Hexalith.Works.UnitTests.dll -result-xml /tmp/story415-requalification-51am5Z/unit.xml` — exit 0; 0.51 seconds.
- `dotnet build tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj --configuration Debug -m:1` — exit 0; 2.486 seconds.
- `dotnet tests/Hexalith.Works.PropertyTests/bin/Debug/net10.0/Hexalith.Works.PropertyTests.dll -result-xml /tmp/story415-requalification-51am5Z/property.xml` — exit 0; 0.463 seconds.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1 -p:HexalithEventStoreVersion=3.124.1-story415.20261018.1 -p:WorksAllowLocalEventStoreCandidate=true` — exit 0; 5.944 seconds.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/story415-requalification-51am5Z/release-integration.xml` — exit 0; 17.13 seconds.

Verify from any directory:

```bash
python3 /path/to/post-review-patch-requalification/verify.py
python3 /path/to/post-review-patch-requalification/verify.py --current
```

The optional `--current` check requires this Works checkout. Plain verification is portable.
