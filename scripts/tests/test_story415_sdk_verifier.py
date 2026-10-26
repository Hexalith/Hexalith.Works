"""The current SDK verifier must consume the effective imported Parties package version."""

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class Story415SdkVerifierTests(unittest.TestCase):
    def verify_version_selection(self, version):
        with tempfile.TemporaryDirectory(prefix="works-sdk-verifier-") as directory:
            root = Path(directory)
            scripts = root / "scripts"
            scripts.mkdir()
            script = scripts / "verify-story415-sdk-candidate.sh"
            shutil.copyfile(ROOT / "scripts/verify-story415-sdk-candidate.sh", script)
            shutil.copyfile(ROOT / "global.json", root / "global.json")
            (root / "Directory.Packages.props").write_text(
                '<Project><PropertyGroup><HexalithEventStoreVersion>3.128.0</HexalithEventStoreVersion>'
                '</PropertyGroup><Import Project="catalog.props" /></Project>')
            (root / "catalog.props").write_text(
                f"<Project><PropertyGroup><HexalithPartiesVersion>{version}</HexalithPartiesVersion>"
                "</PropertyGroup></Project>")
            project = root / "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj"
            project.parent.mkdir(parents=True)
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                               '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
            commands = root / "commands"
            commands.mkdir()
            shim = commands / "dotnet"
            shim.write_text(
                '#!/usr/bin/env bash\nset -euo pipefail\n'
                'if [[ "$1" == "msbuild" ]]; then\n'
                '    exec "$SDK_VERIFIER_REAL_DOTNET" "$@"\n'
                'fi\n'
                'if [[ "$1" == "run" && "$2" == "--project" ]]; then\n'
                '    cp "$3" "$SDK_VERIFIER_CAPTURE"\n'
                '    exit 73\n'
                'fi\nexit 74\n')
            shim.chmod(0o755)
            capture = root / "consumer.xml"
            environment = {
                key: value for key, value in os.environ.items()
                if key.casefold() not in {"hexalithpartiesversion", "hexalithversionsloaded"}
            } | {
                "PATH": str(commands) + os.pathsep + os.environ["PATH"],
                "SDK_VERIFIER_REAL_DOTNET": shutil.which("dotnet"),
                "SDK_VERIFIER_CAPTURE": str(capture),
            }
            result = subprocess.run(["bash", str(script)], cwd=root, env=environment,
                                    capture_output=True, text=True, timeout=60, check=False)
            return result, capture.read_text() if capture.exists() else None

    def test_package_only_consumer_uses_the_effective_imported_parties_version(self):
        result, consumer = self.verify_version_selection("9.8.7")
        self.assertEqual(result.returncode, 73, result.stdout + result.stderr)
        self.assertIn('<PackageReference Include="Hexalith.Parties.Client" Version="9.8.7" />', consumer)

    def test_empty_effective_parties_version_refuses_before_consumer_restore(self):
        result, consumer = self.verify_version_selection("")
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("Effective HexalithPartiesVersion is empty", result.stderr)
        self.assertIsNone(consumer)
