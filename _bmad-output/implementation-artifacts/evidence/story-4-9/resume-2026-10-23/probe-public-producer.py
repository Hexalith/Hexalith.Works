"""Replay Story 4.9's public-producer prerequisite without builds or Git mutations."""

import argparse
import datetime
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--works-root", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    root = args.works_root.resolve()
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=False)
    version = ET.parse(root / "Directory.Packages.props").findtext(".//HexalithEventStoreVersion")
    helper = root / "references/Hexalith.Platform/eng/verify_works_producer.py"
    specification = importlib.util.spec_from_file_location("story49_public_producer", helper)
    producer = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(producer)
    indexes = {}
    packages = {}
    for package in ("Hexalith.EventStore.Client", "Hexalith.EventStore.DomainService", "Hexalith.EventStore.Aspire"):
        base = "https://api.nuget.org/v3-flatcontainer/" + package.lower()
        with urllib.request.urlopen(base + "/index.json", timeout=30) as response:
            index = response.read(8 * 1024 * 1024)
        (output / (package + ".index.json")).write_bytes(index)
        stable = [item for item in json.loads(index)["versions"] if "-" not in item]
        indexes[package] = {
            "url": base + "/index.json",
            "latestStable": stable[-1],
            "sha256": hashlib.sha256(index).hexdigest(),
        }
        url = base + "/" + version + "/" + package.lower() + "." + version + ".nupkg"
        with urllib.request.urlopen(url, timeout=30) as response:
            archive_bytes = response.read(64 * 1024 * 1024)
        with zipfile.ZipFile(io.BytesIO(archive_bytes)) as archive:
            nuspec = archive.read(next(name for name in archive.namelist() if name.endswith(".nuspec")))
        (output / (package + ".nuspec")).write_bytes(nuspec)
        repository = ET.fromstring(nuspec).find(".//{*}repository")
        packages[package] = {
            "version": version,
            "source": "https://api.nuget.org/v3/index.json",
            "archiveUrl": url,
            "sha256": hashlib.sha256(archive_bytes).hexdigest(),
            "repositoryUrl": repository.get("url"),
            "repositoryCommit": repository.get("commit"),
        }
    write_json(output / "public-indexes.json", indexes)
    write_json(output / "published-producers.json", packages)
    snapshot = producer.runtime_source_snapshot(root / "references/Hexalith.EventStore")
    write_json(output / "runtime-source-snapshot.json", snapshot)
    receipts = []

    def fetch(url):
        data = producer._fetch(url)
        name = "official-response-" + str(len(receipts) + 1) + ".json"
        (output / name).write_bytes(data)
        receipts.append({"url": url, "path": name, "sha256": hashlib.sha256(data).hexdigest()})
        return data

    result = {
        "checkedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "sdkRoot": snapshot["root"],
        "selectedVersion": version,
        "canonicalHelper": str(helper),
        "canonicalHelperSha256": hashlib.sha256(helper.read_bytes()).hexdigest(),
    }
    try:
        result["qualification"] = producer.qualify_runtime_source(snapshot, packages, fetch=fetch, tag="v" + version)
        result["status"] = "passed"
    except Exception as error:
        result.update(status="refused", errorType=type(error).__name__, error=str(error))
    write_json(output / "official-source-receipts.json", receipts)
    write_json(output / "qualification-result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
