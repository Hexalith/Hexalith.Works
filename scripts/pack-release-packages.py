#!/usr/bin/env python3
"""Pack exactly the NuGet packages owned by the Works release manifest."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "tools" / "release-packages.json"
EXPECTED_PACKAGE_COUNT = 5
PACKAGE_ID_PATTERN = re.compile(r"^Hexalith\.Works(?:\.[A-Za-z0-9]+)+$")
VERSION_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$")


@dataclass(frozen=True)
class ReleasePackage:
    """One normalized package-manifest entry."""

    package_id: str
    project: Path


def load_manifest() -> list[ReleasePackage]:
    """Load and fail closed on an invalid or drifting release inventory."""

    with MANIFEST.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    rows = payload.get("packages") if isinstance(payload, dict) else None
    if not isinstance(rows, list) or len(rows) != EXPECTED_PACKAGE_COUNT:
        raise ValueError(f"Release manifest must contain exactly {EXPECTED_PACKAGE_COUNT} packages.")

    packages: list[ReleasePackage] = []
    ids: set[str] = set()
    projects: set[Path] = set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"id", "project"}:
            raise ValueError("Every manifest entry must contain only string 'id' and 'project' values.")
        package_id = row.get("id")
        project_value = row.get("project")
        if not isinstance(package_id, str) or PACKAGE_ID_PATTERN.fullmatch(package_id) is None:
            raise ValueError(f"Invalid Works package id: {package_id!r}")
        if not isinstance(project_value, str) or "\\" in project_value:
            raise ValueError(f"Invalid normalized project path for {package_id}: {project_value!r}")

        relative_project = Path(project_value)
        if relative_project.is_absolute() or ".." in relative_project.parts or relative_project.suffix != ".csproj":
            raise ValueError(f"Invalid normalized project path for {package_id}: {project_value!r}")
        project = (ROOT / relative_project).resolve()
        if ROOT not in project.parents or not project.is_file():
            raise ValueError(f"Manifest project does not exist inside the repository: {project_value}")
        if package_id.casefold() in ids:
            raise ValueError(f"Duplicate release package id: {package_id}")
        if project in projects:
            raise ValueError(f"Duplicate release package project: {project_value}")

        ids.add(package_id.casefold())
        projects.add(project)
        packages.append(ReleasePackage(package_id, project))

    return packages


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output_directory", type=Path)
    parser.add_argument("version")
    args = parser.parse_args()

    if VERSION_PATTERN.fullmatch(args.version) is None:
        raise ValueError("Package version must be a plain semantic version, optionally with a prerelease suffix.")

    output = args.output_directory.resolve()
    output.mkdir(parents=True, exist_ok=True)
    stale_packages = sorted(output.glob("*.nupkg")) + sorted(output.glob("*.snupkg"))
    if stale_packages:
        raise ValueError(
            "Output directory already contains package archives: "
            + ", ".join(path.name for path in stale_packages)
        )

    packages = load_manifest()
    for package in packages:
        print(f"Packing {package.package_id} from {package.project.relative_to(ROOT)}...", flush=True)
        subprocess.run(
            [
                "dotnet",
                "pack",
                str(package.project),
                "--configuration",
                "Release",
                "--no-build",
                "--no-restore",
                "--output",
                str(output),
                f"-p:Version={args.version}",
                f"-p:PackageVersion={args.version}",
                f"-p:MinVerVersionOverride={args.version}",
                "-p:UseHexalithProjectReferences=false",
                "-p:ContinuousIntegrationBuild=true",
            ],
            cwd=ROOT,
            check=True,
        )

    archives = [path for path in output.glob("*.nupkg") if not path.name.endswith(".symbols.nupkg")]
    if len(archives) != EXPECTED_PACKAGE_COUNT:
        raise ValueError(
            f"Packing produced {len(archives)} package archives; expected {EXPECTED_PACKAGE_COUNT}."
        )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        print(f"Package packing failed with exit code {error.returncode}.", file=sys.stderr)
        raise SystemExit(error.returncode)
    except Exception as error:  # noqa: BLE001 - the CI entry point must report concise failures.
        print(f"Package packing failed: {error}", file=sys.stderr)
        raise SystemExit(1)
