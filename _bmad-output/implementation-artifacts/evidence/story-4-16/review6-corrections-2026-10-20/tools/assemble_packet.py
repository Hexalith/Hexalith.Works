"""Copy one verifier artifacts directory into an evidence folder and summarize it without relabeling its outcome.

Usage: assemble_packet.py <artifacts dir> <destination folder> <exact command> <started UTC> <finished UTC> <exit code>
Copies the ledger, command logs, test XML, host-start records and gzip-compressed persisted outcomes.
Large build trees, runtime binaries and synthetic credentials stay in the disposable artifacts directory.
"""
import gzip
import hashlib
import json
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

source, destination = Path(sys.argv[1]), Path(sys.argv[2])
command, started, finished, exit_code = sys.argv[3], sys.argv[4], sys.argv[5], int(sys.argv[6])
destination.mkdir(parents=True, exist_ok=False)
for path in sorted(source.iterdir()):
    if path.is_file() and path.suffix in (".log", ".xml", ".json") or path.name == "host-starts.jsonl":
        shutil.copy2(path, destination / path.name)
persisted = source / "persisted-states.jsonl"
if persisted.is_file():
    with persisted.open("rb") as raw, gzip.GzipFile(destination / "persisted-states.jsonl.gz", "wb", mtime=0) as packed:
        shutil.copyfileobj(raw, packed)
ledger = json.loads((source / "ledger.json").read_text())
phases = []
if persisted.is_file():
    phases = [json.loads(line)["phase"] for line in persisted.read_text().splitlines() if line]
starts = [json.loads(line) for line in (source / "host-starts.jsonl").read_text().splitlines() if line] if (source / "host-starts.jsonl").is_file() else []
result = {
    "scope": "Story 4.16 public 3.126.0 requalification: canonical Platform Works parity and complete fleet rollback on the corrected graph; disposable synthetic data only",
    "checkedUtc": datetime.now(timezone.utc).isoformat(), "exactCommand": command, "cwd": str(Path(ledger["host"]["source"]).parent),
    "startedUtc": started, "finishedUtc": finished, "exitCode": exit_code, "status": ledger.get("status"), "failure": ledger.get("failure"),
    "selectedSdk": ledger.get("selectedSdk"),
    "runtimeProducerQualification": {key: (ledger.get("runtimeProducerQualification") or {}).get(key) for key in
                                     ("repositoryCommit", "qualification", "runtimeFileCount", "matchesPublishedProducer", "namedTag")},
    "checks": ledger.get("checks"),
    "commands": [{key: entry.get(key) for key in ("name", "exitCode", "log", "sha256")} for entry in ledger.get("commands", [])],
    "rows": {key: {"status": row.get("status"), "requiredProofs": row.get("requiredProofs")} for key, row in ledger.get("rows", {}).items()},
    "rollback": {key: value for key, value in (ledger.get("rollback") or {}).items() if key not in ("snapshot", "restored")},
    "starts": [{"role": start.get("role"), "readinessPassed": start.get("readinessPassed"),
                "worksReadinessExpectation": start.get("worksReadinessExpectation"),
                "modules": sorted(module.get("resource") for module in start.get("runtimeModules", [])),
                "platformHosting": start.get("platformHosting"), "platformComposition": start.get("platformComposition"),
                "schedulerVolume": start.get("schedulerVolume"),
                "compositionGuard": {resource: {"permittedOverlaps": entry.get("permittedOverlaps"), "composition": entry.get("composition")}
                                     for resource, entry in (start.get("compositionGuard") or {}).items()}}
               for start in ledger.get("actualStarts", starts)],
    "platformRuntimeIdentity": ledger.get("platformRuntimeIdentity"),
    "phases": phases, "duplicatePhases": sorted({phase for phase in phases if phases.count(phase) > 1}),
    "verifierSha256": ledger.get("verifierSha256"), "verifierHelpers": ledger.get("verifierHelpers"),
    "fixtureSources": ledger.get("fixtureSources"), "platformConsumerSources": len(ledger.get("platformConsumerSources", {})),
    "worksConsumerSource": {key: (ledger.get("worksConsumerSource") or {}).get(key) for key in ("commit", "inventorySha256", "provenance")},
}
(destination / "result.json").write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps({"status": result["status"], "exitCode": exit_code, "phases": len(phases), "starts": len(result["starts"])}))
