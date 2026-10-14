#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fixture_dir="$(mktemp -d)"
mkdir -p "$fixture_dir/components"
container_name="works-sdk-rollback-414-$$"
dapr_process=""
container_started=false

cleanup() {
    if [[ -n "$dapr_process" ]]; then
        kill -INT "$dapr_process" 2>/dev/null || true
        wait "$dapr_process" 2>/dev/null || true
    fi
    if [[ "$container_started" == true ]]; then docker stop "$container_name" >/dev/null 2>&1 || true; fi
    rm -rf "$fixture_dir"
}
trap cleanup EXIT

docker run --rm --name "$container_name" -p 127.0.0.1::6379 -d redis:6 \
    redis-server --save '' --appendonly no >/dev/null
container_started=true

redis_endpoint="$(docker port "$container_name" 6379/tcp)"
if [[ ! "$redis_endpoint" =~ ^127\.0\.0\.1:([0-9]+)$ || "${BASH_REMATCH[1]}" == "6379" ]]; then
    echo "The isolated Redis container did not receive a dedicated loopback endpoint." >&2
    exit 1
fi
read -r dapr_http_port dapr_grpc_port dapr_internal_port metrics_port profile_port < <(python3 - <<'PORTS'
import socket
sockets = [socket.socket() for _ in range(5)]
for item in sockets:
    item.bind(('127.0.0.1', 0))
print(*(item.getsockname()[1] for item in sockets))
for item in sockets:
    item.close()
PORTS
)

cat >"$fixture_dir/components/statestore.yaml" <<YAML
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: $redis_endpoint
  - name: redisPassword
    value: ""
  - name: actorStateStore
    value: "true"
YAML

"${HOME}/.dapr/bin/daprd" \
    --app-id "$container_name" \
    --dapr-http-port "$dapr_http_port" \
    --dapr-grpc-port "$dapr_grpc_port" \
    --dapr-internal-grpc-port "$dapr_internal_port" \
    --metrics-port "$metrics_port" \
    --profile-port "$profile_port" \
    --resources-path "$fixture_dir/components" \
    --placement-host-address localhost:50005 \
    --scheduler-host-address localhost:50006 \
    --log-level warn >"$fixture_dir/daprd.log" 2>&1 &
dapr_process=$!

verify_sidecar() {
    kill -0 "$dapr_process" 2>/dev/null || return 1
    curl --silent --fail --connect-timeout 1 --max-time 1 \
        "http://127.0.0.1:$dapr_http_port/v1.0/healthz" >/dev/null || return 1
    curl --silent --fail --connect-timeout 1 --max-time 1 \
        "http://127.0.0.1:$dapr_http_port/v1.0/metadata" >"$fixture_dir/metadata.json" || return 1
    python3 - "$fixture_dir/metadata.json" "$container_name" <<'IDENTITY' || return 1
import json, sys
with open(sys.argv[1]) as source:
    actual = json.load(source).get('id')
if actual != sys.argv[2]:
    raise SystemExit('The responding sidecar does not belong to this isolated fixture.')
IDENTITY
    kill -0 "$dapr_process" 2>/dev/null
}
ready=false
for _ in {1..120}; do
    if ! kill -0 "$dapr_process" 2>/dev/null; then break; fi
    if verify_sidecar; then ready=true; break; fi
    sleep 0.25
done
if [[ "$ready" != true ]]; then
    tail -60 "$fixture_dir/daprd.log" >&2
    exit 1
fi

printf 'Isolated fixture %s owns Redis %s and Dapr HTTP/gRPC %s/%s.\n' \
    "$container_name" "$redis_endpoint" "$dapr_http_port" "$dapr_grpc_port"
cd "$repository_root"
projection_results=()
rollback_results=()
if [[ -n "${WORKS_SDK_RESULT_DIRECTORY:-}" ]]; then
    mkdir -p "$WORKS_SDK_RESULT_DIRECTORY"
    projection_results=(-result-xml "$WORKS_SDK_RESULT_DIRECTORY/projections.xml")
    rollback_results=(-result-xml "$WORKS_SDK_RESULT_DIRECTORY/rollback.xml")
fi
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
    dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj \
    --configuration Release --no-restore -warnaserror -m:1 \
    -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0

verify_sidecar
WORKS_SDK_DAPR_GRPC_ENDPOINT="http://127.0.0.1:$dapr_grpc_port" \
    tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests \
    -noLogo -class Hexalith.Works.IntegrationTests.WorksSdkProjectionQueryTests "${projection_results[@]}"

verify_sidecar
WORKS_SDK_ROLLBACK_DAPR_GRPC_ENDPOINT="http://127.0.0.1:$dapr_grpc_port" \
WORKS_SDK_ROLLBACK_REDIS_ENDPOINT="$redis_endpoint" \
WORKS_SDK_ROLLBACK_ALLOW_ISOLATED_FLUSH=true \
    tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests \
    -noLogo -class Hexalith.Works.IntegrationTests.WorksSdkRollbackRestoreTests "${rollback_results[@]}"
