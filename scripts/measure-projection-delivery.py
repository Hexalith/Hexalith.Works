#!/usr/bin/env python3
"""Measure ordinary Works delivery against an owned Dapr and Redis instance."""

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
METHOD = "Hexalith.Works.IntegrationTests.WorksSdkProjectionQueryTests.OrdinaryDeliveryMeasuresRealStoreCostNearHistoryLimits"


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
    parser.add_argument("--results", type=Path, required=True, help="Destination for the three-scenario xUnit XML report")
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
            subprocess.run(("dotnet", binary, "-explicit", "on", "-method", METHOD,
                            "-showLiveOutput", "-result-xml", result), env=environment, check=True, timeout=480)
            assemblies = ElementTree.parse(result)
            passed = [case for case in assemblies.findall(".//test") if case.get("result") == "Pass"]
            if len(passed) != 3 or not all(case.get("method") == METHOD.rsplit(".", 1)[1] for case in passed):
                raise RuntimeError("The measurement requires three successful delivery scenarios.")
            # xUnit escapes quotes in test output before writing the XML text node.
            samples = [json.loads(json.loads('"' + case.findtext("output", "").strip() + '"')) for case in passed]
            scenarios = {(sample["ownerCount"], sample["largeHistories"]) for sample in samples}
            if scenarios != {(32, False), (32, True), (512, False)} or any(
                sample["measurement"] != "ordinary-delivery" or len(sample["samplesMilliseconds"]) != 5
                or sample["runtimeImage"] != runtime_id or sample["redisImage"] != redis_id for sample in samples
            ):
                raise RuntimeError("The report must retain every delivery sample and the selected images.")
            print(json.dumps(samples, indent=2))
        except (Exception, KeyboardInterrupt):
            subprocess.run(("docker", "logs", sidecar), check=False, timeout=30)
            raise


if __name__ == "__main__":
    main()
