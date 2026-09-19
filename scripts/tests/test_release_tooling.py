"""Executable regression tests for the Works release tooling."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
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


def write_manifest(path: Path, package_ids: list[str] | None = None) -> None:
    """Write a normalized release manifest for shell-script tests."""

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


def write_executable(path: Path, contents: str) -> None:
    """Write one executable command stub."""

    path.write_text(contents, encoding="utf-8")
    path.chmod(0o755)


def write_package(
    directory: Path,
    *,
    package_id: str = "Hexalith.Works.Contracts",
    version: str = VERSION,
    dependencies: tuple[tuple[str, str], ...] = (),
    include_license: bool = True,
    include_readme: bool = True,
    assembly_version: str | None = None,
) -> Path:
    """Create the smallest package archive needed by the metadata validator."""

    license_xml = '<license type="expression">MIT</license>' if include_license else ""
    readme_xml = "<readme>README.md</readme>" if include_readme else ""
    dependency_xml = "".join(
        f'<dependency id="{dependency_id}" version="{dependency_version}" />'
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
        carried_version = assembly_version if assembly_version is not None else version
        archive.writestr(f"lib/net10.0/{package_id}.dll", b"fixture\0" + carried_version.encode() + b"\0")
    return package_path


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
            with self.assertRaisesRegex(ValueError, "license metadata"):
                VALIDATE.validate_packages(directory, boundaries)

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

    def test_package_rejects_an_assembly_built_at_another_version(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory, assembly_version="0.0.0-preview.0")
            boundaries = {"Hexalith.Works.Contracts": frozenset()}
            with self.assertRaisesRegex(ValueError, "does not carry package version"):
                VALIDATE.validate_packages(directory, boundaries)


class ShellReleaseTests(unittest.TestCase):
    """Execute collision, transport, and partial-publication shell paths with command stubs."""

    def test_preflight_rejects_stale_dispatch_before_collision_probe(self) -> None:
        result, calls = self._run_preflight(["404"] * 5, main_sha="b" * 40)

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, calls)
        self.assertIn("release source is stale", result.stderr)

    def test_preflight_probes_all_ids_before_rejecting_mixed_collisions(self) -> None:
        result, calls = self._run_preflight(["404", "200", "404", "200", "404"])

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(5, calls)
        self.assertIn("Hexalith.Works.Server", result.stderr)
        self.assertIn("Hexalith.Works.Reactor", result.stderr)

    def test_preflight_reports_transport_failure(self) -> None:
        result, calls = self._run_preflight(["transport-error"])

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(1, calls)
        self.assertIn("could not be queried", result.stderr)

    def test_publisher_resumes_unchanged_partial_publication_on_exact_409s(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package_directory = self._write_candidate(root)
            manifest = root / "release-packages.json"
            write_manifest(manifest)
            command_log = root / "dotnet.log"
            bin_directory = root / "bin"
            bin_directory.mkdir()
            write_executable(
                bin_directory / "dotnet",
                """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
case "$3" in
  *Hexalith.Works.Contracts*)
    echo 'Response status code does not indicate success: 409 (Conflict).' >&2
    exit 1
    ;;
esac
echo 'push accepted'
""",
            )

            result = self._run_publisher(root, package_directory, manifest, bin_directory, command_log)

            self.assertEqual(0, result.returncode, result.stderr)
            commands = command_log.read_text(encoding="utf-8").splitlines()
            self.assertEqual(10, len(commands))
            primary = [command for command in commands if ".nupkg" in command and ".snupkg" not in command]
            symbols = [command for command in commands if ".snupkg" in command]
            self.assertTrue(all("--no-symbols" in command for command in primary))
            self.assertTrue(all("--no-symbols" not in command for command in symbols))
            self.assertIn("byte-identically recovered 10 artifacts", result.stdout)

    def test_publisher_rejects_changed_candidate_before_any_push(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package_directory = self._write_candidate(root)
            manifest = root / "release-packages.json"
            write_manifest(manifest)
            changed = package_directory / f"{PACKAGE_IDS[0]}.{VERSION}.nupkg"
            changed.write_bytes(changed.read_bytes() + b"changed")
            command_log = root / "dotnet.log"
            bin_directory = root / "bin"
            bin_directory.mkdir()
            write_executable(
                bin_directory / "dotnet",
                "#!/usr/bin/env bash\nprintf '%s\\n' \"$*\" >> \"$HEXALITH_TEST_COMMAND_LOG\"\nexit 99\n",
            )

            result = self._run_publisher(root, package_directory, manifest, bin_directory, command_log)

            self.assertNotEqual(0, result.returncode)
            self.assertIn("changed after the candidate bytes were frozen", result.stderr)
            self.assertFalse(command_log.exists())

    def test_publisher_rejects_empty_manifest_before_any_push(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package_directory = self._write_candidate(root)
            manifest = root / "release-packages.json"
            write_manifest(manifest, [])
            command_log = root / "dotnet.log"
            bin_directory = root / "bin"
            bin_directory.mkdir()
            write_executable(
                bin_directory / "dotnet",
                "#!/usr/bin/env bash\nprintf '%s\\n' \"$*\" >> \"$HEXALITH_TEST_COMMAND_LOG\"\nexit 99\n",
            )

            result = self._run_publisher(root, package_directory, manifest, bin_directory, command_log)

            self.assertNotEqual(0, result.returncode)
            self.assertIn("manifest is invalid", result.stderr)
            self.assertFalse(command_log.exists())

    def _run_preflight(
        self,
        statuses: list[str],
        *,
        main_sha: str | None = None,
    ) -> tuple[subprocess.CompletedProcess[str], int]:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        manifest = root / "tools" / "release-packages.json"
        write_manifest(manifest)
        bin_directory = root / "bin"
        bin_directory.mkdir()
        sha = "a" * 40
        resolved_main_sha = sha if main_sha is None else main_sha
        count_file = root / "curl-count"
        count_file.write_text("0", encoding="utf-8")
        status_file = root / "curl-statuses"
        status_file.write_text("\n".join(statuses) + "\n", encoding="utf-8")
        workflow_runs = json.dumps(
            {
                "workflow_runs": [
                    {
                        "head_sha": sha,
                        "head_branch": "main",
                        "event": "push",
                        "status": "completed",
                        "conclusion": "success",
                    }
                ]
            },
            separators=(",", ":"),
        )
        write_executable(bin_directory / "git", f"#!/usr/bin/env bash\nprintf '%s\\n' '{sha}'\n")
        write_executable(
            bin_directory / "gh",
            f"""#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" == *git/ref/heads/main* ]]; then
  printf '%s\n' '{resolved_main_sha}'
