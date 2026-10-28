"""Executing controls for retained proof input membership and byte checks."""

import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "recheck-platform-proof-inputs.py"
SPEC = importlib.util.spec_from_file_location("proof_inputs", SCRIPT)
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


class PlatformProofInputsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.platform, self.works, self.current = (self.root / name for name in ("platform", "works", "current"))
        for root, files in (
            (
                self.platform,
                [
                    "apphost.cs",
                    "src/Host/Host.cs",
                    "src/Host/Host.csproj",
                    "eng/verify_works_host.py",
                    "eng/helper.py",
                    "DaprComponents/policy.yaml",
                ],
            ),
            (
                self.works,
                [
                    "src/Works/Work.cs",
                    "src/Works/Work.csproj",
                    "Directory.Build.props",
                    "tests/Hexalith.Works.IntegrationTests/Proof.cs",
                    "tests/Hexalith.Works.Testing/Fixture.cs",
                    "xunit.runner.json",
                ],
            ),
        ):
            for name in files:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(name)
        shutil.copytree(self.platform, self.current)
        self.ledger = {
            "platformConsumerSources": gate.inventory(self.platform, "platform"),
            "worksConsumerSource": {"sourceFiles": gate.inventory(self.works, "works")},
            "fixtureSources": gate.inventory(self.works, "fixture"),
            "verifierSha256": gate.digest(self.platform / "eng/verify_works_host.py"),
            "verifierHelpers": {"helper.py": gate.digest(self.platform / "eng/helper.py")},
            "rows": {
                "R10": {
                    "configurationConsumers": [
                        {
                            "path": "DaprComponents/policy.yaml",
                            "sha256": gate.digest(self.platform / "DaprComponents/policy.yaml"),
                        }
                    ]
                }
            },
        }
        self.isolation = {
            "platform": str(self.platform),
            "works": str(self.works),
            "platformBaseCommit": "a" * 40,
            "overlays": {"src/Host/Host.cs": gate.digest(self.platform / "src/Host/Host.cs")},
        }

    def run_gate(self):
        ledger, isolation, output = (self.root / name for name in ("ledger.json", "isolation.json", "output.json"))
        ledger.write_text(json.dumps(self.ledger))
        isolation.write_text(json.dumps(self.isolation))
        result = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                str(ledger),
                str(isolation),
                str(output),
                "--current-platform-root",
                str(self.current),
            ],
            capture_output=True,
            text=True,
            timeout=10,
        )
        return result, json.loads(output.read_text()) if output.exists() else None

    def test_unchanged_inputs_pass_and_generated_outputs_are_excluded(self):
        for root in (self.platform, self.works):
            path = root / "src/Host/obj/Generated.cs"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("generated")
        result, output = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(output["mismatches"], [])

    def test_additions_changes_and_removals_refuse_in_each_complete_inventory(self):
        for owner, relative in (
            (self.platform, "src/Host/Host.cs"),
            (self.works, "src/Works/Work.cs"),
            (self.works, "tests/Hexalith.Works.IntegrationTests/Proof.cs"),
        ):
            original = owner / relative
            contents = original.read_bytes()
            for mutation in ("added", "changed", "missing"):
                with self.subTest(owner=owner, mutation=mutation):
                    target = original.with_name("Added.cs") if mutation == "added" else original
                    if mutation == "missing":
                        target.unlink()
                    else:
                        target.write_text("mutation")
                    result, output = self.run_gate()
                    self.assertEqual(result.returncode, 1, result.stderr)
                    self.assertTrue(any(row["reason"] == mutation for row in output["mismatches"]), output)
                    if mutation == "added":
                        target.unlink()
                    else:
                        original.write_bytes(contents)

    def test_unrelated_current_platform_membership_is_reported_separately(self):
        (self.current / "src/Host/Unrelated.cs").write_text("unqualified")
        result, output = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(output["mismatches"], [])
        self.assertIn({"path": "src/Host/Unrelated.cs", "reason": "added"}, output["currentPlatformDifferences"])

    def test_consumer_sources_under_fixture_only_exclusion_names_are_still_bound(self):
        for root in (self.platform, self.works):
            for folder in ("TestResults", "__pycache__"):
                with self.subTest(root=root, folder=folder):
                    path = root / "src" / folder / "Added.cs"
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text("consumer source")
                    result, output = self.run_gate()
                    self.assertEqual(result.returncode, 1, result.stderr)
                    self.assertTrue(any(row["path"] == f"src/{folder}/Added.cs" for row in output["mismatches"]))
                    path.unlink()

    def test_linked_consumer_directories_refuse_instead_of_hiding_compiled_sources(self):
        external = self.root / "external"
        external.mkdir()
        (external / "Hidden.cs").write_text("compiled through the directory link")
        for owner in (self.platform, self.works):
            with self.subTest(owner=owner):
                link = owner / "src" / "External"
                link.symlink_to(external, target_is_directory=True)
                result, _ = self.run_gate()
                self.assertEqual(result.returncode, 2, result.stderr)
                self.assertIn("symbolic link", result.stderr)
                link.unlink()

    def test_recorded_configuration_overlay_verifier_and_helper_changes_refuse(self):
        for root, relative in (
            (self.platform, "eng/verify_works_host.py"),
            (self.platform, "eng/helper.py"),
            (self.current, "eng/helper.py"),
            (self.platform, "DaprComponents/policy.yaml"),
            (self.current, "DaprComponents/policy.yaml"),
            (self.current, "src/Host/Host.cs"),
        ):
            with self.subTest(root=root, relative=relative):
                target = root / relative
                original = target.read_bytes()
                target.write_text("changed")
                result, output = self.run_gate()
                self.assertEqual(result.returncode, 1, result.stderr)
                self.assertTrue(output["mismatches"])
                target.write_bytes(original)

    def test_refusal_replaces_a_previous_success_result(self):
        result, _ = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.isolation["platform"] = str(self.root / "missing")
        result, output = self.run_gate()
        self.assertEqual(result.returncode, 2, result.stderr)
        self.assertTrue(output["mismatches"])
        self.assertIn("no input comparison qualified", output["scope"])

    def test_success_identifies_exact_inputs_helper_and_selected_roots(self):
        result, output = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr)
        for name, path in (
            ("ledger", self.root / "ledger.json"),
            ("isolation", self.root / "isolation.json"),
            ("helper", SCRIPT),
        ):
            self.assertEqual(output["inputs"][name], {"path": str(path.resolve()), "sha256": gate.digest(path)})
        self.assertEqual(output["roots"]["isolatedPlatform"], str(self.platform.resolve()))
        self.assertEqual(output["roots"]["works"], str(self.works.resolve()))
        self.assertEqual(output["roots"]["currentPlatform"], str(self.current.resolve()))

    def test_malformed_inventory_also_replaces_a_previous_success_result(self):
        result, _ = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.ledger["worksConsumerSource"]["sourceFiles"] = []
        result, output = self.run_gate()
        self.assertEqual(result.returncode, 2, result.stderr)
        self.assertTrue(output["mismatches"])
        self.assertIn("no input comparison qualified", output["scope"])


if __name__ == "__main__":
    unittest.main()
