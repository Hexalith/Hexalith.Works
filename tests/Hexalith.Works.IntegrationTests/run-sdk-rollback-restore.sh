#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fixture_dir="$(mktemp -d)"
mkdir -p "$fixture_dir/components"
container_name="works-sdk-rollback-414-$$"
dapr_process=""

cleanup() {
    if [[ -n "$dapr_process" ]]; then
        kill -INT "$dapr_process" 2>/dev/null || true
        wait "$dapr_process" 2>/dev/null || true
    fi
    docker stop "$container_name" >/dev/null 2>&1 || true
    rm -rf "$fixture_dir"
}
trap cleanup EXIT

docker run --rm --name "$container_name" -p 127.0.0.1:6389:6379 -d redis:6 \
    redis-server --save '' --appendonly no >/dev/null

cat >"$fixture_dir/components/statestore.yaml" <<'YAML'
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: localhost:6389
  - name: redisPassword
    value: ""
  - name: actorStateStore
    value: "true"
YAML

"${HOME}/.dapr/bin/daprd" \
    --app-id "$container_name" \
    --dapr-http-port 56518 \
    --dapr-grpc-port 56517 \
    --dapr-internal-grpc-port 56519 \
    --metrics-port 56520 \
    --profile-port 56521 \
    --resources-path "$fixture_dir/components" \
    --placement-host-address localhost:50005 \
    --scheduler-host-address localhost:50006 \
    --log-level warn >"$fixture_dir/daprd.log" 2>&1 &
dapr_process=$!

ready=false
for _ in {1..120}; do
    if curl --silent --fail http://127.0.0.1:56518/v1.0/healthz >/dev/null; then
        ready=true
        break
    fi
    if ! kill -0 "$dapr_process" 2>/dev/null; then
        break
    fi
    sleep 0.25
done
if [[ "$ready" != true ]]; then
    tail -60 "$fixture_dir/daprd.log" >&2
    exit 1
fi

cd "$repository_root"
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
    dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj \
    --configuration Release --no-restore -warnaserror -m:1 \
    -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0

WORKS_SDK_DAPR_GRPC_ENDPOINT=http://127.0.0.1:56517 \
    tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests \
    -noLogo -class Hexalith.Works.IntegrationTests.WorksSdkProjectionQueryTests

WORKS_SDK_ROLLBACK_DAPR_GRPC_ENDPOINT=http://127.0.0.1:56517 \
WORKS_SDK_ROLLBACK_REDIS_ENDPOINT=127.0.0.1:6389 \
WORKS_SDK_ROLLBACK_ALLOW_ISOLATED_FLUSH=true \
    tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests \
    -noLogo -class Hexalith.Works.IntegrationTests.WorksSdkRollbackRestoreTests
