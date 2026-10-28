"""Keep effective SDK consumer versions and isolated package-proof refusals trustworthy."""

import json
import os
import shutil
import subprocess
import sys
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


class Story415PackageProofTests(unittest.TestCase):
    def verify_package_proof(self, case, optimization, public=False):
        """Execute the production heredoc, isolating only its public archive transport."""
        script = (ROOT / "scripts/verify-story415-sdk-candidate.sh").read_text()
        block = script.split("<<'HASHES'\n", 1)[1].split("\nHASHES\n", 1)[0]
        with tempfile.TemporaryDirectory(prefix="works-package-proof-") as directory:
            fixture = Path(directory)
            feed = fixture / "feed"
            feed.mkdir()
            (fixture / "obj").mkdir()
            downloads = fixture / "public-downloads"
            downloads.mkdir()
            version = "3.128.3" if public else "3.128.3-story415.1"
            libraries = {}
            public_urls = {}
            for suffix in ("Contracts", "Client", "DomainService", "Server", "Testing"):
                name = f"Hexalith.EventStore.{suffix}"
                libraries[f"{name}/{version}"] = {"type": "package"}
                filename = f"{name.lower()}.{version}.nupkg"
                contents = f"selected immutable {name} archive".encode()
                archive = fixture / "packages" / name.lower() / version / filename
                archive.parent.mkdir(parents=True)
                archive.write_bytes(contents)
                (downloads / filename).write_bytes(contents)
                public_urls[f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/{version}/{filename}"] = filename
                if not public:
                    (feed / f"{name}.{version}.nupkg").write_bytes(contents)
            first = f"Hexalith.EventStore.Contracts/{version}"
            if case == "project":
                libraries["Foreign.Project/1.0.0"] = {"type": "project"}
            elif case == "version":
                libraries["Hexalith.EventStore.Contracts/0.0.0"] = libraries.pop(first)
            elif case == "hash":
                archive.write_bytes(b"different restored bytes")
            elif case == "count":
                libraries.pop(first)
            elif case == "existing-public":
                (feed / f"Hexalith.EventStore.Contracts.{version}.nupkg").write_bytes(b"prior archive")
            (fixture / "obj/project.assets.json").write_text(json.dumps({"libraries": libraries}))
            (fixture / "public-urls.json").write_text(json.dumps(public_urls))
            transport = '''import json, pathlib, sys, urllib.request
fixture = pathlib.Path(sys.argv[1])
expected_urls = json.loads((fixture / 'public-urls.json').read_text())
def download(url, timeout):
    if url not in expected_urls or timeout != 30:
        raise RuntimeError('Unexpected public archive request')
    return (fixture / 'public-downloads' / expected_urls[url]).open('rb')
urllib.request.urlopen = download
'''
            return subprocess.run(
                [sys.executable, "-c", transport + block, str(fixture), str(feed), version],
                env=os.environ | {"PYTHONOPTIMIZE": str(optimization)},
                capture_output=True, text=True, timeout=15, check=False,
            )

    def test_valid_candidate_and_public_graphs_keep_complete_hash_evidence_under_optimization(self):
        for optimization in (0, 1, 2):
            for public in (False, True):
                with self.subTest(optimization=optimization, public=public):
                    result = self.verify_package_proof("valid", optimization, public)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                    packages = json.loads(result.stdout)["isolatedPackageProof"]
                    self.assertEqual(len(packages), 5)
                    for package in packages:
                        self.assertEqual(package["sourceSha256"], package["isolatedCacheSha256"])
                        self.assertEqual(package["source"].startswith("https://"), public)

    def test_every_package_proof_refusal_survives_optimization(self):
        cases = {
            "project": "Project reference in package-only proof",
            "version": "Hexalith.EventStore.Contracts/0.0.0",
            "hash": "Restored archive differs from selected source",
            "count": "Incomplete SDK package graph",
            "existing-public": "Public verification archive already exists",
        }
        for optimization in (0, 1, 2):
            for public in (False, True):
                for case, message in cases.items():
                    if case == "existing-public" and not public:
                        continue
                    with self.subTest(optimization=optimization, public=public, case=case):
                        result = self.verify_package_proof(case, optimization, public)
                        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                        self.assertIn(message, result.stderr)
                        self.assertEqual(result.stdout, "", "A refused graph must not emit qualifying proof")
