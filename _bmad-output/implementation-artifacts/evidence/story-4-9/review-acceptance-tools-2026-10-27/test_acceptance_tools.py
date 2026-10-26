"""Focused offline controls for the successor tools; no network/runtime execution."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
import zipfile

from acceptance_support import ARTIFACTS, WORKS_COMMIT, sha256, verified_members


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parent / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


audit = load("successor_audit", "audit-hosted.py")
download = load("successor_download", "download-artifact.py")
packet = load("successor_packet", "build-packet.py")


class MatrixTests(unittest.TestCase):
    def setUp(self):
        self.matrix = {"status": "old successful claim", "rows": [{
            "scenario": "migration", "facts": ["RequiredMethod"],
            "localExecutedCoverage": {"RequiredMethod": {"type": "TestType", "result": "Pass"}},
            "hostedExecutedCoverage": {"RequiredMethod": {"xml": "old.xml", "result": "Pass"}}}]}

    def test_missing_hosted_method_refuses_and_replaces_stale_pass(self):
        result, errors = audit.matrix_coverage(self.matrix, {}, True)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["rows"][0]["hostedCoverageStatus"], "failed")
        self.assertEqual(result["rows"][0]["hostedExecutedCoverage"]["RequiredMethod"],
                         {"xml": None, "result": "Missing"})
        self.assertIn("TestType.RequiredMethod: Missing", errors[0])
        self.assertEqual(self.matrix["rows"][0]["hostedExecutedCoverage"]["RequiredMethod"]["result"], "Pass")

    def test_failed_hosted_method_is_recorded_as_failed(self):
        result, errors = audit.matrix_coverage(self.matrix, {"TestType.RequiredMethod": [
            {"xml": "recovery.xml", "result": "Fail"}]}, True)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["rows"][0]["hostedExecutedCoverage"]["RequiredMethod"]["result"], "Fail")
        self.assertTrue(errors)

    def test_ambiguous_method_does_not_pass(self):
        result, errors = audit.matrix_coverage(self.matrix, {"TestType.RequiredMethod": [
            {"xml": "one.xml", "result": "Pass"}, {"xml": "two.xml", "result": "Pass"}]}, True)
        self.assertEqual(result["status"], "failed")
        self.assertTrue(errors)

    def test_other_failed_check_prevents_matrix_wide_success(self):
        result, errors = audit.matrix_coverage(self.matrix, {"TestType.RequiredMethod": [
            {"xml": "recovery.xml", "result": "Pass"}]}, False)
        self.assertFalse(errors)
        self.assertEqual(result["status"], "failed")

    def test_complete_coverage_passes_only_after_other_checks_pass(self):
        result, errors = audit.matrix_coverage(self.matrix, {"TestType.RequiredMethod": [
            {"xml": "recovery.xml", "result": "Pass"}]}, True)
        self.assertFalse(errors)
        self.assertEqual(result["status"], "passed")


class EvaluatorTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.marker = self.root / "import-side-effect"
        code = (f"from pathlib import Path\nimport works_toolchain\n"
                f"Path({str(self.marker)!r}).write_text('imported')\n"
                "VALUE = works_toolchain.VALUE\n")
        (self.root / "verify_works_host.py").write_text(code)
        for name in audit.REQUIRED_IMPORTS:
            (self.root / name).write_text("VALUE = 'verified'\n")
        self.ledger = {"verifierHelpers": {name: sha256((self.root / name).read_bytes())
                                          for name in audit.REQUIRED_IMPORTS},
                       "canonicalSources": {"Platform": {"sourceFiles": {
                           "eng/verify_works_host.py": sha256(code.encode())}}}}

    def test_matching_evaluator_and_helpers_execute_private_verified_bytes(self):
        poison = types.ModuleType("works_toolchain")
        poison.VALUE = "cached unverified helper"
        previous = sys.modules.get("works_toolchain")
        sys.modules["works_toolchain"] = poison
        try:
            with audit.verified_evaluator(self.root, self.ledger) as (module, hashes):
                self.assertEqual(module.VALUE, "verified")
                self.assertNotEqual(Path(module.__file__).parent, self.root)
                self.assertIn("verify_works_host.py", hashes)
            self.assertIs(sys.modules["works_toolchain"], poison)
        finally:
            if previous is None:
                sys.modules.pop("works_toolchain", None)
            else:
                sys.modules["works_toolchain"] = previous

    def test_mismatching_evaluator_refused_before_import(self):
        with (self.root / "verify_works_host.py").open("a") as stream:
            stream.write("# changed evaluator\n")
        with self.assertRaisesRegex(ValueError, "digest mismatch before import: verify_works_host.py"):
            with audit.verified_evaluator(self.root, self.ledger):
                self.fail("mismatching evaluator was admitted")
        self.assertFalse(self.marker.exists())

    def test_mismatching_required_helper_refused_before_evaluator_import(self):
        (self.root / "works_toolchain.py").write_text("raise RuntimeError('must not run')\n")
        with self.assertRaisesRegex(ValueError, "digest mismatch before import: works_toolchain.py"):
            with audit.verified_evaluator(self.root, self.ledger):
                self.fail("mismatching helper was admitted")
        self.assertFalse(self.marker.exists())

    def test_missing_recorded_import_is_refused(self):
        del self.ledger["verifierHelpers"]["works_toolchain.py"]
        with self.assertRaisesRegex(ValueError, "omit required"):
            with audit.verified_evaluator(self.root, self.ledger):
                self.fail("unrecorded helper was admitted")
        self.assertFalse(self.marker.exists())


class ZipBindingTests(unittest.TestCase):
    def test_tampered_extraction_is_refused_with_unchanged_original_zip(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "original.zip"
            with zipfile.ZipFile(archive, "w") as stream:
                stream.writestr("gate/ledger.json", '{"status":"failed"}')
            digest = sha256(archive.read_bytes())
            extracted = root / "extracted"
            (extracted / "gate").mkdir(parents=True)
            target = extracted / "gate/ledger.json"
            target.write_text('{"status":"failed"}')
            self.assertEqual(verified_members(archive, digest, extracted)["gate/ledger.json"], target.read_bytes())
            target.write_text('{"status":"passed"}')
            with self.assertRaisesRegex(ValueError, "differs from verified ZIP member"):
                verified_members(archive, digest, extracted)
            self.assertEqual(sha256(archive.read_bytes()), digest)

    def test_missing_or_extra_extracted_member_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "original.zip"
            with zipfile.ZipFile(archive, "w") as stream:
                stream.writestr("proof.json", "native")
            digest = sha256(archive.read_bytes())
            extracted = root / "extracted"
            extracted.mkdir()
            with self.assertRaisesRegex(ValueError, "missing ZIP members"):
                verified_members(archive, digest, extracted)
            (extracted / "proof.json").write_text("native")
            (extracted / "extra.json").write_text("injected")
            with self.assertRaisesRegex(ValueError, "differs from verified ZIP member"):
                verified_members(archive, digest, extracted)


class DownloadDigestTests(unittest.TestCase):
    def test_missing_and_null_api_digest_are_unavailable_not_matched(self):
        for metadata in [{}, {"digest": None}]:
            with self.subTest(metadata=metadata):
                result = download.digest_evidence(metadata, "a" * 64)
                self.assertEqual(result["declaredDigestStatus"], "unavailable")
                self.assertIsNone(result["declaredDigestMatched"])

    def test_supplied_matching_digest_is_matched(self):
        result = download.digest_evidence({"digest": "sha256:" + "a" * 64}, "a" * 64)
        self.assertEqual(result["declaredDigestStatus"], "matched")
        self.assertIs(result["declaredDigestMatched"], True)

    def test_supplied_mismatch_or_empty_digest_is_refused(self):
        for digest in ["sha256:" + "b" * 64, ""]:
            with self.subTest(digest=digest), self.assertRaisesRegex(ValueError, "digest mismatch"):
                download.digest_evidence({"digest": digest}, "a" * 64)


class SummaryClaimTests(unittest.TestCase):
    def setUp(self):
        self.runs = {name: {"id": run_id, "head_sha": WORKS_COMMIT,
                           "status": "completed", "conclusion": "success"}
                     for name, run_id in packet.RUN_IDS.items()}
        self.jobs = [{"name": name, "status": "completed", "conclusion": "success"}
                     for name in packet.REQUIRED_JOBS]
        methods = ["Type.Topology" + str(number) for number in range(7)]
        ci = {"rows": {"R" + str(number): {"requiredProofs": {
            kind: {"passed": True} for kind in ["persisted", "negative", "restart", "rollback"]}}
            for number in range(1, 12)}, "rollback": {"complete": True}}
        self.canonical = {"gate/ledger.json": json.dumps(ci).encode(),
                          "gate/cases.xml": ("<assemblies><assembly>" + "".join(
                              f'<test type="Type" method="Topology{number}" result="Pass" />'
                              for number in range(7)) + "</assembly></assemblies>").encode()}
        self.acceptance = {"status": "passed", "errors": [], "canonicalFacts": 7,
                           "rows": 11, "proofs": 44, "rollbackComplete": True,
                           "consumedLedgerSha256": sha256(self.canonical["gate/ledger.json"]),
                           "verifiedOriginalZips": ARTIFACTS,
                           "extractedBytesBoundDirectlyToOriginalZips": True}
        self.ordinary = {}
        for name, count in packet.EXPECTED_ORDINARY.items():
            exclusions = methods if name == "IntegrationTests" else []
            root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
            results = ET.SubElement(root, "Results")
            for number in range(count):
                ET.SubElement(results, "UnitTestResult", testName=name + str(number), outcome="Passed")
            for method in exclusions:
                ET.SubElement(results, "UnitTestResult", testName=method, outcome="NotRunnable")
            summary = ET.SubElement(root, "ResultSummary")
            ET.SubElement(summary, "Counters", total=str(count + len(exclusions)), executed=str(count),
                          passed=str(count), notRunnable=str(len(exclusions)), failed="0", error="0")
            self.ordinary[f"Hexalith.Works.{name}/Hexalith.Works.{name}.trx"] = ET.tostring(root)
        self.log = ("Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n" + "".join(
            name + " -> output.dll\n" for name in packet.COMPILED_PROJECTS)).encode()

    def validate(self):
        return packet.validate_claims(self.runs, self.jobs, self.ordinary, self.log, self.canonical, self.acceptance)

    def test_supported_summary_derives_success_counts_and_compilation_claims(self):
        claims = self.validate()
        self.assertEqual(claims["ordinary"]["UnitTests"]["passed"], 597)
        self.assertEqual(len(claims["ordinaryExclusionsExecutedCanonically"]), 7)
        self.assertEqual(claims["build"]["compileOutputCounts"]["Hexalith.Platform.Custody"], 1)
        self.assertIn("597 UnitTests", packet.summary_text(claims))

    def test_missing_ancillary_evidence_is_refused(self):
        del self.runs["CodeQL"]
        with self.assertRaisesRegex(ValueError, "Missing or failed.*CodeQL"):
            self.validate()

    def test_failed_ancillary_evidence_is_refused(self):
        self.runs["Commitlint"]["conclusion"] = "failure"
        with self.assertRaisesRegex(ValueError, "Missing or failed.*Commitlint"):
            self.validate()

    def test_ancillary_wrong_revision_is_refused(self):
        self.runs["CodeQL"]["head_sha"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "Missing or failed.*CodeQL"):
            self.validate()

    def test_failed_required_job_is_refused(self):
        self.jobs[0]["conclusion"] = "failure"
        with self.assertRaisesRegex(ValueError, "Required CI job did not succeed"):
            self.validate()

    def test_incorrect_ordinary_counters_are_refused(self):
        name = "Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.trx"
        self.ordinary[name] = self.ordinary[name].replace(b'passed="597"', b'passed="598"')
        with self.assertRaisesRegex(ValueError, "Incorrect ordinary counts.*UnitTests"):
            self.validate()

    def test_missing_actual_case_is_refused_even_with_adjusted_counters(self):
        name = "Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.trx"
        root = ET.fromstring(self.ordinary[name])
        results = list(root)[0]
        results.remove(results[0])
        counters = list(list(root)[1])[0]
        for key in ["total", "executed", "passed"]:
            counters.set(key, "2")
        self.ordinary[name] = ET.tostring(root)
        with self.assertRaisesRegex(ValueError, "Incorrect ordinary counts.*PropertyTests"):
            self.validate()

    def test_duplicate_compilation_or_warning_is_refused(self):
        original = self.log
        for changed in [original + b"Hexalith.Platform.Custody -> duplicate.dll\n",
                        original.replace(b"0 Warning(s)", b"1 Warning(s)"),
                        original + b"MSB3026 collision\n", b""]:
            self.log = changed
            with self.subTest(log=changed), self.assertRaisesRegex(ValueError, "native log support"):
                self.validate()

    def test_incorrect_acceptance_count_is_refused(self):
        self.acceptance["canonicalFacts"] = 459
        with self.assertRaisesRegex(ValueError, "Acceptance counts/rollback disagree"):
            self.validate()

    def test_packet_failure_writes_failed_report_and_never_publishes_success_readme(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            run = root / "native"
            run.mkdir()
            (run / "latest.json").write_text('{"at":"final"}')
            for name in ["CI", "Commitlint"]:
                folder = run / "run-logs" / str(packet.RUN_IDS[name])
                folder.mkdir(parents=True)
                (folder / "run.json").write_text(json.dumps(self.runs[name]))
            output = root / "new-output"
            arguments = ["build-packet.py", "--run", str(run), "--hosted-packet", str(root),
                         "--acceptance", str(root / "not-read.json"), "--output", str(output)]
            with mock.patch.object(sys, "argv", arguments), mock.patch.object(
                    packet, "sealed_native_reader", return_value=lambda path: path.read_bytes()):
                self.assertEqual(packet.main(), 1)
            self.assertEqual(json.loads((output / "summary-claims.json").read_bytes())["status"], "failed")
            self.assertFalse((output / "README.md").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
