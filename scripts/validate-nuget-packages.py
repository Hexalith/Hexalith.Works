#!/usr/bin/env python3
"""Validate the exact Works NuGet inventory and its dependency boundaries."""

from __future__ import annotations

import argparse
import json
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "tools" / "release-packages.json"
EXPECTED_PACKAGE_COUNT = 5
FORBIDDEN_DEPENDENCY_FRAGMENTS = (
    ".AppHost",
    ".IntegrationTests",
    ".PropertyTests",
    ".UnitTests",
    ".ArchitectureTests",
    ".Sample",
    ".ServiceDefaults",
)


@dataclass(frozen=True)
class PackageMetadata:
    """The release-relevant metadata read from one package archive."""

    package_id: str
    version: str
    dependencies: frozenset[str]
    readme: str | None
    has_license: bool


def assert_release_restore_graph(package_id: str, restore: dict) -> None:
    """Fail closed unless obj/project.assets.json holds the Release (package-mode) dependency graph.

    project.assets.json is configuration-dependent and is overwritten in place by any later Debug
    restore. Comparing Release nuspecs against a Debug graph would silently accept the wrong
    dependency boundary, so the distinguishing property is checked explicitly: in package mode no
    external Hexalith module is consumed as a ProjectReference.
    """

    source_edges: set[str] = set()
    frameworks = restore.get("frameworks")
    if not isinstance(frameworks, dict) or not frameworks:
        raise ValueError(f"Restore evidence has no framework section for {package_id}")

    for framework in frameworks.values():
        if not isinstance(framework, dict):
            raise ValueError(f"Restore framework evidence is malformed for {package_id}")
        references = framework.get("projectReferences")
        if references is None:
            continue
        if not isinstance(references, dict):
            raise ValueError(f"Restore project-reference evidence is malformed for {package_id}")
        for reference in references:
            name = Path(str(reference)).stem
            if name.startswith("Hexalith.") and not name.startswith("Hexalith.Works."):
                source_edges.add(name)

    if source_edges:
        raise ValueError(
            f"{package_id}: obj/project.assets.json is a Debug (source-mode) graph, not the Release "
            f"package-mode graph — it consumes {sorted(source_edges)} as project references. "
            "Re-run `dotnet restore Hexalith.Works.slnx` (Release is the default configuration) before "
            "validating packages; a Debug restore overwrites this evidence in place."
        )


def expected_package_boundaries() -> dict[str, frozenset[str]]:
    """Derive the packed dependency boundary from Release restore evidence."""

    with MANIFEST.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)
    rows = payload.get("packages") if isinstance(payload, dict) else None
    if not isinstance(rows, list) or len(rows) != EXPECTED_PACKAGE_COUNT:
        raise ValueError("Release manifest must contain exactly five packages.")

    boundaries: dict[str, frozenset[str]] = {}
    projects: set[Path] = set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"id", "project"}:
            raise ValueError("Every manifest entry must contain only 'id' and 'project'.")
        package_id = row.get("id")
        project_value = row.get("project")
        if not isinstance(package_id, str) or not package_id.startswith("Hexalith.Works."):
            raise ValueError(f"Invalid Works package id: {package_id!r}")
        if not isinstance(project_value, str):
            raise ValueError(f"Invalid project path for {package_id}: {project_value!r}")
        project = (ROOT / project_value).resolve()
        if ROOT not in project.parents or project.suffix != ".csproj" or not project.is_file():
            raise ValueError(f"Manifest project is invalid: {project_value}")
        if package_id.casefold() in {value.casefold() for value in boundaries} or project in projects:
            raise ValueError(f"Duplicate package id or project in release manifest: {package_id}")

        assets_path = project.parent / "obj" / "project.assets.json"
        if not assets_path.is_file():
            raise ValueError(f"Release restore evidence is missing for {package_id}: {assets_path}")
        with assets_path.open("r", encoding="utf-8") as handle:
            assets = json.load(handle)
        restore = assets.get("project", {}).get("restore", {})
        if Path(str(restore.get("projectPath", ""))).resolve() != project:
            raise ValueError(f"Restore evidence does not belong to {project_value}")
        if str(restore.get("projectName", "")).casefold() != package_id.casefold():
            raise ValueError(f"Restore evidence identifies the wrong package for {package_id}")
        assert_release_restore_graph(package_id, restore)

        direct_groups = assets.get("projectFileDependencyGroups")
        central_groups = assets.get("centralTransitiveDependencyGroups")
        project_frameworks = assets.get("project", {}).get("frameworks")
        if (
            not isinstance(direct_groups, dict)
            or not isinstance(central_groups, dict)
            or not isinstance(project_frameworks, dict)
        ):
            raise ValueError(f"Restore evidence has no usable dependency groups for {package_id}")
        if (
            not direct_groups
            or set(direct_groups) != set(central_groups)
            or set(direct_groups) != set(project_frameworks)
        ):
            raise ValueError(f"Restore dependency groups are inconsistent for {package_id}")

        dependencies: set[str] = set()
        for framework in direct_groups:
            direct = direct_groups[framework]
            central = central_groups[framework]
            declared = project_frameworks[framework].get("dependencies")
            if not isinstance(direct, list) or not isinstance(central, dict) or not isinstance(declared, dict):
                raise ValueError(f"Restore dependency group is malformed for {package_id}/{framework}")
            for dependency in direct:
                if not isinstance(dependency, str) or len(dependency.split(maxsplit=1)) != 2:
                    raise ValueError(f"Restore dependency is malformed for {package_id}/{framework}")
                dependency_id = dependency.split(maxsplit=1)[0]
                details = declared.get(dependency_id)
                if isinstance(details, dict) and str(details.get("suppressParent", "")).casefold() == "all":
                    continue
                dependencies.add(dependency_id)
            dependencies.update(central)

        boundaries[package_id] = frozenset(dependencies)
        projects.add(project)

    return boundaries