else
  printf '%s\n' '{workflow_runs}'
fi
""",
        )
        write_executable(
            bin_directory / "curl",
            """#!/usr/bin/env bash
set -euo pipefail
count="$(cat "$HEXALITH_TEST_CURL_COUNT")"
count=$((count + 1))
printf '%s\n' "$count" > "$HEXALITH_TEST_CURL_COUNT"
status="$(sed -n "${count}p" "$HEXALITH_TEST_CURL_STATUSES")"
if [ "$status" = 'transport-error' ]; then
  exit 7
fi
printf '%s' "$status"
""",
        )
        environment = {
            **os.environ,
            "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
            "GITHUB_SHA": sha,
            "GITHUB_REPOSITORY": "Hexalith/Hexalith.Works",
            "GH_TOKEN": "fixture-token",
            "HEXALITH_BUILDS_EXECUTION_SHA": "04d961759994396132bb2b113ee465b64740a543",
            "HEXALITH_RELEASE_ENVIRONMENT": "production",
            "HEXALITH_RELEASE_EXPECTED_PACKAGE_COUNT": "5",
            "HEXALITH_RELEASE_PACKAGE_MANIFEST": "tools/release-packages.json",
            "HEXALITH_RELEASE_SOURCE_BRANCH": "main",
            "HEXALITH_RELEASE_SOURCE_CI_WORKFLOW": "ci.yml",
            "HEXALITH_TEST_CURL_COUNT": str(count_file),
            "HEXALITH_TEST_CURL_STATUSES": str(status_file),
        }
        result = subprocess.run(
            ["bash", str(ROOT / "scripts" / "validate-publication-preflight.sh"), VERSION, "verify"],
            cwd=root,
            env=environment,
            text=True,
            capture_output=True,
            check=False,
        )
        return result, int(count_file.read_text(encoding="utf-8"))

    @staticmethod
    def _write_candidate(root: Path) -> Path:
        package_directory = root / "nupkgs"
        package_directory.mkdir()
        artifacts: list[Path] = []
        for package_id in PACKAGE_IDS:
            for suffix in ("nupkg", "snupkg"):
                artifact = package_directory / f"{package_id}.{VERSION}.{suffix}"
                artifact.write_bytes(f"{package_id}-{suffix}".encode())
                artifacts.append(artifact)
        (package_directory / "release-artifacts.sha256").write_text(
            "".join(
                f"{hashlib.sha256(artifact.read_bytes()).hexdigest()}  {artifact.name}\n"
                for artifact in sorted(artifacts)
            ),
            encoding="utf-8",
        )
        return package_directory

    @staticmethod
    def _run_publisher(
        root: Path,
        package_directory: Path,
        manifest: Path,
        bin_directory: Path,
        command_log: Path,
    ) -> subprocess.CompletedProcess[str]:
        environment = {
            **os.environ,
            "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
            "HEXALITH_RELEASE_PACKAGE_MANIFEST": str(manifest),
            "HEXALITH_TEST_COMMAND_LOG": str(command_log),
            "NUGET_API_KEY": "fixture-key",
        }
        return subprocess.run(
            ["bash", str(ROOT / "scripts" / "push-release-packages.sh"), VERSION, str(package_directory)],
            cwd=root,
            env=environment,
            text=True,
            capture_output=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
