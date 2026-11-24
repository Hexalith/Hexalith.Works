#!/usr/bin/env bash
# Compatibility entry point for published Builds preparation.
set -euo pipefail
script="${BASH_SOURCE[0]}"
if [[ "$script" == */* ]]; then
    exec python3 -- "${script%/*}/prepare-platform-builds.py"
fi
exec python3 ./prepare-platform-builds.py
