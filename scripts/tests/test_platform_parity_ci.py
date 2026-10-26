"""Bootstrap and fail-closed acceptance contracts for the Platform-owned parity gate."""

import copy
import hashlib
import importlib.util
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("platform_parity_ci", ROOT / "scripts/verify-platform-parity-ci.py")
PARITY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PARITY)
RUNTIME = "e" * 64


def commit(root):
    subprocess.run(["git", "init", "-q", str(root)], check=True)
    subprocess.run(["git", "-C", str(root), "add", "."], check=True)
    subprocess.run(
        [
            "git",
            "-C",
            str(root),
            "-c",
            "user.name=Parity fixture",
            "-c",
            "user.email=parity@example.invalid",
            "commit",
            "-qm",
            "test: fixture",
        ],
        check=True,
    )


def modules(names):
    return "".join(
        f'[submodule "{name}"]\n path = references/{name}\n url = https://example.invalid/{name}\n' for name in names
    )


def bootstrap_fixture(directory):
    works = Path(directory) / "works"
    works.mkdir()
    (works / ".gitmodules").write_text(modules(PARITY.DEPENDENCIES))
    (works / "Directory.Packages.props").write_text(
        "<Project><PropertyGroup><HexalithEventStoreVersion>3.126.0</HexalithEventStoreVersion></PropertyGroup></Project>"
    )
    for name in PARITY.DEPENDENCIES:
        dependency = works / "references" / name
        dependency.mkdir(parents=True)
        (dependency / ".git").mkdir()
    catalog = works / "references/Hexalith.Builds/Props/Directory.Packages.props"
    catalog.parent.mkdir()
    catalog.write_text(
        "<Project><PropertyGroup><HexalithEventStoreVersion>3.126.0</HexalithEventStoreVersion></PropertyGroup></Project>"
    )
    sdk = works / "references/Hexalith.EventStore"
    (sdk / ".gitmodules").write_text(modules(["Hexalith.Builds", "Hexalith.Commons"]))
    (sdk / "global.json").write_bytes(b'{"sdk":{"version":"10.0.402"}}\r\n')
    commit(sdk)
    subprocess.run(["git", "-C", str(sdk), "tag", "v3.126.0"], check=True)
    platform = works / "references/Hexalith.Platform"
    (platform / ".gitmodules").write_text(
        modules([*PARITY.DEPENDENCIES, "Hexalith.Works", "Not.Root.Declared"])
    )
    (platform / "eng").mkdir()
    shutil.copyfile(
        ROOT / "references/Hexalith.Platform/eng/works_host_snapshot.py",
        platform / "eng/works_host_snapshot.py",
    )
    (platform / "eng/verify-works-host.sh").write_text(
        '#!/usr/bin/env bash\nset -euo pipefail\n[[ "$1" == "--help" ]]\n'
    )
    commit(platform)
    return works, sdk, platform


