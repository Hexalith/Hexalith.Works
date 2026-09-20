#!/usr/bin/env bash
# Push exactly the packages named by tools/release-packages.json at one exact version.
#
# A `./nupkgs/*.nupkg` glob is wider than every validation gate: the gates count and inspect exactly the
# manifest packages, so any stray archive left in the directory would be published unvalidated. This script
# resolves each file from the manifest instead, and fails closed when one is missing.
#
# --skip-duplicate is deliberately absent. A first-attempt 409 is always a collision. A later exact 409 is
# accepted only when this invocation already attempted the unchanged artifact and received an ambiguous
# transport/server result, because only that sequence can mean the prior request may have committed. Primary
# packages use --no-symbols so each matching .snupkg is pushed exactly once by the explicit symbol step.
set -euo pipefail

version="${1:-}"
package_directory="${2:-./nupkgs}"
manifest="${HEXALITH_RELEASE_PACKAGE_MANIFEST:-tools/release-packages.json}"
source_url="https://api.nuget.org/v3/index.json"
expected_package_count=5
max_attempts=3
retry_delay_seconds="${HEXALITH_RELEASE_RETRY_DELAY_SECONDS:-5}"

fail() {
  echo "[push-release-packages] $1" >&2
  exit 1
}

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] ||
  fail "A plain semantic release version is required."
[ -f "$manifest" ] || fail "The authoritative package manifest is missing: $manifest"
[ -d "$package_directory" ] || fail "The package directory is missing: $package_directory"
[ -n "${NUGET_API_KEY:-}" ] || fail "NUGET_API_KEY is required before publishing NuGet packages."
[[ "$retry_delay_seconds" =~ ^[0-9]+$ ]] || fail "The retry delay must be a whole number of seconds."
[ -f "${package_directory}/release-artifacts.sha256" ] ||
  fail "The candidate-byte ledger is missing: ${package_directory}/release-artifacts.sha256"

package_ids_text="$(jq -er --argjson expected "$expected_package_count" '
  .packages
  | if type == "array" and length == $expected then . else error("package count mismatch") end
  | map(.id | if type == "string" and test("^[A-Za-z0-9][A-Za-z0-9._-]*$") then . else error("invalid package id") end)
  | if (unique | length) == $expected then .[] else error("duplicate package ids") end
' "$manifest")" || fail "The release package manifest is invalid."
mapfile -t package_ids <<< "$package_ids_text"
[ "${#package_ids[@]}" -eq "$expected_package_count" ] || fail "The release package manifest is invalid."

payload_names=()
for package_id in "${package_ids[@]}"; do
  archive="${package_directory}/${package_id}.${version}.nupkg"
  [ -f "$archive" ] || fail "Validated package is missing: $archive"
  payload_names+=("$(basename "$archive")")

  symbols="${package_directory}/${package_id}.${version}.snupkg"
  [ -f "$symbols" ] || fail "Symbol package is missing: $symbols"
  payload_names+=("$(basename "$symbols")")
done

ledger_names="$(awk '{print $2}' "${package_directory}/release-artifacts.sha256")" ||
  fail "The candidate-byte ledger is unreadable."
mapfile -t frozen_names <<< "$ledger_names"
[ "${#frozen_names[@]}" -eq "$((expected_package_count * 2))" ] ||
  fail "The candidate-byte ledger must name exactly $((expected_package_count * 2)) artifacts."
if ! diff -u \
  <(printf '%s\n' "${payload_names[@]}" | sort) \
  <(printf '%s\n' "${frozen_names[@]}" | sort) >/dev/null; then
  fail "The candidate-byte ledger does not match the exact manifest package and symbol inventory."
fi
(
  cd "$package_directory"
  sha256sum --check --strict release-artifacts.sha256
) || fail "A release artifact changed after the candidate bytes were frozen."

push_artifact() {
  local artifact="$1"
  shift
  local ambiguous_attempt=false
  local artifact_name
  local expected_hash
  local actual_hash
  local attempt
  local lower_output
  local output
  local status

  artifact_name="$(basename "$artifact")"
  expected_hash="$(awk -v name="$artifact_name" '$2 == name { print $1 }' \
    "${package_directory}/release-artifacts.sha256")"
  [ -n "$expected_hash" ] || fail "The candidate-byte ledger does not name ${artifact_name}."

  for ((attempt = 1; attempt <= max_attempts; attempt++)); do
    actual_hash="$(sha256sum "$artifact" | awk '{print $1}')"
    [ "$actual_hash" = "$expected_hash" ] || fail \
      "Release artifact ${artifact_name} changed during publication; refusing to retry."

    if output="$(dotnet nuget push "$artifact" \
      --source "$source_url" \
      --api-key "$NUGET_API_KEY" \
      --force-english-output \
      "$@" 2>&1)"; then
      printf '%s\n' "$output"
      return 0
    else
      status=$?
    fi

    printf '%s\n' "$output" >&2
    lower_output="${output,,}"
    if [[ "$lower_output" == *"409"* && "$lower_output" == *"conflict"* ]]; then
      if [ "$ambiguous_attempt" = true ]; then
        echo "[push-release-packages] Exact 409 for ${artifact_name} followed an ambiguous attempt in this invocation; treating the unchanged artifact as accepted." >&2
        return 0
      fi
      echo "[push-release-packages] First-attempt 409 for ${artifact_name} is a publication collision." >&2
      return "$status"
    fi

    if [[ "$lower_output" == *"timed out"* \
      || "$lower_output" == *"timeout"* \
      || "$lower_output" == *"connection reset"* \
      || "$lower_output" == *"connection refused"* \
      || "$lower_output" == *"unexpected end"* \
      || "$lower_output" == *"500"* \
      || "$lower_output" == *"502"* \
      || "$lower_output" == *"503"* \
      || "$lower_output" == *"504"* \
      || "$lower_output" == *"internal server error"* \
      || "$lower_output" == *"bad gateway"* \
      || "$lower_output" == *"service unavailable"* \
      || "$lower_output" == *"gateway timeout"* ]]; then
      ambiguous_attempt=true
      if [ "$attempt" -lt "$max_attempts" ]; then
        echo "[push-release-packages] Ambiguous attempt ${attempt}/${max_attempts} for ${artifact_name}; retrying unchanged bytes." >&2
        if [ "$retry_delay_seconds" -gt 0 ]; then
          sleep "$retry_delay_seconds"
        fi
        continue
      fi
      echo "[push-release-packages] Retry budget exhausted for ${artifact_name}. Version ${version} may be partially published; do not rerun this ephemeral candidate. Publish a new patch version containing all five packages." >&2
    fi

    return "$status"
  done
}

echo "[push-release-packages] Publishing ${#package_ids[@]} packages and their symbols at ${version}."
for package_id in "${package_ids[@]}"; do
  archive="${package_directory}/${package_id}.${version}.nupkg"
  symbols="${package_directory}/${package_id}.${version}.snupkg"
  push_artifact "$archive" --no-symbols || fail "NuGet package push failed for $archive."
  push_artifact "$symbols" || fail "NuGet symbol push failed for $symbols."
done
echo "[push-release-packages] Published ${#payload_names[@]} artifacts at ${version}; any accepted 409 followed an ambiguous attempt for the same unchanged artifact in this invocation."
