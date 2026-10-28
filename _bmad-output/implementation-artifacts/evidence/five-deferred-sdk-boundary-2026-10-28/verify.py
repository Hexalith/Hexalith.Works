#!/usr/bin/env python3
"""Verify retained artifacts, qualifying tests, source bindings and five ledger annotations."""

import gzip
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def require(condition, message):
    if not condition:
        raise ValueError(message)


def main():
    packet = Path(__file__).resolve().parent
    root = packet.parents[3]
    manifest = json.loads((packet / "manifest.json").read_text())
    for relative, expected in manifest["artifacts"].items():
        data = (packet / relative).read_bytes()
        require(hashlib.sha256(data).hexdigest() == expected, f"Changed artifact: {relative}")
    for relative, expected in manifest["sources"].items():
        require(hashlib.sha256((root / relative).read_bytes()).hexdigest() == expected,
                f"Changed source: {relative}")
    for relative, expected in manifest["assemblies"].items():
        path = root / relative
        require(path.is_file(), f"Missing exercised assembly: {relative}")
        require(hashlib.sha256(path.read_bytes()).hexdigest() == expected,
                f"Changed exercised assembly: {relative}")
    commands = json.loads((packet / "commands.json").read_text())
    for lane in manifest["required_commands"]:
        matches = [entry for entry in commands if entry["id"] == lane]
        require(len(matches) == 1 and matches[0]["exit_code"] == 0, f"Nonpassing command: {lane}")
    for filename, expected in manifest["qualifying_tests"].items():
        assembly = ET.fromstring(gzip.decompress((packet / filename).read_bytes())).find("assembly")
        require(assembly is not None, f"Missing test assembly: {filename}")
        for name in ("errors", "failed", "skipped", "not-run"):
            require(assembly.get(name) == "0", f"Nonqualifying {name}: {filename}")
        require(int(assembly.get("total", "0")) == expected, f"Unexpected total: {filename}")
        tests = assembly.findall(".//test")
        require(len(tests) == expected and all(test.get("result") == "Pass" for test in tests),
                f"Missing or nonpassing cases: {filename}")
        for method, count in manifest.get("required_cases", {}).get(filename, {}).items():
            matches = [test for test in tests if test.get("method") == method]
            require(len(matches) == count, f"Missing named regression: {method}")
    ledger = (root / "_bmad-output/implementation-artifacts/deferred-work.md").read_bytes()
    for annotation in manifest["ledger_annotations"]:
        encoded = annotation.encode()
        require(ledger.count(encoded) == 1, "Missing or duplicate ledger annotation")
        ledger = ledger.replace(encoded, b"", 1)
    require(len(manifest["ledger_annotations"]) == 5, "Expected five independent closures")
    require(hashlib.sha256(ledger).hexdigest() == manifest["baseline_ledger_sha256"],
            "Original ledger bytes were modified")
    print("Verified artifact/source/assembly hashes, passing commands and tests, and five additive ledger closures.")


if __name__ == "__main__":
    main()
