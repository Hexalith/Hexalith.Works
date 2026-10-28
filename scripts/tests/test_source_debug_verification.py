"""Exercise source/package admission and the actual local verification command pipeline."""

from __future__ import annotations

import contextlib
import copy
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("source_debug", ROOT / "scripts/verify-source-debug.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class SourceDebugVerificationTests(unittest.TestCase):
    """The two producers remain independent identities and neither can drift silently."""

    def setUp(self):
        self.manifest = json.loads((ROOT / "tools/source-debug-dependencies.json").read_text())
        self.gitlinks = {key: value["sourceRevision"] for key, value in self.manifest["dependencies"].items()}
        self.checkouts = dict(self.gitlinks)
        self.versions = {value["releaseProperty"]: value["releaseVersion"]
                         for value in self.manifest["dependencies"].values()}

    def validate(self, manifest=None):
        MODULE.validate_pairs(self.manifest if manifest is None else manifest,
                              self.gitlinks, self.checkouts, self.versions)

    def test_reviewed_source_package_pairs_pass_without_implying_equal_producers(self):
        self.validate()
        with self.assertRaises(ValueError):
            self.validate({})

    def test_each_independent_identity_drift_refuses(self):
        for path, pair in self.manifest["dependencies"].items():
            for surface in ("gitlink", "checkout", "version"):
                with self.subTest(path=path, surface=surface):
                    original = (dict(self.gitlinks), dict(self.checkouts), dict(self.versions))
                    if surface == "gitlink":
                        self.gitlinks[path] = "a" * 40
                    elif surface == "checkout":
                        self.checkouts[path] = "b" * 40
                    else:
                        self.versions[pair["releaseProperty"]] = "0.0.0-unreviewed"
                    with self.assertRaisesRegex(ValueError, "drift"):
                        self.validate()
                    self.gitlinks, self.checkouts, self.versions = original

    def test_missing_or_malformed_manifest_entries_refuse(self):
        for field in ("missing", "extra", "revision", "property", "version", "schema"):
            with self.subTest(field=field):
                manifest = copy.deepcopy(self.manifest)
                dependencies = manifest["dependencies"]
                first = next(iter(dependencies))
                if field == "missing":
                    del dependencies[first]
                elif field == "extra":
                    dependencies["references/Unreviewed"] = dependencies[first]
                elif field == "revision":
                    dependencies[first]["sourceRevision"] = "invalid"
                elif field == "property":
                    dependencies[first]["releaseProperty"] = "WrongVersion"
                elif field == "version":
                    dependencies[first]["releaseVersion"] = ""
                else:
                    manifest["schemaVersion"] = 2
                with self.assertRaises(ValueError):
                    self.validate(manifest)

    def test_actual_failed_and_timed_out_commands_retain_nonpassing_evidence(self):
        for command, timeout, expected in (
            ([sys.executable, "-c", "print('failure-control'); raise SystemExit(7)"], 10, 7),
            ([sys.executable, "-c", "import time; print('timeout-control', flush=True); time.sleep(5)"], 0.2, 124),
            ([str(ROOT / "deliberately-absent-executable")], 10, 127),
        ):
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temporary:
                artifacts = Path(temporary)
                records = []
                with self.assertRaises(RuntimeError):
                    MODULE.execute(command, ROOT, dict(os.environ), artifacts, records, timeout)
                self.assertEqual(expected, records[0]["exitCode"])
                self.assertTrue((artifacts / records[0]["log"]).read_text())

    def exercise_pipeline(self, failure=None, uninitialized=False, check_only=False, manifest="default",
                          source_changes=False):
        temporary = tempfile.TemporaryDirectory(prefix="works source debug &,; ")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "tools").mkdir()
        selected_manifest = self.manifest if manifest == "default" else manifest
        (root / "tools/source-debug-dependencies.json").write_text(json.dumps(selected_manifest))
        for path in MODULE.DEPENDENCIES:
            checkout = root / path
            checkout.mkdir(parents=True)
            if not uninitialized:
                (checkout / ".git").write_text("gitdir: fixture\n")
        artifacts = root / "artifacts"
        artifacts.mkdir()
        (artifacts / "result.json").write_text('{"status": "passed", "stale": true}')
        calls = []

        def popen(command, **kwargs):
            self.assertEqual("1", kwargs["env"].get("GIT_NO_REPLACE_OBJECTS"))
            self.assertNotIn("GIT_DIR", kwargs["env"])
            current_report = json.loads((artifacts / "result.json").read_text())
            self.assertEqual("running", current_report["status"])
            self.assertNotIn("stale", current_report)
            calls.append((command, kwargs))
            output = ""
            if command[-2:] == ["rev-parse", "--show-toplevel"]:
                output = command[2] if "-C" in command else str(root)
            elif command[:3] == ["git", "ls-files", "--stage"]:
                path = command[-1]
                output = f"160000 {self.gitlinks[path]} 0\t{path}\n"
            elif command[-2:] == ["rev-parse", "HEAD"]:
                path = Path(command[2]).relative_to(root).as_posix()
                output = self.checkouts[path]
            elif command[:2] == ["dotnet", "msbuild"]:
                output = json.dumps({"Properties": self.versions})
            elif "diff" in command and source_changes and any(call[0][:2] == ["dotnet", "build"] for call in calls):
                output = "changed source bytes"
            failed = command[:2] == ["dotnet", failure]
            kwargs["stdout"].write(output.encode())
            return Mock(wait=Mock(return_value=23 if failed else 0))

        with patch.object(MODULE.subprocess, "Popen", side_effect=popen), contextlib.redirect_stderr(sys.stdout):
            status = MODULE.verify(root, root / "artifacts", check_only=check_only)
        report = json.loads((root / "artifacts/result.json").read_text())
        return status, report, calls, root

    def test_pipeline_compiles_full_solution_in_explicit_source_debug_mode(self):
        status, report, calls, root = self.exercise_pipeline()
        self.assertEqual(0, status)
        self.assertEqual("passed", report["status"])
        dotnet = [command for command, _ in calls if command[0] == "dotnet"]
        self.assertEqual(["msbuild", "restore", "build"], [command[1] for command in dotnet])
        for command in dotnet[1:]:
            self.assertIn(str(root / "Hexalith.Works.slnx"), command)
            for flag in ("UseHexalithProjectReferences=true", "UseNuGetDeps=false",
                         "HexalithEventStoreFromSource=true", "HexalithPolymorphicSerializationsFromSource=true"):
                self.assertIn(f"-p:{flag}", command)
            for path, (_, property_name) in MODULE.DEPENDENCIES.items():
                self.assertIn(f"-p:{property_name}={MODULE.msbuild_escape(root / path)}", command)
        self.assertIn("-p:Configuration=Debug", dotnet[1])
        self.assertEqual("Debug", dotnet[2][dotnet[2].index("--configuration") + 1])
        self.assertIn("--no-restore", dotnet[2])
        self.assertTrue(all(record["timeoutSeconds"] > 0 for record in report["commands"]))

    def test_ci_preflight_uses_release_evaluation_without_restore_or_compile(self):
        status, report, calls, _ = self.exercise_pipeline(check_only=True)
        self.assertEqual(0, status)
        self.assertEqual("passed", report["status"])
        self.assertEqual("preflight", report["mode"])
        self.assertEqual("Release", report["configuration"])
        dotnet = [command for command, _ in calls if command[0] == "dotnet"]
        self.assertEqual(1, len(dotnet))
        self.assertEqual("msbuild", dotnet[0][1])
        self.assertIn("-p:Configuration=Release", dotnet[0])
        self.assertIn("-p:UseHexalithProjectReferences=false", dotnet[0])
        self.assertIn("-p:UseNuGetDeps=true", dotnet[0])

    def test_preflight_and_build_failures_never_report_success(self):
        for failure in ("restore", "build", "msbuild"):
            with self.subTest(failure=failure):
                status, report, calls, _ = self.exercise_pipeline(failure=failure)
                self.assertEqual(1, status)
                self.assertEqual("failed", report["status"])
                self.assertEqual(23, report["commands"][-1]["exitCode"])
                if failure != "build":
                    self.assertFalse(any(command[:2] == ["dotnet", "build"] for command, _ in calls))
        status, report, calls, _ = self.exercise_pipeline(uninitialized=True)
        self.assertEqual(1, status)
        self.assertIn("initialize", report["error"])
        self.assertFalse(any(command[0] == "dotnet" for command, _ in calls))

    def test_malformed_manifest_pipeline_records_failure_without_invoking_tools(self):
        bad = [None, [], {}, {"schemaVersion": 1, "dependencies": None}]
        for field, value in (("sourceRevision", 42), ("releaseVersion", [])):
            manifest = copy.deepcopy(self.manifest)
            next(iter(manifest["dependencies"].values()))[field] = value
            bad.append(manifest)
        manifest = copy.deepcopy(self.manifest)
        manifest["dependencies"][next(iter(manifest["dependencies"]))] = None
        bad.append(manifest)
        for manifest in bad:
            with self.subTest(manifest=manifest):
                status, report, calls, _ = self.exercise_pipeline(manifest=manifest)
                self.assertEqual(1, status)
                self.assertEqual("failed", report["status"])
                self.assertFalse(calls)

    def test_pipeline_identity_mutations_refuse_before_restore_in_both_modes(self):
        for check_only in (True, False):
            for surface in ("gitlink", "checkout", "version"):
                with self.subTest(check_only=check_only, surface=surface):
                    original = (dict(self.gitlinks), dict(self.checkouts), dict(self.versions))
                    path = next(iter(self.gitlinks))
                    if surface == "gitlink":
                        self.gitlinks[path] = "a" * 40
                    elif surface == "checkout":
                        self.checkouts[path] = "b" * 40
                    else:
                        self.versions["HexalithEventStoreVersion"] = "0.0.0-unreviewed"
                    status, report, calls, _ = self.exercise_pipeline(check_only=check_only)
                    self.assertEqual(1, status)
                    self.assertEqual("failed", report["status"])
                    self.assertFalse(any(command[:2] in (["dotnet", "restore"], ["dotnet", "build"])
                                         for command, _ in calls))
                    self.gitlinks, self.checkouts, self.versions = original

    def test_changed_source_content_during_build_invalidates_the_result(self):
        status, report, _, _ = self.exercise_pipeline(source_changes=True)
        self.assertEqual(1, status)
        self.assertEqual("failed", report["status"])
        self.assertIn("source contents changed", report["error"])

    def test_git_environment_cannot_redirect_worktrees_or_enable_replacements(self):
        with patch.dict(os.environ, {"GIT_DIR": "foreign", "GIT_WORK_TREE": "foreign",
                                     "GIT_INDEX_FILE": "foreign", "GIT_CONFIG_COUNT": "1",
                                     "GIT_CONFIG_KEY_0": "core.worktree", "GIT_NO_REPLACE_OBJECTS": "0"}):
            environment = MODULE.inspection_environment()
        self.assertEqual({"GIT_NO_REPLACE_OBJECTS", "GIT_NO_LAZY_FETCH", "GIT_OPTIONAL_LOCKS"},
                         {key for key in environment if key.startswith("GIT_")})
        self.assertEqual("1", environment["GIT_NO_REPLACE_OBJECTS"])

    @unittest.skipUnless(os.name == "posix", "POSIX process group observation")
    def test_timeout_terminates_a_child_before_it_can_write_after_the_deadline(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            marker = root / "child-ran"
            ready = root / "child-ready"
            child = (f"import time,pathlib; pathlib.Path({str(ready)!r}).write_text('ready'); "
                     f"time.sleep(1); pathlib.Path({str(marker)!r}).write_text('alive')")
            parent = f"import subprocess,sys,time; subprocess.Popen([sys.executable,'-c',{child!r}]); time.sleep(5)"
            with self.assertRaises(RuntimeError):
                MODULE.execute([sys.executable, "-c", parent], root, dict(os.environ), root, [], 0.5)
            self.assertTrue(ready.exists(), "Control must execute the child before timeout")
            time.sleep(1.1)
            self.assertFalse(marker.exists())

    def test_actual_ci_gate_ignores_ambient_git_repository_selectors(self):
        with tempfile.TemporaryDirectory() as temporary:
            artifacts = Path(temporary)
            environment = dict(os.environ, GIT_DIR=str(artifacts / "foreign.git"),
                               GIT_WORK_TREE=str(artifacts), GIT_INDEX_FILE=str(artifacts / "foreign.index"),
                               GIT_NO_REPLACE_OBJECTS="0")
            result = subprocess.run([sys.executable, str(ROOT / "scripts/verify-source-debug.py"),
                                     "--check-only", "--artifacts-path", str(artifacts)],
                                    capture_output=True, text=True, timeout=60, env=environment, check=False)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertEqual("passed", json.loads((artifacts / "result.json").read_text())["status"])

    def test_actual_msbuild_property_parser_accepts_reserved_path_characters(self):
        with tempfile.TemporaryDirectory(prefix="works property &,; ") as temporary:
            root = Path(temporary)
            project = root / "fixture.proj"
            project.write_text("<Project />")
            value = str(root / "source %,;$(literal)")
            result = subprocess.run(["dotnet", "msbuild", str(project), "-nologo",
                                     f"-p:ProbeRoot={MODULE.msbuild_escape(value)}", "-getProperty:ProbeRoot"],
                                    capture_output=True, text=True, timeout=30, check=False)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertEqual(value, result.stdout.strip())


if __name__ == "__main__":
    unittest.main()
