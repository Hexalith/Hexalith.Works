"""Recheck packet hashes, result counts, frozen intent and exact ledger preservation."""

import gzip
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def git(*arguments):
    return subprocess.check_output(["git", "-C", str(ROOT), *arguments], timeout=30)


PACKET = Path(__file__).resolve().parent
ROOT = PACKET.parents[3]
MANIFEST = json.loads((PACKET / "manifest.json").read_text())
BASELINE = MANIFEST["baseline"]
require(git("rev-parse", "--verify", BASELINE + "^{commit}").decode().strip() == BASELINE,
        "The baseline must be a complete canonical commit identifier.")

for group, parent in [("sources", ROOT), ("artifacts", PACKET)]:
    for name, expected in MANIFEST[group].items():
        data = (parent / name).read_bytes()
        require(hashlib.sha256(data).hexdigest() == expected, "Hash mismatch: " + name)
        if name.endswith(".gz"):
            gzip.decompress(data)

for lane, expected in MANIFEST["results"].items():
    data = gzip.decompress((PACKET / (lane + ".xml.gz")).read_bytes())
    assembly = ET.fromstring(data).find("assembly")
    require(assembly is not None, "Missing assembly result: " + lane)
    require({key: assembly.get(key) for key in expected} == expected, "Result mismatch: " + lane)

integrity = json.loads((PACKET / "integrity.json").read_text())
for name, expected in integrity.items():
    if not name.endswith(".md"):
        continue
    old = git("show", BASELINE + ":" + name)
    require(hashlib.sha256(old).hexdigest() == expected["baselineSha256"], "Baseline drift: " + name)
    frozen = rb"<frozen-after-approval\b[^>]*>.*?</frozen-after-approval>"
    require(re.findall(frozen, old, re.S) == re.findall(frozen, (ROOT / name).read_bytes(), re.S),
            "Frozen intent changed: " + name)

ledger_name = "_bmad-output/implementation-artifacts/deferred-work.md"
ledger = (ROOT / ledger_name).read_bytes()
diff = git("diff", "--unified=0", BASELINE, "--", ledger_name).decode().splitlines()
require(not any(line.startswith("-") and not line.startswith("---") for line in diff),
        "An original ledger line was removed or modified.")
added = [line[1:] for line in diff if line.startswith("+") and not line.startswith("+++")]
require(len(added) == 12, "Expected twelve ledger annotation lines.")
require(sum(line.startswith("  resolution: [spec-five-deferred-planning-evidence.md]") for line in added) == 5,
        "Expected five canonical closures.")
require(sum(line.startswith("  resolution: Aggregate alias,") for line in added) == 1,
        "Expected one aggregate alias, counted separately.")
for offset in range(0, len(added), 2):
    require(added[offset] == "  status: done 2026-10-28" and added[offset + 1].startswith("  resolution:"),
            "Unexpected ledger annotation.")
    block = ("\n".join(added[offset:offset + 2]) + "\n").encode()
    require(ledger.count(block) == 1, "Ledger annotation is ambiguous.")
    ledger = ledger.replace(block, b"", 1)
require(ledger == git("show", BASELINE + ":" + ledger_name), "Original ledger bytes were not preserved.")

failure = integrity["preExistingArchitectureFailure"]
for name, expected in failure.items():
    if not isinstance(expected, dict):
        continue
    data = (ROOT / name).read_bytes()
    require(data == git("show", BASELINE + ":" + name), "Broad-gate input changed: " + name)
    require(hashlib.sha256(data).hexdigest() == expected["sha256"], "Broad-gate hash mismatch: " + name)
gitlink = git("ls-tree", BASELINE, "references/Hexalith.EventStore").decode().split()[2]
require(gitlink == failure["baselineGitlink"] != failure["baselineMarker"],
        "The recorded baseline marker mismatch was not reproduced.")

for command in MANIFEST["commands"]:
    require(command["log"] in MANIFEST["artifacts"], "Unbound command output: " + command["log"])
    require(isinstance(command["exitCode"], int), "Missing process exit code.")
    if command.get("result"):
        require(command["result"] in MANIFEST["artifacts"], "Unbound command result.")

print("Verified source/artifact hashes, XML totals, frozen intent, five canonical closures, one alias and the unchanged broad-gate mismatch.")
