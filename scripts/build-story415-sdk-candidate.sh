#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
producer_root="$repository_root/references/Hexalith.EventStore"
feed="$repository_root/.artifacts/story-4-15/feed"
candidate_version="$(python3 - "$repository_root/Directory.Packages.props" <<'PY'
import sys
import xml.etree.ElementTree as ET
print(ET.parse(sys.argv[1]).findtext('PropertyGroup/HexalithEventStoreVersion'))
PY
)"
if [[ "$candidate_version" != *-story415.* ]]; then
    echo "The configured EventStore version is not a Story 4.15 local candidate." >&2
    exit 1
fi
mkdir -p "$feed"
for package in Contracts Client ServiceDefaults DomainService Server Admin.Abstractions Testing Gateway Aspire; do
    if [[ -e "$feed/Hexalith.EventStore.$package.$candidate_version.nupkg" ]]; then
        echo "Candidate $candidate_version already exists; select a unique version before packing." >&2
        exit 1
    fi
done
cd "$producer_root"
echo "Local producer checkout: $(git rev-parse HEAD)"
echo "Producer changes relative to that checkout (including untracked sources):"
git status --short
for package in Contracts Client ServiceDefaults DomainService Server Admin.Abstractions Testing Gateway Aspire; do
    dotnet pack "src/Hexalith.EventStore.$package/Hexalith.EventStore.$package.csproj" \
        --configuration Release --output "$feed" -m:1 \
        -p:NuGetAudit=false -p:Version="$candidate_version" -p:PackageVersion="$candidate_version" \
        -p:HexalithEventStoreVersion="$candidate_version"
done
sha256sum "$feed/"*".$candidate_version.nupkg"
echo "Local candidate packed from the checkout and working-tree sources reported above. No public package was published."
