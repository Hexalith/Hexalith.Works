"""Verify qualification provenance, expected outcomes and five append-only closures."""

import gzip
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


packet = Path(__file__).resolve().parent
root = packet.parents[3]
manifest = json.loads((packet / "manifest.json").read_text())


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git(*args):
    return subprocess.run(["git", *args], cwd=root, capture_output=True, check=True, timeout=30).stdout


for relative, expected in manifest["sources"].items():
    require(digest((root / relative).read_bytes()) == expected, f"Source changed: {relative}")
for relative, expected in manifest["artifacts"].items():
    require(digest((packet / relative).read_bytes()) == expected, f"Artifact changed: {relative}")
for relative, expected in manifest["assemblies"].items():
    require((root / relative).is_file(), f"Executed assembly missing; rebuild qualification: {relative}")
    require(digest((root / relative).read_bytes()) == expected, f"Executed assembly changed: {relative}")
for prefix, repository in [("eventstore", "Hexalith.EventStore"), ("platform", "Hexalith.Platform")]:
    dependency = "references/" + repository
    require(git("ls-tree", manifest["baseline"], dependency).decode().strip() == manifest[prefix + "_gitlink"],
            f"Baseline dependency changed: {repository}")
    require(git("-C", dependency, "rev-parse", "HEAD").decode().strip() == manifest[prefix + "_checkout"],
            f"Consumed dependency checkout changed: {repository}")
for relative in manifest["existing_blocker_inputs_unchanged"]:
    require(git("show", manifest["baseline"] + ":" + relative) == (root / relative).read_bytes(),
            "Pre-existing blocker input changed: " + relative)

integration = root / "tests/Hexalith.Works.IntegrationTests"
current = {str(p.relative_to(root)) for p in integration.rglob("*.cs")
           if not {"bin", "obj"}.intersection(p.relative_to(integration).parts)}
bound = {p for p in manifest["sources"] if p.startswith("tests/Hexalith.Works.IntegrationTests/") and p.endswith(".cs")}
require(current == bound, "Integration source inventory changed")
audit = []
for relative in sorted(current):
    source = (root / relative).read_text()
    factories = re.findall(r"DistributedApplicationTestingBuilder\s*\.\s*CreateAsync", source)
    require(not factories or Path(relative).name == "WorksAppHostSmokeHarness.cs", "Unshared topology starter: " + relative)
    calls = [i for i, line in enumerate(source.splitlines(), 1) if "WorksAppHostSmokeHarness.WithAppHostAsync(" in line]
    if calls:
        audit.append(dict(source=relative, shared_harness_call_lines=calls, direct_factory_calls=factories))
require(audit == json.loads((packet / "harness-audit.json").read_text()), "Harness call inventory changed")

ledger_path = "_bmad-output/implementation-artifacts/deferred-work.md"
original = git("show", manifest["baseline"] + ":" + ledger_path)
require(digest(original) == manifest["original_ledger_sha256"], "Original ledger differs")
ledger = (root / ledger_path).read_bytes()
require(len(manifest["ledger_annotations"]) == 5, "Expected exactly five closures")
for annotation in manifest["ledger_annotations"]:
    anchor = annotation["anchor"].encode()
    inserted = annotation["inserted"].encode()
    require(ledger.count(anchor + inserted) == 1, "Missing or duplicated closure")
    ledger = ledger.replace(anchor + inserted, anchor, 1)
require(ledger == original, "Other historical ledger bytes changed")

commands = json.loads((packet / "commands.json").read_text())
names = [c["name"] for c in commands]
require(len(names) == len(set(names)), "Duplicate command lane")
expected_counts = {
    "final-integration-focused": (121, 121, 0),
    "final-host-inventory-http": (6, 6, 0),
    "final-valid-configured-copy": (4, 4, 0),
    "architecture-focused": (39, 39, 0),
    "dependency-mode": (1, 1, 0),
    "architecture-all": (404, 403, 1),
}
for scope in ["control-plane", "acl", "caller", "caller-identity"]:
    for prefix in ["final-mutation-", "final-fixture-mutation-"]:
        expected_counts[prefix + scope] = (4, 3, 1)
required = set(expected_counts) | {"final-integration-build", "architecture-build", "format"}
require(required.issubset(names), "Missing qualifying lanes: " + str(required - set(names)))
for command in commands:
    name = command["name"]
    require(command["exit_code"] == command.get("expected_exit", 0), "Unexpected command exit: " + name)
    if "assemblies" not in command:
        require(name in {"integration-build", "integration-build-corrected", "final-integration-build", "architecture-build", "format", "review-analyzer-build-failure"},
                "Unknown command without XML: " + name)
        continue
    xml = gzip.decompress((packet / (name + ".xml.gz")).read_bytes())
    document = ET.fromstring(xml)
    assemblies = [dict(a.attrib) for a in document.findall("assembly")]
    require(assemblies == command["assemblies"], "XML metadata differs: " + name)
    counts = tuple(sum(int(a[field]) for a in assemblies) for field in ["total", "passed", "failed"])
    require(counts[0] > 0, "Empty lane: " + name)
    require(all(int(a["errors"]) == 0 and int(a["skipped"]) == 0 and int(a["not-run"]) == 0 for a in assemblies),
            "Errors, skipped or unexecuted cases: " + name)
    if name in expected_counts:
        require(counts == expected_counts[name], "Incorrect qualifying totals: " + name)
    failures = [t for t in document.iter("test") if t.get("result") == "Fail"]
    if command["exit_code"] == 0:
        require(not failures, "Passing lane has failures: " + name)
    elif name == "architecture-all":
        require(len(failures) == 1 and failures[0].get("method") == "P0_CurrentSourceRevisionMarkersMatchGitlinkAndConsumedCheckout",
                "Unexpected broad failure")
        message = failures[0].findtext("failure/message", "")
        require("API surface current-source marker differs" in message
                and "boundary record current-source marker differs" in message
                and "36c78f3435d2d492a73f1b0707cf865c8735d740" in message,
                "Broad failure is not the documented marker mismatch")
    elif "mutation" in name:
        require(len(failures) == 1 and counts == (4, 3, 1), "Mutation did not refuse exactly one case")
        failed = failures[0]
        require(failed.get("method") == "PlatformAndFixturePoliciesPinEveryTrustDomain"
                and "accesscontrol.works.yaml" in failed.get("name", ""), "Wrong mutation case")
        failure = failed.find("failure")
        require(failure is not None and failure.get("exception-type") == "Shouldly.ShouldAssertException",
                "Mutation failed through an unrelated exception")
        message = failed.findtext("failure/message", "")
        require("accesscontrol.works.yaml" in message, "Mutation omitted its filename")
        field = "appId" if name.endswith("caller-identity") else (
            "controlPlaneTrustDomain" if name.endswith("control-plane") else "trustDomain")
        require(field in message, "Wrong mutation assertion")
        expression = "policies.Children" if name.endswith("caller-identity") else (
            "mtls.Children" if name.endswith("control-plane") else (
                "policy.Children" if name.endswith("caller") else "acl.Children"))
        require(expression in message, "Wrong mutation scope")
        if not name.endswith("caller-identity"):
            require("public" in message and "localhost" in message, "Wrong trust-domain mutation")
    elif name != "initial-host-inventory-http":
        raise RuntimeError("Unexpected failed lane: " + name)

print("Verified provenance, required passing lanes, eight mutation refusals, existing blocker and five ledger closures.")
