#!/usr/bin/env python3
"""Isolate root dependencies for Platform's verifier; never implement its topology or proof lanes."""

import argparse
import configparser
import hashlib
import importlib.util
import io
import json
import os
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET
from pathlib import Path

DEPENDENCIES = (
    "Hexalith.AI.Tools",
    "Hexalith.Builds",
    "Hexalith.Commons",
    "Hexalith.EventStore",
    "Hexalith.PolymorphicSerializations",
    "Hexalith.Tenants",
    "Hexalith.Parties",
    "Hexalith.Platform",
)
COORDINATORS = ("verify-platform-parity-ci.py", "verify-platform-parity-ci.sh")
REQUIRED_CHECKS = {
    "Release": "platform-release.xml",
    "supportingConsumer": "works-consumer-supporting.xml",
    "retainedStartupPreflight": "retained-preflight.xml",
    "liveRecovery": "platform-recovery.xml",
    "liveHistorical": "platform-historical.xml",
    "liveHa": "platform-ha.xml",
    "liveMtls": "platform-mtls.xml",
    "liveProjection": "platform-projection.xml",
}


def git(root, *arguments):
    return subprocess.check_output(["git", "-C", str(root), *arguments])


def coordinator_hashes():
    root = Path(__file__).resolve().parent
    return {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in COORDINATORS}


def require_unchanged_coordinators(expected):
    if coordinator_hashes() != expected:
        raise ValueError("Works parity coordinator changed after bootstrap")


def declared(root):
    config = configparser.ConfigParser()
    config.read(root / ".gitmodules")
    paths = {config[section]["path"] for section in config.sections() if section.startswith('submodule "')}
    if any(
        len(Path(path).parts) != 2 or Path(path).parts[0] != "references" or ".." in Path(path).parts for path in paths
    ):
        raise ValueError("Dependencies must be declared directly under the workspace references directory")
    return paths


def load_snapshot(platform):
    specification = importlib.util.spec_from_file_location(
        "platform_works_snapshot", platform / "eng/works_host_snapshot.py"
    )
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def link_roots(destination, sources, declarations):
    """Link existing root sources only; never initialize a nested submodule."""
    for name, source in sources.items():
        relative = "references/" + name
        if relative not in declarations:
            continue
        target = destination / relative
        if target.exists():
            target.rmdir()  # Refuse any populated directory instead of changing its contents.
        target.parent.mkdir(parents=True, exist_ok=True)
        target.symlink_to(source, target_is_directory=True)


