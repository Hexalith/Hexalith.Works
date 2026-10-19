"""Scan every retained packet file for credential-shaped material; gzip files are scanned decompressed.

Usage: secret_scan.py <packet folder> <output.json>
JWT, PEM and bearer matches are always unclassified findings. Long base64 runs are classified by the JSON key that
precedes them; a run under any other key is an unclassified finding.
"""
import gzip
import json
import re
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

PATTERNS = {
    "jwt": r"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}",
    "pem": r"-----BEGIN [A-Z ]*(?:PRIVATE|RSA|EC) KEY",
    "bearer": r"(?i)bearer\s+[A-Za-z0-9._-]{20,}",
    "longBase64": r"[A-Za-z0-9+/]{120,}={0,2}",
}
# Keys whose values are synthetic proof payloads or SDK digests, never credentials.
CLASSIFIED = {
    "Payload": "synthetic-json-payload (base64 command and event payloads)",
    "payload": "synthetic-json-payload (base64 command and event payloads)",
    "CommandPayload": "synthetic-json-payload (base64 command and event payloads)",
    "CapturedEventDigests": "SDK captured-event digests (concurrentDelivery.caughtUpCheckpoint.CapturedEventDigests)",
}
KEY_BEFORE = re.compile(r'"([A-Za-z0-9_]+)"\s*:\s*\[?\s*(?:"[^"]*"\s*,\s*)*"?$')

root, output = Path(sys.argv[1]), Path(sys.argv[2])
files = sorted(path for path in root.rglob("*") if path.is_file() and path != output and "tools" not in path.relative_to(root).parts)
summary, unclassified = Counter(), []
for path in files:
    raw = path.read_bytes()
    if path.suffix == ".gz":
        raw = gzip.decompress(raw)
    text = raw.decode("utf-8", errors="replace")
    for name, pattern in PATTERNS.items():
        for match in re.finditer(pattern, text):
            if name != "longBase64":
                unclassified.append({"file": str(path.relative_to(root)), "pattern": name, "offset": match.start()})
                continue
            prefix = text[max(0, match.start() - 4000):match.start()]
            key = KEY_BEFORE.search(prefix)
            label = CLASSIFIED.get(key.group(1)) if key else None
            if label is None:
                unclassified.append({"file": str(path.relative_to(root)), "pattern": name, "offset": match.start(),
                                     "precedingKey": key.group(1) if key else None})
            else:
                summary[label] += 1

result = {
    "checkedUtc": datetime.now(timezone.utc).isoformat(),
    "patterns": PATTERNS,
    "files": len(files),
    "summary": [{"pattern": "longBase64", "classification": label, "count": count} for label, count in sorted(summary.items())],
    "unclassifiedMatches": len(unclassified),
    "unclassified": unclassified[:50],
    "credentialMaterialFound": bool(unclassified),
    "note": "Persisted outcomes are scanned decompressed. The identity fixture bearer and synthetic keys stay in the disposable artifacts directory.",
}
output.write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps({key: result[key] for key in ("files", "summary", "unclassifiedMatches")}))
