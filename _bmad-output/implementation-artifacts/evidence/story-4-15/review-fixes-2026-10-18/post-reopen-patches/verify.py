#!/usr/bin/env python3
"""Verify the portable post-reopen patch packet; optionally bind current sources."""
import gzip
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parent
report = json.loads((root / "report.json").read_text())


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def unpack(path):
    return gzip.decompress((root / path).read_bytes())


for item in report["artifacts"]:
    packed = (root / item["path"]).read_bytes()
    require(hashlib.sha256(packed).hexdigest() == item["sha256"], item["path"])
    require(hashlib.sha256(gzip.decompress(packed)).hexdigest() == item["uncompressedSha256"], item["path"])

expected = {
    "sdk-client": (1668, 1666, 0, 2),
    "sdk-store": (30, 30, 0, 0),
    "works-architecture": (269, 269, 0, 0),
    "works-debug-focused": (83, 83, 0, 0),
    "works-debug-integration": (853, 853, 0, 0),
    "works-release-candidate-integration": (853, 853, 0, 0),
    "works-release-candidate-no-opt-in-contract": (2, 1, 1, 0),
    "works-release-default-contract": (2, 2, 0, 0),
}
require(set(report["reports"]) == set(expected), "report inventory")
for name, counts in expected.items():
    result = report["reports"][name]
    assembly = ET.fromstring(unpack(result["path"])).find("assembly")
    require(assembly is not None, name)
    observed = tuple(int(assembly.get(field)) for field in ("total", "passed", "failed", "skipped"))
    require(observed == counts, name)
    require(tuple(result[field] for field in ("total", "passed", "failed", "skipped")) == counts, name + " summary")
    require(int(assembly.get("errors", "0")) == 0, name + " errors")

negative = ET.fromstring(unpack(report["reports"]["works-release-candidate-no-opt-in-contract"]["path"]))
failed = [test for test in negative.findall(".//test") if test.get("result") == "Fail"]
require(len(failed) == 1 and failed[0].get("name", "").endswith("ReleaseConsumerLoadsSelectedSdkPackagesAndPartiesContracts"), "expected rejected override")
require("WorksAllowLocalEventStoreCandidate=true" in "".join(failed[0].itertext()), "override rejected by static-pin guard")
for command in report["commands"]:
    require(command["exitCode"] == (1 if command["name"] == "works-release-candidate-no-opt-in-contract" else 0), command["name"])
    if "build" in command["command"]:
        text = unpack(command["log"]).decode()
        require("Build succeeded." in text and "0 Warning(s)" in text and "0 Error(s)" in text, command["name"])

for label, version in (("default", "3.124.0"), ("candidate", "3.124.1-story415.20261018.1")):
    binding = json.loads(unpack("raw/bindings-release-" + label + ".json.gz"))
    libraries = binding["sdkLibraries"]
    require(len(libraries) >= 9 and all(name.endswith("/" + version) and kind == "package" for name, kind in libraries.items()), label + " dependency graph")
require(json.loads(unpack("raw/candidate-archives-before.json.gz")) == report["candidateArchives"], "candidate identity")
require(len(report["candidateArchives"]) == 9 and report["candidateArchivesUnchanged"], "immutable archive inventory")
for path, item in report["sources"].items():
    require(hashlib.sha256(unpack(item["snapshot"])).hexdigest() == item["sha256"], path)
    if "--current" in sys.argv:
        workspace = next(parent for parent in root.parents if (parent / "Hexalith.Works.slnx").is_file())
        require(hashlib.sha256((workspace / path).read_bytes()).hexdigest() == item["sha256"], "current " + path)
print(f"Verified {len(expected)} test reports, {len(report['artifacts'])} artifacts, {len(report['sources'])} source snapshots and nine unchanged candidate identities.")
