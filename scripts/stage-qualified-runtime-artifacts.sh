#!/usr/bin/env bash
# Copy serial-prebuilt runtime outputs into the AppHost artifacts directory Aspire uses.
set -euo pipefail
src=${1:?qualified runtime artifacts directory}
dst=${2:?destination artifacts directory}
src=$(cd "$src" && pwd)
mkdir -p "$dst"
dst=$(cd "$dst" && pwd)
if [ "$src" = "$dst" ]; then
  exit 0
fi
lock="$dst/.hexalith-qualified-runtime-staged.lock"
stamp="$dst/.hexalith-qualified-runtime-staged"
exec 9>"$lock"
flock 9
if [ -f "$stamp" ] && [ "$(cat "$stamp")" = "$src" ]; then
  exit 0
fi
mkdir -p "$dst/bin" "$dst/obj"
if [ -d "$src/bin" ]; then
  cp -a "$src/bin/." "$dst/bin/"
fi
if [ -d "$src/obj" ]; then
  cp -a "$src/obj/." "$dst/obj/"
fi
printf '%s\n' "$src" > "$stamp"
