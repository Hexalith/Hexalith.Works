"""Focused checks for the replay recipe's proof gates."""

from __future__ import annotations

import hashlib
import io
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch


REPLAY = runpy.run_path(str(Path(__file__).with_name("replay-proof.py")))


class ReplayProofTests(unittest.TestCase):
    def test_result_xml_requires_recorded_non_skipped_passes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            result = Path(directory) / "results.xml"
            for total, passed, skipped in ((0, 0, 0), (1, 0, 1)):
                with self.subTest(total=total, skipped=skipped):
                    result.write_text(
                        f'<assemblies><assembly total="{total}" passed="{passed}" '
                        f'failed="0" skipped="{skipped}" errors="0" /></assemblies>')
                    with self.assertRaises(ValueError):
                        REPLAY["verify_result_xml"](result, 1)
            result.write_text(
                '<assemblies><assembly total="1" passed="1" failed="0" '
                'skipped="0" errors="0" /></assemblies>')
            REPLAY["verify_result_xml"](result, 1)

    def test_download_is_bounded_by_recorded_archive_length(self) -> None:
        class Response(io.BytesIO):
            read_limit: int | None = None

            def read(self, size: int = -1) -> bytes:
                self.read_limit = size
                return super().read(size)

        response = Response(b"five!")
        inventory = {"packages": [{"id": "example", "url": "https://example.test/archive",
                                   "archive_bytes": 4}]}
        with tempfile.TemporaryDirectory() as directory:
            with patch("urllib.request.urlopen", return_value=response):
                with self.assertRaisesRegex(ValueError, "Archive length mismatch"):
                    REPLAY["download_packages"](inventory, Path(directory) / "packages", [])
        self.assertEqual(response.read_limit, 5)

    def test_copied_fixture_checks_test_and_retained_dependency_hashes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            source = repository / "tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0"
            source.mkdir(parents=True)
            (source / "Hexalith.EventStore.DomainService.Tests.deps.json").write_text(
                '{"libraries":{"Hexalith.Commons.UniqueIds/1.0.0":{"type":"project"}}}')
            test_assembly = source / "Hexalith.EventStore.DomainService.Tests.dll"
            dependency = source / "Hexalith.Commons.UniqueIds.dll"
            test_assembly.write_bytes(b"recorded test")
            dependency.write_bytes(b"recorded dependency")

            def digest(path: Path) -> str:
                return hashlib.sha256(path.read_bytes()).hexdigest()

            binding = {
                "deps_sha256": digest(source / "Hexalith.EventStore.DomainService.Tests.deps.json"),
                "test_assembly_sha256": digest(test_assembly),
                "fixture_graph": {"retained_source_dependencies": [{
                    "assembly": dependency.name, "assembly_sha256": digest(dependency)}]},
                "assemblies": [],
            }
            REPLAY["prepare_host"](repository, repository, repository / "valid", binding)
            for altered in (test_assembly, dependency):
                with self.subTest(assembly=altered.name):
                    original = altered.read_bytes()
                    altered.write_bytes(b"different bytes")
                    with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                        REPLAY["prepare_host"](
                            repository, repository, repository / (altered.stem + "-invalid"), binding)
                    altered.write_bytes(original)


if __name__ == "__main__":
    unittest.main()
