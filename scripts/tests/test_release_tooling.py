"""Executable regression tests for the Works release tooling."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import unittest.mock
import zipfile
from pathlib import Path
from types import ModuleType

ROOT = Path(__file__).resolve().parents[2]
VERSION = "1.2.3-test.1"
PACKAGE_IDS = [
    "Hexalith.Works.Contracts",
    "Hexalith.Works.Server",
    "Hexalith.Works.Projections",
    "Hexalith.Works.Reactor",
    "Hexalith.Works.Testing",
]


def load_script_module(name: str, script: str) -> ModuleType:
    """Load one Python release script without requiring scripts to be a package."""

    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / script)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load {script}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


PACK = load_script_module("pack_release_packages", "pack-release-packages.py")
VALIDATE = load_script_module("validate_nuget_packages", "validate-nuget-packages.py")
CONSUMERS = load_script_module("validate_consumer_package_references", "validate-consumer-package-references.py")


def write_manifest(path: Path, package_ids: list[str] | None = None) -> None:
    """Write a normalized release manifest for package fixture tests."""

    ids = PACKAGE_IDS if package_ids is None else package_ids
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(
            {
                "packages": [
                    {"id": package_id, "project": f"src/{package_id}/{package_id}.csproj"}
                    for package_id in ids
                ]
            }
        ),
        encoding="utf-8",
    )


def write_package(
    directory: Path,
    *,
    package_id: str = "Hexalith.Works.Contracts",
    version: str = VERSION,
    dependencies: tuple[tuple[str, str | None], ...] = (),
    include_license: bool = True,
    license_expression: str = "MIT",
    license_type: str = "expression",
    include_readme: bool = True,
) -> Path:
    """Create the smallest package archive needed by the metadata validator."""

    license_xml = (
        f'<license type="{license_type}">{license_expression}</license>' if include_license else ""
    )
    readme_xml = "<readme>README.md</readme>" if include_readme else ""
    dependency_xml = "".join(
        (
            f'<dependency id="{dependency_id}" version="{dependency_version}" />'
            if dependency_version is not None
            else f'<dependency id="{dependency_id}" />'
        )
        for dependency_id, dependency_version in dependencies
    )
    nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package>
  <metadata>
    <id>{package_id}</id>
    <version>{version}</version>
    {license_xml}
    {readme_xml}
    <dependencies><group targetFramework="net10.0">{dependency_xml}</group></dependencies>
  </metadata>
</package>
"""
    package_path = directory / f"{package_id}.{version}.nupkg"
    with zipfile.ZipFile(package_path, "w") as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)
        if include_readme:
            archive.writestr("README.md", "fixture")
        archive.writestr(f"lib/net10.0/{package_id}.dll", b"fixture")
    return package_path


