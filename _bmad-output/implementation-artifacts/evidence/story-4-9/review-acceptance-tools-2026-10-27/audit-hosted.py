"""Corrected offline successor of the sealed parent hosted acceptance audit."""
import argparse
from contextlib import contextmanager
import copy
import gzip
import importlib
import json
from pathlib import Path, PurePosixPath
import sys
import tempfile
import xml.etree.ElementTree as ET

from acceptance_support import ARTIFACTS, HOSTED_MANIFEST_SHA256, sha256, verified_members, write_json_new


REQUIRED_IMPORTS = {"isolated_daprd.py", "retained_works_host.py", "works_host_snapshot.py",
                    "works_toolchain.py", "works_verifier_command.py", "verify_works_producer.py"}
EXPECTED_XML = {"platform-release.xml": 144, "works-consumer-supporting.xml": 306,
                "retained-preflight.xml": 1, "platform-recovery.xml": 4,
                "platform-historical.xml": 1, "platform-ha.xml": 1,
                "platform-mtls.xml": 1, "platform-projection.xml": 1}


@contextmanager
def verified_evaluator(root, ledger):
    """Check the actual evaluator and all recorded helpers before importing a private copy."""
    root = Path(root)
    recorded = ledger["verifierHelpers"]
    if not REQUIRED_IMPORTS.issubset(recorded):
        raise ValueError("Recorded verifier helpers omit required evaluator imports")
    expected = dict(recorded)
    expected["verify_works_host.py"] = ledger["canonicalSources"]["Platform"]["sourceFiles"]["eng/verify_works_host.py"]
    contents = {}
    for name, digest in expected.items():
        relative = PurePosixPath(name)
        if relative.is_absolute() or ".." in relative.parts or "\\" in name:
            raise ValueError("Unsafe recorded helper path: " + name)
        raw = (root / name).read_bytes()
        if sha256(raw) != digest:
            raise ValueError("Evaluator/helper digest mismatch before import: " + name)
        contents[name] = raw
    modules = {Path(name).stem for name in contents if name.endswith(".py") and "/" not in name}
    previous = {name: sys.modules[name] for name in modules if name in sys.modules}
    old_path, old_bytecode = list(sys.path), sys.dont_write_bytecode
    with tempfile.TemporaryDirectory(prefix="story49-verified-evaluator-") as temporary:
        stage = Path(temporary)
        for name, raw in contents.items():
            path = stage / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        try:
            for name in modules:
                sys.modules.pop(name, None)
            sys.path.insert(0, str(stage))
            sys.dont_write_bytecode = True
            importlib.invalidate_caches()
            evaluator = importlib.import_module("verify_works_host")
            for name in modules:
                module = sys.modules.get(name)
                if module is not None and Path(module.__file__).resolve() != (stage / (name + ".py")).resolve():
                    raise ValueError("Evaluator imported an unverified helper: " + name)
            yield evaluator, {name: sha256(raw) for name, raw in contents.items()}
        finally:
            sys.path[:] = old_path
            sys.dont_write_bytecode = old_bytecode
            for name in modules:
                sys.modules.pop(name, None)
            sys.modules.update(previous)


def matrix_coverage(original, methods, acceptance_ok):
    """Missing, nonpassing and ambiguous facts never receive a passing coverage stamp."""
    matrix = copy.deepcopy(original)
    errors = []
    for row in matrix["rows"]:
        coverage = {}
        for name, local in row["localExecutedCoverage"].items():
            qualified = local["type"] + "." + name
            found = methods.get(qualified, [])
            if not found:
                item = {"xml": None, "result": "Missing"}
            elif len(found) != 1:
                item = {"xml": [entry["xml"] for entry in found], "result": "Ambiguous"}
            else:
                item = dict(found[0])
            coverage[name] = item
            if item["result"] != "Pass" or local.get("result") != "Pass":
                errors.append("matrix hosted fact " + qualified + ": " + item["result"])
        if set(coverage) != set(row["facts"]) or not coverage:
            errors.append("matrix row fact coverage differs: " + row["scenario"])
        row["hostedExecutedCoverage"] = coverage
        row["hostedCoverageStatus"] = "passed" if coverage and all(
            item["result"] == "Pass" and row["localExecutedCoverage"][name].get("result") == "Pass"
            for name, item in coverage.items()) and set(coverage) == set(row["facts"]) else "failed"
    matrix["status"] = "passed" if acceptance_ok and not errors else "failed"
    matrix["limits"] = "Success requires this successor's complete audit; independent workflow reviews remain separate."
    return matrix, errors


