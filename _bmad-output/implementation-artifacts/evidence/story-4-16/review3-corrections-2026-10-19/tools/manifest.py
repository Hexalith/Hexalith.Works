"""Write manifest.json binding every retained packet file, tools included, by SHA-256.

Usage: manifest.py <packet folder>
"""
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

root = Path(sys.argv[1])
files = {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
         for path in sorted(root.rglob("*")) if path.is_file() and path.name != "manifest.json"}
manifest = {"scope": "Story 4.16 review iteration 3 corrections packet; hashes bind every retained file",
            "createdUtc": datetime.now(timezone.utc).isoformat(), "files": files}
(root / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
print(len(files), "files")
