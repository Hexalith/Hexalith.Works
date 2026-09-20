#!/usr/bin/env bash
# Push exactly the packages named by tools/release-packages.json at one exact version.
#
# A `./nupkgs/*.nupkg` glob is wider than every validation gate: the gates count and inspect exactly the
# manifest packages, so any stray archive left in the directory would be published unvalidated. This script
# resolves each file from the manifest instead, and fails closed when one is missing.
#
# --skip-duplicate is deliberately absent. Every HTTP 409 is a collision because local runner state cannot prove
# which bytes reached NuGet. Only explicitly classified transport/server failures are retried, using the same
# checksum-locked bytes in this invocation. Primary packages use --no-symbols so each matching .snupkg is pushed
# exactly once by the explicit symbol step.
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

mapfile -t actual_archive_names < <(
  find "$package_directory" -maxdepth 1 -type f -name '*nupkg' -printf '%f\n' | sort
)
if ! diff -u \
  <(printf '%s\n' "${payload_names[@]}" | sort) \
  <(printf '%s\n' "${actual_archive_names[@]}" | sort) >/dev/null; then
  fail "The package directory contains release archives outside the exact manifest inventory."
fi

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

is_http_collision() {
  local value="${1,,}"
  [[ "$value" =~ (http|status[[:space:]]+code|response[[:space:]]+status)[^0-9]*409([^0-9]|$) \
    || "$value" =~ (^|[^0-9])409[[:space:]]*\(conflict\) ]]
}

is_retryable_failure() {
  local value="${1,,}"
  local phrase
  local retryable_phrases=(
    "timed out"
    "operation timeout"
    "connection reset"
    "connection refused"
    "broken pipe"
    "unexpected end"
    "unexpected eof"
    "temporary failure in name resolution"
    "could not resolve host"
    "no such host is known"
    "tls handshake timeout"
    "ssl connection could not be established"
  )

  for phrase in "${retryable_phrases[@]}"; do
    if [[ "$value" == *"$phrase"* ]]; then
      return 0
    fi
  done

  [[ "$value" =~ (http|status[[:space:]]+code|response[[:space:]]+status)[^0-9]*(408|429|5[0-9][0-9])([^0-9]|$) \
    || "$value" =~ (^|[^0-9])(408[[:space:]]+request[[:space:]]+timeout|429[[:space:]]+too[[:space:]]+many[[:space:]]+requests|500[[:space:]]+internal[[:space:]]+server[[:space:]]+error|502[[:space:]]+bad[[:space:]]+gateway|503[[:space:]]+service[[:space:]]+unavailable|504[[:space:]]+gateway[[:space:]]+timeout) ]]
}

push_artifact() {
  local artifact="$1"
  shift
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
    if is_http_collision "$lower_output"; then
      echo "[push-release-packages] HTTP 409 for ${artifact_name} is a publication collision; remote bytes cannot be authenticated from local runner state." >&2
      return "$status"
    fi

    if is_retryable_failure "$lower_output"; then
      if [ "$attempt" -lt "$max_attempts" ]; then
        echo "[push-release-packages] Retryable attempt ${attempt}/${max_attempts} for ${artifact_name}; retrying unchanged bytes." >&2
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
echo "[push-release-packages] Published ${#payload_names[@]} checksum-locked artifacts at ${version}."
