"""Reproduce latest-public source admission independently of a disposable fleet."""
import argparse
import json
from pathlib import Path
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--platform-root", type=Path, required=True)
parser.add_argument("--sdk-root", type=Path, required=True)
args = parser.parse_args()
sys.path.insert(0, str(args.platform_root / "eng"))
from verify_works_producer import qualify_runtime_source, runtime_source_snapshot

packages = json.loads((Path(__file__).parent / "public-producers.json").read_text())["packages"]
producers = {row["id"]: {"repositoryCommit": row["repository"]["commit"],
                         "repositoryUrl": row["repository"]["url"]} for row in packages}
try:
    result = qualify_runtime_source(runtime_source_snapshot(args.sdk_root), producers)
except (ValueError, OSError) as error:
    print(str(error))
    raise SystemExit(1)
print(json.dumps(result, indent=2))
