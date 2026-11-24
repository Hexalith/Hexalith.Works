#!/usr/bin/env bash
# Bootstrap root-declared inputs, then run Platform's complete canonical gate.
set -euo pipefail
script="${BASH_SOURCE[0]}"
if [[ "$script" != */* ]]; then
    script="./$script"
fi
exec python3 -- "${script%/*}/verify-platform-parity-ci.py" --works-root "${script%/*}/.." "$@"
