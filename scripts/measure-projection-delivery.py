#!/usr/bin/env python3
"""Measure Works projection and recovery paths against an owned Dapr and Redis instance."""

import argparse
import json
from contextlib import ExitStack
import os
from pathlib import Path
import subprocess
from tempfile import TemporaryDirectory
import time
from urllib.error import URLError
from urllib.request import ProxyHandler, build_opener
from uuid import uuid4
from xml.etree import ElementTree


WORKSPACE = Path(__file__).resolve().parents[1]
METHODS = {
    "ordinary-delivery": "WorksProjectionDeliveryMeasurementTests.OrdinaryDeliveryMeasuresRealStoreCostNearHistoryLimits",
    "recovery-catalog": "WorksRecoveryCatalogMeasurementTests.CatalogSweepMeasuresRealStoreCost",
    "terminal-queue": "WorksQueueMeasurementTests.WhatsNextMeasuresRetainedTerminalMembershipCost",
    "sharded-reads": "WorksQueueMeasurementTests.ShardedProjectionReadsMeasureDetailAndRecoveryDeadlines",
    "unpaged-queue": "WorksQueueMeasurementTests.WhatsNextMeasuresUnpagedEligibleResponses",
}
# Seeding the 10,000-owner admitted queue cap on real Redis takes minutes; other scenarios fit the default.
TIMEOUTS = {"unpaged-queue": 2_400}


def docker(*arguments):
    return subprocess.check_output(("docker", *arguments), text=True, timeout=90).strip()


def cleanup_container(name):
    subprocess.run(("docker", "container", "rm", "--force", "--volumes", name), timeout=30, check=True, stdout=subprocess.DEVNULL)


