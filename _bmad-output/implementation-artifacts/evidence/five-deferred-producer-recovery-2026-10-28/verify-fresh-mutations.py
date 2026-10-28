#!/usr/bin/env python3
"""Refuse duplicated theory arguments even when XML hashes and totals are rebound."""

import gzip
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

packet = Path(__file__).resolve().parent
root = packet.parents[3]
relative = packet.relative_to(root)
manifest = json.loads((packet / "fresh-verification.json").read_bytes())
xml = gzip.decompress((packet / "focused.xml.gz").read_bytes())
methods = [
    "ComposedProfileFitsTwelveSourcesAndFiveEffects",
    "OldProfileCannotAdmitTheTwelfthSource",
    "UnsafeEffectOrPopulationBudgetIsRefused",
]
results = []
with tempfile.TemporaryDirectory(prefix="works-fresh-controls-") as temporary:
    target = Path(temporary)
    inputs = [
        *manifest["sourcesSha256"],
        *(str(relative / command["log"]) for command in manifest["commands"]),
        str(relative / "verify-fresh.py"),
    ]
    for name in inputs:
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / name, destination)
    copied = target / relative
    for optimization in ([], ["-O"]):
        command = [sys.executable, *optimization, str(copied / "verify-fresh.py")]
        (copied / "focused.xml.gz").write_bytes(gzip.compress(xml, mtime=0))
        (copied / "fresh-verification.json").write_text(json.dumps(manifest))
        baseline = subprocess.run(command, capture_output=True, text=True, timeout=30)
        if baseline.returncode != 0:
            raise ValueError("Valid relocated fresh packet failed: " + baseline.stderr)
        for method in methods:
            document = ET.fromstring(xml)
            cases = [
                test
                for test in document.iter("test")
                if test.attrib["type"].endswith("WorksSdkRecoveryProfileTests") and test.attrib["method"] == method
            ]
            if len(cases) != 2:
                raise ValueError("Expected two original theory cases")
            cases[1].set("name", cases[0].attrib["name"])
            altered = ET.tostring(document)
            (copied / "focused.xml.gz").write_bytes(gzip.compress(altered, mtime=0))
            changed_manifest = dict(manifest, xmlSha256=hashlib.sha256(altered).hexdigest())
            (copied / "fresh-verification.json").write_text(json.dumps(changed_manifest))
            execution = subprocess.run(command, capture_output=True, text=True, timeout=30)
            if execution.returncode == 0 or "Missing distinct profile arguments" not in execution.stderr:
                raise ValueError("Duplicate theory arguments qualified: " + execution.stderr)
            results.append(
                {"method": method, "optimization": 1 if optimization else 0, "exitCode": execution.returncode}
            )
print(json.dumps({"validRelocatedPasses": 2, "duplicateArgumentRefusals": results}, indent=2))