class ConsumerIsolationTests(unittest.TestCase):
    """The isolated package consumers keep the repository SDK pin but no other repository build policy."""

    def test_isolation_boundaries_carry_only_the_repository_sdk_pin(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            CONSUMERS.write_isolation_boundaries(directory)
            repository = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
            isolated = json.loads((directory / "global.json").read_text(encoding="utf-8"))
            self.assertEqual(isolated, {"sdk": repository["sdk"]})
            self.assertEqual((directory / "Directory.Build.props").read_text(encoding="utf-8"), "<Project />\n")
            self.assertEqual((directory / "Directory.Build.targets").read_text(encoding="utf-8"), "<Project />\n")
            self.assertIn(
                "<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>",
                (directory / "Directory.Packages.props").read_text(encoding="utf-8"),
            )

    def test_isolation_boundaries_fail_closed_without_a_repository_sdk_pin(self) -> None:
        with tempfile.TemporaryDirectory() as repository, tempfile.TemporaryDirectory() as temporary:
            with unittest.mock.patch.object(CONSUMERS, "ROOT", Path(repository)):
                with self.assertRaises(FileNotFoundError):
                    CONSUMERS.write_isolation_boundaries(Path(temporary))
            (Path(repository) / "global.json").write_text('{"test": {}}', encoding="utf-8")
            with unittest.mock.patch.object(CONSUMERS, "ROOT", Path(repository)):
                with self.assertRaises(ValueError):
                    CONSUMERS.write_isolation_boundaries(Path(temporary))

    @unittest.skipUnless(shutil.which("dotnet"), "dotnet is required to resolve SDKs")
    def test_consumer_directory_resolves_the_repository_sdk(self) -> None:
        environment = {**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"}

        def sdk(cwd: Path) -> str:
            result = subprocess.run(["dotnet", "--version"], cwd=cwd, env=environment, check=False,
                                    capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return result.stdout.strip()

        with tempfile.TemporaryDirectory() as unpinned, tempfile.TemporaryDirectory() as temporary:
            pinned = sdk(ROOT)
            if sdk(Path(unpinned)) == pinned:
                self.skipTest(f"An unpinned directory already resolves {pinned}; this host cannot show the difference.")
            directory = Path(temporary)
            CONSUMERS.write_isolation_boundaries(directory)
            consumer = directory / "Hexalith.Works.Contracts"
            consumer.mkdir()
            self.assertEqual(sdk(consumer), pinned)


class PackageValidationTests(unittest.TestCase):
    """Exercise fail-closed manifest and package rejection paths."""

    def test_pack_manifest_rejects_an_empty_inventory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            manifest = Path(temporary) / "release-packages.json"
            write_manifest(manifest, [])
            original = PACK.MANIFEST
            PACK.MANIFEST = manifest
            try:
                with self.assertRaisesRegex(ValueError, "exactly 5"):
                    PACK.load_manifest()
            finally:
                PACK.MANIFEST = original

    def test_package_rejects_missing_readme(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            package = write_package(Path(temporary), include_readme=False)
            with self.assertRaisesRegex(ValueError, "README"):
                VALIDATE.package_metadata(package)

    def test_package_rejects_missing_license(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory, include_license=False)
            boundaries = {"Hexalith.Works.Contracts": frozenset()}
            with self.assertRaisesRegex(ValueError, "license must be the exact expression MIT"):
                VALIDATE.validate_packages(directory, boundaries)

    def test_package_rejects_non_mit_license_expression(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory, license_expression="Apache-2.0")
            boundaries = {"Hexalith.Works.Contracts": frozenset()}
            with self.assertRaisesRegex(ValueError, "license must be the exact expression MIT"):
                VALIDATE.validate_packages(directory, boundaries)

    def test_package_rejects_mit_file_license(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory, license_type="file")
            boundaries = {"Hexalith.Works.Contracts": frozenset()}
            with self.assertRaisesRegex(ValueError, "license must be the exact expression MIT"):
                VALIDATE.validate_packages(directory, boundaries)

    def test_package_rejects_dependency_without_version(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            package = write_package(
                directory,
                dependencies=(("Hexalith.EventStore.Contracts", None),),
            )
            with self.assertRaisesRegex(ValueError, "non-empty id and version"):
                VALIDATE.package_metadata(package)

    def test_package_rejects_dependency_version_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory, dependencies=(("Hexalith.EventStore.Contracts", "9.9.9"),))
            boundaries = {
                "Hexalith.Works.Contracts": frozenset({("Hexalith.EventStore.Contracts", "3.106.0")})
            }
            with self.assertRaisesRegex(ValueError, "dependency id/version mismatch"):
                VALIDATE.validate_packages(directory, boundaries)

    def test_package_rejects_forbidden_dependency(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            dependency = ("Hexalith.Works.AppHost", VERSION)
            write_package(directory, dependencies=(dependency,))
            boundaries = {"Hexalith.Works.Contracts": frozenset({dependency})}
            with self.assertRaisesRegex(ValueError, "forbidden host/sample/test"):
                VALIDATE.validate_packages(directory, boundaries)

    def test_packer_rejects_legacy_symbols_archive_outside_exact_inventory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            packages = [PACK.ReleasePackage(package_id, Path("unused.csproj")) for package_id in PACKAGE_IDS]
            for package_id in PACKAGE_IDS:
                (directory / f"{package_id}.{VERSION}.nupkg").write_bytes(b"package")
                (directory / f"{package_id}.{VERSION}.snupkg").write_bytes(b"symbols")
            (directory / f"{PACKAGE_IDS[0]}.{VERSION}.symbols.nupkg").write_bytes(b"legacy")

            with self.assertRaisesRegex(ValueError, "expected exactly"):
                PACK.validate_output_inventory(directory, packages, VERSION)


    def test_checksum_ledger_records_primary_and_symbols_in_name_order(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            primary = directory / f"{PACKAGE_IDS[0]}.{VERSION}.nupkg"
            symbols = directory / f"{PACKAGE_IDS[0]}.{VERSION}.snupkg"
            primary.write_bytes(b"package")
            symbols.write_bytes(b"symbols")
            ledger = PACK.write_checksum_ledger(directory, [symbols, primary])
            expected = [
                f"{hashlib.sha256(contents).hexdigest()}  {path.name}"
                for path, contents in [(primary, b"package"), (symbols, b"symbols")]
            ]
            self.assertEqual(ledger.read_text(encoding="utf-8").splitlines(), expected)


if __name__ == "__main__":
    unittest.main()
