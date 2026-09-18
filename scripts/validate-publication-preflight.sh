#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"
phase="${2:-}"
expected_builds_sha="04d961759994396132bb2b113ee465b64740a543"
expected_package_count=5
package_manifest="${HEXALITH_RELEASE_PACKAGE_MANIFEST:-}"
source_branch="${HEXALITH_RELEASE_SOURCE_BRANCH:-}"
source_ci_workflow="${HEXALITH_RELEASE_SOURCE_CI_WORKFLOW:-}"
release_environment="${HEXALITH_RELEASE_ENVIRONMENT:-}"
source_sha="${GITHUB_SHA:-}"
repository="${GITHUB_REPOSITORY:-}"
github_token="${GH_TOKEN:-${GITHUB_TOKEN:-}}"

fail() {
  echo "[publication-preflight] $1" >&2
  exit 1
}

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] ||
  fail "A plain semantic release version is required."
[[ "$phase" =~ ^(verify|publish)$ ]] || fail "Publication phase must be verify or publish."
[[ "${HEXALITH_BUILDS_EXECUTION_SHA:-}" = "$expected_builds_sha" ]] ||
  fail "HEXALITH_BUILDS_EXECUTION_SHA must match the approved Builds commit."
[[ "$source_sha" =~ ^[0-9a-f]{40}$ ]] || fail "GITHUB_SHA must be an exact lowercase commit SHA."
[[ "$source_branch" = "main" ]] || fail "HEXALITH_RELEASE_SOURCE_BRANCH must be exactly main."
[[ "$source_ci_workflow" = "ci.yml" ]] || fail "The exact-source proof must use ci.yml."
[[ "$package_manifest" = "tools/release-packages.json" ]] ||
  fail "The authoritative package manifest must be tools/release-packages.json."
[[ "$release_environment" = "production" ]] || fail "The release environment must be production."
[[ "${HEXALITH_RELEASE_EXPECTED_PACKAGE_COUNT-}" = "$expected_package_count" ]] ||
  fail "The caller-declared package count must be exactly $expected_package_count."
[[ "$repository" = "Hexalith/Hexalith.Works" ]] || fail "Unexpected release repository '$repository'."
[ -n "$github_token" ] || fail "A repository-scoped GitHub token is required for exact-source proof."
[ -f "$package_manifest" ] || fail "The authoritative package manifest is missing."

export GH_TOKEN="$github_token"
checked_out_sha="$(git rev-parse HEAD)"
[[ "$checked_out_sha" = "$source_sha" ]] || fail "The checkout does not match the dispatched source."
live_main_sha="$(gh api "repos/${repository}/git/ref/heads/main" --jq '.object.sha')"
[[ "$live_main_sha" =~ ^[0-9a-f]{40}$ ]] || fail "The live main SHA could not be resolved safely."
[[ "$live_main_sha" = "$source_sha" ]] || fail "The release source is stale; main has advanced."

ci_runs="$(gh api --method GET \
  "repos/${repository}/actions/workflows/${source_ci_workflow}/runs" \
  -f branch=main \
  -f event=push \
  -f head_sha="$source_sha" \
  -f status=success \
  -f per_page=100)"
printf '%s\n' "$ci_runs" | jq -e --arg sha "$source_sha" '
  [.workflow_runs[] | select(
    .head_sha == $sha and
    .head_branch == "main" and
    .event == "push" and
    .status == "completed" and
    .conclusion == "success"
  )] | length > 0
' >/dev/null || fail "No successful push CI run exists for the exact current main SHA."

package_ids_text="$(jq -er --argjson expected "$expected_package_count" '
  .packages
  | if type == "array" and length == $expected then . else error("package count mismatch") end
  | map(.id | if type == "string" and test("^[A-Za-z0-9][A-Za-z0-9._-]*$") then . else error("invalid package id") end)
  | if (unique | length) == $expected then .[] else error("duplicate package ids") end
' "$package_manifest")" || fail "The release package manifest is invalid."
mapfile -t package_ids <<< "$package_ids_text"
[ "${#package_ids[@]}" -eq "$expected_package_count" ] || fail "The release package manifest is invalid."

collisions=()
for package_id in "${package_ids[@]}"; do
  normalized_id="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
  normalized_version="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"
  package_url="https://api.nuget.org/v3-flatcontainer/${normalized_id}/${normalized_version}/${normalized_id}.${normalized_version}.nupkg"
  status="$(curl --silent --show-error --location --max-time 30 \
    --retry 3 --retry-connrefused --retry-all-errors \
    --output /dev/null --write-out '%{http_code}' "$package_url")" ||
    fail "The NuGet destination could not be queried for ${package_id} ${version}."
  case "$status" in
    404) ;;
    200) collisions+=("${package_id} ${version}") ;;
    *) fail "NuGet returned unexpected HTTP ${status} for ${package_id} ${version}." ;;
  esac
done

if [ "${#collisions[@]}" -ne 0 ]; then
  echo "[publication-preflight] Refusing to publish because exact versions already exist:" >&2
  printf '  - %s\n' "${collisions[@]}" >&2
  exit 1
fi

echo "[publication-preflight] ${phase} passed for ${#package_ids[@]} packages at ${version}."