def wait_for_sidecar(address):
    opener = build_opener(ProxyHandler({}))
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        try:
            with opener.open(f"http://{address}/v1.0/healthz/outbound", timeout=2) as response:
                if response.status == 204:
                    return
        except (URLError, TimeoutError, ConnectionError):
            time.sleep(0.2)
    raise RuntimeError("The disposable Dapr instance did not become ready within 60 seconds.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, required=True, help="Destination for the xUnit XML report")
    parser.add_argument("--scenario", choices=METHODS, default="ordinary-delivery")
    args = parser.parse_args()
    result = args.results.resolve()
    result.parent.mkdir(parents=True, exist_ok=True)
    catalog = ElementTree.parse(WORKSPACE / "references/Hexalith.Builds/Props/Directory.Packages.props")
    version = catalog.findtext(".//HexalithDaprRuntimeVersion")
    redis_image = catalog.findtext(".//HexalithRedisImage")
    redis_tag = catalog.findtext(".//HexalithRedisImageTag")
    redis_digest = catalog.findtext(".//HexalithRedisImageDigest")
    if not all((version, redis_image, redis_tag, redis_digest)):
        raise ValueError("The shared catalog must select a Dapr runtime and an exact Redis image.")
    name = "works-projection-measure-" + uuid4().hex
    redis = name + "-redis"
    sidecar = name + "-dapr"
    binary = WORKSPACE / "tests/Hexalith.Works.IntegrationTests/bin/Release/net11.0/Hexalith.Works.IntegrationTests.dll"
    if not binary.is_file():
        raise FileNotFoundError("Build Hexalith.Works.IntegrationTests in Release first.")

    with ExitStack() as resources:
        components = Path(resources.enter_context(TemporaryDirectory(prefix="works-projection-components-")))
        components.chmod(0o755)
        (components / "statestore.yaml").write_text(f"""\
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
spec:
  type: state.redis
  version: v1
  metadata:
    - name: redisHost
      value: {redis}:6379
    - name: redisPassword
      value: ""
scopes:
  - {name}
""", encoding="utf-8")
        (components / "statestore.yaml").chmod(0o644)
        docker("network", "create", "--label", "hexalith.works.measurement=" + name, name)
        resources.callback(docker, "network", "rm", name)
        docker("run", "--detach", "--name", redis, "--label", "hexalith.works.measurement=" + name,
               "--network", name, f"{redis_image}:{redis_tag}@{redis_digest}",
               "redis-server", "--appendonly", "yes", "--dir", "/data")
        resources.callback(cleanup_container, redis)
        docker("run", "--detach", "--name", sidecar, "--label", "hexalith.works.measurement=" + name, "--network", name,
               "--publish", "127.0.0.1::3500", "--publish", "127.0.0.1::50001",
               "--volume", f"{components}:/components:ro", "--entrypoint", "/daprd", "daprio/daprd:" + version,
               "--app-id", name, "--resources-path", "/components", "--dapr-http-port", "3500",
               "--dapr-grpc-port", "50001", "--dapr-listen-addresses", "0.0.0.0", "--log-level", "warn")
        resources.callback(cleanup_container, sidecar)
        runtime_id = docker("inspect", "--format", "{{.Image}}", sidecar)
        redis_id = docker("inspect", "--format", "{{.Image}}", redis)
        try:
            wait_for_sidecar(docker("port", sidecar, "3500/tcp"))
            environment = os.environ.copy()
            environment["WORKS_PROJECTION_MEASUREMENT_RUNTIME"] = runtime_id
            environment["WORKS_PROJECTION_MEASUREMENT_REDIS"] = redis_id
            environment["WORKS_PROJECTION_MEASUREMENT_GRPC"] = "http://" + docker("port", sidecar, "50001/tcp")
            method = METHODS[args.scenario]
            subprocess.run(("dotnet", binary, "-explicit", "on", "-method", "Hexalith.Works.IntegrationTests." + method,
                            "-showLiveOutput", "-result-xml", result), env=environment, check=True,
                           timeout=TIMEOUTS.get(args.scenario, 480))
            print(json.dumps(validate_report(result, args.scenario, method, runtime_id, redis_id), indent=2))
        except (Exception, KeyboardInterrupt):
            subprocess.run(("docker", "logs", sidecar), check=False, timeout=30)
            raise


def validate_report(result, scenario, method, runtime_id, redis_id):
    cases = ElementTree.parse(result).findall(".//test")
    if not cases or any(case.get("result") != "Pass" or case.get("method") != method.rsplit(".", 1)[1] for case in cases):
        raise RuntimeError("The measurement requires all selected cases to pass.")
    # xUnit escapes quotes in test output before writing the XML text node.
    samples = [json.loads(json.loads('"' + line + '"'))
               for case in cases for line in case.findtext("output", "").splitlines() if line.strip()]
    if any(sample["measurement"] != scenario or len(sample["samplesMilliseconds"]) == 0
           or sample["runtimeImage"] != runtime_id or sample["redisImage"] != redis_id for sample in samples):
        raise RuntimeError("The report must retain the timing samples and the selected images.")
    if scenario == "ordinary-delivery":
        observed = {(sample["ownerCount"], sample["largeHistories"], len(sample["samplesMilliseconds"])) for sample in samples}
        expected = {(32, False, 5), (32, True, 5), (512, False, 5)}
    elif scenario == "recovery-catalog":
        observed = {(sample["candidates"], sample["multiPage"], len(sample["samplesMilliseconds"])) for sample in samples}
        expected = {(6, False, 5), (45, True, 15)}
    elif scenario == "terminal-queue":
        observed = {(sample["terminalOwners"], sample["activeOwners"], len(sample["samplesMilliseconds"])) for sample in samples}
        expected = {(0, 8, 3), (1_024, 8, 3), (4_096, 8, 3)}
    elif scenario == "unpaged-queue":
        if any(sample["unpagedPayloadBytes"] <= 0 or sample["unpagedWireBytes"] <= sample["unpagedPayloadBytes"]
               or len(sample["responseBytes"]) != sample["successful"] or len(sample["responseWireBytes"]) != sample["successful"]
               or (sample["eligibleOwners"] <= 256 and sample["successful"] != 3) for sample in samples):
            raise RuntimeError("The unpaged report must retain payload and wire sizes for every successful read.")
        observed = {(sample["eligibleOwners"], len(sample["samplesMilliseconds"])) for sample in samples}
        expected = {(256, 3), (1_024, 3), (2_048, 3), (4_096, 3), (10_000, 3)}
    else:
        observed = {(sample["owners"], len(sample["samplesMilliseconds"]), len(sample["discoveryMilliseconds"]),
                     len(sample["boundedDiscoveryMilliseconds"])) for sample in samples}
        expected = {(2_048, 5, 5, 5)}
    if len(samples) != len(expected) or observed != expected:
        raise RuntimeError("The report must retain every expected workload and timing sample.")
    return samples


if __name__ == "__main__":
    main()