def package_metadata(package_path: Path) -> PackageMetadata:
    with zipfile.ZipFile(package_path) as package:
        nuspec_names = [name for name in package.namelist() if name.endswith(".nuspec")]
        if len(nuspec_names) != 1:
            raise ValueError(f"{package_path.name}: expected exactly one .nuspec file")
        root = ElementTree.fromstring(package.read(nuspec_names[0]))
        namespace = {"n": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {}

        def find_text(name: str) -> str | None:
            element = root.find(f".//n:metadata/n:{name}", namespace) if namespace else root.find(f".//metadata/{name}")
            return element.text.strip() if element is not None and element.text else None

        dependency_path = ".//n:metadata/n:dependencies//n:dependency"
        dependency_elements = (
            root.findall(dependency_path, namespace)
            if namespace
            else root.findall(dependency_path.replace("n:", ""))
        )
        dependencies = frozenset(
            element.attrib["id"].strip()
            for element in dependency_elements
            if element.attrib.get("id", "").strip()
        )
        package_id = find_text("id")
        version = find_text("version")
        if not package_id or not version:
            raise ValueError(f"{package_path.name}: missing package id or version")
        readme = find_text("readme")
        if not readme or readme not in package.namelist():
            raise ValueError(f"{package_path.name}: declared README is missing from the archive")
        return PackageMetadata(
            package_id,
            version,
            dependencies,
            readme,
            bool(find_text("license") or find_text("licenseFile")),
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package_directory", type=Path)
    args = parser.parse_args()

    expected_boundaries = expected_package_boundaries()
    expected_ids = set(expected_boundaries)
    archives = sorted(
        path
        for path in args.package_directory.glob("*.nupkg")
        if ".symbols." not in path.name and not path.name.endswith(".snupkg")
    )
    if len(archives) != len(expected_ids):
        raise ValueError(f"Expected exactly five NuGet packages, found {len(archives)}.")

    metadata = [package_metadata(path) for path in archives]
    actual_ids = {package.package_id for package in metadata}
    if actual_ids != expected_ids:
        raise ValueError(
            f"Package inventory mismatch. Missing: {sorted(expected_ids - actual_ids)}; "
            f"unexpected: {sorted(actual_ids - expected_ids)}"
        )
    versions = {package.version for package in metadata}
    if len(versions) != 1:
        raise ValueError(f"All Works packages must share one version; found {sorted(versions)}")

    for package in metadata:
        if not package.has_license:
            raise ValueError(f"{package.package_id}: license metadata is missing")
        expected = expected_boundaries[package.package_id]
        if set(package.dependencies) != expected:
            raise ValueError(
                f"{package.package_id}: dependency mismatch. Expected {sorted(expected)}; "
                f"found {sorted(package.dependencies)}"
            )
        forbidden = sorted(
            dependency
            for dependency in package.dependencies
            if any(fragment.casefold() in dependency.casefold() for fragment in FORBIDDEN_DEPENDENCY_FRAGMENTS)
        )
        if forbidden:
            raise ValueError(f"{package.package_id}: forbidden host/sample/test dependencies: {forbidden}")

    version = next(iter(versions))
    print(f"Validated exactly five Works NuGet packages at version {version}:")
    for package in sorted(metadata, key=lambda item: item.package_id):
        print(f"- {package.package_id}: {', '.join(sorted(package.dependencies))}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:  # noqa: BLE001 - the CI entry point must report concise failures.
        print(f"Package validation failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
