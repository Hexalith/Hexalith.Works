#!/usr/bin/env bash
# Prove that a manual release targets the exact current main commit with a successful push CI run.
set -euo pipefail

repository="${REPOSITORY:-${GITHUB_REPOSITORY:-}}"
dispatch_ref="${DISPATCH_REF:-${GITHUB_REF:-}}"
dispatch_sha="${DISPATCH_SHA:-${GITHUB_SHA:-}}"
source_ci_workflow="${SOURCE_CI_WORKFLOW:-ci.yml}"
github_token="${GH_TOKEN:-${GITHUB_TOKEN:-}}"

fail() {
  echo "[release-source] $1" >&2
  exit 1
}

[[ "$repository" = "Hexalith/Hexalith.Works" ]] ||
  fail "Refusing to release from unexpected repository '${repository}'."
[[ "$dispatch_ref" = "refs/heads/main" ]] || fail "Release must be dispatched from refs/heads/main."
[[ "$dispatch_sha" =~ ^[0-9a-f]{40}$ ]] || fail "The dispatch source must be an exact lowercase commit SHA."
[[ "$source_ci_workflow" = "ci.yml" ]] || fail "The exact-source proof must use ci.yml."
[ -n "$github_token" ] || fail "A repository-scoped GitHub token is required for exact-source proof."

export GH_TOKEN="$github_token"
live_main_sha="$(gh api "repos/${repository}/git/ref/heads/main" --jq '.object.sha')" ||
  fail "The live main SHA could not be queried."
[[ "$live_main_sha" =~ ^[0-9a-f]{40}$ ]] || fail "The live main SHA could not be resolved safely."
[[ "$dispatch_sha" = "$live_main_sha" ]] || fail "The dispatched source is not the current main tip."

ci_runs="$(gh api --method GET \
  "repos/${repository}/actions/workflows/${source_ci_workflow}/runs" \
  -f branch=main \
  -f event=push \
  -f head_sha="$dispatch_sha" \
  -f status=success \
  -f per_page=100)" || fail "Exact-source CI runs could not be queried."
if ! printf '%s\n' "$ci_runs" | jq -e --arg sha "$dispatch_sha" '
  [.workflow_runs[] | select(
    .head_sha == $sha and
    .head_branch == "main" and
    .event == "push" and
    .status == "completed" and
    .conclusion == "success"
  )] | length > 0
' >/dev/null; then
  fail "No successful push CI run exists for the exact current main SHA."
fi

echo "[release-source] Exact current main ${dispatch_sha} has a successful push CI run."