def bootstrap(works, artifacts):
    coordinators = coordinator_hashes()
    paths = declared(works)
    sources = {}
    for name in DEPENDENCIES:
        path = works / "references" / name
        if "references/" + name not in paths or not (path / ".git").exists():
            raise ValueError("Initialize the root-declared dependency before parity: references/" + name)
        sources[name] = path.resolve()
    platform = sources["Hexalith.Platform"]
    snapshot = load_snapshot(platform)
    original = snapshot.origin_files(platform)
    commit = snapshot.revision(platform)
    version = ET.parse(works / "Directory.Packages.props").findtext(".//HexalithEventStoreVersion")
    if not version or not all(part.isdecimal() for part in version.split(".")) or len(version.split(".")) != 3:
        raise ValueError("Parity requires a stable public EventStore version")
    sdk_commit = git(sources["Hexalith.EventStore"], "rev-parse", "v" + version + "^{commit}").decode().strip()
    artifacts.mkdir(parents=True, exist_ok=False)
    workspace = artifacts / "workspace"
    workspace.mkdir()
    selected_platform = workspace / "platform-origin"
    # This local clone supplies an owning Git index for the canonical verifier. It neither fetches
    # remote refs nor changes the initialized dependency. Preserve exact working overlays as well.
    subprocess.run(["git", "clone", "--shared", "--no-checkout", str(platform), str(selected_platform)], check=True)
    subprocess.run(["git", "-C", str(selected_platform), "checkout", "--detach", commit], check=True)
    for name in snapshot.origin_files(selected_platform).keys() - original.keys():
        (selected_platform / name).unlink()
    for name, digest in original.items():
        target = selected_platform / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(platform / name, target)
        if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
            raise ValueError("Platform source changed during bootstrap: " + name)
    sdk = workspace / "Hexalith.EventStore"
    sdk.mkdir()
    with tarfile.open(fileobj=io.BytesIO(git(sources["Hexalith.EventStore"], "archive", sdk_commit))) as archive:
        archive.extractall(sdk, filter="data")
    # git archive may apply export/eol attributes. Restore this known transformed file as its exact blob;
    # the canonical verifier independently qualifies every runtime/build input against the public producer.
    (sdk / "global.json").write_bytes(git(sources["Hexalith.EventStore"], "show", sdk_commit + ":global.json"))
    selected_sources = sources | {"Hexalith.EventStore": sdk, "Hexalith.Works": works}
    link_roots(sdk, selected_sources, declared(sdk))
    link_roots(selected_platform, selected_sources, declared(selected_platform))
    (workspace / "works").symlink_to(works, target_is_directory=True)
    execution_root = workspace / "platform"
    binding = artifacts / "platform-source-binding.json"
    snapshot.create(selected_platform, execution_root, binding, upstream_origin=platform)
    record = {
        "status": "bootstrap-validated; parity-incomplete",
        "coordinatorRoot": str(Path(__file__).resolve().parent),
        "coordinatorSha256": coordinators,
        "worksRoot": str(works),
        "platformOrigin": str(platform),
        "platformCommit": commit,
        "platformSourceFiles": original,
        "sdkVersion": version,
        "sdkCommit": sdk_commit,
        "sdkRoot": str(sdk),
        "platformRoot": str(execution_root),
        "platformSourceBinding": str(binding),
        "rootDependencies": {name: str(path) for name, path in sources.items()},
    }
    (artifacts / "bootstrap.json").write_text(json.dumps(record, indent=2) + "\n")
    environment = os.environ.copy() | {
        "HexalithBuildsRoot": str(sources["Hexalith.Builds"]),
        "HexalithEventStoreRoot": str(sdk),
        "HexalithCommonsRoot": str(sources["Hexalith.Commons"]),
        "HexalithPartiesRoot": str(sources["Hexalith.Parties"]),
        "HexalithTenantsRoot": str(sources["Hexalith.Tenants"]),
        "HexalithPolymorphicSerializationsRoot": str(sources["Hexalith.PolymorphicSerializations"]),
        "DOTNET_USE_POLLING_FILE_WATCHER": "1",
    }
    command = [
        "bash",
        str(execution_root / "eng/verify-works-host.sh"),
        "--profile",
        "ci",
        "--works-root",
        str(works),
        "--runtime-sdk-root",
        str(sdk),
        "--platform-source-binding",
        str(binding),
        "--artifacts-path",
        str(artifacts / "gate"),
    ]
    return command, environment, record, snapshot


def toolchain_runtime_sha256(platform):
    """Read the qualified Linux daprd hash from the bootstrapped Platform toolchain owner, not from the ledger."""
    pins = json.loads((Path(platform) / "config/works-proof-toolchain.json").read_text())
    digest = pins.get("linuxAmd64DaprdSha256") if isinstance(pins, dict) else None
    if not isinstance(digest, str) or len(digest) != 64 or any(character not in "0123456789abcdef" for character in digest):
        raise ValueError("Platform Works proof toolchain lacks a qualified Linux runtime hash")
    return digest


def require_qualified_runtime(ledger, pinned_sha256):
    """The executed daprd must be the Platform-pinned runtime for both the live fleet and the retained preflight."""
    qualified = ledger.get("qualifiedToolchain")
    observed = {
        "pin": pinned_sha256,
        "ledgerPin": qualified.get("linuxAmd64DaprdSha256") if isinstance(qualified, dict) else None,
        "qualified": qualified.get("runtimeSha256") if isinstance(qualified, dict) else None,
        "isolated": (ledger.get("isolatedRuntime") or {}).get("runtimeSha256"),
        "retainedPreflight": (ledger.get("retainedPreflightRuntime") or {}).get("runtimeSha256"),
    }
    if any(value != pinned_sha256 for value in observed.values()):
        raise ValueError("Canonical gate did not execute the Platform-qualified Dapr runtime: " + json.dumps(observed, sort_keys=True))


