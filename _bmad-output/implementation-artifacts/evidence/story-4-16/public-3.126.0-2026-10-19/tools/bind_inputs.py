"""Re-hash every input a canonical verifier ledger bound and report mismatches against the current working trees.

Usage: bind_inputs.py <ledger.json> <output.json>
Binds Platform consumer sources, R10 configuration consumers, the verifier and its helpers, Works fixture sources and the
Works consumer source inventory. Both repositories carry these inputs as working-tree bytes, so this check records that
the bytes reviewers see are the bytes the run used.
"""
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

PLATFORM = Path(sys.argv[3]) if len(sys.argv) > 3 else Path("/home/administrator/projects/hexalith/platform")
WORKS = Path("/home/administrator/projects/hexalith/works")


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def head(root):
    return subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL).strip()


ledger = json.loads(Path(sys.argv[1]).read_text())
mismatches = []


def check(kind, path, digest):
    if not Path(path).is_file():
        mismatches.append({"kind": kind, "path": str(path), "reason": "missing"})
    elif sha256(path) != digest:
        mismatches.append({"kind": kind, "path": str(path), "reason": "changed"})


for relative, digest in ledger["platformConsumerSources"].items():
    check("platformConsumerSource", PLATFORM / relative, digest)
configuration = ledger["rows"]["R10"]["configurationConsumers"]
for entry in configuration:
    check("configurationConsumer", PLATFORM / entry["path"], entry["sha256"])
check("verifier", PLATFORM / "eng/verify_works_host.py", ledger["verifierSha256"])
for name, digest in ledger["verifierHelpers"].items():
    check("verifierHelper", PLATFORM / "eng" / name, digest)
for relative, digest in ledger["fixtureSources"].items():
    check("fixtureSource", WORKS / relative, digest)
consumer = ledger["worksConsumerSource"]["sourceFiles"]
for relative, digest in consumer.items():
    check("worksConsumerSource", WORKS / relative, digest)

result = {
    "checkedUtc": datetime.now(timezone.utc).isoformat(),
    "platformHead": head(PLATFORM),
    "platformSourceRoot": str(PLATFORM),
    "worksHead": head(WORKS),
    "platformConsumerSources": len(ledger["platformConsumerSources"]),
    "configurationConsumers": len(configuration),
    "verifierAndHelpers": 1 + len(ledger["verifierHelpers"]),
    "fixtureSources": len(ledger["fixtureSources"]),
    "worksConsumerSourceFiles": len(consumer),
    "mismatches": mismatches,
    "note": "Current working-tree bytes compared with the hashes the canonical run recorded; both repositories carry these inputs as uncommitted changes.",
}
Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps({key: value for key, value in result.items() if key != "note"}))