class PlatformParityBootstrapTests(unittest.TestCase):
    def test_real_snapshot_helper_and_local_git_bootstrap_use_only_root_sources(self):
        with tempfile.TemporaryDirectory() as directory:
            works, sdk, platform = bootstrap_fixture(directory)
            # Preserve an ordinary working overlay without changing any dependency's Git index.
            (platform / "apphost.cs").write_text("// synthetic source overlay\n")
            artifacts = Path(directory) / "attempt"
            command, environment, record, snapshot = PARITY.bootstrap(works, artifacts)
            self.assertEqual(record["coordinatorSha256"], PARITY.coordinator_hashes())
            selected = Path(record["platformRoot"])
            self.assertEqual((selected / "apphost.cs").read_text(), "// synthetic source overlay\n")
            self.assertEqual((selected / "references/Hexalith.Works").resolve(), works)
            self.assertEqual((selected.parent / "works").resolve(), works)
            self.assertEqual(Path(environment["HexalithEventStoreRoot"]).resolve(), Path(record["sdkRoot"]))
            self.assertEqual((Path(record["sdkRoot"]) / "global.json").read_bytes(), (sdk / "global.json").read_bytes())
            self.assertFalse((selected / "references/Not.Root.Declared").exists())
            self.assertNotIn("--prepare-only", command)
            self.assertIn("--runtime-sdk-root", command)
            self.assertEqual(command[command.index("--profile") + 1], "ci")
            self.assertIn("--platform-source-binding", command)
            binding = snapshot.verify(Path(record["platformSourceBinding"]), selected)
            self.assertEqual(binding["upstreamOrigin"]["root"], str(platform))
            subprocess.run(command[:2] + ["--help"], env=environment, check=True)
            self.assertEqual(snapshot.origin_files(platform), record["platformSourceFiles"])
            self.assertFalse((artifacts / "gate").exists())
            with self.assertRaises(FileExistsError):
                PARITY.bootstrap(works, artifacts)

    def test_missing_root_dependency_refuses_before_creating_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".gitmodules").write_text(modules(PARITY.DEPENDENCIES))
            with self.assertRaisesRegex(ValueError, "root-declared dependency"):
                PARITY.bootstrap(root, root / "attempt")
            self.assertFalse((root / "attempt").exists())

    def test_nested_or_parent_dependency_declarations_refuse(self):
        for path in ("references/owner/nested", "references/../owner", "../references/owner"):
            with self.subTest(path=path), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                (root / ".gitmodules").write_text('[submodule "bad"]\n path = ' + path + '\n')
                with self.assertRaisesRegex(ValueError, "directly under"):
                    PARITY.declared(root)

    def test_populated_link_target_is_preserved_and_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "references/Hexalith.Builds"
            target.mkdir(parents=True)
            marker = target / "owned.txt"
            marker.write_text("preserve")
            with self.assertRaises(OSError):
                PARITY.link_roots(root, {"Hexalith.Builds": root / "source"}, {"references/Hexalith.Builds"})
            self.assertEqual(marker.read_text(), "preserve")

    def test_nonstable_version_or_missing_public_tag_refuses_before_creating_evidence(self):
        for mutation in ("nonstable", "missing-tag"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                works, sdk, _ = bootstrap_fixture(directory)
                if mutation == "nonstable":
                    (works / "Directory.Packages.props").write_text(
                        "<Project><PropertyGroup><HexalithEventStoreVersion>3.126.0-preview</HexalithEventStoreVersion></PropertyGroup></Project>")
                    exception = ValueError
                else:
                    subprocess.run(["git", "-C", str(sdk), "tag", "-d", "v3.126.0"], check=True, capture_output=True)
                    exception = subprocess.CalledProcessError
                artifacts = Path(directory) / "attempt"
                with self.assertRaises(exception):
                    PARITY.bootstrap(works, artifacts)
                self.assertFalse(artifacts.exists())

    def test_platform_drift_refuses_final_acceptance_and_preserves_bootstrap(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            artifacts = root / "attempt"
            artifacts.mkdir()
            bootstrap = artifacts / "bootstrap.json"
            bootstrap.write_text('{"status":"bootstrap-validated; parity-incomplete"}')
            original = bootstrap.read_bytes()
            record = {
                "platformOrigin": str(root),
                "platformRoot": str(root),
                "platformSourceFiles": {"source.cs": "original"},
            }
            snapshot = Mock()
            snapshot.origin_files.return_value = {"source.cs": "changed"}
            with (
                patch("sys.argv", ["parity", "--artifacts-path", str(artifacts)]),
                patch.object(PARITY, "bootstrap", return_value=(["canonical-gate"], {}, record, snapshot)),
                patch.object(PARITY.subprocess, "run"),
                patch.object(PARITY, "toolchain_runtime_sha256", return_value=RUNTIME),
                patch.object(PARITY, "require_complete_gate"),
                self.assertRaisesRegex(ValueError, "Platform inputs changed"),
            ):
                PARITY.main()
            self.assertEqual(bootstrap.read_bytes(), original)

    def test_mutated_python_or_shell_coordinator_refuses_final_acceptance(self):
        for filename in PARITY.COORDINATORS:
            with self.subTest(coordinator=filename), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                scripts = root / "scripts"
                scripts.mkdir()
                for name in PARITY.COORDINATORS:
                    (scripts / name).write_text("original coordinator")
                with patch.object(PARITY, "__file__", str(scripts / "verify-platform-parity-ci.py")):
                    expected = PARITY.coordinator_hashes()
                    PARITY.require_unchanged_coordinators(expected)
                    artifacts = root / "attempt"
                    artifacts.mkdir()
                    bootstrap = artifacts / "bootstrap.json"
                    bootstrap.write_text('{"status":"bootstrap-validated; parity-incomplete"}')
                    original = bootstrap.read_bytes()
                    record = {
                        "coordinatorSha256": expected,
                        "platformOrigin": str(root),
                        "platformRoot": str(root),
                        "platformSourceFiles": {"source.cs": "original"},
                    }
                    snapshot = Mock()
                    snapshot.origin_files.return_value = {"source.cs": "original"}

                    def mutate(*_args, path=scripts / filename, **_kwargs):
                        path.write_text("mutated coordinator")

                    with (
                        patch("sys.argv", ["parity", "--artifacts-path", str(root / "attempt")]),
                        patch.object(PARITY, "bootstrap", return_value=(["canonical-gate"], {}, record, snapshot)),
                        patch.object(PARITY.subprocess, "run", side_effect=mutate),
                        patch.object(PARITY, "toolchain_runtime_sha256", return_value=RUNTIME),
                        patch.object(PARITY, "require_complete_gate"),
                        self.assertRaisesRegex(ValueError, "coordinator changed"),
                    ):
                        PARITY.main()
                    self.assertEqual(bootstrap.read_bytes(), original)


class PlatformParityAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.artifacts = Path(self.temporary.name)
        self.gate = self.artifacts / "gate"
        self.gate.mkdir()
        checks = {}
        for name, filename in PARITY.REQUIRED_CHECKS.items():
            result = self.gate / filename
            result.write_text('<assemblies total="1" passed="1" failed="0" skipped="0" />')
            checks[name] = {
                "total": 1,
                "passed": 1,
                "skipped": 0,
                "sha256": hashlib.sha256(result.read_bytes()).hexdigest(),
            }
        self.ledger = {
            "profile": {"name": "ci", "consumerConfigurations": ["Release"], "agentsConfiguration": "Release"},
            "status": "passed-disposable-synthetic-parity-and-rollback",
            "rollback": {"complete": True},
            "checks": checks,
            "rows": {
                "R" + str(index): {
                    "status": "passed",
                    "requiredProofs": {
                        kind: {"passed": True, "evidence": [{"phase": "fixture"}]}
                        for kind in ("persisted", "negative", "restart", "rollback")
                    },
                }
                for index in range(1, 12)
            },
            "runtimeProducerQualification": {"matchesPublishedProducer": True, "repositoryCommit": "fixture"},
            "publishedProducers": {
                "SDK": {"source": "https://api.nuget.org/v3/index.json", "repositoryCommit": "fixture"}
            },
            "qualifiedToolchain": {"linuxAmd64DaprdSha256": RUNTIME, "runtimeSha256": RUNTIME},
            "isolatedRuntime": {"runtimeSha256": RUNTIME},
            "retainedPreflightRuntime": {"runtimeSha256": RUNTIME},
        }

    def require(self, ledger, pinned=RUNTIME):
        (self.gate / "ledger.json").write_text(json.dumps(ledger))
        return PARITY.require_complete_gate(self.artifacts, pinned)

    def test_runtime_other_than_the_platform_pin_refuses(self):
        other = "f" * 64
        mutations = {
            "qualified-runtime": lambda ledger: ledger["qualifiedToolchain"].update(runtimeSha256=other),
            "ledger-pin": lambda ledger: ledger["qualifiedToolchain"].update(linuxAmd64DaprdSha256=other),
            "isolated-runtime": lambda ledger: ledger["isolatedRuntime"].update(runtimeSha256=other),
            "retained-preflight-runtime": lambda ledger: ledger["retainedPreflightRuntime"].update(runtimeSha256=other),
            "missing-qualification": lambda ledger: ledger.pop("qualifiedToolchain"),
            "missing-isolated-runtime": lambda ledger: ledger.pop("isolatedRuntime"),
        }
        for name, mutation in mutations.items():
            with self.subTest(mutation=name):
                ledger = copy.deepcopy(self.ledger)
                mutation(ledger)
                with self.assertRaisesRegex(ValueError, "Platform-qualified Dapr runtime"):
                    self.require(ledger)
        with (
            self.subTest(mutation="platform-source-pin"),
            self.assertRaisesRegex(ValueError, "Platform-qualified Dapr runtime"),
        ):
            self.require(self.ledger, pinned=other)

    def test_platform_toolchain_pin_is_read_from_the_bootstrapped_owner(self):
        with tempfile.TemporaryDirectory() as directory:
            platform = Path(directory)
            (platform / "config").mkdir()
            pin = platform / "config/works-proof-toolchain.json"
            pin.write_text(json.dumps({"qualifiedDaprRuntime": "1.18.7", "linuxAmd64DaprdSha256": RUNTIME}))
            self.assertEqual(PARITY.toolchain_runtime_sha256(platform), RUNTIME)
            for value in ("E" * 64, "e" * 63, None, ["e" * 64]):
                with self.subTest(value=value):
                    pin.write_text(json.dumps({"linuxAmd64DaprdSha256": value}))
                    with self.assertRaisesRegex(ValueError, "qualified Linux runtime hash"):
                        PARITY.toolchain_runtime_sha256(platform)
            pin.write_text("[]")
            with self.assertRaisesRegex(ValueError, "qualified Linux runtime hash"):
                PARITY.toolchain_runtime_sha256(platform)

    def test_accepts_the_complete_canonical_result(self):
        self.assertEqual(self.require(self.ledger), self.ledger)

    def main_fixture(self):
        origin = self.artifacts / "platform-origin"
        selected = self.artifacts / "platform-selected"
        for platform, digest in ((origin, "f" * 64), (selected, RUNTIME)):
            (platform / "config").mkdir(parents=True)
            (platform / "config/works-proof-toolchain.json").write_text(
                json.dumps({"linuxAmd64DaprdSha256": digest}))
        record = {
            "status": "bootstrap-validated; parity-incomplete",
            "coordinatorSha256": PARITY.coordinator_hashes(),
            "platformOrigin": str(origin),
            "platformRoot": str(selected),
            "platformSourceFiles": {"source.cs": "original"},
        }
        snapshot = Mock()
        snapshot.origin_files.return_value = record["platformSourceFiles"]
        bootstrap = self.artifacts / "bootstrap.json"
        bootstrap.write_text(json.dumps(record, indent=2) + "\n")
        return record, snapshot, bootstrap

    def test_main_refuses_incomplete_profile_or_runtime_ledgers_without_stamping_bootstrap(self):
        record, snapshot, bootstrap = self.main_fixture()
        original = bootstrap.read_bytes()
        for mutation, message in (
            (lambda ledger: ledger.update(status="preparation-passed; parity-incomplete"), "rollback did not pass"),
            (lambda ledger: ledger["profile"].update(name="full"), "Release/package CI profile"),
            (lambda ledger: ledger["qualifiedToolchain"].update(runtimeSha256="f" * 64),
             "Platform-qualified Dapr runtime"),
        ):
            with self.subTest(message=message):
                ledger = copy.deepcopy(self.ledger)
                mutation(ledger)
                (self.gate / "ledger.json").write_text(json.dumps(ledger))
                with (
                    patch("sys.argv", ["parity", "--artifacts-path", str(self.artifacts)]),
                    patch.object(PARITY, "bootstrap", return_value=(["canonical-gate"], {}, record, snapshot)),
                    patch.object(PARITY.subprocess, "run"),
                    self.assertRaisesRegex(ValueError, message),
                ):
                    PARITY.main()
                self.assertEqual(bootstrap.read_bytes(), original)

    def test_main_accepts_complete_gate_using_the_bootstrapped_platform_pin(self):
        record, snapshot, bootstrap = self.main_fixture()
        (self.gate / "ledger.json").write_text(json.dumps(self.ledger))
        with (
            patch("sys.argv", ["parity", "--artifacts-path", str(self.artifacts)]),
            patch.object(PARITY, "bootstrap", return_value=(["canonical-gate"], {}, record, snapshot)),
            patch.object(PARITY.subprocess, "run") as run,
        ):
            PARITY.main()
        run.assert_called_once_with(["canonical-gate"], env={}, check=True)
        self.assertEqual(json.loads(bootstrap.read_text())["status"],
                         "passed-disposable-synthetic-parity-and-rollback")
        snapshot.origin_files.assert_called_once_with(Path(record["platformOrigin"]))

    def test_missing_skipped_unexecuted_or_failed_required_lane_refuses(self):
        for lane in PARITY.REQUIRED_CHECKS:
            for mutation in ("missing", "skipped", "unexecuted", "failed"):
                with self.subTest(lane=lane, mutation=mutation):
                    ledger = copy.deepcopy(self.ledger)
                    if mutation == "missing":
                        del ledger["checks"][lane]
                    elif mutation == "skipped":
                        ledger["checks"][lane]["skipped"] = 1
                    elif mutation == "unexecuted":
                        ledger["checks"][lane]["total"] = 0
                    else:
                        ledger["checks"][lane]["passed"] = 0
                    with self.assertRaisesRegex(ValueError, "canonical lane"):
                        self.require(ledger)

    def test_preparation_only_missing_row_proof_rollback_or_public_qualification_refuses(self):
        mutations = [
            lambda ledger: ledger.update(status="preparation-passed; parity-incomplete"),
            lambda ledger: ledger["rows"].pop("R11"),
            lambda ledger: ledger["rows"]["R4"]["requiredProofs"].pop("restart"),
            lambda ledger: ledger["rollback"].update(complete=False),
            lambda ledger: ledger["runtimeProducerQualification"].update(matchesPublishedProducer=False),
            lambda ledger: ledger["publishedProducers"]["SDK"].update(source="local-feed"),
            lambda ledger: ledger["publishedProducers"].clear(),
            lambda ledger: ledger["publishedProducers"]["SDK"].update(repositoryCommit="different"),
        ]
        for mutation in mutations:
            ledger = copy.deepcopy(self.ledger)
            mutation(ledger)
            with self.assertRaises(ValueError):
                self.require(ledger)

    def test_each_failed_row_or_incomplete_proof_refuses(self):
        for row in self.ledger["rows"]:
            for mutation in ("failed", "false-proof", "empty-evidence"):
                with self.subTest(row=row, mutation=mutation):
                    ledger = copy.deepcopy(self.ledger)
                    if mutation == "failed":
                        ledger["rows"][row]["status"] = "failed"
                    elif mutation == "false-proof":
                        ledger["rows"][row]["requiredProofs"]["negative"]["passed"] = False
                    else:
                        ledger["rows"][row]["requiredProofs"]["negative"]["evidence"] = []
                    with self.assertRaisesRegex(ValueError, "every proof"):
                        self.require(ledger)

    def test_missing_or_changed_xml_refuses(self):
        result = self.gate / PARITY.REQUIRED_CHECKS["liveRecovery"]
        result.write_text("changed")
        with self.assertRaisesRegex(ValueError, "result changed"):
            self.require(self.ledger)
        result.unlink()
        with self.assertRaisesRegex(ValueError, "canonical lane"):
            self.require(self.ledger)

    def test_full_or_misreported_ci_profile_cannot_qualify_ci(self):
        for configuration in ("full", "debug-check", "debug-agents", "debug-consumers", "absent"):
            ledger = copy.deepcopy(self.ledger)
            if configuration == "full":
                ledger["profile"]["name"] = "full"
            elif configuration == "debug-check":
                ledger["checks"]["Debug"] = ledger["checks"]["Release"]
            elif configuration == "debug-agents":
                ledger["profile"]["agentsConfiguration"] = "Debug"
            elif configuration == "debug-consumers":
                ledger["profile"]["consumerConfigurations"] = ["Debug", "Release"]
            else:
                del ledger["profile"]
            with self.assertRaisesRegex(ValueError, "Release/package CI profile"):
                self.require(ledger)
