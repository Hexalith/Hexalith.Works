#!/usr/bin/env python3
"""Replay the public package consumers and the source-based reminder regression fixture."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


PACKET = Path(__file__).resolve().parent


def verify_bytes(data: bytes, expected: str, subject: str) -> None:
    actual = hashlib.sha256(data).hexdigest()
    if actual != expected:
        raise ValueError(f"SHA-256 mismatch for {subject}: {actual} != {expected}")


def download_packages(inventory: dict, output: Path, ledger: list[dict]) -> None:
    output.mkdir()
    for package in inventory["packages"]:
        with urllib.request.urlopen(package["url"], timeout=45) as response:
            data = response.read()
        verify_bytes(data, package["archive_sha256"], package["id"])
        archive = output / f"{package['id']}.{package['version']}.nupkg"
        archive.write_bytes(data)
        with zipfile.ZipFile(archive) as contents:
            nuspec = ET.fromstring(contents.read(next(
                name for name in contents.namelist() if name.endswith(".nuspec"))))
            repository = nuspec.find("{*}metadata/{*}repository")
            if repository is None or repository.get("commit") != package["repository"]["commit"]:
                raise ValueError(f"Source commit mismatch for {package['id']}")
        ledger.append({"operation": "download and verify", "url": package["url"],
                       "archive": str(archive), "sha256": package["archive_sha256"],
                       "source_commit": package["repository"]["commit"]})


def prepare_host(repository: Path, packages: Path, output: Path, binding: dict) -> dict:
    source = repository / "tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0"
    shutil.copytree(source, output)
    deps = output / "Hexalith.EventStore.DomainService.Tests.deps.json"
    verify_bytes(deps.read_bytes(), binding["deps_sha256"], "source fixture dependency manifest")
    libraries = json.loads(deps.read_bytes())["libraries"]
    if libraries.get("Hexalith.Commons.UniqueIds/1.0.0", {}).get("type") != "project":
        raise ValueError("The recorded fixture requires source-built Hexalith.Commons.UniqueIds/1.0.0")
    installed = []
    for assembly in binding["assemblies"]:
        archive = packages / f"{assembly['package']}.{binding['public_version']}.nupkg"
        verify_bytes(archive.read_bytes(), assembly["archive_sha256"], archive.name)
        with zipfile.ZipFile(archive) as contents:
            data = contents.read(assembly["archive_entry"])
        verify_bytes(data, assembly["public_assembly_sha256"], assembly["assembly"])
        target = output / assembly["assembly"]
        if not target.exists():
            raise ValueError(f"Source fixture lacks {target.name}")
        target.write_bytes(data)
        target.with_suffix(".pdb").unlink(missing_ok=True)
        installed.append({"assembly": target.name, "sha256": hashlib.sha256(data).hexdigest()})
    verify_bytes(deps.read_bytes(), binding["deps_sha256"], "prepared fixture dependency manifest")
    return {"operation": "prepare source-based regression host", "source": str(source),
            "host": str(output), "deps_sha256": binding["deps_sha256"],
            "retained_source_dependency": "Hexalith.Commons.UniqueIds/1.0.0", "assemblies": installed}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--eventstore-root", type=Path, required=True,
                        help="EventStore checkout at the source revision recorded in public-source-audit.json")
    parser.add_argument("--output", type=Path,
                        help="New directory for downloads, prepared host, logs and XML; defaults to a fresh temporary directory")
    args = parser.parse_args()
    repository = args.eventstore_root.resolve()
    audit = json.loads((PACKET / "public-source-audit.json").read_text())
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repository, text=True).strip()
    if source != audit["checkout_commit"]:
        raise ValueError(f"Use recorded fixture source {audit['checkout_commit']}; found {source}")
    output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="story-4-11-replay-"))
    if args.output:
        output.mkdir(exist_ok=True)
        if any(output.iterdir()):
            raise ValueError("Replay output directory must be empty")
    print(f"Replay output: {output}", flush=True)
    ledger: list[dict] = []

    def run(name: str, command: list[str], *, environment: dict | None = None, timeout: int = 180) -> None:
        with (output / f"{name}.log").open("w") as log:
            result = subprocess.run(command, cwd=repository, env={**os.environ, **(environment or {})},
                                    stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
        ledger.append({"operation": "command", "name": name, "command": command,
                       "cwd": str(repository), "environment": environment or {},
                       "timeout_seconds": timeout, "exit_code": result.returncode})
        (output / "replay-commands.json").write_text(json.dumps(ledger, indent=2) + "\n")
        if result.returncode:
            raise RuntimeError(f"{name} failed; inspect {output / (name + '.log')}")

    inventory = json.loads((PACKET / "public-packages.json").read_text())
    binding = json.loads((PACKET / "public-runtime-bindings.json").read_text())
    packages = output / "packages"
    download_packages(inventory, packages, ledger)
    for project in ["Hexalith.EventStore.Contracts.Tests", "Hexalith.EventStore.DomainService.Tests"]:
        run("build-" + project, ["dotnet", "build", f"tests/{project}/{project}.csproj", "-c", "Debug",
                                "-m:1", "-p:UseHexalithProjectReferences=true", "-p:NuGetAudit=false",
                                "-p:MinVerVersionOverride=1.0.0"])
    run("package-validation", ["python3", "tools/validate-release-packages.py", str(packages),
                               inventory["version"]])
    contracts = repository / "tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests"
    run("package-consumers", [str(contracts), "-method", "*PackagedReminderApiRunsWithoutWorksTypes",
                              "-result-xml", str(output / "package-consumers.xml")],
        environment={"EVENTSTORE_PACKAGE_CONTRACT_DIR": str(packages)}, timeout=240)
    host = output / "source-regression-host"
    ledger.append(prepare_host(repository, packages, host, binding))
    run("reminder-regressions", [str(host / "Hexalith.EventStore.DomainService.Tests"),
                                 "-class", "*Reminder*", "-result-xml", str(output / "reminder-regressions.xml")])
    for assembly in binding["assemblies"]:
        verify_bytes((host / assembly["assembly"]).read_bytes(), assembly["public_assembly_sha256"],
                     assembly["assembly"])
    print("Public package-only consumers and the source-based reminder regression fixture passed.")


if __name__ == "__main__":
    main()
