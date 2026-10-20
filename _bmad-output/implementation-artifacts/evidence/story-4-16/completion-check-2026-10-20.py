"""Verify retained Story 4.16 evidence; optionally refresh the adjacent JSON report."""

import argparse
import gzip
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify():
    root = Path(__file__).resolve().parents[4]
    packet = Path(__file__).resolve().parent / "public-3.126.0-2026-10-19"
    manifest = json.loads((packet / "manifest.json").read_text())
    for name, digest in manifest["files"].items():
        path = packet / name
        require(path.is_file() and sha256(path) == digest, "Packet mismatch: " + name)

    ledger = json.loads((packet / "canonical-admission/ledger.json").read_text())
    command = json.loads((packet / "canonical-admission/command-result.json").read_text())
    require(command["exitCode"] == 0, "Canonical command failed")
    require(ledger["status"] == "passed-disposable-synthetic-parity-and-rollback", "Canonical status failed")
    require(ledger["selectedSdk"]["version"] == "3.126.0", "Selected SDK differs")
    source_counts = {}
    for name, inventory in (
        ("consumer", ledger["worksConsumerSource"]["sourceFiles"]),
        ("fixture", ledger["fixtureSources"]),
    ):
        for name_in_root, digest in inventory.items():
            path = root / name_in_root
            require(path.is_file() and sha256(path) == digest, "Current Works source mismatch: " + name_in_root)
        source_counts[name] = len(inventory)

    with gzip.open(packet / "canonical-admission/persisted-states.jsonl.gz", "rt") as stream:
        records = [json.loads(line) for line in stream if line.strip()]
    phases = {record["phase"]: record for record in records}
    require(len(records) == len(phases) == 32, "Persisted phases missing or duplicated")
    require(set(ledger["rows"]) == {f"R{i}" for i in range(1, 12)}, "R1-R11 row set differs")
    proof_count = 0
    record_count = 0
    for row in ledger["rows"].values():
        require(row["status"] == "passed", "Parity row failed")
        require(set(row["requiredProofs"]) == {"persisted", "negative", "restart", "rollback"}, "Proof set differs")
        for proof in row["requiredProofs"].values():
            require(proof["passed"] is True and bool(proof["evidence"]), "Required proof failed")
            proof_count += 1
            for evidence in proof["evidence"]:
                require(evidence["phase"] in phases, "Required phase missing")
                digest = hashlib.sha256(json.dumps(phases[evidence["phase"]], sort_keys=True).encode()).hexdigest()
                require(digest == evidence["recordSha256"], "Persisted proof-record hash differs")
                record_count += 1
    require(ledger["rollback"]["complete"] is True, "Complete rollback missing")
    for name, check in ledger["checks"].items():
        if isinstance(check, dict):
            require(check["total"] == check["passed"] and check["skipped"] == 0, "Canonical tests failed: " + name)

    commands = {entry["name"]: entry for entry in ledger["commands"]}
    retained_gates = {}
    for name, expected_code, marker in (
        ("agents-full-refusal", 1, "DependencyNotAvailable: EXT-HOST-1"),
        ("agents-local-scaffold", 0, "LocalScaffold build succeeded."),
        ("evidence-gate-tests", 0, "Ran 52 tests"),
    ):
        entry = commands[name]
        log = packet / "canonical-admission" / entry["log"]
        require(entry["exitCode"] == expected_code and sha256(log) == entry["sha256"], "Gate result differs: " + name)
        require(marker in log.read_text(), "Gate log lacks expected outcome: " + name)
        if name == "evidence-gate-tests":
            require(log.read_text().rstrip().endswith("OK"), "Evidence-gate tests did not pass")
        retained_gates[name] = {"exitCode": expected_code, "logSha256": entry["sha256"], "outcomeMarker": marker}

    xml_paths = ["canonical-admission/" + name + ".xml" for name in (
        "platform-debug", "platform-release", "works-consumer-supporting", "platform-recovery",
        "platform-historical", "platform-ha", "platform-projection",
    )]
    xml_paths += ["retained-source-debug/retained-recovery.xml"]
    xml_paths += [f"deterministic-suites/debug-suites/{name}Tests.xml" for name in (
        "Unit", "Property", "Architecture", "Integration",
    )]
    xml_paths += [
        "deterministic-suites/release-suites/UnitTests.xml",
        "deterministic-suites/release-suites/PropertyTests.xml",
        "deterministic-suites/release-after-restore/ArchitectureTests.xml",
        "deterministic-suites/release-integration-repo/IntegrationTests.xml",
        "supporting-checks/story416-31260-isolated-composition.xml",
    ]
    results = {}
    for name in xml_paths:
        assemblies = list(ET.parse(packet / name).iter("assembly"))
        require(bool(assemblies), "Missing assembly test results: " + name)
        for assembly in assemblies:
            require(int(assembly.get("total", "0")) > 0 and assembly.get("passed") == assembly.get("total"), "Tests did not pass: " + name)
            require(all(int(assembly.get(key, "0")) == 0 for key in ("failed", "errors", "skipped", "not-run")), "Tests failed or skipped: " + name)
        results[name] = sum(int(assembly.get("passed")) for assembly in assemblies)

    artifacts = root / "_bmad-output/implementation-artifacts"
    spec = artifacts / "spec-4-16-prove-platform-works-parity-and-rollback.md"
    sprint = artifacts / "sprint-status.yaml"
    require("status: 'done'" in spec.read_text().split("---")[1], "Original spec is not done")
    require(not re.search(r"^- \[ \]", spec.read_text(), re.M), "Original execution task remains open")
    require("  4-16-prove-platform-works-parity-and-rollback: review" in sprint.read_text(), "Sprint differs from workflow closeout")
    snapshot = "_bmad/render/bmad-build/works-f2b4635042f0/a5b668c72d9d564936a3/"
    return {
        "checkedUtc": datetime.now(timezone.utc).isoformat(),
        "worksCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
        "scope": "Retained 2026-10-19 public 3.126.0 evidence integrity and current Works source binding; no live rerun or current Platform requalification",
        "verificationCommand": "python3 " + str(Path(__file__).resolve().relative_to(root)),
        "checkerSourceSha256": sha256(Path(__file__).resolve()),
        "packet": str(packet.relative_to(root)),
        "packetManifestSha256": sha256(packet / "manifest.json"),
        "packetFilesVerified": len(manifest["files"]),
        "currentWorksSourceBindings": source_counts,
        "canonicalExitCode": command["exitCode"],
        "canonicalStatus": ledger["status"],
        "parityRows": len(ledger["rows"]),
        "requiredProofsVerified": proof_count,
        "persistedProofRecordBindings": record_count,
        "persistedPhases": len(phases),
        "completeRollback": True,
        "retainedGates": retained_gates,
        "retainedXmlPassCounts": results,
        "storySpecStatus": "done",
        "sprintStatus": "review",
        "trackingDecision": "Preserve review: build completion targets review; no tracking inconsistency",
        "workflowRules": {
            snapshot + "step-oneshot.md": "with `{target_status}` = `review`.",
            snapshot + "sync-sprint-status.md": "If the story is already at `{target_status}` or later, stop.",
        },
        "newLiveTestsRun": False,
        "status": "passed",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-report", action="store_true", help="Refresh the adjacent completion-check JSON")
    args = parser.parse_args()
    report = verify()
    if args.write_report:
        Path(__file__).with_suffix(".json").write_bytes((json.dumps(report, indent=2) + "\n").replace("\n", "\r\n").encode())
    print(json.dumps(report, indent=2))
