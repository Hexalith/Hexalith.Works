"""Verify the portable Story 4.15 deterministic requalification packet."""
from pathlib import Path
import argparse
import gzip
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def verify(current=False):
    packet = Path(__file__).resolve().parent
    report = json.loads((packet / "report.json").read_text())
    raw = {}
    for artifact in report["artifacts"]:
        path = artifact["path"]
        packed = (packet / path).read_bytes()
        require(sha256(packed) == artifact["sha256"], "Compressed hash mismatch: " + path)
        data = gzip.decompress(packed)
        require(sha256(data) == artifact["originalSha256"], "Original hash mismatch: " + path)
        require(len(data) == artifact["originalBytes"], "Original size mismatch: " + path)
        raw[path] = data

    expected = {"debug-focused": 87, "debug-integration": 887, "release-integration": 887,
                "architecture": 269, "unit": 589, "property": 3}
    tests = {}
    for command in report["commands"]:
        label = command["label"]
        require(command["exitCode"] == (1 if label == "debug-build" else 0), "Command exit: " + label)
        if label.endswith("build") or label == "debug-build-final":
            log = raw[command["log"]].decode()
            require("\n    0 Warning(s)\n" in log, "Build warning count: " + label)
            require("\n    0 Error(s)\n" in log if command["exitCode"] == 0 else "\n    6 Error(s)\n" in log,
                    "Build error count: " + label)
        if label not in expected:
            continue
        xml = ET.fromstring(raw[command["xml"]])
        assembly = xml.find("assembly")
        stats = {key: int(assembly.attrib[key]) for key in command["tests"]}
        require(stats == command["tests"], "XML/report counts disagree: " + label)
        require(stats["total"] == stats["passed"] == expected[label], "Passing count: " + label)
        require(all(stats[key] == 0 for key in ("errors", "failed", "skipped", "not-run")), "Nonpass: " + label)
        tests[label] = {test.attrib["name"]: test.attrib["result"] for test in xml.findall(".//test")}
        require(len(tests[label]) == stats["total"], "Duplicate/missing test names: " + label)
        require(set(tests[label].values()) == {"Pass"}, "Nonpassing test: " + label)
    require(set(tests) == set(expected), "Missing accepting report")

    for mode in ("debug", "release"):
        cases = tests[mode + "-integration"]
        scalar = set()
        for name in cases:
            if "RequiredValueMemberPresenceDoesNotRejectExplicitDefaults(" in name:
                match = re.search(r'kind: "([^"]+)", field: "([^"]+)", present: (True|False)', name.replace('\\', ''))
                require(match is not None, "Unexpected scalar case name: " + name)
                scalar.add(match.groups())
        require(len(scalar) == report["scalarPresenceCasesPerMode"] == 36, "Scalar coverage: " + mode)
        sequenced = {kind for kind, field, _ in scalar if field == "Sequence"}
        require(len(sequenced) == report["sequencedPayloadCount"] == 15, "Sequence inventory: " + mode)
        require(all((kind, "Sequence", present) in scalar for kind in sequenced for present in ("True", "False")),
                "Missing absent/default sequence pair: " + mode)
        require(any(name.endswith(".StateChangingInventoryCoversEverySequencedEvent") for name in cases),
                "Missing reflection inventory check: " + mode)
        require(any('RejectionRequiresItsCommittedFailureCondition(kind: "depth", value: 1, limit: 0, valid: True)' in name.replace('\\', '')
                    for name in cases), "Missing explicit-zero MaxDepth: " + mode)
        dependencies = report["dependencies"][mode]
        actual = {name: value["type"] for name, value in json.loads(raw[dependencies["artifact"]])["libraries"].items()
                  if name.startswith("Hexalith.EventStore.")}
        require(actual == dependencies["libraries"] and len(actual) == 9, "SDK graph: " + mode)
        require(set(actual.values()) == ({"project"} if mode == "debug" else {"package"}), "Dependency mode: " + mode)

    observations = 0
    for row in report["matrix"]:
        require(row["passingCases"] == len(row["cases"]), "Matrix case count")
        for case in row["cases"]:
            require(tests[row["mode"] + "-integration"].get(case) == "Pass", "Matrix case: " + case)
            observations += 1
    require(observations == report["matrixObservations"] == 130, "Matrix observations")
    before = json.loads(raw["raw/candidate-archives-before.json.gz"])
    require(before == json.loads(raw["raw/candidate-archives-after.json.gz"]) and len(before) == 9, "Candidate immutability")
    require(len(report["candidatePackages"]) == 9, "Candidate package count")
    for package in report["candidatePackages"]:
        require(before[package["archive"]] == package["sha256"], "Candidate archive binding")
        require(package["archiveAssemblySha256"] == package["consumerAssemblySha256"], "Candidate assembly binding")
    require(report["archiveTag"]["target"] == "575c8070355ff9b6afb17521d6eb529a85327e4d"
            and report["archiveTag"]["pushPending"], "Archive preservation scope")
    require(report["sdkHead"] == "f43b121c6c7539487b1f38b3c28508e3ab6264cc" and report["sdkWorkingTree"] == "", "SDK producer")
    require(not report["currentSourceLiveRerun"] and not report["candidateRepacked"], "Historical runtime/candidate scope")

    if current:
        root = next(parent for parent in packet.parents if (parent / ".gitmodules").is_file())
        for name, expected_hash in report["sources"].items():
            require(sha256((root / name).read_bytes()) == expected_hash, "Current source mismatch: " + name)
        sdk = root / "references/Hexalith.EventStore"
        require(subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=sdk, text=True).strip() == report["sdkHead"], "Current SDK head")
        require(subprocess.check_output(["git", "rev-parse", "archive/works-c0ed219"], cwd=sdk, text=True).strip()
                == report["archiveTag"]["target"], "Current local archive tag")
        for package in report["candidatePackages"]:
            require(sha256((root / package["archive"]).read_bytes()) == package["sha256"], "Current candidate archive")
    print("Verified six accepting reports, 130 matrix observations, 36 scalar cases per mode, nine unchanged candidate archives and "
          + str(len(report["sources"])) + " source bindings" + (" against current checkout." if current else "."))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--current", action="store_true", help="Also compare captured source and producer bindings with this checkout.")
    verify(parser.parse_args().current)
