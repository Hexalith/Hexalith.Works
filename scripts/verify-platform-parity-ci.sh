#!/usr/bin/env bash
# Bootstrap root-declared inputs, then run Platform's complete canonical gate.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$root/scripts/verify-platform-parity-ci.py" --works-root "$root" "$@"
