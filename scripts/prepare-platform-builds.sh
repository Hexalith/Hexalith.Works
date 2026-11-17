#!/usr/bin/env bash
# Bind Platform's test-only build dependency to its verified published archive.
set -euo pipefail
# Git inspection must refer to these physical checkouts, even from a redirected caller.
for git_variable in ${!GIT_@}; do
    unset "$git_variable"
done
export GIT_NO_REPLACE_OBJECTS=1 GIT_NO_LAZY_FETCH=1 GIT_OPTIONAL_LOCKS=0
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
owner="$root/references/Hexalith.Platform"
builds="$root/references/Hexalith.Builds"
owner_revision="$(git -C "$owner" rev-parse --verify HEAD)"
owner_index_is_visible() {
    local entries
    entries="$(git -C "$owner" ls-files -v)" || return 1
    [[ ! "$entries" =~ (^|$'\n')([a-z]|S)' ' ]]
}
if [[ -n "$(git -C "$owner" status --porcelain --untracked-files=all)" ]] || ! owner_index_is_visible; then
    echo "Published Builds preparation requires clean recorded Platform source." >&2
    exit 1
fi
artifacts="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/works-published-builds.XXXXXX")"
workspace="$artifacts/workspace"
trap 'rm -rf -- "$workspace"' EXIT
mkdir "$workspace"
# Platform verifies an immutable published producer. Keep that checkout separate from
# the workspace's current package catalog, without initializing nested submodules.
git -c submodule.recurse=false clone --quiet --shared --no-checkout -- "$owner" "$workspace/Hexalith.Platform"
git -C "$workspace/Hexalith.Platform" -c submodule.recurse=false checkout --quiet --detach "$owner_revision"
python3 "$workspace/Hexalith.Platform/eng/bound_builds.py" \
    --platform-root "$workspace/Hexalith.Platform" --source-root "$builds" \
    --output "$workspace/Hexalith.Builds" > /dev/null
tool_root="$(python3 "$workspace/Hexalith.Platform/eng/prepare_builds_artifacts.py" --require-recorded --output "$artifacts/verified")"
if [[ "$tool_root" = *$'\n'* || "$tool_root" = *$'\r'* || ! -f "$tool_root/Hexalith.Builds.Tooling.dll" ]]; then
    echo "Platform did not return a verified published Builds tool directory." >&2
    exit 1
fi
if [[ "$(git -C "$owner" rev-parse --verify HEAD)" != "$owner_revision" || -n "$(git -C "$owner" status --porcelain --untracked-files=all)" ]] || ! owner_index_is_visible; then
    echo "Platform source changed during published Builds preparation; rerun on settled inputs." >&2
    exit 1
fi
if [[ -n "${GITHUB_ENV:-}" ]]; then
    printf 'HexalithBuildsPublishedToolRoot=%s\n' "$tool_root" >> "$GITHUB_ENV"
fi
printf '%s\n' "$tool_root"
