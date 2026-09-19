#!/usr/bin/env bash
# Push exactly the packages named by tools/release-packages.json at one exact version.
#
# A `./nupkgs/*.nupkg` glob is wider than every validation gate: the gates count and inspect exactly the
# manifest packages, so any stray archive left in the directory would be published unvalidated. This script
# resolves each file from the manifest instead, and fails closed when one is missing.
#
# --skip-duplicate is deliberately absent. A collision means the exact version already exists, which the
# publication preflight has already proven should not happen, so it must fail loudly rather than be ignored.
# Partial-publication recovery: pushes are ordered and idempotent per file, but NuGet has no transaction.
# If this script fails part-way, some packages are already live and immutable. Do NOT retry with a
# different payload at the same version and do NOT delete the published ones. Re-run this script unchanged
# to push the remainder; a package that was already pushed fails with a 409 conflict, which is the signal
# to move on. If the remainder cannot be pushed at all, ship a new patch version containing every package
# so consumers never see a partially published version, and leave the incomplete version unlisted.
set -euo pipefail

version="${1:-}"
package_directory="${2:-./nupkgs}"
manifest="${HEXALITH_RELEASE_PACKAGE_MANIFEST:-tools/release-packages.json}"
source_url="${HEXALITH_RELEASE_NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

fail() {
  echo "[push-release-packages] $1" >&2
  exit 1
}

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] ||
  fail "A plain semantic release version is required."
[ -f "$manifest" ] || fail "The authoritative package manifest is missing: $manifest"
[ -d "$package_directory" ] || fail "The package directory is missing: $package_directory"
[ -n "${NUGET_API_KEY:-}" ] || fail "NUGET_API_KEY is required before publishing NuGet packages."

mapfile -t package_ids < <(jq -er '
  .packages
  | if type == "array" and length > 0 then . else error("empty package manifest") end
  | map(.id | if type == "string" and test("^[A-Za-z0-9][A-Za-z0-9._-]*$") then . else error("invalid package id") end)
  | if (unique | length) == length then .[] else error("duplicate package ids") end
' "$manifest") || fail "The release package manifest is invalid."

payload=()
for package_id in "${package_ids[@]}"; do
  archive="${package_directory}/${package_id}.${version}.nupkg"
  [ -f "$archive" ] || fail "Validated package is missing: $archive"
  payload+=("$archive")

  symbols="${package_directory}/${package_id}.${version}.snupkg"
  if [ -f "$symbols" ]; then
    payload+=("$symbols")
  else
    fail "Symbol package is missing: $symbols"
  fi
done

echo "[push-release-packages] Publishing ${#package_ids[@]} packages and their symbols at ${version}."
for artifact in "${payload[@]}"; do
  dotnet nuget push "$artifact" --source "$source_url" --api-key "$NUGET_API_KEY"
done
echo "[push-release-packages] Published ${#payload[@]} artifacts at ${version}."