def require_complete_gate(artifacts, pinned_runtime_sha256):
    """Accept only the complete result emitted by the owning canonical verifier."""
    gate = artifacts / "gate"
    ledger = json.loads((gate / "ledger.json").read_text())
    profile = ledger.get("profile", {})
    if (
        profile.get("name") != "ci"
        or profile.get("consumerConfigurations") != ["Release"]
        or profile.get("agentsConfiguration") != "Release"
        or "Debug" in ledger.get("checks", {})
    ):
        raise ValueError("Canonical gate did not use the Release/package CI profile")
    if (
        ledger.get("status") != "passed-disposable-synthetic-parity-and-rollback"
        or ledger.get("rollback", {}).get("complete") is not True
    ):
        raise ValueError("Canonical Platform parity and complete-fleet rollback did not pass")
    rows = ledger.get("rows", {})
    if set(rows) != {"R" + str(index) for index in range(1, 12)}:
        raise ValueError("Canonical gate omitted a required R1–R11 row")
    for name, row in rows.items():
        proofs = row.get("requiredProofs", {})
        if (
            row.get("status") != "passed"
            or set(proofs) != {"persisted", "negative", "restart", "rollback"}
            or any(proof.get("passed") is not True or not proof.get("evidence") for proof in proofs.values())
        ):
            raise ValueError("Canonical gate did not complete every proof for " + name)
    for name, filename in REQUIRED_CHECKS.items():
        check = ledger.get("checks", {}).get(name, {})
        result = gate / filename
        if (
            not result.is_file()
            or check.get("total", 0) <= 0
            or check.get("passed") != check.get("total")
            or check.get("skipped") != 0
        ):
            raise ValueError("Missing, failed, skipped or unexecuted canonical lane: " + name)
        if hashlib.sha256(result.read_bytes()).hexdigest() != check.get("sha256"):
            raise ValueError("Canonical lane result changed: " + name)
    qualification = ledger.get("runtimeProducerQualification", {})
    producers = ledger.get("publishedProducers", {})
    if (
        qualification.get("matchesPublishedProducer") is not True
        or not producers
        or any(
            producer.get("source") != "https://api.nuget.org/v3/index.json"
            or producer.get("repositoryCommit") != qualification.get("repositoryCommit")
            for producer in producers.values()
        )
    ):
        raise ValueError("Canonical public producer qualification is missing")
    require_qualified_runtime(ledger, pinned_runtime_sha256)
    return ledger


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--works-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument(
        "--artifacts-path", type=Path, required=True, help="New directory; failed attempts remain preserved"
    )
    parser.add_argument(
        "--bootstrap-only",
        action="store_true",
        help="Validate isolated inputs and canonical CLI only; never qualifies parity",
    )
    args = parser.parse_args()
    artifacts = args.artifacts_path.resolve()
    command, environment, record, snapshot = bootstrap(args.works_root.resolve(), artifacts)
    if args.bootstrap_only:
        subprocess.run(command[:2] + ["--help"], env=environment, check=True)
        print("Bootstrap validated; parity remains incomplete.")
        return
    subprocess.run(command, env=environment, check=True)
    require_complete_gate(artifacts, toolchain_runtime_sha256(record["platformRoot"]))
    if snapshot.origin_files(Path(record["platformOrigin"])) != record["platformSourceFiles"]:
        raise ValueError("Canonical Platform inputs changed after bootstrap")
    require_unchanged_coordinators(record["coordinatorSha256"])
    record["status"] = "passed-disposable-synthetic-parity-and-rollback"
    (artifacts / "bootstrap.json").write_text(json.dumps(record, indent=2) + "\n")


if __name__ == "__main__":
    main()
