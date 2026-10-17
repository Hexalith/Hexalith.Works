"""Verify retained raw evidence without consulting its original temporary locations."""
from pathlib import Path
import gzip
import hashlib
import json
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent
index = json.loads((root / "raw-artifacts.json").read_text())
assert index["version"] == 1
manifests = {}
for artifact in index["artifacts"]:
    compressed = (root / artifact["archive"]).read_bytes()
    assert hashlib.sha256(compressed).hexdigest() == artifact["compressed_sha256"], "compressed hash mismatch"
    assert len(compressed) == artifact["compressed_bytes"], "compressed length mismatch"
    raw = gzip.decompress(compressed)
    assert hashlib.sha256(raw).hexdigest() == artifact["uncompressed_sha256"], "uncompressed hash mismatch"
    assert len(raw) == artifact["uncompressed_bytes"], "uncompressed length mismatch"
    assert artifact["role"] in ("selected", "preliminary", "supporting")
    for reference in artifact["references"]:
        filename = reference["manifest"]
        if filename not in manifests:
            manifests[filename] = json.loads((root / filename).read_text())
        value = manifests[filename]
        for part in reference["pointer"].split("/")[1:]:
            part = part.replace("~1", "/").replace("~0", "~")
            value = value[int(part)] if isinstance(value, list) else value[part]
        assert value in (artifact["original_path"], artifact["uncompressed_sha256"]), "original provenance mismatch"
print(f"Verified {len(index['artifacts'])} compressed/raw hash pairs and their original manifest provenance.")
