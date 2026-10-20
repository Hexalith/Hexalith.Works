"""Recheck the ledger's isolated Platform inputs and the current Works/changed Platform inputs.

Usage: bind_inputs.py <ledger.json> <isolation.json> <output.json>
The isolated graph remains authoritative; unrelated current Platform changes are listed separately.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

ledger = json.loads(Path(sys.argv[1]).read_text())
isolation = json.loads(Path(sys.argv[2]).read_text())
platform, works = Path(isolation["platform"]), Path(isolation["works"])
current_platform = Path("/home/administrator/projects/hexalith/platform")
mismatches, current_differences = [], []

def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None

def check(kind, path, digest):
    if sha256(path) != digest:
        mismatches.append({"kind": kind, "path": str(path), "reason": "missing or changed"})

for relative, digest in ledger["platformConsumerSources"].items():
    check("isolatedPlatformConsumer", platform / relative, digest)
    if sha256(current_platform / relative) != digest:
        current_differences.append(relative)
for item in ledger["rows"]["R10"]["configurationConsumers"]:
    check("isolatedConfiguration", platform / item["path"], item["sha256"])
    check("currentConfiguration", current_platform / item["path"], item["sha256"])
for relative, digest in isolation["overlays"].items():
    check("isolatedReviewedOverlay", platform / relative, digest)
    check("currentReviewedOverlay", current_platform / relative, digest)
check("verifier", platform / "eng/verify_works_host.py", ledger["verifierSha256"])
for relative, digest in ledger["verifierHelpers"].items():
    check("isolatedVerifierHelper", platform / "eng" / relative, digest)
    check("currentVerifierHelper", current_platform / "eng" / relative, digest)
for relative, digest in ledger["fixtureSources"].items():
    check("currentWorksFixture", works / relative, digest)
for relative, digest in ledger["worksConsumerSource"]["sourceFiles"].items():
    check("currentWorksConsumer", works / relative, digest)
sys.path.insert(0, str(platform / "eng"))
from verify_works_host import require_unchanged_fixture_sources
require_unchanged_fixture_sources(works, ledger["fixtureSources"])
result = {"checkedAt": datetime.now(timezone.utc).isoformat(), "isolatedPlatformBaseCommit": isolation["platformBaseCommit"],
          "currentPlatformHead": subprocess.check_output(["git", "-C", str(current_platform), "rev-parse", "HEAD"], text=True).strip(),
          "worksHead": subprocess.check_output(["git", "-C", str(works), "rev-parse", "HEAD"], text=True).strip(),
          "fixtureCount": len(ledger["fixtureSources"]), "isolatedPlatformSourceCount": len(ledger["platformConsumerSources"]),
          "currentPlatformDifferencesOutsideReviewedOverlays": sorted(current_differences), "mismatches": mismatches,
          "scope": "Complete isolated Platform input comparison, unchanged current Works source/fixture membership and bytes, and current reviewed overlays/configuration/helpers. Unrelated current Platform source differences remain explicit and unqualified."}
Path(sys.argv[3]).write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps({"mismatches": mismatches, "currentPlatformDifferencesOutsideReviewedOverlays": sorted(current_differences)}))
if mismatches:
    raise SystemExit(1)
