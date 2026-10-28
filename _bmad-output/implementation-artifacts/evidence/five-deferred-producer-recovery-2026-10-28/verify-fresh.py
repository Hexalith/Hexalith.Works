#!/usr/bin/env python3
"""Check retained fresh deterministic results and their source bindings."""

import gzip
import hashlib
import json
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


packet = Path(__file__).resolve().parent
root = packet.parents[3]
record = json.loads((packet / "fresh-verification.json").read_bytes())
require(
    record["schema"] == 1 and record["baseline"] == "2773039ef77fc36df287527bef70040926efaf9e", "Wrong fresh baseline"
)
expected_commands = {"initial-build": 1, "build": 0, "focused": 0, "format-existing": 2, "format-new": 0}
commands = record["commands"]
require(len(commands) == 5 and {item["name"] for item in commands} == set(expected_commands), "Missing fresh commands")
for command in commands:
    name = command["name"]
    require(command["exitCode"] == expected_commands[name] and bool(command["command"]), f"Wrong fresh command: {name}")
    require(command["log"] == name + ".log.gz", f"Wrong log selection: {name}")
    data = (packet / command["log"]).read_bytes()
    require(sha256(data) == command["sha256"], f"Changed fresh log: {name}")
    text = gzip.decompress(data).decode()
    if name == "initial-build":
        require("CA2007" in text, "Initial build does not preserve the recorded analyzer failure")
    if name == "build":
        require("0 Warning(s)" in text and "0 Error(s)" in text, "Final build is not warning/error free")

counts = {"total": 85, "passed": 85, "failed": 0, "errors": 0, "skipped": 0, "not-run": 0}
require(record["counts"] == counts, "Wrong fresh test totals")
data = gzip.decompress((packet / "focused.xml.gz").read_bytes())
require(sha256(data) == record["xmlSha256"], "Changed fresh XML")
document = ET.fromstring(data)
assemblies = document.findall("assembly")
require(
    len(assemblies) == 1 and all(int(assemblies[0].attrib[key]) == value for key, value in counts.items()),
    "Nonqualifying fresh XML assembly",
)
tests = list(document.iter("test"))
require(len(tests) == 85 and all(test.attrib["result"] == "Pass" for test in tests), "Missing or failed fresh cases")
profile = [
    test for test in tests if test.attrib["type"] == "Hexalith.Works.IntegrationTests.WorksSdkRecoveryProfileTests"
]
require(
    Counter(test.attrib["method"] for test in profile)
    == {
        "ComposedProfileFitsTwelveSourcesAndFiveEffects": 2,
        "OldProfileCannotAdmitTheTwelfthSource": 2,
        "UnsafeEffectOrPopulationBudgetIsRefused": 2,
        "ExactFiveEffectQuantumIsAccepted": 1,
    },
    "Wrong seven profile controls",
)
expected_cases = {
    'ComposedProfileFitsTwelveSourcesAndFiveEffects(resourceName: "works")',
    'ComposedProfileFitsTwelveSourcesAndFiveEffects(resourceName: "works-ha")',
    "OldProfileCannotAdmitTheTwelfthSource(sources: 11)",
    "OldProfileCannotAdmitTheTwelfthSource(sources: 12)",
    (
        'UnsafeEffectOrPopulationBudgetIsRefused(field: "SourceServiceQuantum", '
        'value: "00:00:55.0999999", failurePrefix: "SourceServiceQuantum must fit")'
    ),
    (
        'UnsafeEffectOrPopulationBudgetIsRefused(field: "SupportedCandidateCount", '
        'value: "13", failurePrefix: "SupportedCandidateCount cannot fit")'
    ),
    "ExactFiveEffectQuantumIsAccepted",
}
actual_cases = {
    test.attrib["name"]
    .removeprefix("Hexalith.Works.IntegrationTests.WorksSdkRecoveryProfileTests.")
    .replace(chr(92) + '"', '"')
    for test in profile
}
require(len(profile) == 7 and actual_cases == expected_cases, "Missing distinct profile arguments")
sources = record["sourcesSha256"]
require(
    {
        "tests/Hexalith.Works.IntegrationTests/WorksSdkRecoveryProfileTests.cs",
        "tests/Hexalith.Works.IntegrationTests/WorksSdkRecoveryPipelineSmokeTests.cs",
        "references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreProcessOptions.cs",
    }
    <= set(sources),
    "Missing exercised profile source bindings",
)
for name, expected in sources.items():
    path = Path(name)
    require(not path.is_absolute() and ".." not in path.parts, f"Unsafe source path: {name}")
    require(sha256((root / path).read_bytes()) == expected, f"Changed exercised source: {name}")
print(f"Fresh deterministic evidence passed: 85/85 cases, seven profile controls, {len(sources)} source bindings.")
print("Local DLL hashes are recorded provenance; replay does not require retained local build outputs.")
