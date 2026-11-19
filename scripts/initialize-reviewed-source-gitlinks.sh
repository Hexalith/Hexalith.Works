#!/usr/bin/env bash
# Initialize the root gitlinks Debug-lane architecture evaluation needs.
# Shared CI checks out only Platform and Parties; this is not the R1–R11 set.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
git_dir="$(git -C "$root" rev-parse --absolute-git-dir)"
mkdir -p "$git_dir"
exec 9>"$git_dir/hexalith-reviewed-source.lock"
flock 9
required=(
  references/Hexalith.Commons/src/libraries/Hexalith.Commons.UniqueIds
  references/Hexalith.McpCli/src/Hexalith.McpCli.Abstractions
  references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Hexalith.EventStore.Contracts.csproj
  references/Hexalith.PolymorphicSerializations/src/libraries/Hexalith.PolymorphicSerializations
)
for path in "${required[@]}"; do
  if [[ ! -e "$path" ]]; then
    git --literal-pathspecs -c protocol.file.allow=never -c submodule.recurse=false \
      submodule update --init --checkout -- \
      references/Hexalith.Commons \
      references/Hexalith.McpCli \
      references/Hexalith.EventStore \
      references/Hexalith.PolymorphicSerializations
    break
  fi
done
