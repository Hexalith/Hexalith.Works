"""Reproduce the published-producer gate without starting a fleet or changing state."""
import argparse
import json
from pathlib import Path
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--works-root", type=Path, required=True)
parser.add_argument("--platform-root", type=Path, required=True)
args = parser.parse_args()
sys.path.insert(0, str(args.platform_root / "eng"))
from verify_works_host import require_published_runtime_source, source_snapshot

packet = Path(__file__).parent / "public-producers.json"
producers = {row["id"]: {"repositoryCommit": row["repository"]["commit"]}
             for row in json.loads(packet.read_text())["packages"]}
try:
    result = require_published_runtime_source(source_snapshot(args.works_root / "references/Hexalith.EventStore"), producers)
except ValueError as error:
    print(str(error))
    raise SystemExit(1)
print(json.dumps(result, indent=2))
