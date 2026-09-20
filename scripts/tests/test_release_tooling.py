"""Executable regression tests for the Works release tooling."""

from __future__ import annotations

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


class ShellReleaseTests(unittest.TestCase):
    """Execute collision, transport, and partial-publication shell paths with command stubs."""

    def test_source_proof_rejects_non_main_before_external_queries(self) -> None:
        result, calls = self._run_source_verifier(dispatch_ref="refs/heads/feature/release")

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, calls)
        self.assertIn("refs/heads/main", result.stderr)

    def test_source_proof_rejects_unexpected_repository_before_external_queries(self) -> None:
        result, calls = self._run_source_verifier(repository="someone/Hexalith.Works")

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, calls)
        self.assertIn("unexpected repository", result.stderr)

    def test_source_proof_rejects_stale_main(self) -> None:
        result, calls = self._run_source_verifier(main_sha="b" * 40)

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(1, calls)
        self.assertIn("not the current main tip", result.stderr)

    def test_source_proof_rejects_main_without_exact_successful_push_ci(self) -> None:
        result, calls = self._run_source_verifier(has_successful_ci=False)

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(2, calls)
        self.assertIn("No successful push CI run", result.stderr)

    def test_source_proof_accepts_exact_current_main_with_successful_push_ci(self) -> None:
        result, calls = self._run_source_verifier()

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(2, calls)
        self.assertIn("has a successful push CI run", result.stdout)

    def test_preflight_rejects_stale_dispatch_before_collision_probe(self) -> None:
        result, curl_calls, _ = self._run_preflight(["404"] * 5, main_sha="b" * 40)

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, curl_calls)
        self.assertIn("release source is stale", result.stderr)

    def test_preflight_probes_all_ids_before_rejecting_mixed_collisions(self) -> None:
        result, curl_calls, _ = self._run_preflight(["404", "200", "404", "200", "404"])

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(5, curl_calls)
        self.assertIn("Hexalith.Works.Server", result.stderr)
        self.assertIn("Hexalith.Works.Reactor", result.stderr)

    def test_preflight_reports_transport_failure(self) -> None:
        result, curl_calls, _ = self._run_preflight(["transport-error"])

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(1, curl_calls)
        self.assertIn("could not be queried", result.stderr)

    def test_preflight_rejects_unexpected_repository_before_any_external_query(self) -> None:
        result, curl_calls, github_calls = self._run_preflight(
            ["404"] * 5,
            repository="someone/Hexalith.Works",
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, curl_calls)
        self.assertEqual(0, github_calls)
        self.assertIn("Refusing to publish", result.stderr)

    def test_publisher_rejects_first_attempt_409_as_collision(self) -> None:
        result, commands = self._run_publisher_with_stub(
            """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
echo 'Response status code does not indicate success: 409 (Conflict).' >&2
exit 1
"""
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(1, len(commands))
        self.assertIn("publication collision", result.stderr)

    def test_publisher_rejects_409_after_same_invocation_ambiguous_result(self) -> None:
        result, commands = self._run_publisher_with_stub(
            """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
count="$(wc -l < "$HEXALITH_TEST_COMMAND_LOG")"
if [ "$count" -eq 1 ]; then
  echo '503 Service Unavailable after upload' >&2
  exit 1
fi
if [ "$count" -eq 2 ]; then
  echo 'Response status code does not indicate success: 409 (Conflict).' >&2
  exit 1
fi
echo 'push accepted'
"""
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(2, len(commands))
        self.assertIn("publication collision", result.stderr)
        self.assertNotIn("Published 10", result.stdout)

    def test_publisher_retries_transient_failure_then_succeeds(self) -> None:
        result, commands = self._run_publisher_with_stub(
            """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
count="$(wc -l < "$HEXALITH_TEST_COMMAND_LOG")"
if [ "$count" -eq 1 ]; then
  echo '504 Gateway Timeout' >&2
  exit 1
fi
echo 'push accepted'
"""
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(11, len(commands))
        primary = [command for command in commands if ".nupkg" in command and ".snupkg" not in command]
        symbols = [command for command in commands if ".snupkg" in command]
        self.assertTrue(all("--no-symbols" in command for command in primary))
        self.assertTrue(all("--no-symbols" not in command for command in symbols))

    def test_publisher_rejects_candidate_changed_between_retry_attempts(self) -> None:
        result, commands = self._run_publisher_with_stub(
            """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
printf 'changed-during-retry' >> "$3"
echo '503 Service Unavailable after upload' >&2
exit 1
"""
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(1, len(commands))
        self.assertIn("changed during publication", result.stderr)

    def test_publisher_reports_retry_exhaustion_and_requires_new_patch(self) -> None:
        result, commands = self._run_publisher_with_stub(
            """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
echo '500 Internal Server Error' >&2
exit 1
"""
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(3, len(commands))
        self.assertIn("Retry budget exhausted", result.stderr)
        self.assertIn("new patch version", result.stderr)

    def test_publisher_does_not_retry_an_unclassified_error_containing_500(self) -> None:
        result, commands = self._run_publisher_with_stub(
            """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
echo 'Package policy rejects payloads larger than 500 bytes.' >&2
exit 1
"""
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(1, len(commands))
        self.assertNotIn("Retryable attempt", result.stderr)

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

    def test_publisher_rejects_an_extra_release_archive_before_any_push(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package_directory = self._write_candidate(root)
            (package_directory / f"Unexpected.Package.{VERSION}.nupkg").write_bytes(b"unexpected")
            manifest = root / "release-packages.json"
            write_manifest(manifest)
            command_log = root / "dotnet.log"
            bin_directory = root / "bin"
            bin_directory.mkdir()
            write_executable(
                bin_directory / "dotnet",
                "#!/usr/bin/env bash\nprintf '%s\\n' \"$*\" >> \"$HEXALITH_TEST_COMMAND_LOG\"\nexit 99\n",
            )

            result = self._run_publisher(root, package_directory, manifest, bin_directory, command_log)

            self.assertNotEqual(0, result.returncode)
            self.assertIn("outside the exact manifest inventory", result.stderr)
            self.assertFalse(command_log.exists())

    def test_packer_ledger_is_accepted_by_the_real_publisher_validation_path(self) -> None:
        result, commands = self._run_publisher_with_stub(
            """#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$HEXALITH_TEST_COMMAND_LOG"
echo 'push accepted'
"""
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(10, len(commands))
        self.assertIn("Published 10 checksum-locked artifacts", result.stdout)

    def test_post_publication_matching_tag_verifies_all_packages(self) -> None:
        sha = "a" * 40
        result, calls = self._run_publication_verifier({"v1.2.3-test.1": sha}, ["200"] * 5, sha)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(5, calls)
        for package_id in PACKAGE_IDS:
            self.assertIn(f"Verified {package_id} {VERSION}", result.stdout)

    def test_post_publication_unrelated_tag_is_a_successful_no_op(self) -> None:
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": "b" * 40},
            [],
            "a" * 40,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(0, calls)
        self.assertIn("no publication was warranted", result.stdout)

    def test_post_publication_zero_tags_is_a_successful_no_op(self) -> None:
        result, calls = self._run_publication_verifier({}, [], "a" * 40)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(0, calls)
        self.assertIn("no publication was warranted", result.stdout)

    def test_post_publication_unresolvable_tag_fails_closed(self) -> None:
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": "unresolvable"},
            [],
            "a" * 40,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, calls)
        self.assertIn("could not be resolved", result.stderr)

    def test_post_publication_multiple_matching_tags_fail(self) -> None:
        sha = "a" * 40
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": sha, "v1.2.4": sha},
            [],
            sha,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, calls)
        self.assertIn("found 2", result.stderr)

    def test_post_publication_names_missing_package(self) -> None:
        sha = "a" * 40
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": sha},
            ["200", "404", "200", "200", "200"],
            sha,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(5, calls)
        self.assertIn(f"{PACKAGE_IDS[1]} {VERSION} (HTTP 404)", result.stderr)

    def test_post_publication_reports_transport_error(self) -> None:
        sha = "a" * 40
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": sha},
            ["transport-error", "200", "200", "200", "200"],
            sha,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(5, calls)
        self.assertIn(f"{PACKAGE_IDS[0]} {VERSION} (transport error)", result.stderr)

    def test_post_publication_retries_visibility_and_succeeds_on_second_pass(self) -> None:
        sha = "a" * 40
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": sha},
            ["200", "404", "200", "200", "200", *(["200"] * 5)],
            sha,
            attempt_limit=2,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(10, calls)

    def test_post_publication_rejects_tag_that_moves_after_visibility(self) -> None:
        sha = "a" * 40
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": sha},
            ["200"] * 5,
            sha,
            final_tag_sha="b" * 40,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(5, calls)
        self.assertIn("moved away from the dispatched commit", result.stderr)

    def test_post_publication_rejects_zero_delay_with_unlimited_attempts(self) -> None:
        sha = "a" * 40
        result, calls = self._run_publication_verifier(
            {"v1.2.3-test.1": sha},
            ["200"] * 5,
            sha,
            attempt_limit=0,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual(0, calls)
        self.assertIn("zero retry delay requires a finite attempt limit", result.stderr)

    def _run_publisher_with_stub(
        self,
        dotnet_stub: str,
    ) -> tuple[subprocess.CompletedProcess[str], list[str]]:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        package_directory = self._write_candidate(root)
        manifest = root / "release-packages.json"
        write_manifest(manifest)
        command_log = root / "dotnet.log"
        bin_directory = root / "bin"
        bin_directory.mkdir()
        write_executable(bin_directory / "dotnet", dotnet_stub)

        result = self._run_publisher(root, package_directory, manifest, bin_directory, command_log)
        commands = command_log.read_text(encoding="utf-8").splitlines() if command_log.exists() else []
        return result, commands

    def _run_publication_verifier(
        self,
        tags: dict[str, str],
        statuses: list[str],
        dispatch_sha: str,
        *,
        attempt_limit: int = 1,
        final_tag_sha: str = "",
    ) -> tuple[subprocess.CompletedProcess[str], int]:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        manifest = root / "tools" / "release-packages.json"
        write_manifest(manifest)
        bin_directory = root / "bin"
        bin_directory.mkdir()
        tag_file = root / "tags"
        tag_file.write_text(
            "".join(f"{tag} {sha}\n" for tag, sha in tags.items()),
            encoding="utf-8",
        )
        count_file = root / "curl-count"
        count_file.write_text("0", encoding="utf-8")
        status_file = root / "curl-statuses"
        status_file.write_text("\n".join(statuses) + ("\n" if statuses else ""), encoding="utf-8")
        resolution_count_file = root / "tag-resolution-count"
        resolution_count_file.write_text("0", encoding="utf-8")
        refs = json.dumps([{"ref": f"refs/tags/{tag}"} for tag in tags], separators=(",", ":"))
        write_executable(
            bin_directory / "gh",
            f"""#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" == *matching-refs/tags/v* ]]; then
  printf '%s\n' '{refs}'
  exit 0
fi
tag="${{2##*/}}"
resolution_count="$(cat "$HEXALITH_TEST_TAG_RESOLUTION_COUNT")"
resolution_count=$((resolution_count + 1))
printf '%s\n' "$resolution_count" > "$HEXALITH_TEST_TAG_RESOLUTION_COUNT"
sha="$(awk -v tag="$tag" '$1 == tag {{ print $2 }}' "$HEXALITH_TEST_TAGS")"
if [ "$resolution_count" -gt 1 ] && [ -n "$HEXALITH_TEST_FINAL_TAG_SHA" ]; then
  sha="$HEXALITH_TEST_FINAL_TAG_SHA"
fi
if [ "$sha" = 'unresolvable' ] || [ -z "$sha" ]; then
  exit 1
fi
printf '%s\n' "$sha"
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
            "DISPATCH_SHA": dispatch_sha,
            "GH_TOKEN": "fixture-token",
            "HEXALITH_RELEASE_VERIFY_ATTEMPT_LIMIT": str(attempt_limit),
            "HEXALITH_RELEASE_VERIFY_RETRY_DELAY_SECONDS": "0",
            "HEXALITH_TEST_CURL_COUNT": str(count_file),
            "HEXALITH_TEST_CURL_STATUSES": str(status_file),
            "HEXALITH_TEST_FINAL_TAG_SHA": final_tag_sha,
            "HEXALITH_TEST_TAG_RESOLUTION_COUNT": str(resolution_count_file),
            "HEXALITH_TEST_TAGS": str(tag_file),
            "REPOSITORY": "Hexalith/Hexalith.Works",
        }
        result = subprocess.run(
            ["bash", str(ROOT / "scripts" / "verify-release-publication.sh")],
            cwd=root,
            env=environment,
            text=True,
            capture_output=True,
            check=False,
        )
        return result, int(count_file.read_text(encoding="utf-8"))

    def _run_preflight(
        self,
        statuses: list[str],
        *,
        main_sha: str | None = None,
        repository: str = "Hexalith/Hexalith.Works",
    ) -> tuple[subprocess.CompletedProcess[str], int, int]:
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
        github_count_file = root / "github-count"
        github_count_file.write_text("0", encoding="utf-8")
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
count="$(cat "$HEXALITH_TEST_GITHUB_COUNT")"
printf '%s\n' "$((count + 1))" > "$HEXALITH_TEST_GITHUB_COUNT"
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
            "GITHUB_REPOSITORY": repository,
            "GH_TOKEN": "fixture-token",
            "HEXALITH_BUILDS_EXECUTION_SHA": "04d961759994396132bb2b113ee465b64740a543",
            "HEXALITH_RELEASE_ENVIRONMENT": "production",
            "HEXALITH_RELEASE_EXPECTED_PACKAGE_COUNT": "5",
            "HEXALITH_RELEASE_PACKAGE_MANIFEST": "tools/release-packages.json",
            "HEXALITH_RELEASE_SOURCE_BRANCH": "main",
            "HEXALITH_RELEASE_SOURCE_CI_WORKFLOW": "ci.yml",
            "HEXALITH_TEST_CURL_COUNT": str(count_file),
            "HEXALITH_TEST_CURL_STATUSES": str(status_file),
            "HEXALITH_TEST_GITHUB_COUNT": str(github_count_file),
        }
        result = subprocess.run(
            ["bash", str(ROOT / "scripts" / "validate-publication-preflight.sh"), VERSION, "verify"],
            cwd=root,
            env=environment,
            text=True,
            capture_output=True,
            check=False,
        )
        return (
            result,
            int(count_file.read_text(encoding="utf-8")),
            int(github_count_file.read_text(encoding="utf-8")),
        )

    def _run_source_verifier(
        self,
        *,
        dispatch_ref: str = "refs/heads/main",
        main_sha: str | None = None,
        has_successful_ci: bool = True,
        repository: str = "Hexalith/Hexalith.Works",
    ) -> tuple[subprocess.CompletedProcess[str], int]:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        bin_directory = root / "bin"
        bin_directory.mkdir()
        sha = "a" * 40
        resolved_main_sha = sha if main_sha is None else main_sha
        count_file = root / "github-count"
        count_file.write_text("0", encoding="utf-8")
        workflow_runs = json.dumps(
            {
                "workflow_runs": (
                    [
                        {
                            "head_sha": sha,
                            "head_branch": "main",
                            "event": "push",
                            "status": "completed",
                            "conclusion": "success",
                        }
                    ]
                    if has_successful_ci
                    else []
                )
            },
            separators=(",", ":"),
        )
        write_executable(
            bin_directory / "gh",
            f"""#!/usr/bin/env bash
set -euo pipefail
count="$(cat "$HEXALITH_TEST_GITHUB_COUNT")"
printf '%s\n' "$((count + 1))" > "$HEXALITH_TEST_GITHUB_COUNT"
if [[ "$*" == *git/ref/heads/main* ]]; then
  printf '%s\n' '{resolved_main_sha}'
else
  printf '%s\n' '{workflow_runs}'
fi
""",
        )
        environment = {
            **os.environ,
            "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
            "DISPATCH_REF": dispatch_ref,
            "DISPATCH_SHA": sha,
            "GH_TOKEN": "fixture-token",
            "HEXALITH_TEST_GITHUB_COUNT": str(count_file),
            "REPOSITORY": repository,
        }
        result = subprocess.run(
            ["bash", str(ROOT / "scripts" / "validate-release-source.sh")],
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
        PACK.write_checksum_ledger(package_directory, artifacts)
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
            "HEXALITH_RELEASE_RETRY_DELAY_SECONDS": "0",
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
