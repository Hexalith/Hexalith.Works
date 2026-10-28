#!/usr/bin/env python3
"""Exercise historical verifier refusals in a disposable copy, including python -O."""

import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

packet = Path(__file__).resolve().parent
root = packet.parents[3]
relative_packet = packet.relative_to(root)
original_manifest = json.loads((packet / "historical-inputs.json").read_bytes())
history = Path("_bmad-output/implementation-artifacts/evidence/story-4-16")
ledger = history / "public-3.126.0-2026-10-19/canonical-admission/ledger.json"
result = history / "public-3.126.0-2026-10-19/canonical-admission/result.json"
ancestry = relative_packet / "corrected-producer-ancestry.json"
source_debug = history / "public-3.125.1-2026-10-18/source-debug-lane/lane3/results.json"
cases = [
    ("changed-input", result, False, lambda value: value.update(status="failed"), "Changed input"),
    (
        "wrong-producer",
        ledger,
        True,
        lambda value: value["runtimeProducerQualification"].update(repositoryCommit="0" * 40),
        "Wrong runtime producer",
    ),
    (
        "failed-proof",
        ledger,
        True,
        lambda value: value["rows"]["R4"]["requiredProofs"]["restart"].update(passed=False),
        "Failed proof",
    ),
    ("missing-row", ledger, True, lambda value: value["rows"].pop("R11"), "Incomplete R1-R11 inventory"),
    (
        "broken-ancestry",
        ancestry,
        True,
        lambda value: value["commits"][0].update(nextParent="0" * 40),
        "Git ancestry chain is broken",
    ),
    (
        "missing-consumer-pins",
        ledger,
        True,
        lambda value: value["selectedSdk"]["worksConsumerPins"].clear(),
        "Incomplete consumer pin inventory",
    ),
    (
        "bare-true-new-proof",
        ledger,
        True,
        lambda value: value["rows"]["R4"]["requiredProofs"].update(restart=True),
        "Failed proof",
    ),
    (
        "changed-restored-fleet",
        ledger,
        True,
        lambda value: value["rollback"]["restored"]["fleet"].update({"statestore.yaml": "0" * 64}),
        "Restored fleet inventory mismatch",
    ),
    (
        "missing-actual-starts",
        ledger,
        True,
        lambda value: value.update(actualStarts=[]),
        "Incomplete actual host starts",
    ),
    (
        "invented-canonical-command",
        ledger,
        True,
        lambda value: value["commands"][0].update(name="invented-command"),
        "Incomplete canonical commands",
    ),
    (
        "wrong-row-producer",
        ledger,
        True,
        lambda value: value["rows"]["R4"]["producer"].update(version="3.124.0"),
        "Row producer mismatch",
    ),
    (
        "wrong-debug-sdk-inventory",
        source_debug,
        True,
        lambda value: value["producer"].update(inventorySha256="0" * 64),
        "Source Debug SDK inventory mismatch",
    ),
    (
        "wrong-debug-environment",
        source_debug,
        True,
        lambda value: value["environment"].update(UseNuGetDeps="true"),
        "Source Debug environment mismatch",
    ),
]
results = []
with tempfile.TemporaryDirectory(prefix="works-history-controls-") as temporary:
    target = Path(temporary)
    for name in original_manifest["inputsSha256"]:
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / name, destination)
    manifest_path = target / relative_packet / "historical-inputs.json"
    for optimization in ([], ["-O"]):
        command = [sys.executable, *optimization, str(packet / "verify-history.py"), "--root", str(target)]
        manifest_path.write_text(json.dumps(original_manifest))
        baseline = subprocess.run(command, capture_output=True, text=True, timeout=30)
        if baseline.returncode != 0:
            raise ValueError("Valid relocated historical packet failed: " + baseline.stderr)
        for name, path, rebind, mutate, diagnostic in cases:
            original = (target / path).read_bytes()
            value = json.loads(original)
            mutate(value)
            changed = json.dumps(value).encode()
            (target / path).write_bytes(changed)
            manifest = json.loads(json.dumps(original_manifest))
            if rebind:
                manifest["inputsSha256"][path.as_posix()] = hashlib.sha256(changed).hexdigest()
            manifest_path.write_text(json.dumps(manifest))
            execution = subprocess.run(command, capture_output=True, text=True, timeout=30)
            if execution.returncode == 0 or diagnostic not in execution.stderr:
                raise ValueError(f"Control {name} failed to refuse with {diagnostic}: {execution.stderr}")
            results.append(
                {
                    "control": name,
                    "optimization": 1 if optimization else 0,
                    "exitCode": execution.returncode,
                    "diagnostic": diagnostic,
                }
            )
            (target / path).write_bytes(original)
            manifest_path.write_text(json.dumps(original_manifest))
print(json.dumps({"validRelocatedPasses": 2, "refusals": results}, indent=2))