def audit(run, full_root, helper_root, original_matrix, packet):
    errors = []

    def require(ok, message):
        if not ok:
            errors.append(message)

    # Verify every extracted file before parsing any of it, then evaluate ZIP-derived
    # private copies. No later audit read can escape to the separately extracted input.
    artifacts = {}
    for artifact, digest in ARTIFACTS.items():
        folder = run / "artifacts" / artifact
        artifacts[artifact] = verified_members(folder / "original-artifact.zip", digest, folder / "extracted")
    with tempfile.TemporaryDirectory(prefix="story49-bound-hosted-") as temporary:
        a = Path(temporary)
        for name, raw in artifacts["12693567134"].items():
            path = a / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        g = a / "gate"
        ci = json.loads((g / "ledger.json").read_bytes())
        boot = json.loads((a / "bootstrap.json").read_bytes())
        full = json.loads((full_root / "gate/ledger.json").read_bytes())
        require(boot["status"] == ci["status"] == "passed-disposable-synthetic-parity-and-rollback", "terminal acceptance")
        require(boot["sdkVersion"] == ci["selectedSdk"]["version"] == "3.128.0", "selected SDK")
        require(boot["platformCommit"] == "73bebc0a336ebf68cee77ab0114d96dde66c386d", "owner commit")
        require(ci["profile"] == {"name": "ci", "consumerConfigurations": ["Release"],
                "agentsConfiguration": "Release", "sourceConformance": "not-requested; full-local-profile-only",
                "liveProofs": "all-R1-R11-and-complete-fleet-rollback"}, "CI profile")
        require("Debug" not in ci["checks"] and set(ci["dependencyModes"]) == {"Release"}, "no false Debug qualification")
        require("GITHUB_TOKEN" not in ci["runtimeEnvironment"], "credential excluded from runtime ledger")
        producer = "c5d1c280d8c1c1a207cd159d817f614e93c93130"
        qualification = ci["runtimeProducerQualification"]
        require(qualification["repositoryCommit"] == producer and qualification["matchesPublishedProducer"] is True,
                "official producer source qualification")
        for name, item in ci["publishedProducers"].items():
            require(item["version"] == "3.128.0" and item["repositoryCommit"] == producer
                    and item["source"] == "https://api.nuget.org/v3/index.json", name + " public provenance")
        methods, xml_counts = {}, {}
        for name, count in EXPECTED_XML.items():
            xml = ET.parse(g / name).getroot()
            tests = list(xml.iter("test"))
            xml_counts[name] = len(tests)
            require(len(tests) == count, name + " count")
            for test in tests:
                result = test.get("result")
                require(result == "Pass", name + " executed Pass")
                qualified = test.get("type", "") + "." + test.get("method", "")
                methods.setdefault(qualified, []).append({"xml": name, "result": result})
            for assembly in xml.iter("assembly"):
                for key in ["failed", "errors", "skipped", "not-run"]:
                    require(assembly.get(key, "0") == "0", name + " " + key)
        records = [json.loads(line) for line in (g / "persisted-states.jsonl").read_text().splitlines() if line]
        phases = {record["phase"]: record for record in records}
        require(len(records) == len(phases) == 32, "32 unique persisted phases")
        require(len(ci["actualStarts"]) == 11, "11 actual main starts")
        require(set(ci["rows"]) == {"R" + str(number) for number in range(1, 12)}, "11 rows")
        proof_count = 0
        for name, row in ci["rows"].items():
            require(row["status"] == "passed" and row["producer"]["version"] == "3.128.0", name + " status/producer")
            require(set(row["requiredProofs"]) == {"persisted", "negative", "restart", "rollback"}, name + " proof kinds")
            for kind, proof in row["requiredProofs"].items():
                proof_count += 1
                require(proof["passed"] is True and bool(proof["evidence"]), name + " " + kind + " passed")
                for evidence in proof["evidence"]:
                    phase = phases.get(evidence["phase"])
                    require(phase is not None and sha256(json.dumps(phase, sort_keys=True).encode())
                            == evidence["recordSha256"], name + " " + kind + " native record SHA")
        require(ci["rollback"]["complete"] is True, "complete rollback")
        for command in ci["commands"]:
            require(command["exitCode"] == (1 if command["name"] == "agents-full-refusal" else 0),
                    command["name"] + " expected exit")
            require(sha256((g / command["log"]).read_bytes()) == command["sha256"], command["name"] + " log digest")
        equivalence = {}
        for owner in ["Works", "Platform"]:
            left = full["canonicalSources"][owner]["sourceFiles"]
            right = ci["canonicalSources"][owner]["sourceFiles"]
            require(left == right, owner + " full/hosted source equivalence")
            equivalence[owner] = len(right)
        for key in ["fixtureSources", "platformConsumerSources", "verifierHelpers"]:
            require(full[key] == ci[key], key + " equivalence")
            equivalence[key] = len(ci[key])
        require(json.loads((full_root / "bootstrap.json").read_bytes())["coordinatorSha256"]
                == boot["coordinatorSha256"], "coordinator equivalence")

        def normalized(ledger):
            roots = [(ledger["canonicalSources"][key]["root"], key) for key in ["Platform", "Works"]]
            roots.append((ledger["linkedFixtureSources"]["compositionRoot"], "Platform"))
            result = {}
            require(ledger["linkedFixtureSources"]["compositionRoot"] == str(Path(ledger["host"]["source"]).parent),
                    "linked composition root equals actual host source root")
            for name, digest in ledger["linkedFixtureSources"]["sourceFiles"].items():
                matches = sorted([(len(root), label + "/" + name[len(root) + 1:])
                                  for root, label in roots if name.startswith(root + "/")], reverse=True)
                require(bool(matches), "linked fixture root " + name)
                if matches:
                    selected = matches[0][1]
                    require(selected not in result, "unique normalized fixture")
                    result[selected] = digest
            return result

        left, right = normalized(full), normalized(ci)
        require(left == right, "normalized linked fixture equivalence")
        equivalence["normalizedLinkedFixtures"] = len(right)
        for key in ["daprCli", "daprRuntime", "linuxAmd64DaprdSha256", "runtimeSha256", "pinSha256"]:
            require(full["qualifiedToolchain"][key] == ci["qualifiedToolchain"][key], key + " pin")
        for key in ["archiveSha256", "manifestSha256", "sourceFiles"]:
            require(full["retainedHostSource"][key] == ci["retainedHostSource"][key], "retained " + key)
        with verified_evaluator(helper_root, ci) as (evaluator, evaluator_hashes):
            rebound = evaluator.bind_persisted_outcomes(g / "persisted-states.jsonl",
                                                       copy.deepcopy(ci["rows"]), ci["actualStarts"])
        require(rebound == ci["rollback"], "all owning proof predicates/cross-phase rebind")
        manifest_raw = (packet / "manifest.json").read_bytes()
        require(sha256(manifest_raw) == HOSTED_MANIFEST_SHA256, "sealed hosted manifest digest")
        manifest = json.loads(manifest_raw)
        native = 0
        for name, item in manifest["files"].items():
            data = (packet / name).read_bytes()
            require(len(data) == item["bytes"] and sha256(data) == item["sha256"], name + " packed bytes")
            if "sourceSha256" in item:
                native += 1
                raw = gzip.decompress(data) if item.get("compression") == "gzip" else data
                require(len(raw) == item["sourceBytes"] and sha256(raw) == item["sourceSha256"], name + " decoded source")
                source = Path(item["source"])
                require(source.is_file() and sha256(source.read_bytes()) == item["sourceSha256"], name + " native source")
        matrix, missing = matrix_coverage(original_matrix, methods, not errors)
        errors.extend(missing)
        return {"status": "failed" if errors else "passed", "errors": errors, "ciRun": 40407181685,
                "rootCommit": "09cc8e19c23ec15a0542499892bbf5cd4b8f38dd", "profile": "ci Release-only",
                "canonicalFacts": sum(xml_counts.values()), "xml": xml_counts,
                "rows": len(ci["rows"]), "proofs": proof_count, "phases": len(phases),
                "fleetStarts": len(ci["actualStarts"]), "rollbackComplete": ci["rollback"]["complete"],
                "sourceEquivalence": equivalence, "frozenMatrixRows": len(matrix["rows"]),
                "manifestEntries": len(manifest["files"]), "nativeBindings": native,
                "manifestSha256": sha256(manifest_raw), "verifiedEvaluatorAndHelpers": evaluator_hashes,
                "verifiedOriginalZips": ARTIFACTS,
                "consumedLedgerSha256": sha256(artifacts["12693567134"]["gate/ledger.json"]),
                "extractedBytesBoundDirectlyToOriginalZips": True,
                "limit": "CI binaries are bound by executed owning final closure and uploaded inventory/start records; they are not uploaded for local rehash."}, matrix


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", type=Path, default=Path("/tmp/story49-hosted-20261026T215001Z"))
    parser.add_argument("--full-root", type=Path, default=Path("/tmp/story49-final-20261026T195205Z/full2"))
    parser.add_argument("--helper-root", type=Path, default=Path("references/Hexalith.Platform/eng"))
    parser.add_argument("--matrix", type=Path, default=Path(__file__).parent.parent / "ci-build-parent-acceptance-2026-10-26/frozen-matrix-parent-audit.json")
    parser.add_argument("--hosted-packet", type=Path, default=Path(__file__).parent.parent / "ci-build-hosted-qualification-2026-10-26")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    matrix = {"status": "failed", "rows": [], "coverage": "not evaluated"}
    try:
        original = json.loads(args.matrix.read_bytes())
        matrix = copy.deepcopy(original)
        matrix["status"] = "failed"
        matrix["coverage"] = "not evaluated; audit did not complete"
        for row in matrix["rows"]:
            row["hostedCoverageStatus"] = "not-evaluated"
            row["hostedExecutedCoverage"] = {name: {"xml": None, "result": "NotEvaluated"}
                                              for name in row["localExecutedCoverage"]}
        report, matrix = audit(args.run, args.full_root, args.helper_root, original, args.hosted_packet)
    except (OSError, ValueError, KeyError, TypeError, ET.ParseError) as error:
        report = {"status": "failed", "errors": [str(error)], "coverage": "not accepted"}
    write_json_new(args.output / "hosted-audit.json", report)
    write_json_new(args.output / "matrix-audit.json", matrix)
    print(json.dumps(report, indent=2))
    return int(report["status"] != "passed")


if __name__ == "__main__":
    raise SystemExit(main())
