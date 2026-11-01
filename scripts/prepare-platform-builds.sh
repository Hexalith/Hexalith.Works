#!/usr/bin/env bash
# Bind Platform's test-only build dependency to its verified published archive.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
owner="$root/references/Hexalith.Platform"
artifacts="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/works-published-builds.XXXXXX")"
tool_root="$(python3 "$owner/eng/prepare_builds_artifacts.py" --require-recorded --output "$artifacts/verified")"
if [[ "$tool_root" = *$'\n'* || "$tool_root" = *$'\r'* || ! -f "$tool_root/Hexalith.Builds.Tooling.dll" ]]; then
    echo "Platform did not return a verified published Builds tool directory." >&2
    exit 1
fi
if [[ -n "${GITHUB_ENV:-}" ]]; then
    printf 'HexalithBuildsPublishedToolRoot=%s\n' "$tool_root" >> "$GITHUB_ENV"
fi
printf '%s\n' "$tool_root"
