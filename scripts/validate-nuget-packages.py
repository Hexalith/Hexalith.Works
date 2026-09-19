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
    dependencies: frozenset[tuple[str, str]]
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


def expected_package_boundaries() -> dict[str, frozenset[tuple[str, str | None]]]:
    """Derive the packed dependency boundary from Release restore evidence."""

    with MANIFEST.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)
    rows = payload.get("packages") if isinstance(payload, dict) else None
    if not isinstance(rows, list) or len(rows) != EXPECTED_PACKAGE_COUNT:
        raise ValueError("Release manifest must contain exactly five packages.")

    boundaries: dict[str, frozenset[tuple[str, str | None]]] = {}
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

        dependencies: set[tuple[str, str | None]] = set()
        for framework in direct_groups:
            direct = direct_groups[framework]
            central = central_groups[framework]
            framework_evidence = project_frameworks[framework]
            declared = framework_evidence.get("dependencies")
            central_versions = framework_evidence.get("centralPackageVersions")
            if (
                not isinstance(direct, list)
                or not isinstance(central, dict)
                or not isinstance(declared, dict)
                or not isinstance(central_versions, dict)
            ):
                raise ValueError(f"Restore dependency group is malformed for {package_id}/{framework}")
            for dependency in direct:
                if not isinstance(dependency, str) or len(dependency.split(maxsplit=1)) != 2:
                    raise ValueError(f"Restore dependency is malformed for {package_id}/{framework}")
                dependency_id = dependency.split(maxsplit=1)[0]
                details = declared.get(dependency_id)
                if isinstance(details, dict) and str(details.get("suppressParent", "")).casefold() == "all":
                    continue
                is_works_dependency = dependency_id.startswith("Hexalith.Works.")
                dependency_version = None if is_works_dependency else central_versions.get(dependency_id)
                if not is_works_dependency and not isinstance(dependency_version, str):
                    raise ValueError(
                        f"Restore evidence has no exact central version for {package_id}/{dependency_id}"
                    )
                dependencies.add((dependency_id, dependency_version))
            for dependency_id in central:
                dependency_version = central_versions.get(dependency_id)
                if not isinstance(dependency_version, str):
                    raise ValueError(
                        f"Restore evidence has no exact central version for {package_id}/{dependency_id}"
                    )
                dependencies.add((dependency_id, dependency_version))

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
            (element.attrib["id"].strip(), element.attrib.get("version", "").strip())
            for element in dependency_elements
            if element.attrib.get("id", "").strip() and element.attrib.get("version", "").strip()
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


def assert_assembly_versions(package_path: Path, package: PackageMetadata) -> None:
    """Require every packaged library assembly to carry the resolved informational version."""

    version_bytes = package.version.encode("utf-8")
    with zipfile.ZipFile(package_path) as archive:
        assembly_names = sorted(
            name for name in archive.namelist() if name.startswith("lib/") and name.endswith(".dll")
        )
        if not assembly_names:
            raise ValueError(f"{package_path.name}: package contains no library assembly")
        for assembly_name in assembly_names:
            assembly = archive.read(assembly_name)
            if version_bytes not in assembly:
                raise ValueError(
                    f"{package.package_id}: {assembly_name} does not carry package version {package.version}; "
                    "compile with the resolved semantic version before packing"
                )


def validate_packages(
    package_directory: Path,
    expected_boundaries: dict[str, frozenset[tuple[str, str | None]]],
) -> list[PackageMetadata]:
    """Validate package inventory, metadata, assemblies, and exact dependency versions."""

    expected_ids = set(expected_boundaries)
    archives = sorted(
        path
        for path in package_directory.glob("*.nupkg")
        if ".symbols." not in path.name and not path.name.endswith(".snupkg")
    )
    if len(archives) != len(expected_ids):
        raise ValueError(f"Expected exactly {len(expected_ids)} NuGet packages, found {len(archives)}.")

    metadata_by_id: dict[str, tuple[Path, PackageMetadata]] = {}
    for archive in archives:
        package = package_metadata(archive)
        if package.package_id in metadata_by_id:
            raise ValueError(f"Duplicate package id in release archives: {package.package_id}")
        metadata_by_id[package.package_id] = (archive, package)

    actual_ids = set(metadata_by_id)
    if actual_ids != expected_ids:
        raise ValueError(
            f"Package inventory mismatch. Missing: {sorted(expected_ids - actual_ids)}; "
            f"unexpected: {sorted(actual_ids - expected_ids)}"
        )
    metadata = [package for _, package in metadata_by_id.values()]
    versions = {package.version for package in metadata}
    if len(versions) != 1:
        raise ValueError(f"All Works packages must share one version; found {sorted(versions)}")

    release_version = next(iter(versions))
    for package_id, (archive, package) in metadata_by_id.items():
        if not package.has_license:
            raise ValueError(f"{package.package_id}: license metadata is missing")
        expected = frozenset(
            (dependency_id, dependency_version or release_version)
            for dependency_id, dependency_version in expected_boundaries[package_id]
        )
        if package.dependencies != expected:
            raise ValueError(
                f"{package.package_id}: dependency id/version mismatch. Expected {sorted(expected)}; "
                f"found {sorted(package.dependencies)}"
            )
        forbidden = sorted(
            dependency_id
            for dependency_id, _ in package.dependencies
            if any(fragment.casefold() in dependency_id.casefold() for fragment in FORBIDDEN_DEPENDENCY_FRAGMENTS)
        )
        if forbidden:
            raise ValueError(f"{package.package_id}: forbidden host/sample/test dependencies: {forbidden}")
        assert_assembly_versions(archive, package)

    return metadata


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package_directory", type=Path)
    args = parser.parse_args()

    metadata = validate_packages(args.package_directory, expected_package_boundaries())

    version = metadata[0].version
    print(f"Validated exactly five Works NuGet packages at version {version}:")
    for package in sorted(metadata, key=lambda item: item.package_id):
        dependencies = ", ".join(
            f"{dependency_id} {dependency_version}"
            for dependency_id, dependency_version in sorted(package.dependencies)
        )
        print(f"- {package.package_id}: {dependencies}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:  # noqa: BLE001 - the CI entry point must report concise failures.
        print(f"Package validation failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
