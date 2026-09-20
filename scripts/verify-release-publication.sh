#!/usr/bin/env bash
# Resolve the semantic-release tag for the dispatched source and verify the exact manifest packages.
set -euo pipefail

repository="${REPOSITORY:-${GITHUB_REPOSITORY:-}}"
dispatch_sha="${DISPATCH_SHA:-${GITHUB_SHA:-}}"
manifest="tools/release-packages.json"
expected_package_count=5
poll_timeout_seconds="${HEXALITH_RELEASE_VERIFY_TIMEOUT_SECONDS:-420}"
retry_delay_seconds="${HEXALITH_RELEASE_VERIFY_RETRY_DELAY_SECONDS:-15}"
request_timeout_seconds="${HEXALITH_RELEASE_VERIFY_REQUEST_TIMEOUT_SECONDS:-15}"
attempt_limit="${HEXALITH_RELEASE_VERIFY_ATTEMPT_LIMIT:-0}"

fail() {
  echo "[publication-verification] $1" >&2
  exit 1
}

[[ "$repository" =~ ^[^/]+/[^/]+$ ]] || fail "REPOSITORY must be an owner/name pair."
[[ "$dispatch_sha" =~ ^[0-9a-f]{40}$ ]] || fail "DISPATCH_SHA must be an exact lowercase commit SHA."
[[ "$poll_timeout_seconds" =~ ^[0-9]+$ ]] || fail "The verification timeout must be a whole number of seconds."
[[ "$retry_delay_seconds" =~ ^[0-9]+$ ]] || fail "The retry delay must be a whole number of seconds."
[[ "$request_timeout_seconds" =~ ^[1-9][0-9]*$ ]] || fail "The request timeout must be a positive whole number of seconds."
[[ "$attempt_limit" =~ ^[0-9]+$ ]] || fail "The attempt limit must be a non-negative whole number."
if [ "$retry_delay_seconds" -eq 0 ] && [ "$attempt_limit" -eq 0 ]; then
  fail "A zero retry delay requires a finite attempt limit."
fi
[ -n "${GH_TOKEN:-}" ] || fail "GH_TOKEN is required to resolve release tags."
[ -f "$manifest" ] || fail "The authoritative package manifest is missing."

# semantic-release creates its tag before invoking publish plugins. Query tags rather than GitHub Releases so
# a publish failure after tagging is still inspected. An unresolvable semantic-version tag is a fail-closed
# race: treating it as unrelated could incorrectly classify a real partial release as a no-op.
tags="$(gh api --paginate "repos/${repository}/git/matching-refs/tags/v?per_page=100" \
  | jq -s '[.[][] | .ref | sub("^refs/tags/"; "")]')" || fail "Release tags could not be listed."
matching_versions=()
while IFS= read -r tag; do
  [ -n "$tag" ] || continue
  [[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || continue
  encoded_tag="$(jq -rn --arg tag "$tag" '$tag | @uri')"
  if ! commit_sha="$(gh api "repos/${repository}/commits/${encoded_tag}" --jq '.sha')"; then
    fail "Release tag '${tag}' could not be resolved; exact publication verification cannot continue."
  fi
  [[ "$commit_sha" =~ ^[0-9a-f]{40}$ ]] || fail "Release tag '${tag}' resolved to an invalid commit SHA."
  if [ "$commit_sha" = "$dispatch_sha" ]; then
    matching_versions+=("${tag#v}")
  fi
done < <(printf '%s\n' "$tags" | jq -r '.[]')

if [ "${#matching_versions[@]}" -eq 0 ]; then
  echo "[publication-verification] No semantic release tag targets ${dispatch_sha}; no publication was warranted."
  exit 0
fi
if [ "${#matching_versions[@]}" -ne 1 ]; then
  fail "Expected exactly one semantic release tag for ${dispatch_sha}; found ${#matching_versions[@]}."
fi
version="${matching_versions[0]}"

package_ids_text="$(jq -er --argjson expected "$expected_package_count" '
  .packages
  | if type == "array" and length == $expected then . else error("package count mismatch") end
  | map(.id | if type == "string" and test("^[A-Za-z0-9][A-Za-z0-9._-]*$") then . else error("invalid package id") end)
  | if (unique | length) == $expected then .[] else error("duplicate package ids") end
' "$manifest")" || fail "The release package manifest is invalid."
mapfile -t package_ids <<< "$package_ids_text"
[ "${#package_ids[@]}" -eq "$expected_package_count" ] || fail "The release package manifest is invalid."

poll_deadline=$((SECONDS + poll_timeout_seconds))
attempt=0
missing=()
while true; do
  attempt=$((attempt + 1))
  missing=()
  for package_id in "${package_ids[@]}"; do
    remaining=$((poll_deadline - SECONDS))
    if [ "$remaining" -le 0 ]; then
      missing+=("${package_id} ${version} (verification deadline exceeded)")
      continue
    fi

    max_time="$request_timeout_seconds"
    if [ "$remaining" -lt "$max_time" ]; then
      max_time="$remaining"
    fi
    normalized_id="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
    normalized_version="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"
    package_url="https://api.nuget.org/v3-flatcontainer/${normalized_id}/${normalized_version}/${normalized_id}.${normalized_version}.nupkg"
    if ! status="$(curl --silent --show-error --location --max-time "$max_time" \
      --output /dev/null --write-out '%{http_code}' "$package_url")"; then
      missing+=("${package_id} ${version} (transport error)")
      continue
    fi
    if [ "$status" != "200" ]; then
      missing+=("${package_id} ${version} (HTTP ${status})")
    fi
  done

  [ "${#missing[@]}" -eq 0 ] && break
  [ "$SECONDS" -lt "$poll_deadline" ] || break
  if [ "$attempt_limit" -gt 0 ] && [ "$attempt" -ge "$attempt_limit" ]; then
    break
  fi
  if [ "$retry_delay_seconds" -gt 0 ]; then
    remaining=$((poll_deadline - SECONDS))
    [ "$remaining" -gt 0 ] || break
    delay="$retry_delay_seconds"
    if [ "$remaining" -lt "$delay" ]; then
      delay="$remaining"
    fi
    sleep "$delay"
  fi
done

if [ "${#missing[@]}" -ne 0 ]; then
  echo "[publication-verification] Release publication is incomplete; missing exact NuGet packages:" >&2
  printf '  - %s\n' "${missing[@]}" >&2
  exit 1
fi

selected_tag="v${version}"
encoded_selected_tag="$(jq -rn --arg tag "$selected_tag" '$tag | @uri')"
final_commit_sha="$(gh api "repos/${repository}/commits/${encoded_selected_tag}" --jq '.sha')" ||
  fail "Release tag '${selected_tag}' could not be re-resolved after package visibility succeeded."
[[ "$final_commit_sha" =~ ^[0-9a-f]{40}$ ]] ||
  fail "Release tag '${selected_tag}' re-resolved to an invalid commit SHA."
[ "$final_commit_sha" = "$dispatch_sha" ] ||
  fail "Release tag '${selected_tag}' moved away from the dispatched commit during publication verification."

for package_id in "${package_ids[@]}"; do
  printf '[publication-verification] Verified %s %s\n' "$package_id" "$version"
done
