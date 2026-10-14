#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
producer_root="$repository_root/references/Hexalith.EventStore"
feed="$repository_root/.artifacts/story-4-14/feed"
candidate_version="$(python3 - "$repository_root/Directory.Packages.props" <<'PY'
import sys
import xml.etree.ElementTree as ET
print(ET.parse(sys.argv[1]).findtext('PropertyGroup/HexalithEventStoreVersion'))
PY
)"
if [[ "$candidate_version" != *-story414.* ]]; then
    echo "The configured EventStore version is not a Story 4.14 local candidate." >&2
    exit 1
fi
mkdir -p "$feed"
platform_candidate_version="$(python3 - "$repository_root/Directory.Packages.props" <<'PYTHON'
import sys
import xml.etree.ElementTree as ET
print(ET.parse(sys.argv[1]).findtext('PropertyGroup/HexalithPlatformIdentityVersion'))
PYTHON
)"
for package in Contracts Client ServiceDefaults DomainService Server Admin.Abstractions Testing Gateway Aspire; do
    if [[ -e "$feed/Hexalith.EventStore.$package.$candidate_version.nupkg" ]]; then
        echo "Candidate $candidate_version already exists; select a unique version before packing." >&2
        exit 1
    fi
done
if [[ -e "$feed/Hexalith.Platform.Identity.$platform_candidate_version.nupkg" ]]; then
    echo "Platform candidate $platform_candidate_version already exists; select a unique version before packing." >&2
    exit 1
fi
candidate_restore_config="$(mktemp)"
trap 'rm -f "$candidate_restore_config"' EXIT
python3 - "$feed" "$candidate_restore_config" <<'PYTHON'
import sys
import xml.etree.ElementTree as ET
configuration = ET.Element('configuration')
sources = ET.SubElement(configuration, 'packageSources')
ET.SubElement(sources, 'clear')
ET.SubElement(sources, 'add', key='story414', value=sys.argv[1])
ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
mapping = ET.SubElement(configuration, 'packageSourceMapping')
ET.SubElement(ET.SubElement(mapping, 'packageSource', key='story414'), 'package', pattern='Hexalith.EventStore.*')
ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget.org'), 'package', pattern='*')
ET.ElementTree(configuration).write(sys.argv[2], encoding='utf-8', xml_declaration=True)
PYTHON
cd "$producer_root"
for package in Contracts Client ServiceDefaults DomainService Server Admin.Abstractions Testing Gateway Aspire; do
    dotnet pack "src/Hexalith.EventStore.$package/Hexalith.EventStore.$package.csproj" \
        --configuration Release --output "$feed" -m:1 \
        -p:NuGetAudit=false -p:Version="$candidate_version" -p:PackageVersion="$candidate_version"
done
cd "$repository_root/references/Hexalith.Platform"
dotnet pack src/Hexalith.Platform.Identity/Hexalith.Platform.Identity.csproj \
    --configuration Release --output "$feed" -m:1 -p:NuGetAudit=false \
    -p:Version="$platform_candidate_version" -p:PackageVersion="$platform_candidate_version" \
    -p:HexalithEventStoreVersion="$candidate_version" -p:RestoreConfigFile="$candidate_restore_config"
sha256sum "$feed/"*".$candidate_version.nupkg" "$feed/Hexalith.Platform.Identity.$platform_candidate_version.nupkg"
echo "Local candidate only. No public package was published."
