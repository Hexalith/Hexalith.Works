#!/usr/bin/env python3
"""Test-only adapter for Dapr CLI 1.18's hardcoded local Sentry address.

Every launch retains the original hashed daprd and every argument except the exact
CLI-generated localhost:50001 value. Production topology never selects this adapter.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def prepare(directory, runtime, sentry_port):
    directory, runtime = Path(directory), Path(runtime).resolve(strict=True)
    if type(sentry_port) is not int or not 1024 <= sentry_port <= 65535:
        raise ValueError("An isolated unprivileged Sentry port is required")
    directory.mkdir(parents=True, exist_ok=False)
    shim = directory / ".dapr/bin/daprd"
    shim.parent.mkdir(parents=True)
    shutil.copyfile(Path(__file__), shim)
    shim.chmod(0o700)
    binding = {"runtime": str(runtime), "runtimeSha256": digest(runtime), "shimSha256": digest(shim),
               "sentryAddress": "127.0.0.1:" + str(sentry_port)}
    (directory / "binding.json").write_text(json.dumps(binding, indent=2) + "\n")
    return binding


def translated(arguments, endpoint):
    if arguments in (["--version"], ["--build-info"], ["--help"]):
        return list(arguments)
    indexes = [index for index, value in enumerate(arguments)
               if value == "--sentry-address" or value.startswith("--sentry-address=")]
    if arguments.count("--enable-mtls") != 1 or len(indexes) != 1:
        raise ValueError("Expected exactly the Dapr CLI's mTLS Sentry argument")
    index = indexes[0]
    if arguments[index] != "--sentry-address" or index + 1 >= len(arguments) or arguments[index + 1] != "localhost:50001":
        raise ValueError("Unexpected Dapr CLI Sentry endpoint; refusing an implicit override")
    result = list(arguments)
    result[index + 1] = endpoint
    return result


def execute(directory, arguments, executor=os.execv):
    directory = Path(directory)
    binding = json.loads((directory / "binding.json").read_text())
    if set(binding) != {"runtime", "runtimeSha256", "shimSha256", "sentryAddress"}:
        raise ValueError("Unexpected isolated runtime binding")
    runtime = Path(binding["runtime"])
    endpoint = binding["sentryAddress"]
    port = endpoint.removeprefix("127.0.0.1:") if isinstance(endpoint, str) else ""
    if not port.isascii() or not port.isdigit() or endpoint != "127.0.0.1:" + str(int(port)) or not 1024 <= int(port) <= 65535:
        raise ValueError("Invalid isolated Sentry endpoint")
    if not runtime.is_absolute() or runtime.resolve() != runtime or digest(runtime) != binding["runtimeSha256"]:
        raise ValueError("The original daprd differs from the selected runtime")
    if digest(directory / ".dapr/bin/daprd") != binding["shimSha256"]:
        raise ValueError("The isolated launch adapter changed")
    result = translated(arguments, endpoint)
    if "--enable-mtls" in result:
        def value(flag):
            if arguments.count(flag) != 1:
                raise ValueError("Expected exactly one " + flag)
            index = arguments.index(flag)
            if index + 1 >= len(arguments):
                raise ValueError("Missing " + flag + " value")
            return arguments[index + 1]
        config = value("--config")
        record = binding | {"appId": value("--app-id"), "config": config, "configSha256": digest(config),
                            "originalSentryAddress": "localhost:50001",
                            "argumentsSha256": hashlib.sha256(json.dumps(result, separators=(",", ":")).encode()).hexdigest()}
        with (directory / "launches.jsonl").open("a") as output:
            output.write(json.dumps(record) + "\n")
    executor(str(runtime), [str(runtime), *result])


def verify(directory, binding, starts):
    """Bind every successful host's sidecars to the actual adapter launches and unchanged runtime."""
    directory = Path(directory)
    if json.loads((directory / "binding.json").read_text()) != binding or digest(directory / ".dapr/bin/daprd") != binding["shimSha256"] \
            or digest(binding["runtime"]) != binding["runtimeSha256"]:
        raise ValueError("The isolated runtime binding changed during verification")
    launches = [json.loads(line) for line in (directory / "launches.jsonl").read_text().splitlines() if line]
    observed = set()
    for launch in launches:
        if any(launch.get(key) != value for key, value in binding.items()) or launch.get("originalSentryAddress") != "localhost:50001":
            raise ValueError("An isolated daprd launch used a foreign runtime or endpoint")
        observed.add((launch["appId"], launch["config"], launch["configSha256"]))
    required = set()
    for start in starts:
        if start.get("daprdRuntimePath") != str(directory) or not start.get("sidecarLaunches"):
            raise ValueError("A successful host did not select the bound isolated daprd")
        for sidecar in start["sidecarLaunches"]:
            required.add((sidecar["appId"], sidecar["config"], sidecar["configSha256"]))
    if not required or observed != required:
        raise ValueError("Successful sidecar starts differ from the bound isolated daprd launches")
    return {"launchCount": len(launches), "sidecarConfigurations": len(required), "launchesSha256": digest(directory / "launches.jsonl"), **binding}


if __name__ == "__main__":
    try:
        execute(Path(__file__).resolve().parents[2], sys.argv[1:])
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Isolated daprd refused: " + str(error), file=sys.stderr)
        sys.exit(1)
