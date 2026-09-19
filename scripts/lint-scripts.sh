#!/usr/bin/env bash
# Lint the release/validation tooling under scripts/.
#
# These scripts gate publication, so a syntax or quoting defect in them is a release defect. Both linters
# are optional locally: when a linter is absent this reports it and still runs the interpreter's own syntax
# check, so the gate degrades to "less thorough" rather than "silently skipped".
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

status=0
shell_scripts=(scripts/*.sh)
python_scripts=(scripts/*.py scripts/tests/*.py)

echo "== shell =="
for script in "${shell_scripts[@]}"; do
  bash -n "$script" || status=1
done
if command -v shellcheck >/dev/null 2>&1; then
  shellcheck --severity=warning "${shell_scripts[@]}" || status=1
else
  echo "shellcheck not installed; ran 'bash -n' syntax checks only." >&2
fi

echo "== python =="
python3 -m compileall -q "${python_scripts[@]}" >/dev/null || status=1
if command -v ruff >/dev/null 2>&1; then
  ruff check "${python_scripts[@]}" || status=1
elif python3 -c "import ruff" >/dev/null 2>&1; then
  python3 -m ruff check "${python_scripts[@]}" || status=1
else
  echo "ruff not installed; ran bytecode compilation checks only." >&2
fi

exit "$status"
